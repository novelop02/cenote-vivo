using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using CenoteVivo.UI;
using static CenoteVivo.EditorTools.UIFabrica;

namespace CenoteVivo.EditorTools
{
    /// <summary>
    /// Arma automáticamente las escenas de Cenote Vivo a partir del modelo existente:
    /// materiales, LOD, puntos de cámara, partículas de contaminación, iluminación y UI (Canvas).
    /// Se puede ejecutar varias veces: reemplaza solo lo que él mismo creó.
    /// </summary>
    static class CenoteVivoBuilder
    {
        const string Raiz = "Assets/CenoteVivo";
        const string CarpetaEscenas = Raiz + "/Scenes";
        const string CarpetaMateriales = Raiz + "/Materials";
        const string EscenaOrigen = "Assets/Scenes/SampleScene.unity";
        const string EscenaCenote = CarpetaEscenas + "/CenoteVivo.unity";
        const string EscenaMenu = CarpetaEscenas + "/MenuInicio.unity";

        // Nombres de los objetos raíz que crea el constructor (se borran al reconstruir).
        static readonly string[] ObjetosGenerados =
            { "[Sistemas]", "[Vistas]", "[Contaminacion]", "[Selva]", "[Noche]", "Luna", "Canvas_PantallaPrincipal", "Canvas_PanelInfo", "EventSystem" };

        [MenuItem("Cenote Vivo/1. Construir escenas", priority = 1)]
        internal static void ConstruirEscenas()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Directory.CreateDirectory(CarpetaEscenas);
            Directory.CreateDirectory(CarpetaMateriales);
            ActivarOpaqueTexture();

