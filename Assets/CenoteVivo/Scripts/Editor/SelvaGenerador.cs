using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

namespace CenoteVivo.EditorTools
{
    /// <summary>
    /// Genera el entorno de selva alrededor del cenote con mallas low-poly procedurales:
    /// terreno con colinas, claro de pasto, árboles, palmas, una ceiba, arbustos con flores,
    /// helechos, lianas y raíces que cuelgan por las paredes, línea de selva lejana,
    /// faroles nocturnos dentro del cenote y luciérnagas.
    /// Todo se combina por sectores y categoría para mantener pocas draw calls en móvil.
    /// </summary>
    static class SelvaGenerador
    {
        const string CarpetaMallas = "Assets/CenoteVivo/Generado/Selva";
        const string CarpetaMateriales = "Assets/CenoteVivo/Materials";

        public struct Resultado
        {
            public float alturaSuelo;
            public Material materialSuelo;
        }

        // ───────────────────────── Malla acumulativa (flat shading + color por vértice) ─────────────────────────

        class Malla
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Color32> c = new List<Color32>();
            public readonly List<int> t = new List<int>();

            // Triángulo orientado: la cara frontal mira hacia "fuera".
            public void Tri(Vector3 a, Vector3 b, Vector3 d, Vector3 fuera, Color32 ca, Color32 cb, Color32 cd)
            {
                var normal = Vector3.Cross(b - a, d - a);
                if (Vector3.Dot(normal, fuera) < 0f) { (b, d) = (d, b); (cb, cd) = (cd, cb); normal = -normal; }
                normal.Normalize();
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(d);
                n.Add(normal); n.Add(normal); n.Add(normal);
                c.Add(ca); c.Add(cb); c.Add(cd);
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }

            public void Tri(Vector3 a, Vector3 b, Vector3 d, Vector3 fuera, Color32 col) => Tri(a, b, d, fuera, col, col, col);

            public Mesh Construir(string nombre)
            {
                var m = new Mesh { name = nombre };
                if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetColors(c);
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        // ───────────────────────── Estado del generador ─────────────────────────

        static Random rng;
        static float R;                 // radio del bloque del cenote
        static Vector3 centro;          // centro del bloque (xz)
        static Vector3 centroPozo;      // centro del agua (xz)
        static float alturaSuelo;       // altura real de la superficie del bloque
        static float nivelAgua;
        static float fondo;             // y inferior del bloque
        static float[] radioBorde;      // radio de la boca del pozo por ángulo (desde centroPozo)
        const int Angulos = 72;
        static Dictionary<string, Malla> bloques;
        static List<Vector4> ocupados;  // xz + radio (w) para evitar encimar plantas grandes

        static float Rand() => (float)rng.NextDouble();
        static float Rango(float a, float b) => a + (b - a) * Rand();
        static Color32 Var(Color32 c, float cuanto)
        {
            float f = 1f + (Rand() - 0.5f) * 2f * cuanto;
            return new Color32((byte)Mathf.Clamp(c.r * f, 0, 255), (byte)Mathf.Clamp(c.g * f, 0, 255), (byte)Mathf.Clamp(c.b * f, 0, 255), c.a);
        }
        static Color32 Peso(Color32 c, float peso) => new Color32(c.r, c.g, c.b, (byte)(Mathf.Clamp01(peso) * 255f));
        static Color32 Elegir(params Color32[] opciones) => opciones[rng.Next(opciones.Length)];

        // Paleta
        static readonly Color32[] Hojas = { new Color32(38, 88, 34, 0), new Color32(52, 108, 40, 0), new Color32(68, 122, 44, 0), new Color32(30, 74, 40, 0), new Color32(82, 128, 48, 0) };
        static readonly Color32[] HojasPalma = { new Color32(74, 118, 48, 0), new Color32(92, 134, 54, 0), new Color32(64, 104, 42, 0) };
        static readonly Color32[] Pastos = { new Color32(92, 142, 52, 0), new Color32(112, 156, 60, 0), new Color32(76, 126, 46, 0), new Color32(128, 150, 64, 0) };
        static readonly Color32[] Flores = { new Color32(214, 60, 126, 0), new Color32(236, 136, 40, 0), new Color32(240, 232, 220, 0), new Color32(200, 40, 50, 0) };
        static readonly Color32 Tronco = new Color32(92, 72, 52, 0);
        static readonly Color32 TroncoPalma = new Color32(122, 104, 80, 0);
        static readonly Color32 TroncoCeiba = new Color32(150, 146, 128, 0);

        // ───────────────────────── Entrada principal ─────────────────────────

        public static Resultado Generar(Transform modelo, Bounds limites, Bounds agua, float nivelDelAgua, DayNightController diaNoche)
        {
            rng = new Random(20261022);
            R = Mathf.Max(limites.extents.x, limites.extents.z);
            centro = new Vector3(limites.center.x, 0f, limites.center.z);
            centroPozo = new Vector3(agua.center.x, 0f, agua.center.z);
            nivelAgua = nivelDelAgua;
            fondo = limites.min.y;
            bloques = new Dictionary<string, Malla>();
            ocupados = new List<Vector4>();

            var materialVeg = Material("CV_Vegetacion", false, limites);
            var materialSuelo = Material("CV_Suelo_Selva", true, limites);

            // Colisionadores temporales para "tocar" la superficie real del modelo con raycasts.
            var temporales = new List<MeshCollider>();
            var lod0 = modelo.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Cenote_LOD0");
            foreach (var mf in (lod0 != null ? lod0 : modelo).GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.name.Contains("Agua")) continue;
                var col = mf.gameObject.AddComponent<MeshCollider>();
                col.sharedMesh = mf.sharedMesh;
                temporales.Add(col);
            }
            Physics.SyncTransforms();

            try
            {
                alturaSuelo = MedirAlturaSuelo(limites);
                MedirBordePozo();

                var raiz = new GameObject("[Selva]").transform;

                Terreno(raiz, materialSuelo, limites);
                Ceiba();
                Arboles();
                Palmas();
                Arbustos();
                Helechos();
                Pasto();
                LianasYRaices();
                SelvaLejana();
                CrearObjetos(raiz, materialVeg);
                Noche(diaNoche);

                return new Resultado { alturaSuelo = alturaSuelo, materialSuelo = materialSuelo };
            }
            finally
            {
                foreach (var c in temporales) Object.DestroyImmediate(c);
            }
        }

