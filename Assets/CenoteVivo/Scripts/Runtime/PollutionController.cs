using System;
using UnityEngine;

namespace CenoteVivo
{
    /// <summary>
    /// Simulador de contaminación del subsuelo. Interpola de forma gradual el nivel de
    /// contaminación (0 = limpio, 1 = contaminado) y lo publica como propiedad global de
    /// shader (_CenotePollution) para que el agua, la roca y el subsuelo reaccionen a la vez
    /// sin tocar materiales individuales (cero draw calls extra, compatible con SRP Batcher).
    /// </summary>
    public class PollutionController : MonoBehaviour
    {
        public static readonly int PollutionId = Shader.PropertyToID("_CenotePollution");

        [Serializable]
        public class EmisorContaminacion
        {
            public ParticleSystem sistema;
            [Tooltip("Partículas por segundo cuando la contaminación es 100%.")]
            public float tasaMaxima = 10f;
            [Tooltip("Nivel de contaminación a partir del cual empieza a emitir.")]
            [Range(0f, 1f)] public float umbral = 0f;
        }

        [Header("Estado")]
        [Range(0f, 1f)]
        [SerializeField] float objetivo;
        [Tooltip("Segundos que tarda en pasar de limpio a contaminado.")]
        [SerializeField] float duracionTransicion = 6f;

        [Header("Elementos visuales")]
        [SerializeField] EmisorContaminacion[] emisores = Array.Empty<EmisorContaminacion>();

        [Header("Niebla bajo el agua (vista sumergida)")]
        [SerializeField] Color nieblaLimpia = new Color(0.12f, 0.45f, 0.5f);
        [SerializeField] Color nieblaContaminada = new Color(0.16f, 0.13f, 0.06f);

        float actual;

        /// <summary>Se dispara cada vez que cambia el nivel visible (0–1).</summary>
        public event Action<float> NivelCambiado;

        public float Actual => actual;
        public float Objetivo => objetivo;
        public bool EstaContaminado => objetivo >= 0.5f;
        public Color ColorNieblaAgua => Color.Lerp(nieblaLimpia, nieblaContaminada, actual);

        void OnEnable()
        {
            actual = objetivo;
            Aplicar();
        }

        void OnDisable() => Shader.SetGlobalFloat(PollutionId, 0f);

        void Update()
        {
            if (Mathf.Approximately(actual, objetivo)) return;
            float paso = Time.deltaTime / Mathf.Max(0.01f, duracionTransicion);
            actual = Mathf.MoveTowards(actual, objetivo, paso);
            Aplicar();
        }

        /// <summary>Fija el nivel deseado; el cambio visual es gradual.</summary>
        public void FijarObjetivo(float nivel) => objetivo = Mathf.Clamp01(nivel);

        /// <summary>Alterna entre "Limpio" y "Contaminado".</summary>
        public void Alternar() => FijarObjetivo(EstaContaminado ? 0f : 1f);

        public static string Estado(float nivel)
        {
            if (nivel < 0.15f) return "Limpio";
            if (nivel < 0.5f) return "En riesgo";
            if (nivel < 0.85f) return "Contaminado";
            return "Muy contaminado";
        }

        void Aplicar()
        {
            Shader.SetGlobalFloat(PollutionId, actual);

            foreach (var e in emisores)
            {
                if (e?.sistema == null) continue;
                float t = Mathf.InverseLerp(e.umbral, 1f, actual);
                var emision = e.sistema.emission;
                emision.rateOverTime = e.tasaMaxima * t;
                if (t > 0f && !e.sistema.isPlaying) e.sistema.Play();
                else if (t <= 0f && e.sistema.isPlaying) e.sistema.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            NivelCambiado?.Invoke(actual);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!Application.isPlaying) { actual = objetivo; Shader.SetGlobalFloat(PollutionId, actual); }
        }
#endif
    }
}