            if (!ConstruirEscenaCenote()) return;
            ConstruirEscenaMenu();

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(EscenaMenu, true),
                new EditorBuildSettingsScene(EscenaCenote, true),
            };
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(EscenaMenu);
            Debug.Log("[Cenote Vivo] Escenas listas: MenuInicio (índice 0) y CenoteVivo (índice 1). Pulsa Play para probar.");
        }

        [MenuItem("Cenote Vivo/2. Configurar proyecto para Android", priority = 2)]
        static void ConfigurarAndroid()
        {
            var android = NamedBuildTarget.Android;
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.productName = "Cenote Vivo";
            PlayerSettings.SetApplicationIdentifier(android, "com.cenotevivo.app");
            Debug.Log("[Cenote Vivo] Android: IL2CPP + ARM64, orientación horizontal, id com.cenotevivo.app. " +
                      "Cambia la plataforma en File > Build Profiles > Android.");
        }

        // ───────────────────────────── Escena 3D ─────────────────────────────

        static bool ConstruirEscenaCenote()
        {
            if (!File.Exists(EscenaCenote))
            {
                if (!AssetDatabase.CopyAsset(EscenaOrigen, EscenaCenote))
                {
                    Debug.LogError($"[Cenote Vivo] No se pudo copiar {EscenaOrigen}.");
                    return false;
                }
            }
            var escena = EditorSceneManager.OpenScene(EscenaCenote, OpenSceneMode.Single);
            foreach (var go in escena.GetRootGameObjects())
                if (ObjetosGenerados.Contains(go.name)) Object.DestroyImmediate(go);

            var raices = escena.GetRootGameObjects();
            var lod0 = raices.SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                             .FirstOrDefault(t => t.name == "Cenote_LOD0");
            if (lod0 == null)
            {
                Debug.LogError("[Cenote Vivo] No encontré 'Cenote_LOD0' en la escena. ¿Está el modelo cenote-prueba-7 en SampleScene?");
                return false;
            }
            var modelo = lod0.root;

            var camara = raices.Select(r => r.GetComponentInChildren<Camera>(true))
                               .FirstOrDefault(c => c != null && c.CompareTag("MainCamera"));
            var sol = raices.Select(r => r.GetComponent<Light>()).FirstOrDefault(l => l != null && l.type == LightType.Directional);

            // El FBX trae su propia cámara y luz: se desactivan para no duplicar renderizado.
            foreach (var c in modelo.GetComponentsInChildren<Camera>(true)) c.enabled = false;
            foreach (var l in modelo.GetComponentsInChildren<Light>(true)) l.enabled = false;

            var agua = modelo.GetComponentsInChildren<Renderer>(true)
                             .FirstOrDefault(r => r.name.Contains("Agua"));
            var limites = Limites(modelo.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && !(r is ParticleSystemRenderer)));
            Bounds limitesAgua = agua != null ? agua.bounds : new Bounds(limites.center, limites.size * 0.5f);
            float nivelAgua = limitesAgua.max.y;
            float radio = Mathf.Max(limites.extents.x, limites.extents.z);
            float radioAgua = Mathf.Max(0.5f, Mathf.Min(limitesAgua.extents.x, limitesAgua.extents.z));

            var materiales = AsignarMateriales(modelo, limites, limitesAgua);
            ConfigurarLOD(modelo);
            ConfigurarRenderizado(modelo, agua);

            // ── Sistemas ──
            var sistemas = new GameObject("[Sistemas]");

            if (sol == null)
            {
                sol = new GameObject("Directional Light").AddComponent<Light>();
                sol.type = LightType.Directional;
            }
            sol.shadows = LightShadows.Hard;
            sol.shadowStrength = 0.8f;
            var luna = new GameObject("Luna").AddComponent<Light>();
            luna.type = LightType.Directional;
            luna.color = new Color(0.55f, 0.65f, 1f);
            luna.shadows = LightShadows.None;
            luna.transform.rotation = Quaternion.Euler(60f, 150f, 0f);
            luna.intensity = 0f;

            var diaNoche = sistemas.AddComponent<DayNightController>();
            Fijar(diaNoche, "sol", sol);
            Fijar(diaNoche, "luna", luna);

            // ── Entorno: selva, vegetación del cenote, faroles nocturnos ──
            SelvaGenerador.Limpiar();
            var selva = SelvaGenerador.Generar(modelo, limites, limitesAgua, nivelAgua, diaNoche);
            if (materiales.TryGetValue("Roca_Calcarea", out var roca)) roca.SetFloat("_Musgo", 0.85f);
            if (materiales.TryGetValue("Piedras", out var piedras)) piedras.SetFloat("_Musgo", 0.35f);

            var contaminacion = sistemas.AddComponent<PollutionController>();
            var emisores = CrearParticulas(limites, limitesAgua, nivelAgua, radioAgua);
            var so = new SerializedObject(contaminacion);
            var lista = so.FindProperty("emisores");
            lista.arraySize = emisores.Count;
            for (int i = 0; i < emisores.Count; i++)
            {
                var e = lista.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("sistema").objectReferenceValue = emisores[i].sistema;
                e.FindPropertyRelative("tasaMaxima").floatValue = emisores[i].tasa;
                e.FindPropertyRelative("umbral").floatValue = emisores[i].umbral;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            // ── Cámara y vistas ──
            if (camara == null)
            {
                camara = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
                camara.tag = "MainCamera";
            }
            float tam = limites.size.magnitude;
            camara.nearClipPlane = Mathf.Max(0.05f, tam * 0.001f);
            camara.farClipPlane = Mathf.Max(tam * 4f, radio * 12f);
            camara.clearFlags = CameraClearFlags.Skybox;

            var vistas = CrearVistas(limites, limitesAgua, nivelAgua, radio);

            // Corte transversal: plano vertical por el centro del cenote, de cara a la vista 3.
            var corte = sistemas.AddComponent<CorteTransversal>();
            so = new SerializedObject(corte);
            so.FindProperty("normal").vector3Value = Vector3.back;
            so.FindProperty("puntoDelPlano").vector3Value = limites.center;
            so.FindProperty("alturaSuperficie").floatValue = selva.alturaSuelo;
            so.FindProperty("profundidad").floatValue = limites.size.y;
            so.FindProperty("nivelAgua").floatValue = nivelAgua;
            so.ApplyModifiedPropertiesWithoutUndo();

            var controlCamara = sistemas.AddComponent<CameraViewController>();
            Fijar(controlCamara, "camara", camara);
            Fijar(controlCamara, "corte", corte);
            Fijar(controlCamara, "superficieAgua", agua);
            Fijar(controlCamara, "contaminacion", contaminacion);
            Fijar(controlCamara, "diaNoche", diaNoche);
            Fijar(controlCamara, "densidadNieblaLimpia", 1.2f / (radioAgua * 2.5f));
            Fijar(controlCamara, "densidadNieblaContaminada", 1.2f / (radioAgua * 0.3f));
            so = new SerializedObject(controlCamara);
            lista = so.FindProperty("vistas");
            lista.arraySize = vistas.Count;
            for (int i = 0; i < vistas.Count; i++)
            {
                var e = lista.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("nombre").stringValue = vistas[i].nombre;
                e.FindPropertyRelative("descripcion").stringValue = TextosCenote.DescripcionesVistas[i];
                e.FindPropertyRelative("punto").objectReferenceValue = vistas[i].punto;
                e.FindPropertyRelative("campoVision").floatValue = vistas[i].fov;
                e.FindPropertyRelative("corte").boolValue = vistas[i].corte;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            camara.transform.SetPositionAndRotation(vistas[0].punto.position, vistas[0].punto.rotation);
            camara.fieldOfView = vistas[0].fov;

            // Bruma húmeda de selva: funde la selva lejana con el cielo (el color lo pone DayNightController).
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = radio * 2.2f;
            RenderSettings.fogEndDistance = radio * 9f;

            // ── UI ──
            ConstruirUIPrincipal(contaminacion, diaNoche, controlCamara);
            SistemaDeEventos();

            EditorSceneManager.MarkSceneDirty(escena);
            EditorSceneManager.SaveScene(escena);
            return true;
        }

        static Bounds Limites(IEnumerable<Renderer> renderers)
        {
            Bounds? b = null;
            foreach (var r in renderers)
            {
                if (b == null) b = r.bounds;
                else { var t = b.Value; t.Encapsulate(r.bounds); b = t; }
            }
            return b ?? new Bounds(Vector3.zero, Vector3.one * 10f);
        }

        // ───────────────────────────── Materiales ─────────────────────────────

        static Dictionary<string, Material> AsignarMateriales(Transform modelo, Bounds limites, Bounds limitesAgua)
        {
            var creados = new Dictionary<string, Material>();
            foreach (var r in modelo.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                // Materiales originales del FBX (cenote-prueba-7): de ahí salen el tipo y el color base.
                var fuente = PrefabUtility.GetCorrespondingObjectFromOriginalSource(r);
                var originales = fuente != null ? fuente.sharedMaterials : mats;
                bool cambio = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var original = i < originales.Length && originales[i] != null ? originales[i] : mats[i];
                    if (original == null) continue;
                    string tipo = Tipo(original.name);
                    if (tipo == null) continue;
                    if (!creados.TryGetValue(tipo, out var nuevo))
                    {
                        nuevo = CrearMaterial(tipo, original, limites, limitesAgua);
                        creados[tipo] = nuevo;
                    }
                    if (mats[i] != nuevo) { mats[i] = nuevo; cambio = true; }
                }
                if (cambio) r.sharedMaterials = mats;
            }
            return creados;
        }

        static string Tipo(string nombre)
        {
            if (nombre.Contains("Agua")) return "Agua";
            if (nombre.Contains("Subsuelo")) return "Subsuelo_Corte";
            if (nombre.Contains("Piedras")) return "Piedras";
            if (nombre.Contains("Roca")) return "Roca_Calcarea";
            return null;
        }

        static Material CrearMaterial(string tipo, Material original, Bounds limites, Bounds limitesAgua)
        {
            string ruta = $"{CarpetaMateriales}/CV_{tipo}.mat";
            var shader = Grafo(tipo == "Agua" ? "SG_Agua" : "SG_Roca");
            var existente = AssetDatabase.LoadAssetAtPath<Material>(ruta);
            if (existente != null && existente.shader == shader) return existente; // Respeta los ajustes hechos a mano.

            // Datos de partida: el material original del FBX.
            var fuente = original;
            Color baseColor = fuente.HasProperty("_BaseColor") ? fuente.GetColor("_BaseColor")
                            : fuente.HasProperty("_Color") ? fuente.GetColor("_Color") : Color.white;
            var albedo = fuente.HasProperty("_BaseMap") ? fuente.GetTexture("_BaseMap") : null;
            var normal = fuente.HasProperty("_BumpMap") ? fuente.GetTexture("_BumpMap") : null;

            var m = Asegurar(existente, shader);
            float radio = Mathf.Max(limites.extents.x, limites.extents.z);
            float radioAgua = Mathf.Max(0.5f, Mathf.Max(limitesAgua.extents.x, limitesAgua.extents.z));

            if (tipo == "Agua")
            {
                m.SetFloat("_EscalaOndas", 38f / radioAgua);
                m.SetFloat("_EscalaTurbidez", 3f / radioAgua);
            }
            else
            {
                m.SetColor("_ColorBase", baseColor);
                if (albedo != null) m.SetTexture("_TexturaBase", albedo);
                if (normal != null) m.SetTexture("_TexturaNormal", normal);
                m.SetFloat("_EscalaVariacion", 4f / radio);
                m.SetFloat("_EscalaManchas", 6f / radio);
                m.SetFloat("_EscalaTriplanar", 8f / radio);

                if (tipo == "Piedras")
                {
                    m.SetFloat("_VariacionColor", 0.5f);
                    m.SetFloat("_Humedad", 0.3f);
                }
                if (tipo == "Subsuelo_Corte")
                {
                    m.SetFloat("_Subsuelo", 1f);
                    m.SetFloat("_FrecuenciaEstratos", 6f / limites.size.y);
                }
            }
            return Guardar(m, ruta, $"CV_{tipo}", existente == null);
        }

        /// <summary>Lit Shader Graph del proyecto (Assets/CenoteVivo/Shaders/Graphs).</summary>
        internal static Shader Grafo(string nombre)
        {
            var shader = Shader.Find($"Cenote Vivo/{nombre}");
            if (shader == null) throw new System.Exception($"No se encontró el Shader Graph 'Cenote Vivo/{nombre}'. ¿Se importó {nombre}.shadergraph?");
            return shader;
        }

        /// <summary>Crea el material o migra uno existente al nuevo shader (limpiando keywords del shader anterior).</summary>
        internal static Material Asegurar(Material existente, Shader shader)
        {
            if (existente == null) return new Material(shader);
            existente.shader = shader;
            existente.shaderKeywords = new string[0];
            existente.renderQueue = -1; // la cola la decide el Shader Graph (opaco / transparente)
            return existente;
        }

        internal static Material Guardar(Material m, string ruta, string nombre, bool nuevo)
        {
            m.name = nombre;
            if (nuevo) AssetDatabase.CreateAsset(m, ruta);
            else EditorUtility.SetDirty(m);
            return m;
        }

        // ───────────────────────────── LOD y renderizado ─────────────────────────────

        static void ConfigurarLOD(Transform modelo)
        {
            Renderer[] Nivel(int n) => modelo.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == $"Cenote_LOD{n}")
                .SelectMany(t => t.GetComponentsInChildren<Renderer>(true)).ToArray();

            var niveles = new[] { Nivel(0), Nivel(1), Nivel(2) };
            if (niveles[0].Length == 0) return;

            var grupo = modelo.GetComponentInChildren<LODGroup>(true);
            if (grupo == null)
            {
                var padre = niveles[0][0].transform.parent != null ? niveles[0][0].transform.parent : modelo;
                grupo = padre.gameObject.AddComponent<LODGroup>();
            }

            // Porcentaje de altura de pantalla en que cambia cada nivel. El último nunca se oculta
            // (0.001) porque el cenote es el escenario completo.
            float[] alturas = { 0.55f, 0.22f, 0.001f };
            var lods = new List<LOD>();
            for (int i = 0; i < niveles.Length; i++)
                if (niveles[i].Length > 0) lods.Add(new LOD(alturas[i], niveles[i]));
            var arr = lods.ToArray();
            arr[arr.Length - 1].screenRelativeTransitionHeight = 0.001f;
            grupo.fadeMode = LODFadeMode.None;
            grupo.SetLODs(arr);
            grupo.RecalculateBounds();
        }

        static void ConfigurarRenderizado(Transform modelo, Renderer agua)
        {
            foreach (var r in modelo.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool esAgua = r == agua;
                r.shadowCastingMode = esAgua ? ShadowCastingMode.Off : ShadowCastingMode.On;
                r.receiveShadows = !esAgua;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = esAgua ? ReflectionProbeUsage.BlendProbesAndSkybox : ReflectionProbeUsage.Off;
                r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                GameObjectUtility.SetStaticEditorFlags(r.gameObject,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            }
            // Las LOD menores no necesitan proyectar sombra: la sombra de LOD0 se ve igual a distancia.
            foreach (var t in modelo.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Cenote_LOD2"))
                foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static void ActivarOpaqueTexture()
        {
            // La refracción del agua lee _CameraOpaqueTexture. Se activa con submuestreo 2x (barato).
            var assets = new HashSet<RenderPipelineAsset>();
            if (GraphicsSettings.defaultRenderPipeline != null) assets.Add(GraphicsSettings.defaultRenderPipeline);
            for (int i = 0; i < QualitySettings.count; i++)
            {
                var a = QualitySettings.GetRenderPipelineAssetAt(i);
                if (a != null) assets.Add(a);
            }
            foreach (var a in assets)
            {
                var so = new SerializedObject(a);
                var opaque = so.FindProperty("m_RequireOpaqueTexture");
                var down = so.FindProperty("m_OpaqueDownsampling");
                if (opaque != null) opaque.boolValue = true;
                if (down != null) down.intValue = 1; // _2xBilinear
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(a);
            }
        }

        // ───────────────────────────── Cámaras ─────────────────────────────

        struct DatosVista { public string nombre; public Transform punto; public float fov; public bool corte; }

        static List<DatosVista> CrearVistas(Bounds limites, Bounds agua, float nivelAgua, float radio)
        {
            var raiz = new GameObject("[Vistas]").transform;
            var lista = new List<DatosVista>();
            var centroAgua = new Vector3(agua.center.x, nivelAgua, agua.center.z);

            // Vista 1: panorámica desde arriba.
            var p1 = new Vector3(limites.center.x, limites.max.y + radio * 0.9f, limites.center.z - radio * 1.5f);
            lista.Add(Vista(raiz, "Vista general", p1, centroAgua, 50f, false));

            // Vista 2: a ras del agua, desde un borde mirando al otro.
            float altura = Mathf.Max(0.15f, agua.extents.magnitude * 0.03f);
            var p2 = centroAgua + new Vector3(0f, altura, -agua.extents.z * 0.8f);
            var objetivo2 = centroAgua + new Vector3(0f, altura * 0.6f, agua.extents.z * 0.6f);
            lista.Add(Vista(raiz, "Nivel del agua", p2, objetivo2, 60f, false));

            // Vista 3: corte transversal. El plano pasa por el centro con normal -Z;
            // la cámara se coloca de frente al plano, ligeramente elevada.
            float tamCorte = Mathf.Max(limites.size.y, radio * 2f);
            float distancia = tamCorte * 0.62f / Mathf.Tan(25f * Mathf.Deg2Rad);
            var objetivo3 = limites.center;
            var p3 = objetivo3 + Vector3.back * distancia + Vector3.up * tamCorte * 0.15f;
            lista.Add(Vista(raiz, "Corte del subsuelo", p3, objetivo3, 50f, true));
            return lista;
        }

        static DatosVista Vista(Transform raiz, string nombre, Vector3 posicion, Vector3 mirarA, float fov, bool corte)
        {
            var t = new GameObject("Vista - " + nombre).transform;
            t.SetParent(raiz, false);
            t.position = posicion;
            t.rotation = Quaternion.LookRotation(mirarA - posicion, Vector3.up);
            return new DatosVista { nombre = nombre, punto = t, fov = fov, corte = corte };
        }

        // ───────────────────────────── Partículas de contaminación ─────────────────────────────

        struct DatosEmisor { public ParticleSystem sistema; public float tasa; public float umbral; }

        static List<DatosEmisor> CrearParticulas(Bounds limites, Bounds agua, float nivelAgua, float radioAgua)
        {
            var raiz = new GameObject("[Contaminacion]").transform;
            var lista = new List<DatosEmisor>();
            var texSuave = TexturaParticula();

            // 1) Basura flotante (mallas instanciadas por GPU).
            {
                var ps = Sistema("Basura flotante", raiz, new Vector3(agua.center.x, nivelAgua + radioAgua * 0.004f, agua.center.z));
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(25f, 40f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(radioAgua * 0.012f, radioAgua * 0.03f);
                main.startRotation3D = true;
                main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.maxParticles = 90;
                main.startColor = new ParticleSystem.MinMaxGradient(Degradado(
                    new Color(0.85f, 0.85f, 0.8f), new Color(0.2f, 0.45f, 0.75f), new Color(0.45f, 0.3f, 0.15f),
                    new Color(0.15f, 0.5f, 0.25f), new Color(0.75f, 0.2f, 0.15f)))
                    { mode = ParticleSystemGradientMode.RandomColor };

                var forma = ps.shape;
                forma.shapeType = ParticleSystemShapeType.Circle;
                forma.radius = radioAgua * 0.85f;
                forma.rotation = new Vector3(90f, 0f, 0f);

                var ruido = ps.noise;
                ruido.enabled = true;
                ruido.strength = radioAgua * 0.01f;
                ruido.frequency = 0.2f;
                ruido.scrollSpeed = 0.05f;
                ruido.quality = ParticleSystemNoiseQuality.Low;
                ruido.separateAxes = true;
                ruido.strengthX = radioAgua * 0.01f;
                ruido.strengthY = 0f;
                ruido.strengthZ = radioAgua * 0.01f;

                Aparecer(ps);

                var r = ps.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Mesh;
                r.SetMeshes(new[] { Resources.GetBuiltinResource<Mesh>("Cube.fbx"), Resources.GetBuiltinResource<Mesh>("Capsule.fbx") });
                r.meshDistribution = ParticleSystemMeshDistribution.UniformRandom;
                r.enableGPUInstancing = true;
                r.sharedMaterial = MaterialParticula("CV_Particula_Basura", null);
                lista.Add(new DatosEmisor { sistema = ps, tasa = 3f, umbral = 0.2f });
            }

            // 2) Filtraciones: escurrimientos oscuros que bajan por las paredes hacia el agua.
            {
                float alturaParedes = Mathf.Max(1f, limites.max.y - nivelAgua);
                var ps = Sistema("Filtraciones en paredes", raiz,
                    new Vector3(agua.center.x, nivelAgua + alturaParedes * 0.6f, agua.center.z));
                var main = ps.main;
                float velocidad = alturaParedes * 0.12f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(radioAgua * 0.02f, radioAgua * 0.045f);
                main.maxParticles = 300;
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.25f, 0.2f, 0.08f, 0.8f), new Color(0.12f, 0.12f, 0.06f, 0.9f));

                var forma = ps.shape;
                forma.shapeType = ParticleSystemShapeType.Circle;
                forma.radius = radioAgua * 1.02f;
                forma.radiusThickness = 0f; // solo el borde
                forma.rotation = new Vector3(90f, 0f, 0f);

                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
                vel.y = new ParticleSystem.MinMaxCurve(-velocidad * 1.3f, -velocidad * 0.7f);
                vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

                Desvanecer(ps);

                var r = ps.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.lengthScale = 4f;
                r.velocityScale = 0f;
                r.sharedMaterial = MaterialParticula("CV_Particula_Filtracion", texSuave);
                lista.Add(new DatosEmisor { sistema = ps, tasa = 40f, umbral = 0.1f });
            }

            // 3) Sedimento suspendido bajo el agua (se aprecia en la vista a ras / sumergida).
            {
                float profundidad = Mathf.Max(1f, (nivelAgua - limites.min.y) * 0.4f);
                var ps = Sistema("Sedimento suspendido", raiz,
                    new Vector3(agua.center.x, nivelAgua - profundidad * 0.5f, agua.center.z));
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(radioAgua * 0.01f, radioAgua * 0.03f);
                main.maxParticles = 250;
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.35f, 0.3f, 0.15f, 0.6f), new Color(0.2f, 0.25f, 0.12f, 0.5f));

                var forma = ps.shape;
                forma.shapeType = ParticleSystemShapeType.Box;
                forma.scale = new Vector3(agua.size.x * 0.8f, profundidad, agua.size.z * 0.8f);

                var ruido = ps.noise;
                ruido.enabled = true;
                ruido.strength = radioAgua * 0.02f;
                ruido.frequency = 0.3f;
                ruido.quality = ParticleSystemNoiseQuality.Low;

                Desvanecer(ps);

                var r = ps.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Billboard;
                r.sharedMaterial = MaterialParticula("CV_Particula_Sedimento", texSuave);
                lista.Add(new DatosEmisor { sistema = ps, tasa = 35f, umbral = 0.3f });
            }
            return lista;
        }

        static ParticleSystem Sistema(string nombre, Transform padre, Vector3 posicion)
        {
            var go = new GameObject(nombre);
            go.transform.SetParent(padre, false);
            go.transform.position = posicion;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emision = ps.emission;
            emision.rateOverTime = 0f; // PollutionController la controla.
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        static void Aparecer(ParticleSystem ps)
        {
            var tam = ps.sizeOverLifetime;
            tam.enabled = true;
            var curva = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.08f, 1f), new Keyframe(0.9f, 1f), new Keyframe(1f, 0f));
            tam.size = new ParticleSystem.MinMaxCurve(1f, curva);
        }

        static void Desvanecer(ParticleSystem ps)
        {
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            color.color = g;
        }

        static Gradient Degradado(params Color[] colores)
        {
            var g = new Gradient { mode = GradientMode.Fixed };
            var claves = new GradientColorKey[colores.Length];
            for (int i = 0; i < colores.Length; i++) claves[i] = new GradientColorKey(colores[i], (i + 1f) / colores.Length);
            g.SetKeys(claves, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        /// <summary>Material de partículas con el Lit Shader Graph SG_Particulas (transparente, doble cara).</summary>
        internal static Material MaterialParticula(string nombre, Texture textura, float emision = 0f, Color? tinte = null)
        {
            string ruta = $"{CarpetaMateriales}/{nombre}.mat";
            var shader = Grafo("SG_Particulas");
            var existente = AssetDatabase.LoadAssetAtPath<Material>(ruta);
            if (existente != null && existente.shader == shader) return existente;

            var m = Asegurar(existente, shader);
            m.SetColor("_ColorBase", tinte ?? Color.white);
            m.SetTexture("_TexturaBase", textura);
            m.SetFloat("_Emision", emision);
            m.enableInstancing = true;
            return Guardar(m, ruta, nombre, existente == null);
        }

        // ───────────────────────────── UI: pantalla principal ─────────────────────────────

        static void ConstruirUIPrincipal(PollutionController contaminacion, DayNightController diaNoche, CameraViewController camaras)
        {
            var canvas = Lienzo("Canvas_PantallaPrincipal", 0, out var area);
            var ui = canvas.gameObject.AddComponent<PantallaPrincipalController>();
            Fijar(ui, "contaminacion", contaminacion);
            Fijar(ui, "diaNoche", diaNoche);
            Fijar(ui, "camaras", camaras);

            // Botón Menú (arriba a la izquierda).
            var menu = Boton("Boton Menu", area, "‹  Menú", FondoPanel, TextoClaro, 30, out _);
            Anclar((RectTransform)menu.transform, new Vector2(0f, 1f), new Vector2(30f, -30f), new Vector2(200f, 90f));
            Fijar(ui, "botonMenu", menu);

            // Banner guía (arriba al centro).
            var banner = Panel("Banner Guia", area, FondoPanel);
            Anclar(banner.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(1060f, 130f));
            banner.gameObject.AddComponent<CanvasGroup>();
            var bannerFade = banner.gameObject.AddComponent<PanelDesvanecible>();
            Fijar(bannerFade, "visibleAlInicio", true);
            var acento = Panel("Acento", banner.transform, Acento);
            var ar = acento.rectTransform;
            ar.anchorMin = new Vector2(0f, 0f); ar.anchorMax = new Vector2(0f, 1f); ar.pivot = new Vector2(0f, 0.5f);
            ar.offsetMin = new Vector2(18f, 22f); ar.offsetMax = new Vector2(26f, -22f);
            var textoBanner = Texto("Texto", banner.transform, TextosCenote.BannerGuia, 27, TextoClaro, TextAnchor.MiddleLeft);
            Estirar(textoBanner.rectTransform, 48, 100, 14, 14);
            var cerrarBanner = Boton("Cerrar", banner.transform, "×", new Color(1f, 1f, 1f, 0.08f), TextoClaro, 40, out _);
            Anclar((RectTransform)cerrarBanner.transform, new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(70f, 70f));
            Fijar(ui, "textoBanner", textoBanner);
            Fijar(ui, "banner", bannerFade);
            Fijar(ui, "botonCerrarBanner", cerrarBanner);

            // Columna de controles (derecha).
            var dn = BotonConRotulo("Boton DiaNoche", area, "ILUMINACIÓN", "Día", out var textoDN);
            Anclar((RectTransform)dn.transform, new Vector2(1f, 1f), new Vector2(-30f, -30f), new Vector2(330f, 110f));
            var cam = BotonConRotulo("Boton Camara", area, "CÁMARA  ›", "Vista general", out var textoCam);
            Anclar((RectTransform)cam.transform, new Vector2(1f, 1f), new Vector2(-30f, -155f), new Vector2(330f, 110f));
            var info = Boton("Boton Info", area, "i  Información", Acento, TextoOscuro, 30, out _);
            Anclar((RectTransform)info.transform, new Vector2(1f, 1f), new Vector2(-30f, -280f), new Vector2(330f, 90f));
            Fijar(ui, "botonDiaNoche", dn);
            Fijar(ui, "textoDiaNoche", textoDN);
            Fijar(ui, "botonCamara", cam);
            Fijar(ui, "textoCamara", textoCam);
            Fijar(ui, "botonInfo", info);

            // Descripción de la vista (abajo a la derecha).
            var desc = Panel("Descripcion Vista", area, new Color(0.03f, 0.11f, 0.13f, 0.6f));
            Anclar(desc.rectTransform, new Vector2(1f, 0f), new Vector2(-30f, 30f), new Vector2(640f, 100f));
            var textoDesc = Texto("Texto", desc.transform, "", 25, TextoSecundario, TextAnchor.MiddleLeft, FontStyle.Italic);
            Estirar(textoDesc.rectTransform, 28, 28, 10, 10);
            Fijar(ui, "textoDescripcionVista", textoDesc);

            // Tarjeta de contaminación (abajo a la izquierda).
            var tarjeta = Panel("Simulador Contaminacion", area, FondoPanel);
            Anclar(tarjeta.rectTransform, new Vector2(0f, 0f), new Vector2(30f, 30f), new Vector2(620f, 290f));
            var rotulo = Texto("Rotulo", tarjeta.transform, "SIMULADOR DE CONTAMINACIÓN DEL SUBSUELO", 20, TextoSecundario, TextAnchor.UpperLeft, FontStyle.Bold);
            Arriba(rotulo.rectTransform, 30, 30, 22, 30);
            var estado = Texto("Estado", tarjeta.transform, "Limpio", 42, Acento, TextAnchor.MiddleLeft, FontStyle.Bold);
            Arriba(estado.rectTransform, 30, 160, 58, 56);
            var porcentaje = Texto("Porcentaje", tarjeta.transform, "0%", 42, TextoClaro, TextAnchor.MiddleRight, FontStyle.Bold);
            Arriba(porcentaje.rectTransform, 400, 30, 58, 56);
            var slider = Deslizador("Deslizador", tarjeta.transform, Alerta);
            Arriba((RectTransform)slider.transform, 30, 30, 126, 50);
            var etiquetaLimpio = Texto("Etiqueta Limpio", tarjeta.transform, "Limpio", 18, TextoSecundario, TextAnchor.MiddleLeft);
            Arriba(etiquetaLimpio.rectTransform, 30, 300, 176, 22);
            var etiquetaSucio = Texto("Etiqueta Contaminado", tarjeta.transform, "Contaminado", 18, TextoSecundario, TextAnchor.MiddleRight);
            Arriba(etiquetaSucio.rectTransform, 300, 30, 176, 22);
            var alternar = Boton("Boton Contaminar", tarjeta.transform, "Contaminar subsuelo", Alerta, TextoOscuro, 28, out var textoAlternar);
            Arriba((RectTransform)alternar.transform, 30, 30, 206, 66);
            Fijar(ui, "sliderContaminacion", slider);
            Fijar(ui, "textoEstado", estado);
            Fijar(ui, "textoPorcentaje", porcentaje);
            Fijar(ui, "botonContaminar", alternar);
            Fijar(ui, "textoBotonContaminar", textoAlternar);

            // Panel informativo en su propio Canvas, por encima del HUD.
            Lienzo("Canvas_PanelInfo", 10, out var areaInfo);
            var modal = Modal("Panel Info", areaInfo, new Vector2(1400f, 900f), out var titulo, out var cuerpo, out var cerrar);
            Fijar(ui, "panelInfo", modal);
            Fijar(ui, "tituloInfo", titulo);
            Fijar(ui, "cuerpoInfo", cuerpo);
            Fijar(ui, "botonCerrarInfo", cerrar);
        }

        // Ancla un elemento a la parte superior de su padre con márgenes (izq, der, arriba) y altura fija.
        static void Arriba(RectTransform rt, float izq, float der, float arriba, float alto)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(izq, -arriba - alto);
            rt.offsetMax = new Vector2(-der, -arriba);
        }

        // ───────────────────────────── Escena: menú de inicio ─────────────────────────────

        static void ConstruirEscenaMenu()
        {
            var escena = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.05f, 0.08f);
            cam.cullingMask = 0; // Solo UI: no renderiza geometría.

            var canvas = Lienzo("Canvas_MenuInicio", 0, out var area);
            var menu = canvas.gameObject.AddComponent<MenuInicioController>();

            var fondo = Crear("Fondo", canvas.transform);
            fondo.SetAsFirstSibling();
            Estirar(fondo);
            fondo.gameObject.AddComponent<RawImage>().raycastTarget = false;
            fondo.gameObject.AddComponent<FondoDegradado>();

            var columna = Crear("Contenido", area);
            Anclar(columna, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(1300f, 900f));

            var gota = Panel("Gota", columna, Acento);
            gota.sprite = Circulo; gota.type = Image.Type.Simple;
            Anclar(gota.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(54f, 54f));

            var titulo = Texto("Titulo", columna, TextosCenote.Titulo, 128, TextoClaro, TextAnchor.MiddleCenter, FontStyle.Bold);
            Arriba(titulo.rectTransform, 0, 0, 80, 150);
            titulo.horizontalOverflow = HorizontalWrapMode.Overflow;
            var subtitulo = Texto("Subtitulo", columna, TextosCenote.Subtitulo, 38, Acento, TextAnchor.MiddleCenter);
            Arriba(subtitulo.rectTransform, 0, 0, 235, 56);

            var linea = Panel("Linea", columna, new Color(1f, 1f, 1f, 0.2f), false);
            Anclar(linea.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -315f), new Vector2(180f, 3f));

            var bloqueFrase = Crear("Frase", columna);
            Arriba(bloqueFrase, 80, 80, 345, 150);
            var grupo = bloqueFrase.gameObject.AddComponent<CanvasGroup>();
            grupo.alpha = 0f;
            var frase = Texto("Texto", bloqueFrase, TextosCenote.FrasesImpacto[0], 36, TextoClaro, TextAnchor.MiddleCenter, FontStyle.Italic);
            Estirar(frase.rectTransform);

            var explorar = Boton("Boton Explorar", columna, "Explorar el cenote", Acento, TextoOscuro, 36, out _);
            Anclar((RectTransform)explorar.transform, new Vector2(0.5f, 1f), new Vector2(0f, -540f), new Vector2(560f, 110f));
            var acerca = Boton("Boton Acerca", columna, "¿Por qué importa?", FondoPanel, TextoClaro, 32, out _);
            Anclar((RectTransform)acerca.transform, new Vector2(0.5f, 1f), new Vector2(0f, -670f), new Vector2(560f, 96f));
            var salir = Boton("Boton Salir", columna, "Salir", new Color(1f, 1f, 1f, 0f), TextoSecundario, 28, out _);
            Anclar((RectTransform)salir.transform, new Vector2(0.5f, 1f), new Vector2(0f, -786f), new Vector2(300f, 80f));

            Lienzo("Canvas_PanelAcerca", 10, out var areaAcerca);
            var modal = Modal("Panel Acerca", areaAcerca, new Vector2(1400f, 900f), out var tituloAcerca, out var cuerpo, out var cerrar);
            tituloAcerca.text = "¿Por qué importa el agua subterránea?";

            Fijar(menu, "titulo", titulo);
            Fijar(menu, "subtitulo", subtitulo);
            Fijar(menu, "frase", frase);
            Fijar(menu, "grupoFrase", grupo);
            Fijar(menu, "botonExplorar", explorar);
            Fijar(menu, "botonAcerca", acerca);
            Fijar(menu, "botonSalir", salir);
            Fijar(menu, "panelAcerca", modal);
            Fijar(menu, "botonCerrarAcerca", cerrar);
            Fijar(menu, "cuerpoAcerca", cuerpo);

            SistemaDeEventos();
            EditorSceneManager.SaveScene(escena, EscenaMenu);
        }

        // ───────────────────────────── Utilidades ─────────────────────────────

        static void Fijar(Object destino, string campo, Object valor)
        {
            var so = new SerializedObject(destino);
            var p = so.FindProperty(campo);
            if (p == null) { Debug.LogWarning($"[Cenote Vivo] Campo '{campo}' no existe en {destino.GetType().Name}."); return; }
            p.objectReferenceValue = valor;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Fijar(Object destino, string campo, float valor)
        {
            var so = new SerializedObject(destino);
            so.FindProperty(campo).floatValue = valor;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Fijar(Object destino, string campo, bool valor)
        {
            var so = new SerializedObject(destino);
            so.FindProperty(campo).boolValue = valor;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
