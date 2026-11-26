using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Vuforia;
using Image = UnityEngine.UI.Image;
using CenoteVivo.AR;
using CenoteVivo.UI;
using static CenoteVivo.EditorTools.UIFabrica;
using static CenoteVivo.EditorTools.CenoteVivoBuilder;

namespace CenoteVivo.EditorTools
{
    /// <summary>
    /// Convierte la escena del cenote en la escena de realidad aumentada (Vuforia):
    ///  - todo el cenote (modelo, selva recortada, partículas, faroles) cuelga de una miniatura "[CenoteAR]";
    ///  - cámara AR de Vuforia (con rastreo de dispositivo para Ground Plane) y buscador de superficies;
    ///  - marcador impreso de respaldo para teléfonos sin Ground Plane;
    ///  - etiquetas informativas, foto y la interfaz (contaminación, día/noche, información y menú).
    /// Lo llama CenoteVivoBuilder al construir la escena CenoteAR.
    /// </summary>
    static class CenoteVivoARBuilder
    {
        const string CarpetaAR = "Assets/CenoteVivo/AR";
        const string RutaMarcador = CarpetaAR + "/Marcador_CenoteVivo.png";
        const string CarpetaMateriales = "Assets/CenoteVivo/Materials";
        /// <summary>Diámetro de la miniatura recién colocada, en metros.</summary>
        const float DiametroMiniatura = 0.35f;
        /// <summary>Ancho del marcador impreso, en metros (imprimir a 20 cm de ancho).</summary>
        const float AnchoMarcador = 0.20f;

        public struct Datos
        {
            public Transform modelo;
            public GameObject sistemas;
            public Camera camaraOriginal;
            public Light sol, luna;
            public DayNightController diaNoche;
            public PollutionController contaminacion;
            public CorteTransversal corte;
            public Bounds limites, limitesAgua;
            public float nivelAgua, radioAgua, alturaSuelo;
        }

