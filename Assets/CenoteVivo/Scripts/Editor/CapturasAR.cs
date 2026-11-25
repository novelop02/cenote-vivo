using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace CenoteVivo.EditorTools
{
    /// <summary>
    /// Renderiza la miniatura de la escena AR (fondo transparente) para presentaciones y documentación:
    /// limpia, contaminada, con el corte del subsuelo y la posición en pantalla de las etiquetas.
    /// Uso: Unity -batchmode -executeMethod CenoteVivo.EditorTools.CapturasAR.Capturar -capturas "C:/ruta"
    /// </summary>
    public static class CapturasAR
    {
        const int Ancho = 1800, Alto = 1350;

        public static void Capturar()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-capturas");
            string carpeta = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Path.GetDirectoryName(Application.dataPath), "Documentacion", "capturas_ar");
            Directory.CreateDirectory(carpeta);

            EditorSettings.asyncShaderCompilation = false; // que los shaders estén listos en el primer render
            EditorSceneManager.OpenScene(CenoteVivoBuilder.EscenaAR, OpenSceneMode.Single);
            var contenido = GameObject.Find("[CenoteAR]").transform;
            var espacio = contenido.Find("Espacio Cenote");
            var corte = UnityEngine.Object.FindAnyObjectByType<CorteTransversal>();
            var puntos = espacio.Find("Puntos de etiquetas").Cast<Transform>().ToArray();

            // Ilumina como de día (en el editor no corre DayNightController).
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.8f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.52f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.22f, 0.18f);
            RenderSettings.fog = false;
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (l.type == LightType.Directional)
                {
                    bool sol = l.name.Contains("Directional");
                    l.enabled = sol;
                    if (sol) { l.intensity = 1.3f; l.color = new Color(1f, 0.95f, 0.85f); l.transform.rotation = Quaternion.Euler(50f, -40f, 0f); }
                }
                else l.enabled = false; // faroles nocturnos apagados
            }
            // Sin luciérnagas ni faroles en las capturas de día.
            var noche = espacio.Find("[Noche]");
            if (noche != null) noche.gameObject.SetActive(false);

            // Miniatura a escala 1 respecto al tamaño de colocación, en el origen.
            contenido.position = Vector3.zero;
            contenido.rotation = Quaternion.identity;
            float diametro = 0.35f;

            var camGo = new GameObject("Camara Captura");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = 38f;
            cam.aspect = Ancho / (float)Alto;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 10f;
            cam.allowHDR = false;
            cam.allowMSAA = true;
            var datos = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            datos.renderPostProcessing = false;
            datos.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None;

            // Vista 3/4 desde el frente (-Z), como el usuario frente a la mesa.
            var limites = new Bounds(contenido.position, Vector3.zero);
            foreach (var r in contenido.GetComponentsInChildren<Renderer>()) if (!(r is ParticleSystemRenderer)) limites.Encapsulate(r.bounds);
            Vector3 objetivo = limites.center;
            cam.transform.position = objetivo + Quaternion.Euler(38f, 0f, 0f) * new Vector3(0f, 0f, -Mathf.Max(diametro, limites.size.magnitude) * 1.55f);
            cam.transform.LookAt(objetivo);

            void Globales(float contaminacion)
            {
                Shader.SetGlobalFloat("_CenoteEspacioAR", 1f);
                Shader.SetGlobalMatrix("_CenoteMundoALocal", espacio.worldToLocalMatrix);
                Shader.SetGlobalMatrix("_CenoteLocalAMundo", espacio.localToWorldMatrix);
                Shader.SetGlobalFloat("_CenotePollution", contaminacion);
            }

            void Particulas(float contaminacion)
            {
                foreach (var ps in espacio.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (ps.transform.IsChildOf(espacio.Find("[Contaminacion]")) == false) continue;
                    ps.Clear(true);
                    if (contaminacion <= 0f) continue;
                    var em = ps.emission;
                    em.rateOverTime = 40f * contaminacion;
                    ps.Simulate(12f, true, true);
                }
            }

            string Render(string nombre, float contaminacion, bool conCorte)
            {
                Globales(contaminacion);
                Particulas(contaminacion);
                if (corte != null)
                {
                    corte.Activar(conCorte);
                    if (conCorte)
                    {
                        var hacia = espacio.InverseTransformPoint(cam.transform.position) - corte.PuntoDelPlano;
                        hacia.y = 0f;
                        corte.FijarNormal(hacia);
                    }
                }
                var rt = new RenderTexture(Ancho, Alto, 24, GraphicsFormat.R8G8B8A8_SRGB) { antiAliasing = 4 };
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Ancho, Alto, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, Ancho, Alto), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;
                string ruta = Path.Combine(carpeta, nombre + ".png");
                File.WriteAllBytes(ruta, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                rt.Release();
                return ruta;
            }

            // Calentamiento: los primeros renders compilan shaders y pueden salir sin materiales.
            for (int w = 0; w < 3; w++) Render("_calentamiento", 0f, false);
            File.Delete(Path.Combine(carpeta, "_calentamiento.png"));
            Render("ar_limpio", 0f, false);
            Render("ar_contaminado", 1f, false);
            Render("ar_corte", 0.85f, true);

            // Posiciones de las etiquetas en la imagen (origen arriba a la izquierda).
            var sb = new StringBuilder("{\n");
            for (int k = 0; k < puntos.Length; k++)
            {
                var vp = cam.WorldToViewportPoint(puntos[k].position);
                float x = vp.x * Ancho, y = (1f - vp.y) * Alto;
                sb.Append($"  \"{TextosCenote.EtiquetasAR[k].titulo}\": [{x:0}, {y:0}]{(k < puntos.Length - 1 ? "," : "")}\n");
            }
            sb.Append("}\n");
            File.WriteAllText(Path.Combine(carpeta, "etiquetas.json"), sb.ToString());

            Debug.Log("[Cenote Vivo] Capturas AR en " + carpeta);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