        // ───────────────────────── Medición del modelo ─────────────────────────

        static float MedirAlturaSuelo(Bounds limites)
        {
            var alturas = new List<float>();
            for (int i = 0; i < 64; i++)
            {
                float a = i / 64f * Mathf.PI * 2f;
                var o = centro + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * R * 0.93f;
                o.y = limites.max.y + R;
                if (Physics.Raycast(o, Vector3.down, out var hit, R * 4f) && hit.normal.y > 0.8f) alturas.Add(hit.point.y);
            }
            if (alturas.Count == 0) return limites.max.y;
            alturas.Sort();
            return alturas[alturas.Count / 2];
        }

        static bool SueloCenote(float x, float z, out float y)
        {
            y = 0f;
            if (!Physics.Raycast(new Vector3(x, alturaSuelo + R, z), Vector3.down, out var hit, R * 4f)) return false;
            y = hit.point.y;
            float profundidad = Mathf.Max(0.5f, alturaSuelo - nivelAgua);
            return hit.point.y > alturaSuelo - profundidad * 0.2f && hit.normal.y > 0.6f;
        }

        static void MedirBordePozo()
        {
            radioBorde = new float[Angulos];
            float paso = R * 0.01f;
            for (int i = 0; i < Angulos; i++)
            {
                var dir = Dir(i * 360f / Angulos);
                float borde = R * 0.3f;
                // Desde el pozo hacia afuera: el primer punto con suelo firme es el borde.
                for (float r = R * 0.05f; r < R * 0.95f; r += paso)
                {
                    var p = centroPozo + dir * r;
                    if (SueloCenote(p.x, p.z, out _)) { borde = r; break; }
                }
                radioBorde[i] = borde;
            }
        }

        static Vector3 Dir(float grados) => new Vector3(Mathf.Cos(grados * Mathf.Deg2Rad), 0f, Mathf.Sin(grados * Mathf.Deg2Rad));

        static float BordeEn(Vector3 p)
        {
            var d = p - centroPozo; d.y = 0f;
            float ang = Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
            if (ang < 0f) ang += 360f;
            float f = ang / 360f * Angulos;
            int i0 = Mathf.FloorToInt(f) % Angulos, i1 = (i0 + 1) % Angulos;
            return Mathf.Lerp(radioBorde[i0], radioBorde[i1], f - Mathf.Floor(f));
        }

