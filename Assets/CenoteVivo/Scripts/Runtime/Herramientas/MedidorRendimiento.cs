using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace CenoteVivo.Herramientas
{
    /// <summary>
    /// Mide el rendimiento en las tres vistas de cámara de la app: 1) vista general (lejos),
    /// 2) corte del subsuelo y 3) dentro del cenote (nivel del agua).
    /// Registra cuadro a cuadro los contadores del Profiler y guarda un CSV y una captura por vista.
    /// </summary>
    public class MedidorRendimiento : MonoBehaviour
    {
        [SerializeField] string etiqueta = "medicion";
        [SerializeField] string carpetaSalida = "";
        [Tooltip("Segundos que se registra cada vista.")]
        [SerializeField] float segundosQuieta = 3f;
        [Tooltip("Tiempo de espera para la transición de cámara (se usa la mitad), sin medir.")]
        [SerializeField] float segundosAcercandose = 4f;

        public bool Terminado { get; private set; }
        public string Resumen { get; private set; }
        /// <summary>Momento que se está registrando ("" entre momentos), para cruzarlo con el Profiler.</summary>
        public string FaseActual { get; private set; } = "";

        /// <summary>Llamar justo después de AddComponent (antes de que corra Start).</summary>
        public void Configurar(string nombre, string carpeta)
        {
            etiqueta = nombre;
            carpetaSalida = carpeta;
        }

        class Contadores
        {
            readonly Dictionary<string, ProfilerRecorder> recorders = new Dictionary<string, ProfilerRecorder>();

            public static readonly (string clave, ProfilerCategory cat, string stat, double escala)[] Definiciones =
            {
                ("CPU PlayerLoop (ms)", ProfilerCategory.Internal, "PlayerLoop", 1e-6),
                ("CPU Main Thread (ms)", ProfilerCategory.Internal, "Main Thread", 1e-6),
                ("Render camara (ms)", ProfilerCategory.Render, "Inl_UniversalRenderPipeline.RenderSingleCameraInternal: Main Camera", 1e-6),
                ("Batches", ProfilerCategory.Render, "Batches Count", 1),
                ("SetPass Calls", ProfilerCategory.Render, "SetPass Calls Count", 1),
                ("Draw Calls", ProfilerCategory.Render, "Draw Calls Count", 1),
                ("Triangulos", ProfilerCategory.Render, "Triangles Count", 1),
                ("Vertices", ProfilerCategory.Render, "Vertices Count", 1),
                ("Shadow Casters", ProfilerCategory.Render, "Shadow Casters Count", 1),
                ("Memoria usada (MB)", ProfilerCategory.Memory, "Total Used Memory", 1.0 / (1024 * 1024)),
                ("Memoria graficos Gfx (MB)", ProfilerCategory.Memory, "Gfx Used Memory", 1.0 / (1024 * 1024)),
                ("Memoria GC (MB)", ProfilerCategory.Memory, "GC Used Memory", 1.0 / (1024 * 1024)),
                ("GC por cuadro (KB)", ProfilerCategory.Memory, "GC Allocated In Frame", 1.0 / 1024),
            };

            public Contadores()
            {
                foreach (var d in Definiciones)
                    recorders[d.clave] = ProfilerRecorder.StartNew(d.cat, d.stat);
            }

            // En el editor, los contadores de render se leen de las estadísticas de la vista Game
            // (ventana Stats), porque los del Profiler mezclan la vista Scene. En el teléfono se usan los del Profiler.
            static readonly System.Type Stats = System.Type.GetType("UnityEditor.UnityStats, UnityEditor");
            static readonly Dictionary<string, string> DesdeStats = new Dictionary<string, string>
            {
                { "Batches", "batches" }, { "SetPass Calls", "setPassCalls" }, { "Draw Calls", "drawCalls" },
                { "Triangulos", "triangles" }, { "Vertices", "vertices" }, { "Shadow Casters", "shadowCasters" },
            };

            public double Valor(string clave, double escala)
            {
                // Batches: el contador del Profiler (válido con el Profiler grabando); si no, la ventana Stats.
                var rec = recorders[clave];
                if (clave == "Batches" && rec.Valid && rec.LastValue > 0) return rec.LastValue * escala;
                if (Stats != null && DesdeStats.TryGetValue(clave, out var prop))
                {
                    var p = Stats.GetProperty(prop, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (p != null) return System.Convert.ToDouble(p.GetValue(null));
                }
                var r = recorders[clave];
                return r.Valid ? r.LastValue * escala : double.NaN;
            }

            public void Liberar()
            {
                foreach (var r in recorders.Values) r.Dispose();
            }
        }

        class Fase
        {
            public string nombre;
            public readonly Dictionary<string, List<double>> muestras = new Dictionary<string, List<double>>();
            public readonly List<double> frameMs = new List<double>();
            public readonly List<double> gpuMs = new List<double>();
            public Dictionary<string, (int objetos, long triangulos)> desglose;

            public void Agregar(string clave, double v)
            {
                if (double.IsNaN(v)) return;
                if (!muestras.TryGetValue(clave, out var l)) muestras[clave] = l = new List<double>();
                l.Add(v);
            }
        }

        IEnumerator Start()
        {
            var cam = Camera.main;
            var vistas = FindAnyObjectByType<CameraViewController>();
            yield return null;

            FrameTimingManager.CaptureFrameTimings();
            var contadores = new Contadores();
            var fases = new List<Fase>();
            var tiempos = new FrameTiming[1];

            // Mide una de las vistas de la app: primero espera a que termine la transición de cámara
            // (no se mide) y descarta los primeros cuadros para no contar pausas del editor.
            IEnumerator Medir(Fase f, int vista)
            {
                fases.Add(f);
                vistas.IrA(vista);
                for (float t = 0f; t < segundosAcercandose * 0.5f; t += Time.unscaledDeltaTime) yield return null;
                for (int i = 0; i < 30; i++) yield return null;

                FaseActual = f.nombre;
                for (float t = 0f; t < segundosQuieta; t += Time.unscaledDeltaTime)
                {
                    yield return null;
                    f.frameMs.Add(Time.unscaledDeltaTime * 1000.0);
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, tiempos) > 0 && tiempos[0].gpuFrameTime > 0)
                        f.gpuMs.Add(tiempos[0].gpuFrameTime);
                    foreach (var d in Contadores.Definiciones)
                        f.Agregar(d.clave, contadores.Valor(d.clave, d.escala));
                }
                FaseActual = "";
                f.desglose = Desglose();
                Capturar(cam, $"{etiqueta}_{f.nombre}");
            }

            // Las tres vistas de la app: lejos (general), corte del subsuelo y dentro del cenote.
            yield return Medir(new Fase { nombre = "1_vista_general" }, 0);
            yield return Medir(new Fase { nombre = "2_corte" }, 2);
            yield return Medir(new Fase { nombre = "3_dentro_cenote" }, 1);
            vistas.IrA(0);
            // Unos cuadros de margen para que el editor registre el fin de la última vista.
            for (int i = 0; i < 10; i++) yield return null;

            contadores.Liberar();
            Resumen = Escribir(fases);
            Terminado = true;
        }

        // Triángulos de las mallas visibles (cámara o sombras), agrupados por tipo de objeto.
        static Dictionary<string, (int objetos, long triangulos)> Desglose()
        {
            var r = new Dictionary<string, (int, long)>();
            foreach (var mr in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!mr.enabled || !mr.isVisible) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                long tris = 0;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) tris += (long)mf.sharedMesh.GetIndexCount(s) / 3;
                string n = mr.name;
                string grupo = n.StartsWith("Cenote_LOD") ? "Modelo cenote " + n.Substring(7)
                             : n.Contains("Agua") ? "Agua"
                             : n.Contains("_") && !n.StartsWith("Cenote") ? "Selva " + n.Split('_')[0]
                             : n.StartsWith("Terreno") ? "Selva Terreno" : "Otros";
                r.TryGetValue(grupo, out var a);
                r[grupo] = (a.Item1 + 1, a.Item2 + tris);
            }
            return r;
        }

        string Carpeta => string.IsNullOrEmpty(carpetaSalida) ? Application.persistentDataPath : carpetaSalida;

        void Capturar(Camera cam, string nombre)
        {
            var rt = new RenderTexture(1280, 720, 24);
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var anterior = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            cam.targetTexture = anterior;
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(Carpeta, $"med_{nombre}.png"), tex.EncodeToPNG());
            Destroy(rt);
            Destroy(tex);
        }

        string Escribir(List<Fase> fases)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("metrica,fase,promedio,mediana,minimo,maximo,muestras");
            void Linea(string metrica, string fase, List<double> l)
            {
                if (l == null || l.Count == 0) { sb.AppendLine($"{metrica},{fase},,,,,0"); return; }
                double s = 0, mn = double.MaxValue, mx = double.MinValue;
                foreach (var v in l) { s += v; mn = System.Math.Min(mn, v); mx = System.Math.Max(mx, v); }
                var orden = new List<double>(l);
                orden.Sort();
                double mediana = orden[orden.Count / 2];
                sb.AppendLine(string.Format(ci, "{0},{1},{2:0.###},{3:0.###},{4:0.###},{5:0.###},{6}", metrica, fase, s / l.Count, mediana, mn, mx, l.Count));
            }
            foreach (var f in fases)
            {
                Linea("Tiempo de cuadro (ms)", f.nombre, f.frameMs);
                var fps = f.frameMs.ConvertAll(ms => ms > 0 ? 1000.0 / ms : 0);
                Linea("FPS", f.nombre, fps);
                Linea("GPU (ms)", f.nombre, f.gpuMs);
                foreach (var d in Contadores.Definiciones)
                    Linea(d.clave, f.nombre, f.muestras.TryGetValue(d.clave, out var l) ? l : null);
            }
            sb.AppendLine();
            sb.AppendLine("desglose,fase,grupo,objetos_visibles,triangulos_malla");
            foreach (var f in fases)
                if (f.desglose != null)
                    foreach (var kv in f.desglose)
                        sb.AppendLine($"desglose,{f.nombre},{kv.Key},{kv.Value.objetos},{kv.Value.triangulos}");
            var ruta = Path.Combine(Carpeta, $"medicion_{etiqueta}.csv");
            File.WriteAllText(ruta, sb.ToString());
            return ruta;
        }
    }
}