        public static void Montar(Datos d)
        {
            // ── Ajustes de render para teléfonos modestos ──
            RenderSettings.fog = false;                 // la niebla en metros no tiene sentido en una miniatura
            d.sol.shadows = LightShadows.None;          // sin mapa de sombras: el mayor ahorro en GPU de gama baja
            d.luna.shadows = LightShadows.None;
            if (d.camaraOriginal != null) Object.DestroyImmediate(d.camaraOriginal.gameObject);

            // ── Miniatura: [CenoteAR] (se mueve/gira/escala) › Espacio Cenote (mundo original) › contenido ──
            var contenido = new GameObject("[CenoteAR]").transform;
            var espacio = new GameObject("Espacio Cenote").transform;
            espacio.SetParent(contenido, false);

            var escena = d.modelo.gameObject.scene;
            var mover = new List<Transform> { d.modelo };
            foreach (var raiz in escena.GetRootGameObjects())
                if (raiz.name == "[Selva]" || raiz.name == "[Contaminacion]" || raiz.name == "[Noche]")
                    mover.Add(raiz.transform);
            foreach (var t in mover) t.SetParent(espacio, true);

            // La base del bloque de tierra queda en el origen: la maqueta se apoya sobre la mesa (o el marcador).
            var baseMaqueta = new Vector3(d.limites.center.x, SelvaGenerador.FondoTerreno, d.limites.center.z);
            espacio.localPosition = -baseMaqueta;

            float radio = Mathf.Max(d.limites.extents.x, d.limites.extents.z);
            float escalaBase = DiametroMiniatura / (2f * radio * SelvaGenerador.RadioAR);
            contenido.localScale = Vector3.one * escalaBase;

            PrepararParaMover(contenido);
            var luces = contenido.GetComponentsInChildren<Light>(true).Where(l => l.type == LightType.Point).ToArray();

            // ── Puntos que señalan las etiquetas (en el espacio del cenote) ──
            var puntos = CrearPuntosEtiquetas(espacio, d, radio);

            // ── Cámara AR de Vuforia (igual que en el proyecto ar-vuforia) + rastreo de dispositivo ──
            var camara = CrearCamaraAR();

            // ── Superficie (Ground Plane) ──
            var indicador = CrearIndicador();
            var buscadorGo = new GameObject("Buscador de superficie");
            var buscador = buscadorGo.AddComponent<PlaneFinderBehaviour>();
            buscador.PlaneIndicator = indicador;
            buscadorGo.SetActive(false); // lo activa ColocadorAR solo si el teléfono admite Ground Plane

            // ── Sistemas AR ──
            var espacioAR = d.sistemas.AddComponent<EspacioCenoteAR>();
            Fijar(espacioAR, "espacioCenote", espacio);
            Fijar(espacioAR, "corte", d.corte);
            var so = new SerializedObject(espacioAR);
            var listaLuces = so.FindProperty("lucesPuntuales");
            listaLuces.arraySize = luces.Length;
            for (int i = 0; i < luces.Length; i++) listaLuces.GetArrayElementAtIndex(i).objectReferenceValue = luces[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            var colocador = d.sistemas.AddComponent<ColocadorAR>();
            Fijar(colocador, "contenido", contenido);
            Fijar(colocador, "escalaBase", escalaBase);
            Fijar(colocador, "buscadorPlanos", buscador);
            Fijar(colocador, "indicador", indicador);
            Fijar(colocador, "imagenMarcador", PrepararMarcador());
            Fijar(colocador, "anchoMarcador", AnchoMarcador);

            var etiquetas = d.sistemas.AddComponent<EtiquetasAR>();
            var foto = d.sistemas.AddComponent<FotoAR>();

            ConstruirUI(d, colocador, etiquetas, foto, puntos);
            SistemaDeEventos();

            // Al añadir componentes de Vuforia en el editor, Vuforia crea un objeto auxiliar de tiempo de
            // ejecución ("New Game Object"); no debe quedar guardado en la escena.
            foreach (var raiz in escena.GetRootGameObjects())
                if (raiz.name == "New Game Object") Object.DestroyImmediate(raiz);
        }

        // Los objetos del cenote ya no son estáticos (se mueven con la miniatura) y se aligeran para gama baja.
        static void PrepararParaMover(Transform contenido)
        {
            foreach (var t in contenido.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);

            foreach (var r in contenido.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }

            // Las partículas siguen a la miniatura: simulación local y escala por jerarquía.
            foreach (var ps in contenido.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.maxParticles = Mathf.Min(main.maxParticles, 150);
                var vel = ps.velocityOverLifetime;
                if (vel.enabled) vel.space = ParticleSystemSimulationSpace.Local;
            }
        }

        static Transform[] CrearPuntosEtiquetas(Transform espacio, Datos d, float radio)
        {
            var centroAgua = new Vector3(d.limitesAgua.center.x, d.nivelAgua, d.limitesAgua.center.z);
            float pared = Mathf.Max(0.5f, d.alturaSuelo - d.nivelAgua);
            Vector3 Dir(float x, float z) => new Vector3(x, 0f, z).normalized;

            // La miniatura se coloca con su lado -Z hacia el usuario: los puntos van en la pared del fondo
            // (+Z), que es la que se ve al asomarse al cenote.
            var posiciones = new[]
            {
                centroAgua + Dir(0.55f, 0.85f) * d.radioAgua * 1.05f + Vector3.up * pared * 0.5f,   // Roca caliza
                centroAgua + Vector3.up * pared * 0.04f,                                              // Agua
                centroAgua + Dir(-0.6f, 0.8f) * d.radioAgua * 1.0f + Vector3.up * pared * 0.85f,    // Raíces
                new Vector3(d.limites.center.x, d.alturaSuelo, d.limites.center.z)
                    + Dir(0.8f, -0.6f) * radio * 1.2f + Vector3.up * radio * 0.02f,                    // Contaminación (suelo)
            };
            var raiz = new GameObject("Puntos de etiquetas").transform;
            raiz.SetParent(espacio, false);
            var puntos = new Transform[posiciones.Length];
            for (int i = 0; i < posiciones.Length; i++)
            {
                puntos[i] = new GameObject("Punto - " + TextosCenote.EtiquetasAR[i].titulo).transform;
                puntos[i].SetParent(raiz, false);
                puntos[i].localPosition = posiciones[i]; // el espacio local es el mundo original
            }
            return puntos;
        }

        static Camera CrearCamaraAR()
        {
            var go = new GameObject("ARCamera");
            go.tag = "MainCamera";
            var camara = go.AddComponent<Camera>();
            camara.clearFlags = CameraClearFlags.SolidColor;
            camara.backgroundColor = Color.black;
            camara.nearClipPlane = 0.02f;
            camara.farClipPlane = 30f;
            go.AddComponent<AudioListener>();

            var urp = go.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = false;
            urp.renderShadows = false;
            urp.antialiasing = AntialiasingMode.None;

            var vuforia = go.AddComponent<VuforiaBehaviour>();
            var so = new SerializedObject(vuforia);
            var centro = so.FindProperty("mWorldCenterMode");
            if (centro != null) { centro.intValue = (int)WorldCenterMode.DEVICE; so.ApplyModifiedPropertiesWithoutUndo(); }
            go.AddComponent<DefaultInitializationErrorHandler>();
            // Rastreo del dispositivo: lo necesita Ground Plane (y mejora el marcador cuando sale de cuadro).
            if (go.GetComponent<DevicePoseBehaviour>() == null) go.AddComponent<DevicePoseBehaviour>();
            return camara;
        }

        // Aro turquesa que marca dónde se colocará el cenote (12 cm de diámetro, sin texturas).
        static GameObject CrearIndicador()
        {
            const int segmentos = 48;
            const float rIn = 0.05f, rOut = 0.06f;
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i <= segmentos; i++)
            {
                float a = i / (float)segmentos * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(dir * rIn + Vector3.up * 0.002f);
                v.Add(dir * rOut + Vector3.up * 0.002f);
                if (i == segmentos) break;
                int k = i * 2;
                t.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
            }
            // Punto central
            int c = v.Count;
            v.Add(Vector3.up * 0.002f);
            for (int i = 0; i < 16; i++)
            {
                float a0 = i / 16f * Mathf.PI * 2f, a1 = (i + 1) / 16f * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * 0.008f + Vector3.up * 0.002f);
                v.Add(new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * 0.008f + Vector3.up * 0.002f);
                t.AddRange(new[] { c, v.Count - 1, v.Count - 2 });
            }
            var malla = new Mesh { name = "IndicadorAR" };
            malla.SetVertices(v);
            malla.SetTriangles(t, 0);
            malla.RecalculateNormals();
            malla.RecalculateBounds();
            AsegurarCarpeta();
            string rutaMalla = CarpetaAR + "/IndicadorAR.asset";
            AssetDatabase.DeleteAsset(rutaMalla);
            AssetDatabase.CreateAsset(malla, rutaMalla);