        static float Altura(float x, float z)
        {
            float r = Vector2.Distance(new Vector2(x, z), new Vector2(centro.x, centro.z));
            float colina = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.3f * R, 6.5f * R, r));
            float ruido = Mathf.PerlinNoise(x / (1.8f * R) + 100f, z / (1.8f * R) + 100f);
            float micro = (Mathf.PerlinNoise(x / (0.15f * R), z / (0.15f * R)) - 0.5f) * R * 0.015f
                        * Mathf.InverseLerp(1.0f * R, 1.3f * R, r);
            return alturaSuelo - R * 0.004f + colina * R * 0.45f * (0.35f + 0.65f * ruido) + micro;
        }

        /// <summary>Posición de apoyo: sobre el bloque del cenote (raycast) o sobre el terreno exterior.</summary>
        static bool Apoyo(float x, float z, float margenPozo, out Vector3 p)
        {
            p = new Vector3(x, 0f, z);
            float r = Vector2.Distance(new Vector2(x, z), new Vector2(centro.x, centro.z));
            if (r < R * 0.97f)
            {
                var desdePozo = p - centroPozo; desdePozo.y = 0f;
                if (desdePozo.magnitude < BordeEn(p) + margenPozo) return false;
                if (!SueloCenote(x, z, out float y)) return false;
                p.y = y;
                return true;
            }
            p.y = Altura(x, z) - R * 0.002f;
            return true;
        }

        static bool Libre(Vector3 p, float radio)
        {
            foreach (var o in ocupados)
            {
                float dx = o.x - p.x, dz = o.z - p.z, s = o.w + radio;
                if (dx * dx + dz * dz < s * s) return false;
            }
            return true;
        }

        // Despeja la línea de visión de la cámara "Vista general" (al sur del cenote).
        static bool EnVisual(Vector3 p)
        {
            var d = p - centro; d.y = 0f;
            float ang = Vector3.Angle(d, Vector3.back);
            return ang < 32f && d.magnitude < 2.7f * R;
        }

        static Vector3 PuntoAnillo(float rMin, float rMax)
        {
            float a = Rand() * Mathf.PI * 2f;
            float r = Mathf.Sqrt(Mathf.Lerp(rMin * rMin, rMax * rMax, Rand()));
            return centro + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
        }

        static Malla Bloque(string categoria, Vector3 p)
        {
            var d = p - centro; d.y = 0f;
            float ang = Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg + 360f;
            int sector = Mathf.FloorToInt(ang / 45f) % 8;
            string anillo = d.magnitude < R ? "Cenote" : d.magnitude < 3f * R ? "Cerca" : "Lejos";
            string clave = $"{categoria}_{anillo}_{sector}";
            if (!bloques.TryGetValue(clave, out var m)) bloques[clave] = m = new Malla();
            return m;
        }

        // ───────────────────────── Terreno exterior ─────────────────────────

        static void Terreno(Transform raiz, Material material, Bounds limites)
        {
            var m = new Malla();
            const int anillos = 30, segmentos = 96;
            float rIn = R * 0.985f, rOut = R * 7.2f;
            var radios = new float[anillos + 1];
            for (int i = 0; i <= anillos; i++) radios[i] = rIn + (rOut - rIn) * Mathf.Pow(i / (float)anillos, 1.6f);

            Vector3 P(int i, int j, float y = float.NaN)
            {
                float a = j / (float)segmentos * Mathf.PI * 2f;
                var p = centro + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radios[i];
                p.y = float.IsNaN(y) ? Altura(p.x, p.z) : y;
                return p;
            }

            Color32 ColorSuelo(Vector3 p)
            {
                float r = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(centro.x, centro.z));
                float bosque = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.6f * R, 2.6f * R, r + (Mathf.PerlinNoise(p.x / (0.5f * R), p.z / (0.5f * R)) - 0.5f) * R));
                var pasto = Color32.Lerp(new Color32(96, 140, 56, 0), new Color32(118, 150, 64, 0), Mathf.PerlinNoise(p.x / (0.2f * R), p.z / (0.2f * R)));
                var hojarasca = Color32.Lerp(new Color32(60, 78, 38, 0), new Color32(84, 74, 46, 0), Mathf.PerlinNoise(p.x / (0.3f * R) + 50f, p.z / (0.3f * R)));
                return Var(Color32.Lerp(pasto, hojarasca, bosque), 0.07f);
            }

            for (int i = 0; i < anillos; i++)
            for (int j = 0; j < segmentos; j++)
            {
                Vector3 a = P(i, j), b = P(i + 1, j), c = P(i + 1, j + 1), d = P(i, j + 1);
                m.Tri(a, b, c, Vector3.up, ColorSuelo((a + b + c) / 3f));
                m.Tri(a, c, d, Vector3.up, ColorSuelo((a + c + d) / 3f));
            }

            // Cierre del volumen (pared exterior y fondo) para que el corte transversal muestre tierra sólida.
            // Sin pared interior: dentro del anillo está el bloque del cenote, y así el corte se ve continuo.
            var tierra = new Color32(120, 98, 70, 0);
            float fondo = SelvaGenerador.fondo - R * 1.5f;
            for (int j = 0; j < segmentos; j++)
            {
                float a0 = j / (float)segmentos * Mathf.PI * 2f, a1 = (j + 1) / (float)segmentos * Mathf.PI * 2f;
                var fueraMedio = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0f, Mathf.Sin((a0 + a1) * 0.5f));
                // Pared exterior
                Vector3 e0 = P(anillos, j), e1 = P(anillos, j + 1);
                Vector3 e0b = P(anillos, j, fondo), e1b = P(anillos, j + 1, fondo);
                m.Tri(e0, e0b, e1b, fueraMedio, tierra); m.Tri(e0, e1b, e1, fueraMedio, tierra);
                Vector3 i0b = P(0, j, fondo), i1b = P(0, j + 1, fondo);
                // Fondo (anillo exterior + disco bajo el bloque del cenote, para un sólido continuo)
                m.Tri(i0b, e0b, e1b, Vector3.down, tierra); m.Tri(i0b, e1b, i1b, Vector3.down, tierra);
                var centroFondo = new Vector3(centro.x, fondo, centro.z);
                m.Tri(centroFondo, i0b, i1b, Vector3.down, tierra);
                // Tapa justo debajo del bloque: cierra el volumen bajo el cenote.
                float tapa = SelvaGenerador.fondo - R * 0.002f;
                m.Tri(new Vector3(centro.x, tapa, centro.z), P(0, j, tapa), P(0, j + 1, tapa), Vector3.up, tierra);
            }

            var go = new GameObject("Terreno Selva");
            go.transform.SetParent(raiz, false);
            go.AddComponent<MeshFilter>().sharedMesh = Guardar(m.Construir("Terreno_Selva"));
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            Estatico(go);
        }

        // ───────────────────────── Primitivas ─────────────────────────

        static List<Vector3> icoV;
        static List<int[]> icoF0, icoF1;

        static void PrepararIcosfera()
        {
            if (icoF1 != null) return;
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            icoV = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < icoV.Count; i++) icoV[i] = icoV[i].normalized;
            icoF0 = new List<int[]>
            {
                new[]{0,11,5}, new[]{0,5,1}, new[]{0,1,7}, new[]{0,7,10}, new[]{0,10,11}, new[]{1,5,9}, new[]{5,11,4},
                new[]{11,10,2}, new[]{10,7,6}, new[]{7,1,8}, new[]{3,9,4}, new[]{3,4,2}, new[]{3,2,6}, new[]{3,6,8},
                new[]{3,8,9}, new[]{4,9,5}, new[]{2,4,11}, new[]{6,2,10}, new[]{8,6,7}, new[]{9,8,1},
            };
            var cache = new Dictionary<long, int>();
            int Medio(int a, int b)
            {
                long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(k, out int idx)) return idx;
                icoV.Add(((icoV[a] + icoV[b]) * 0.5f).normalized);
                return cache[k] = icoV.Count - 1;
            }
            icoF1 = new List<int[]>();
            foreach (var f in icoF0)
            {
                int ab = Medio(f[0], f[1]), bc = Medio(f[1], f[2]), ca = Medio(f[2], f[0]);
                icoF1.Add(new[] { f[0], ab, ca }); icoF1.Add(new[] { f[1], bc, ab });
                icoF1.Add(new[] { f[2], ca, bc }); icoF1.Add(new[] { ab, bc, ca });
            }
        }

        /// <summary>Masa de follaje: icosfera deformada con color facetado.</summary>
        static void Copa(Malla m, Vector3 c, Vector3 radios, Color32 color, float pesoAbajo, float pesoArriba, bool detalle, float semilla)
        {
            PrepararIcosfera();
            var caras = detalle ? icoF1 : icoF0;
            Vector3 Pos(int i)
            {
                var d = icoV[i];
                float j = 1f + (Mathf.PerlinNoise(d.x * 1.7f + semilla, d.z * 1.7f + d.y * 1.3f) - 0.5f) * 0.45f;
                return c + Vector3.Scale(d * j, radios);
            }
            foreach (var f in caras)
            {
                Vector3 a = Pos(f[0]), b = Pos(f[1]), d = Pos(f[2]);
                var medio = (a + b + d) / 3f;
                float alto = Mathf.InverseLerp(c.y - radios.y, c.y + radios.y, medio.y);
                var col = Var(color, 0.12f);
                col = Color32.Lerp(col, new Color32((byte)Mathf.Min(255, col.r + 25), (byte)Mathf.Min(255, col.g + 30), col.b, 0), alto * 0.5f);
                Color32 Pv(Vector3 p) => Peso(col, Mathf.Lerp(pesoAbajo, pesoArriba, Mathf.InverseLerp(c.y - radios.y, c.y + radios.y, p.y)));
                m.Tri(a, b, d, medio - c, Pv(a), Pv(b), Pv(d));
            }
        }

        /// <summary>Tubo (tronco, rama, raíz) a lo largo de una curva.</summary>
        static void Tubo(Malla m, Vector3[] ruta, float[] radios, int lados, Color32 color, float pesoBase, float pesoPunta)
        {
            var anillos = new Vector3[ruta.Length][];
            for (int k = 0; k < ruta.Length; k++)
            {
                var tan = (ruta[Mathf.Min(k + 1, ruta.Length - 1)] - ruta[Mathf.Max(k - 1, 0)]).normalized;
                var u = Vector3.Cross(tan, Mathf.Abs(tan.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
                var w = Vector3.Cross(tan, u);
                anillos[k] = new Vector3[lados];
                for (int s = 0; s < lados; s++)
                {
                    float a = s / (float)lados * Mathf.PI * 2f;
                    anillos[k][s] = ruta[k] + (u * Mathf.Cos(a) + w * Mathf.Sin(a)) * radios[k];
                }
            }
            for (int k = 0; k < ruta.Length - 1; k++)
            {
                float p0 = Mathf.Lerp(pesoBase, pesoPunta, k / (float)(ruta.Length - 1));
                float p1 = Mathf.Lerp(pesoBase, pesoPunta, (k + 1) / (float)(ruta.Length - 1));
                var col = Var(color, 0.08f);
                for (int s = 0; s < lados; s++)
                {
                    int s1 = (s + 1) % lados;
                    Vector3 a = anillos[k][s], b = anillos[k][s1], c = anillos[k + 1][s1], d = anillos[k + 1][s];
                    var eje = (ruta[k] + ruta[k + 1]) * 0.5f;
                    var fuera = (a + b + c + d) * 0.25f - eje;
                    m.Tri(a, b, c, fuera, Peso(col, p0), Peso(col, p0), Peso(col, p1));
                    m.Tri(a, c, d, fuera, Peso(col, p0), Peso(col, p1), Peso(col, p1));
                }
            }
        }

        /// <summary>Hoja alargada (fronda) con nervio central levantado; se dibuja por ambas caras.</summary>
        static void Fronda(Malla m, Vector3 origen, Vector3 dir, float largo, float ancho, float subida, float caida,
                           int segmentos, Color32 color, float pesoBase, float pesoPunta)
        {
            dir.y = 0f; dir.Normalize();
            var lado = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 Centro(float s) => origen + dir * largo * s + Vector3.up * (subida * Mathf.Sin(s * Mathf.PI * 0.6f) - caida * s * s);
            var col = Var(color, 0.1f);
            for (int k = 0; k < segmentos; k++)
            {
                float s0 = k / (float)segmentos, s1 = (k + 1) / (float)segmentos;
                float w0 = ancho * Mathf.Sin(Mathf.Min(1f, s0 * 1.15f) * Mathf.PI), w1 = ancho * Mathf.Sin(Mathf.Min(1f, s1 * 1.15f) * Mathf.PI);
                Vector3 c0 = Centro(s0) + Vector3.up * w0 * 0.25f, c1 = Centro(s1) + Vector3.up * w1 * 0.25f;
                Vector3 l0 = Centro(s0) + lado * w0, l1 = Centro(s1) + lado * w1;
                Vector3 r0 = Centro(s0) - lado * w0, r1 = Centro(s1) - lado * w1;
                var q0 = Peso(col, Mathf.Lerp(pesoBase, pesoPunta, s0));
                var q1 = Peso(col, Mathf.Lerp(pesoBase, pesoPunta, s1));
                m.Tri(c0, l0, l1, Vector3.up, q0, q0, q1); m.Tri(c0, l1, c1, Vector3.up, q0, q1, q1);
                m.Tri(c0, r1, r0, Vector3.up, q0, q1, q0); m.Tri(c0, c1, r1, Vector3.up, q0, q1, q1);
            }
        }

        // ───────────────────────── Plantas ─────────────────────────

        static void Arbol(Vector3 b, float h, bool detalle)
        {
            var m = Bloque("Arboles", b);
            var inclin = new Vector3(Rango(-1f, 1f), 0f, Rango(-1f, 1f)) * h * 0.08f;
            var ruta = new Vector3[4];
            var radios = new float[4];
            for (int k = 0; k < 4; k++)
            {
                float s = k / 3f;
                ruta[k] = b + Vector3.up * (h * 0.7f * s - h * 0.03f) + inclin * s * s;
                radios[k] = h * Mathf.Lerp(0.045f, 0.022f, s);
            }
            Tubo(m, ruta, radios, 5, Var(Tronco, 0.12f), 0f, 0.25f);

            var copa = ruta[3] + Vector3.up * h * 0.08f;
            float rc = h * Rango(0.24f, 0.32f);
            int masas = rng.Next(3, 5);
            var color = Elegir(Hojas);
            for (int k = 0; k < masas; k++)
            {
                var off = new Vector3(Rango(-1f, 1f), Rango(-0.3f, 0.6f), Rango(-1f, 1f)) * rc * 0.6f;
                var rad = new Vector3(Rango(0.75f, 1f), Rango(0.55f, 0.75f), Rango(0.75f, 1f)) * rc;
                Copa(m, copa + off, rad, Var(color, 0.1f), 0.45f, 1f, detalle, Rand() * 100f);
            }
            ocupados.Add(new Vector4(b.x, 0, b.z, rc * 0.8f));
        }

        static void Palma(Vector3 b, float h)
        {
            var m = Bloque("Arboles", b);
            var curva = new Vector3(Rango(-1f, 1f), 0f, Rango(-1f, 1f)).normalized * h * Rango(0.08f, 0.2f);
            const int n = 6;
            var ruta = new Vector3[n];
            var radios = new float[n];
            for (int k = 0; k < n; k++)
            {
                float s = k / (float)(n - 1);
                ruta[k] = b + Vector3.up * (h * s - h * 0.03f) + curva * s * s;
                radios[k] = h * Mathf.Lerp(0.032f, 0.022f, s);
            }
            Tubo(m, ruta, radios, 5, Var(TroncoPalma, 0.1f), 0f, 0.35f);

            var tope = ruta[n - 1];
            int frondas = rng.Next(8, 11);
            var color = Elegir(HojasPalma);
            for (int k = 0; k < frondas; k++)
            {
                float a = (k + Rand() * 0.5f) / frondas * 360f;
                float largo = h * Rango(0.38f, 0.5f);
                Fronda(m, tope, Dir(a), largo, largo * 0.11f, largo * 0.3f, largo * Rango(0.5f, 0.8f), 5, color, 0.5f, 1f);
            }
            Copa(m, tope - Vector3.up * h * 0.02f, Vector3.one * h * 0.035f, new Color32(96, 82, 52, 0), 0.4f, 0.5f, false, Rand() * 100f);
            ocupados.Add(new Vector4(b.x, 0, b.z, h * 0.15f));
        }

        static void Ceiba()
        {
            // La ceiba, árbol sagrado maya, junto al cenote como punto de referencia.
            Vector3 b = default;
            bool ok = false;
            for (int intento = 0; intento < 40 && !ok; intento++)
            {
                var p = centro + Dir(Rango(30f, 80f)) * R * Rango(1.35f, 1.6f);
                ok = Apoyo(p.x, p.z, 0f, out b) && !EnVisual(b);
            }
            if (!ok) return;

            var m = Bloque("Arboles", b);
            float h = R * 0.8f;
            var ruta = new[] { b - Vector3.up * h * 0.03f, b + Vector3.up * h * 0.2f, b + Vector3.up * h * 0.45f, b + Vector3.up * h * 0.62f };
            Tubo(m, ruta, new[] { h * 0.085f, h * 0.06f, h * 0.05f, h * 0.045f }, 7, TroncoCeiba, 0f, 0.15f);

            // Raíces tabulares (contrafuertes).
            for (int k = 0; k < 5; k++)
            {
                var d = Dir(k * 72f + Rango(-15f, 15f));
                var raiz = new[] { b + Vector3.up * h * 0.12f + d * h * 0.03f, b + Vector3.up * h * 0.03f + d * h * 0.11f, b - Vector3.up * h * 0.02f + d * h * 0.18f };
                Tubo(m, raiz, new[] { h * 0.03f, h * 0.02f, h * 0.008f }, 4, TroncoCeiba, 0f, 0f);
            }

            // Ramas casi horizontales y copa ancha en pisos.
            for (int k = 0; k < 6; k++)
            {
                var d = Dir(k * 60f + Rango(-20f, 20f));
                var inicio = b + Vector3.up * h * Rango(0.55f, 0.62f);
                var fin = inicio + d * h * Rango(0.3f, 0.42f) + Vector3.up * h * Rango(0.12f, 0.2f);
                Tubo(m, new[] { inicio, (inicio + fin) * 0.5f + Vector3.up * h * 0.03f, fin }, new[] { h * 0.025f, h * 0.018f, h * 0.012f }, 4, TroncoCeiba, 0.1f, 0.4f);
                Copa(m, fin + Vector3.up * h * 0.04f, new Vector3(h * 0.2f, h * 0.08f, h * 0.2f), Var(Hojas[1], 0.1f), 0.5f, 1f, true, Rand() * 100f);
            }
            Copa(m, b + Vector3.up * h * 0.82f, new Vector3(h * 0.22f, h * 0.1f, h * 0.22f), Hojas[2], 0.5f, 1f, true, Rand() * 100f);
            ocupados.Add(new Vector4(b.x, 0, b.z, h * 0.35f));
        }

        static void Arboles()
        {
            int colocados = 0;
            for (int intento = 0; intento < 5000 && colocados < 175; intento++)
            {
                var p = PuntoAnillo(1.2f * R, 5.6f * R);
                if (EnVisual(p) || !Apoyo(p.x, p.z, 0f, out var b)) continue;
                float r = Vector3.Distance(new Vector3(p.x, 0, p.z), centro);
                float h = R * Rango(0.3f, 0.45f) * Mathf.Lerp(1f, 1.25f, Mathf.InverseLerp(2f * R, 5f * R, r));
                if (!Libre(b, h * 0.22f)) continue;
                Arbol(b, h, r < 3.2f * R);
                colocados++;
            }
        }

        static void Palmas()
        {
            int colocados = 0;
            for (int intento = 0; intento < 2000 && colocados < 45; intento++)
            {
                var p = PuntoAnillo(1.15f * R, 4f * R);
                float h = R * Rango(0.25f, 0.4f);
                if (EnVisual(p) || !Apoyo(p.x, p.z, 0f, out var b) || !Libre(b, h * 0.12f)) continue;
                Palma(b, h);
                colocados++;
            }
            // Algunas palmas pequeñas sobre el borde del cenote.
            colocados = 0;
            for (int intento = 0; intento < 400 && colocados < 4; intento++)
            {
                var p = PuntoAnillo(0.3f * R, 0.9f * R);
                float h = R * Rango(0.18f, 0.26f);
                if (EnVisual(p) || !Apoyo(p.x, p.z, R * 0.08f, out var b) || !Libre(b, h * 0.15f)) continue;
                Palma(b, h);
                colocados++;
            }
        }

        static void Arbusto(Vector3 b, float s)
        {
            var m = Bloque("Sotobosque", b);
            var color = Elegir(Hojas);
            int masas = rng.Next(2, 4);
            for (int k = 0; k < masas; k++)
            {
                var off = new Vector3(Rango(-0.6f, 0.6f), Rango(0.2f, 0.5f), Rango(-0.6f, 0.6f)) * s;
                Copa(m, b + off, new Vector3(Rango(0.8f, 1.1f), Rango(0.6f, 0.8f), Rango(0.8f, 1.1f)) * s, Var(color, 0.1f), 0f, 0.5f, false, Rand() * 100f);
            }
            if (Rand() < 0.35f)
            {
                var flor = Elegir(Flores);
                for (int k = 0; k < rng.Next(3, 7); k++)
                {
                    var d = new Vector3(Rango(-1f, 1f), Rango(0.3f, 1f), Rango(-1f, 1f)).normalized;
                    Copa(m, b + Vector3.up * s * 0.45f + d * s * 0.85f, Vector3.one * s * 0.14f, flor, 0.4f, 0.5f, false, Rand() * 100f);
                }
            }
        }

        static void Arbustos()
        {
            for (int k = 0, puestos = 0; k < 3000 && puestos < 220; k++)
            {
                var p = PuntoAnillo(1.05f * R, 5f * R);
                float s = R * Rango(0.025f, 0.06f);
                if (!Apoyo(p.x, p.z, 0f, out var b) || !Libre(b, s * 0.5f)) continue;
                Arbusto(b, s); puestos++;
            }
            for (int k = 0, puestos = 0; k < 2500 && puestos < 70; k++)
            {
                var p = PuntoAnillo(0.2f * R, 0.96f * R);
                float s = R * Rango(0.02f, 0.045f);
                if (!Apoyo(p.x, p.z, R * 0.03f, out var b)) continue;
                Arbusto(b, s); puestos++;
            }
            // Franja de arbustos sobre la unión entre el bloque del cenote y el terreno.
            for (int k = 0; k < 170; k++)
            {
                var p = centro + Dir(k / 170f * 360f + Rango(-1f, 1f)) * R * Rango(0.96f, 1.06f);
                if (!Apoyo(p.x, p.z, R * 0.03f, out var b)) continue;
                Arbusto(b, R * Rango(0.025f, 0.05f));
            }
        }

        static void Helecho(Vector3 b, float s, Vector3? inclinarHacia = null)
        {
            var m = Bloque("Sotobosque", b);
            int frondas = rng.Next(5, 8);
            var color = Var(Elegir(Pastos), 0.1f);
            for (int k = 0; k < frondas; k++)
            {
                var d = Dir((k + Rand() * 0.6f) / frondas * 360f);
                if (inclinarHacia.HasValue) d = (d + inclinarHacia.Value * 1.2f).normalized;
                float largo = s * Rango(0.8f, 1.2f);
                Fronda(m, b, d, largo, largo * 0.13f, largo * 0.45f, largo * 0.5f, 3, color, 0f, 0.9f);
            }
        }

        static void Helechos()
        {
            for (int k = 0, puestos = 0; k < 2000 && puestos < 150; k++)
            {
                var p = PuntoAnillo(1.0f * R, 2.6f * R);
                if (!Apoyo(p.x, p.z, 0f, out var b)) continue;
                Helecho(b, R * Rango(0.03f, 0.06f)); puestos++;
            }
            for (int k = 0, puestos = 0; k < 2500 && puestos < 110; k++)
            {
                var p = PuntoAnillo(0.2f * R, 0.96f * R);
                if (!Apoyo(p.x, p.z, R * 0.01f, out var b)) continue;
                Helecho(b, R * Rango(0.025f, 0.05f)); puestos++;
            }
            // Helechos en el borde del pozo, inclinados hacia el agua.
            for (int i = 0; i < Angulos; i += 2)
            {
                if (Rand() < 0.35f) continue;
                var d = Dir(i * 360f / Angulos + Rango(-2f, 2f));
                var p = centroPozo + d * (radioBorde[i] + R * Rango(0.01f, 0.03f));
                if (!Apoyo(p.x, p.z, 0f, out var b)) continue;
                Helecho(b, R * Rango(0.03f, 0.05f), -d);
            }
        }

        static void Mata(Malla m, Vector3 b, float s)
        {
            var color = Elegir(Pastos);
            int hojas = rng.Next(3, 5);
            for (int k = 0; k < hojas; k++)
            {
                var d = Dir(Rand() * 360f);
                var lado = Vector3.Cross(Vector3.up, d).normalized * s * 0.07f;
                var basePos = b + d * s * Rango(0f, 0.15f);
                var punta = basePos + Vector3.up * s * Rango(0.7f, 1.2f) + d * s * Rango(0.15f, 0.45f);
                var col = Var(color, 0.12f);
                m.Tri(basePos - lado, basePos + lado, punta, Vector3.Cross(lado, punta - basePos), Peso(col, 0f), Peso(col, 0f), Peso(col, 1f));
            }
        }

        static void Pasto()
        {
            for (int k = 0, puestos = 0; k < 6000 && puestos < 1400; k++)
            {
                var p = PuntoAnillo(1.0f * R, 2.0f * R);
                if (!Apoyo(p.x, p.z, 0f, out var b)) continue;
                Mata(Bloque("Sotobosque", b), b, R * Rango(0.012f, 0.03f)); puestos++;
            }
            for (int k = 0, puestos = 0; k < 5000 && puestos < 800; k++)
            {
                var p = PuntoAnillo(0.2f * R, 1.04f * R);
                if (!Apoyo(p.x, p.z, R * 0.005f, out var b)) continue;
                Mata(Bloque("Sotobosque", b), b, R * Rango(0.012f, 0.025f)); puestos++;
            }
        }

        // Lianas y raíces que cuelgan del borde siguiendo la pared del pozo (icónico de los cenotes de Yucatán).
        static void LianasYRaices()
        {
            float paredAlto = Mathf.Max(0.5f, alturaSuelo - nivelAgua);
            float paso = paredAlto / 14f;
            for (int i = 0; i < Angulos; i++)
            {
                int cuantas = Rand() < 0.75f ? 1 : 2;
                for (int c = 0; c < cuantas; c++)
                {
                    bool raiz = Rand() < 0.2f;
                    var d = Dir(i * 360f / Angulos + Rango(-2.5f, 2.5f));
                    float largo = paredAlto * (raiz ? Rango(0.5f, 1.05f) : Rango(0.25f, 0.85f));
                    var puntos = new List<Vector3>();
                    float radioPrev = radioBorde[i];
                    for (float y = alturaSuelo + R * 0.005f; y > alturaSuelo - largo; y -= paso)
                    {
                        var o = centroPozo + Vector3.up * y;
                        float r = Physics.Raycast(o, d, out var hit, R * 1.2f) ? hit.distance : radioPrev;
                        radioPrev = r;
                        puntos.Add(centroPozo + d * (r - R * (raiz ? 0.012f : 0.008f)) + Vector3.up * y);
                    }
                    if (puntos.Count < 2) continue;
                    var m = Bloque("Sotobosque", puntos[0]);
                    var color = raiz ? Var(new Color32(98, 76, 52, 0), 0.12f) : Var(Elegir(new Color32(46, 88, 36, 0), new Color32(60, 98, 40, 0), new Color32(84, 92, 46, 0)), 0.1f);
                    float ancho = R * (raiz ? 0.007f : 0.004f);
                    var lado = Vector3.Cross(Vector3.up, d).normalized;
                    for (int k = 0; k < puntos.Count - 1; k++)
                    {
                        float w0 = Mathf.Lerp(0.25f, 0.6f, k / (float)puntos.Count), w1 = Mathf.Lerp(0.25f, 0.6f, (k + 1) / (float)puntos.Count);
                        Vector3 a = puntos[k], b = puntos[k + 1];
                        var q0 = Peso(color, k == 0 ? 0f : w0 * 0.4f);
                        var q1 = Peso(color, w1 * 0.4f);
                        m.Tri(a - lado * ancho, a + lado * ancho, b + lado * ancho * 0.85f, -d, q0, q0, q1);
                        m.Tri(a - lado * ancho, b + lado * ancho * 0.85f, b - lado * ancho * 0.85f, -d, q0, q1, q1);
                        // Hojitas a los lados de las lianas.
                        if (!raiz && k % 2 == 1)
                        {
                            float sg = k % 4 == 1 ? 1f : -1f;
                            var hojaBase = (a + b) * 0.5f;
                            var hojaPunta = hojaBase + lado * sg * R * 0.022f - Vector3.up * R * 0.01f - d * R * 0.006f;
                            var hojaCol = Peso(Var(Hojas[2], 0.15f), w0 * 0.4f);
                            m.Tri(hojaBase, hojaPunta, hojaBase - Vector3.up * R * 0.012f, -d, hojaCol);
                        }
                    }
                }
            }
        }

        // Muro de selva lejana: masas grandes de follaje que tapan el horizonte (sin sombras).
        static void SelvaLejana()
        {
            for (int k = 0; k < 190; k++)
            {
                float a = (k + Rand() * 0.7f) / 190f * 360f;
                float r = R * Rango(5.6f, 7f);
                var p = centro + Dir(a) * r;
                p.y = Altura(p.x, p.z);
                float tam = R * Rango(0.28f, 0.42f);
                var m = Bloque("Lejanos", p);
                Copa(m, p + Vector3.up * tam * Rango(0.6f, 1.1f), new Vector3(tam, tam * Rango(0.7f, 1f), tam), Var(Elegir(Hojas), 0.12f), 0.3f, 0.6f, false, Rand() * 100f);
            }
        }

        // ───────────────────────── Noche: faroles del cenote y luciérnagas ─────────────────────────

        static void Noche(DayNightController diaNoche)
        {
            var raiz = new GameObject("[Noche]");
            float paredAlto = Mathf.Max(0.5f, alturaSuelo - nivelAgua);
            float radioMedio = radioBorde.Average();

            // 8 faroles sobre la pared, justo encima del agua; 4 de ellos con luz real
            // (el URP móvil admite 4 luces adicionales por objeto).
            var malla = new Malla();
            var luces = new List<Light>();
            const int faroles = 8;
            for (int k = 0; k < faroles; k++)
            {
                var d = Dir(k * 360f / faroles + 22.5f);
                float y = nivelAgua + paredAlto * 0.22f;
                var o = centroPozo + Vector3.up * y;
                float r = Physics.Raycast(o, d, out var hit, R * 1.2f) ? hit.distance : radioMedio;
                var pos = centroPozo + d * (r - R * 0.012f) + Vector3.up * y;
                Copa(malla, pos, new Vector3(1f, 1.4f, 1f) * R * 0.008f, new Color32(255, 220, 160, 255), 0f, 0f, false, k);
                Tubo(malla, new[] { pos + d * R * 0.012f, pos + d * R * 0.004f }, new[] { R * 0.0015f, R * 0.0015f }, 4, new Color32(40, 34, 30, 0), 0f, 0f);

                if (k % 2 != 0) continue;
                var luz = new GameObject($"Farol {k / 2 + 1}").AddComponent<Light>();
                luz.transform.SetParent(raiz.transform, false);
                luz.transform.position = pos - d * R * 0.07f;
                luz.type = LightType.Point;
                luz.color = new Color(1f, 0.72f, 0.42f);
                luz.range = radioMedio * 1.7f;
                // URP atenúa con el cuadrado de la distancia: se escala con el tamaño del pozo.
                luz.intensity = 0.9f * Mathf.Pow(radioMedio * 0.7f, 2f);
                luz.shadows = LightShadows.None;
                luz.enabled = false;
                luces.Add(luz);
            }
            var brillo = new GameObject("Faroles (brillo)");
            brillo.transform.SetParent(raiz.transform, false);
            brillo.AddComponent<MeshFilter>().sharedMesh = Guardar(malla.Construir("Faroles"));
            var mr = brillo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MaterialFarol("CV_Farol", new Color(2.4f, 1.8f, 1.1f, 1f));
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = false;

            // Luciérnagas sobre el claro y el borde del cenote.
            var ps = new GameObject("Luciernagas").AddComponent<ParticleSystem>();
            ps.transform.SetParent(raiz.transform, false);
            ps.transform.position = new Vector3(centro.x, alturaSuelo + R * 0.08f, centro.z);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(R * 0.006f, R * 0.011f);
            main.maxParticles = 140;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 1f, 0.35f), new Color(1f, 0.9f, 0.35f));
            var emision = ps.emission;
            emision.rateOverTime = 20f;
            var forma = ps.shape;
            forma.shapeType = ParticleSystemShapeType.Circle;
            forma.radius = R * 2.2f;
            forma.rotation = new Vector3(90f, 0f, 0f);
            var ruido = ps.noise;
            ruido.enabled = true;
            ruido.strength = R * 0.04f;
            ruido.frequency = 0.25f;
            ruido.quality = ParticleSystemNoiseQuality.Low;
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var parpadeo = new Gradient();
            parpadeo.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.15f, 0.3f), new GradientAlphaKey(1f, 0.48f),
                        new GradientAlphaKey(0.1f, 0.66f), new GradientAlphaKey(1f, 0.82f), new GradientAlphaKey(0f, 1f) });
            color.color = parpadeo;
            var pr = ps.GetComponent<ParticleSystemRenderer>();
            pr.shadowCastingMode = ShadowCastingMode.Off;
            pr.receiveShadows = false;
            pr.sharedMaterial = MaterialLuciernaga();

            var noche = raiz.AddComponent<ActivoDeNoche>();
            var so = new SerializedObject(noche);
            so.FindProperty("diaNoche").objectReferenceValue = diaNoche;
            Lista(so.FindProperty("luces"), luces.Cast<Object>().ToArray());
            Lista(so.FindProperty("brillos"), new Object[] { mr });
            Lista(so.FindProperty("particulas"), new Object[] { ps });
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Lista(SerializedProperty p, Object[] valores)
        {
            p.arraySize = valores.Length;
            for (int i = 0; i < valores.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = valores[i];
        }

        // Pantalla del farol: Lit Shader Graph SG_Farol con emisión HDR.
        static Material MaterialFarol(string nombre, Color emision)
        {
            string ruta = $"{CarpetaMateriales}/{nombre}.mat";
            var shader = CenoteVivoBuilder.Grafo("SG_Farol");
            var existente = AssetDatabase.LoadAssetAtPath<Material>(ruta);
            if (existente != null && existente.shader == shader) return existente;
            var m = CenoteVivoBuilder.Asegurar(existente, shader);
            m.SetColor("_ColorEmision", emision);
            return CenoteVivoBuilder.Guardar(m, ruta, nombre, existente == null);
        }

        // Luciérnagas: SG_Particulas con emisión alta para que brillen sobre el fondo oscuro.
        static Material MaterialLuciernaga() =>
            CenoteVivoBuilder.MaterialParticula("CV_Particula_Luciernaga", UIFabrica.TexturaParticula(), 3f);

        // ───────────────────────── Salida ─────────────────────────

        static void CrearObjetos(Transform raiz, Material material)
        {
            foreach (var kv in bloques.OrderBy(k => k.Key))
            {
                if (kv.Value.v.Count == 0) continue;
                var go = new GameObject(kv.Key);
                go.transform.SetParent(raiz, false);
                go.AddComponent<MeshFilter>().sharedMesh = Guardar(kv.Value.Construir(kv.Key));
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                bool sombras = kv.Key.StartsWith("Arboles");
                mr.shadowCastingMode = sombras ? ShadowCastingMode.On : ShadowCastingMode.Off;
                mr.receiveShadows = !kv.Key.StartsWith("Lejanos");
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                Estatico(go, batching: false);
            }
        }

        static void Estatico(GameObject go, bool batching = true)
        {
            // Las mallas ya vienen combinadas: el static batching solo duplicaría memoria.
            var flags = StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic;
            if (batching) flags |= StaticEditorFlags.BatchingStatic;
            GameObjectUtility.SetStaticEditorFlags(go, flags);
        }

        static Mesh Guardar(Mesh malla)
        {
            if (!AssetDatabase.IsValidFolder("Assets/CenoteVivo/Generado")) AssetDatabase.CreateFolder("Assets/CenoteVivo", "Generado");
            if (!AssetDatabase.IsValidFolder(CarpetaMallas)) AssetDatabase.CreateFolder("Assets/CenoteVivo/Generado", "Selva");
            string ruta = $"{CarpetaMallas}/{malla.name}.asset";
            AssetDatabase.DeleteAsset(ruta);
            AssetDatabase.CreateAsset(malla, ruta);
            return malla;
        }

        /// <summary>Borra las mallas generadas anteriormente (antes de reconstruir).</summary>
        public static void Limpiar()
        {
            if (AssetDatabase.IsValidFolder(CarpetaMallas)) AssetDatabase.DeleteAsset(CarpetaMallas);
        }

        static Material Material(string nombre, bool suelo, Bounds limites)
        {
            string ruta = $"{CarpetaMateriales}/{nombre}.mat";
            var shader = CenoteVivoBuilder.Grafo("SG_Vegetacion");
            var existente = AssetDatabase.LoadAssetAtPath<Material>(ruta);
            if (existente != null && existente.shader == shader) return existente;

            var m = CenoteVivoBuilder.Asegurar(existente, shader);
            if (suelo)
            {
                m.SetFloat("_Seccion", 1f);
                m.SetFloat("_Viento", 0f);
                m.SetFloat("_EscalaManchas", 2f / R);
                m.SetFloat("_Suavidad", 0.08f);
            }
            else
            {
                m.SetFloat("_Seccion", 0f);
                m.SetFloat("_Viento", R * 0.006f);
                m.SetFloat("_EscalaViento", 0.5f / R);
            }
            m.enableInstancing = true;
            return CenoteVivoBuilder.Guardar(m, ruta, nombre, existente == null);
        }
    }
}
