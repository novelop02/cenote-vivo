using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace CenoteVivo
{
    /// <summary>
    /// Conmutador Día/Noche. Interpola sol, luna, luz ambiental (Trilight), niebla y cielo
    /// entre dos ajustes predefinidos. Todo es iluminación en tiempo real barata: una luz
    /// direccional con sombras y una luna sin sombras.
    /// </summary>
    public class DayNightController : MonoBehaviour
    {
        [Serializable]
        public struct Ajuste
        {
            public Color colorSol;
            public float intensidadSol;
            public Vector3 rotacionSol;
            public float intensidadLuna;
            [ColorUsage(false, true)] public Color ambienteCielo;
            [ColorUsage(false, true)] public Color ambienteHorizonte;
            [ColorUsage(false, true)] public Color ambienteSuelo;
            public Color colorNiebla;
            public Color tonoCielo;
            public float exposicionCielo;
        }

        [SerializeField] Light sol;
        [SerializeField] Light luna;
        [SerializeField] float duracionTransicion = 2f;
        [SerializeField] bool empezarDeNoche;

        [SerializeField] Ajuste dia = new Ajuste
        {
            colorSol = new Color(1f, 0.95f, 0.84f),
            intensidadSol = 1.3f,
            rotacionSol = new Vector3(55f, -30f, 0f),
            intensidadLuna = 0f,
            ambienteCielo = new Color(0.55f, 0.68f, 0.8f),
            ambienteHorizonte = new Color(0.42f, 0.48f, 0.45f),
            ambienteSuelo = new Color(0.2f, 0.18f, 0.15f),
            colorNiebla = new Color(0.62f, 0.75f, 0.8f),
            tonoCielo = new Color(0.5f, 0.5f, 0.5f),
            exposicionCielo = 1.2f,
        };

        [SerializeField] Ajuste noche = new Ajuste
        {
            colorSol = new Color(0.35f, 0.4f, 0.65f),
            intensidadSol = 0f,
            rotacionSol = new Vector3(-20f, -30f, 0f),
            intensidadLuna = 0.35f,
            ambienteCielo = new Color(0.07f, 0.1f, 0.2f),
            ambienteHorizonte = new Color(0.05f, 0.07f, 0.12f),
            ambienteSuelo = new Color(0.02f, 0.02f, 0.03f),
            colorNiebla = new Color(0.03f, 0.05f, 0.1f),
            tonoCielo = new Color(0.12f, 0.15f, 0.3f),
            exposicionCielo = 0.12f,
        };

        static readonly int SkyTintId = Shader.PropertyToID("_SkyTint");
        static readonly int ExposureId = Shader.PropertyToID("_Exposure");

        Material cielo;
        Coroutine transicion;

        public bool EsDeNoche { get; private set; }
        /// <summary>Color de niebla exterior actual (la cámara sumergida lo sobrescribe temporalmente).</summary>
        public Color ColorNiebla { get; private set; }
        public event Action<bool> Cambiado;

        void Awake()
        {
            // Copia del skybox para no modificar el asset original al cambiar de hora.
            if (RenderSettings.skybox != null)
            {
                cielo = new Material(RenderSettings.skybox);
                RenderSettings.skybox = cielo;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            EsDeNoche = empezarDeNoche;
            Aplicar(EsDeNoche ? noche : dia, EsDeNoche ? noche : dia, 1f);
        }

        void OnDestroy()
        {
            if (cielo != null) Destroy(cielo);
        }

        public void Alternar() => FijarNoche(!EsDeNoche);

        public void FijarNoche(bool deNoche)
        {
            if (deNoche == EsDeNoche) return;
            EsDeNoche = deNoche;
            if (transicion != null) StopCoroutine(transicion);
            transicion = StartCoroutine(Transicion(deNoche ? dia : noche, deNoche ? noche : dia));
            Cambiado?.Invoke(EsDeNoche);
        }

        IEnumerator Transicion(Ajuste desde, Ajuste hasta)
        {
            for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, duracionTransicion))
            {
                Aplicar(desde, hasta, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
            Aplicar(desde, hasta, 1f);
            // Actualiza las reflexiones del entorno (agua) una sola vez al terminar.
            DynamicGI.UpdateEnvironment();
            transicion = null;
        }

        void Aplicar(Ajuste a, Ajuste b, float t)
        {
            if (sol != null)
            {
                sol.color = Color.Lerp(a.colorSol, b.colorSol, t);
                sol.intensity = Mathf.Lerp(a.intensidadSol, b.intensidadSol, t);
                sol.transform.rotation = Quaternion.Slerp(Quaternion.Euler(a.rotacionSol), Quaternion.Euler(b.rotacionSol), t);
                sol.enabled = sol.intensity > 0.001f;
            }
            if (luna != null)
            {
                luna.intensity = Mathf.Lerp(a.intensidadLuna, b.intensidadLuna, t);
                luna.enabled = luna.intensity > 0.001f;
            }

            RenderSettings.ambientSkyColor = Color.Lerp(a.ambienteCielo, b.ambienteCielo, t);
            RenderSettings.ambientEquatorColor = Color.Lerp(a.ambienteHorizonte, b.ambienteHorizonte, t);
            RenderSettings.ambientGroundColor = Color.Lerp(a.ambienteSuelo, b.ambienteSuelo, t);
            ColorNiebla = Color.Lerp(a.colorNiebla, b.colorNiebla, t);
            RenderSettings.fogColor = ColorNiebla;

            if (cielo != null)
            {
                if (cielo.HasProperty(SkyTintId)) cielo.SetColor(SkyTintId, Color.Lerp(a.tonoCielo, b.tonoCielo, t));
                if (cielo.HasProperty(ExposureId)) cielo.SetFloat(ExposureId, Mathf.Lerp(a.exposicionCielo, b.exposicionCielo, t));
            }

            var camara = Camera.main;
            if (camara != null && camara.clearFlags == CameraClearFlags.SolidColor)
                camara.backgroundColor = ColorNiebla;
        }
    }
}