            string rutaMat = CarpetaMateriales + "/CV_IndicadorAR.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(rutaMat);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(mat, rutaMat);
            }
            mat.SetColor("_BaseColor", new Color(0.36f, 0.95f, 0.86f));
            EditorUtility.SetDirty(mat);

            var go = new GameObject("Indicador de superficie");
            go.AddComponent<MeshFilter>().sharedMesh = malla;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            return go;
        }

        // Marcador impreso: una captura de la app (mucho detalle = buen rastreo de Vuforia).
        static Texture2D PrepararMarcador()
        {
            AsegurarCarpeta();
            if (!File.Exists(RutaMarcador))
            {
                var origen = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Documentacion", "capturas", "02_dia_vista_general.png");
                File.Copy(origen, RutaMarcador);
                AssetDatabase.ImportAsset(RutaMarcador);
            }
            var imp = (TextureImporter)AssetImporter.GetAtPath(RutaMarcador);
            imp.textureType = TextureImporterType.Default;
            imp.isReadable = true;                     // Vuforia lee los píxeles para crear el Image Target
            imp.mipmapEnabled = false;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.alphaSource = TextureImporterAlphaSource.None;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            var plataforma = imp.GetDefaultPlatformTextureSettings();
            plataforma.format = TextureImporterFormat.RGB24;
            imp.SetPlatformTextureSettings(plataforma);
            var android = imp.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.format = TextureImporterFormat.RGB24;
            imp.SetPlatformTextureSettings(android);
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(RutaMarcador);
        }

        static void AsegurarCarpeta()
        {
            if (!AssetDatabase.IsValidFolder(CarpetaAR)) AssetDatabase.CreateFolder("Assets/CenoteVivo", "AR");
        }

        // ───────────────────────────── Interfaz ─────────────────────────────

        static void ConstruirUI(Datos d, ColocadorAR colocador, EtiquetasAR etiquetas, FotoAR foto, Transform[] puntos)
        {
            var canvas = Lienzo("Canvas_PantallaPrincipal", 0, out var area);

            // Lo común con la escena 3D (contaminación, día/noche, información, menú).
            var ui = canvas.gameObject.AddComponent<PantallaPrincipalController>();
            Fijar(ui, "contaminacion", d.contaminacion);
            Fijar(ui, "diaNoche", d.diaNoche);
            Fijar(ui, "manejarBotonAtras", false);

            var ar = canvas.gameObject.AddComponent<PantallaARController>();
            Fijar(ar, "colocador", colocador);
            Fijar(ar, "etiquetas", etiquetas);
            Fijar(ar, "foto", foto);
            Fijar(ar, "corte", d.corte);

            // Menú (arriba a la izquierda).
            var menu = Boton("Boton Menu", area, "‹  Menú", FondoPanel, TextoClaro, 30, out _);
            Anclar((RectTransform)menu.transform, new Vector2(0f, 1f), new Vector2(30f, -30f), new Vector2(200f, 90f));
            Fijar(ui, "botonMenu", menu);

            // Guía paso a paso (arriba al centro).
            var guia = Panel("Guia AR", area, FondoPanel);
            Anclar(guia.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(1000f, 120f));
            guia.gameObject.AddComponent<CanvasGroup>();
            var guiaFade = guia.gameObject.AddComponent<PanelDesvanecible>();
            Fijar(guiaFade, "visibleAlInicio", true);
            var acento = Panel("Acento", guia.transform, Acento);
            var acr = acento.rectTransform;
            acr.anchorMin = new Vector2(0f, 0f); acr.anchorMax = new Vector2(0f, 1f); acr.pivot = new Vector2(0f, 0.5f);
            acr.offsetMin = new Vector2(18f, 22f); acr.offsetMax = new Vector2(26f, -22f);
            var textoGuia = Texto("Texto", guia.transform, "Iniciando la cámara…", 28, TextoClaro, TextAnchor.MiddleLeft);
            Estirar(textoGuia.rectTransform, 48, 28, 12, 12);
            Fijar(ar, "guia", guiaFade);
            Fijar(ar, "textoGuia", textoGuia);

            // Columna derecha.
            var dn = BotonConRotulo("Boton DiaNoche", area, "ILUMINACIÓN", "Día", out var textoDN);
            Anclar((RectTransform)dn.transform, new Vector2(1f, 1f), new Vector2(-30f, -30f), new Vector2(330f, 110f));
            Fijar(ui, "botonDiaNoche", dn);
            Fijar(ui, "textoDiaNoche", textoDN);

            var subsuelo = BotonConRotulo("Boton Subsuelo", area, "CORTE", "Ver subsuelo", out var textoSub);
            Anclar((RectTransform)subsuelo.transform, new Vector2(1f, 1f), new Vector2(-30f, -155f), new Vector2(330f, 110f));
            Fijar(ar, "botonSubsuelo", subsuelo);
            Fijar(ar, "textoSubsuelo", textoSub);

            var botonEtiquetas = BotonConRotulo("Boton Etiquetas", area, "ETIQUETAS", "Ocultar etiquetas", out var textoEtq);
            textoEtq.fontSize = 30;
            Anclar((RectTransform)botonEtiquetas.transform, new Vector2(1f, 1f), new Vector2(-30f, -280f), new Vector2(330f, 110f));
            Fijar(ar, "botonEtiquetas", botonEtiquetas);
            Fijar(ar, "textoEtiquetas", textoEtq);

            var info = Boton("Boton Info", area, "i  Información", Acento, TextoOscuro, 30, out _);
            Anclar((RectTransform)info.transform, new Vector2(1f, 1f), new Vector2(-30f, -405f), new Vector2(330f, 90f));
            Fijar(ui, "botonInfo", info);

            // Botón de foto (redondo, a la derecha).
            var botonFoto = Boton("Boton Foto", area, "", TextoClaro, TextoOscuro, 30, out var textoFoto);
            var bf = (Image)botonFoto.targetGraphic;
            bf.sprite = Circulo; bf.type = Image.Type.Simple;
            Anclar((RectTransform)botonFoto.transform, new Vector2(1f, 0f), new Vector2(-120f, 160f), new Vector2(150f, 150f));
            var lente = Panel("Lente", botonFoto.transform, Acento);
            lente.sprite = Circulo; lente.type = Image.Type.Simple; lente.raycastTarget = false;
            Anclar(lente.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(104f, 104f));
            textoFoto.transform.SetAsLastSibling();
            textoFoto.text = "FOTO";
            textoFoto.fontSize = 26;
            textoFoto.color = TextoOscuro;
            Fijar(ar, "botonFoto", botonFoto);

            // Recolocar (abajo a la derecha) y cambio de método (abajo al centro).
            var recolocar = Boton("Boton Recolocar", area, "Recolocar", FondoPanel, TextoClaro, 30, out _);
            Anclar((RectTransform)recolocar.transform, new Vector2(1f, 0f), new Vector2(-30f, 30f), new Vector2(330f, 90f));
            Fijar(ar, "botonRecolocar", recolocar);

            var marcador = Boton("Boton Marcador", area, "Usar marcador impreso", Acento, TextoOscuro, 28, out var textoMarcador);
            Anclar((RectTransform)marcador.transform, new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(460f, 90f));
            marcador.gameObject.SetActive(false);
            Fijar(ar, "botonMarcador", marcador);
            Fijar(ar, "textoMarcador", textoMarcador);

            // Tarjeta de contaminación (abajo a la izquierda), igual que en la escena 3D.
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

            // Solo con el cenote en pantalla.
            var so = new SerializedObject(ar);
            var solo = so.FindProperty("soloConCenote");
            var objetos = new GameObject[] { tarjeta.gameObject, subsuelo.gameObject, botonEtiquetas.gameObject, botonFoto.gameObject, recolocar.gameObject };
            solo.arraySize = objetos.Length;
            for (int i = 0; i < objetos.Length; i++) solo.GetArrayElementAtIndex(i).objectReferenceValue = objetos[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            ConstruirEtiquetas(etiquetas, colocador, puntos);

            // Panel informativo (el mismo de la escena 3D).
            Lienzo("Canvas_PanelInfo", 10, out var areaInfo);
            var modal = Modal("Panel Info", areaInfo, new Vector2(1400f, 900f), out var titulo, out var cuerpo, out var cerrar);
            Fijar(ui, "panelInfo", modal);
            Fijar(ui, "tituloInfo", titulo);
            Fijar(ui, "cuerpoInfo", cuerpo);
            Fijar(ui, "botonCerrarInfo", cerrar);
            Fijar(ar, "panelInfo", modal);

            ConstruirVistaPrevia(ar);
            Fijar(foto, "marcoFoto", ConstruirMarcoFoto());
        }

        static void ConstruirEtiquetas(EtiquetasAR etiquetas, ColocadorAR colocador, Transform[] puntos)
        {
            Lienzo("Canvas_Etiquetas", 5, out var area);
            var contenedor = Crear("Etiquetas", area);
            Estirar(contenedor);
            Fijar(etiquetas, "colocador", colocador);
            Fijar(etiquetas, "contenedor", contenedor);

            var so = new SerializedObject(etiquetas);
            var lista = so.FindProperty("etiquetas");
            lista.arraySize = puntos.Length;
            for (int i = 0; i < puntos.Length; i++)
            {
                var (tituloEtq, texto) = TextosCenote.EtiquetasAR[i];
                var boton = Boton("Etiqueta - " + tituloEtq, contenedor, tituloEtq, FondoPanel, TextoClaro, 28, out var textoBoton);
                var rt = (RectTransform)boton.transform;
                // El pivote abajo al centro: la etiqueta "cuelga" sobre su punto, con un puntero debajo.
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(Mathf.Max(220f, tituloEtq.Length * 17f + 90f), 70f);
                Estirar(textoBoton.rectTransform, 46, 16, 6, 6);
                var punto = Panel("Indicador", boton.transform, Acento);
                punto.sprite = Circulo; punto.type = Image.Type.Simple; punto.raycastTarget = false;
                Anclar(punto.rectTransform, new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(18f, 18f));
                var pin = Panel("Puntero", boton.transform, Acento, false);
                pin.raycastTarget = false;
                var pr = pin.rectTransform;
                pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0f); pr.pivot = new Vector2(0.5f, 1f);
                pr.anchoredPosition = Vector2.zero; pr.sizeDelta = new Vector2(4f, 26f);
                var base_ = Panel("Base", pin.transform, Acento);
                base_.sprite = Circulo; base_.type = Image.Type.Simple; base_.raycastTarget = false;
                Anclar(base_.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, -7f), new Vector2(14f, 14f));
                // La etiqueta se ubica sobre el punto: se desplaza hacia arriba lo que mide el puntero.
                rt.pivot = new Vector2(0.5f, -26f / 70f - 0.1f);

                var e = lista.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("titulo").stringValue = tituloEtq;
                e.FindPropertyRelative("explicacion").stringValue = texto;
                e.FindPropertyRelative("punto").objectReferenceValue = puntos[i];
                e.FindPropertyRelative("boton").objectReferenceValue = boton;
                boton.gameObject.SetActive(false);
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            // Tarjeta con la explicación.
            Lienzo("Canvas_TarjetaEtiqueta", 12, out var areaTarjeta);
            var modal = Modal("Tarjeta Etiqueta", areaTarjeta, new Vector2(1100f, 560f), out var titulo, out var cuerpo, out var cerrar);
            cuerpo.fontSize = 34;
            Fijar(etiquetas, "tarjeta", modal);
            Fijar(etiquetas, "tituloTarjeta", titulo);
            Fijar(etiquetas, "textoTarjeta", cuerpo);
            Fijar(etiquetas, "cerrarTarjeta", cerrar);
        }

        static void ConstruirVistaPrevia(PantallaARController ar)
        {
            Lienzo("Canvas_FotoPrevia", 15, out var area);
            var velo = Panel("Vista Previa", area, new Color(0f, 0.02f, 0.03f, 0.75f), false);
            Estirar(velo.rectTransform);
            var grupo = velo.gameObject.AddComponent<CanvasGroup>();
            grupo.alpha = 0f; grupo.blocksRaycasts = false; grupo.interactable = false;
            var panel = velo.gameObject.AddComponent<PanelDesvanecible>();

            var tarjeta = Panel("Tarjeta", velo.transform, new Color(0.04f, 0.14f, 0.16f, 0.97f));
            Anclar(tarjeta.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 760f));
            var imagen = Crear("Foto", tarjeta.transform).gameObject.AddComponent<RawImage>();
            Anclar(imagen.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(880f, 495f));
            var texto = Texto("Mensaje", tarjeta.transform, "", 28, TextoClaro, TextAnchor.MiddleCenter);
            Arriba(texto.rectTransform, 40, 40, 550, 60);
            var compartir = Boton("Boton Compartir", tarjeta.transform, "Compartir", Acento, TextoOscuro, 30, out _);
            Anclar((RectTransform)compartir.transform, new Vector2(0.5f, 0f), new Vector2(-170f, 36f), new Vector2(300f, 90f));
            var cerrar = Boton("Boton Cerrar", tarjeta.transform, "Cerrar", FondoPanel, TextoClaro, 30, out _);
            Anclar((RectTransform)cerrar.transform, new Vector2(0.5f, 0f), new Vector2(170f, 36f), new Vector2(300f, 90f));

            Fijar(ar, "vistaPrevia", panel);
            Fijar(ar, "imagenPrevia", imagen);
            Fijar(ar, "textoPrevia", texto);
            Fijar(ar, "botonCompartir", compartir);
            Fijar(ar, "botonCerrarPrevia", cerrar);
        }

        // Marco que solo aparece en la foto: nombre y lema de la campaña.
        static GameObject ConstruirMarcoFoto()
        {
            var canvas = Lienzo("Canvas_MarcoFoto", 20, out var area);
            var franja = Panel("Franja", area, new Color(0.02f, 0.09f, 0.11f, 0.82f));
            Anclar(franja.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(1160f, 130f));
            var logo = Crear("Logo", franja.transform).gameObject.AddComponent<Image>();
            logo.sprite = LogoSprite();
            logo.preserveAspect = true;
            logo.raycastTarget = false;
            Anclar(logo.rectTransform, new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(110f, 110f));
            var nombre = Texto("Nombre", franja.transform, "CENOTE VIVO", 44, TextoClaro, TextAnchor.UpperLeft, FontStyle.Bold);
            Arriba(nombre.rectTransform, 145, 30, 16, 56);
            var lema = Texto("Lema", franja.transform, TextosCenote.Subtitulo, 32, Acento, TextAnchor.UpperLeft);
            Arriba(lema.rectTransform, 145, 30, 72, 44);
            canvas.gameObject.SetActive(false);
            return canvas.gameObject;
        }
    }
}
