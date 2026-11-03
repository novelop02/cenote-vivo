using System.Collections;
using UnityEngine;

namespace CenoteVivo
{
    /// <summary>
    /// Enciende elementos solo de noche: faroles del cenote (luces puntuales con fundido),
    /// sus pantallas brillantes y las luciérnagas. De día quedan apagados y no cuestan nada.
    /// </summary>
    public class ActivoDeNoche : MonoBehaviour
    {
        [SerializeField] DayNightController diaNoche;
        [SerializeField] Light[] luces = new Light[0];
        [SerializeField] Renderer[] brillos = new Renderer[0];
        [SerializeField] ParticleSystem[] particulas = new ParticleSystem[0];
        [SerializeField] float duracionFundido = 1.5f;
        [Tooltip("Parpadeo suave tipo flama (0 = luz fija).")]
        [Range(0f, 0.3f)] [SerializeField] float parpadeo = 0.08f;

        float[] intensidades;
        float factor;
        Coroutine rutina;

        void Awake()
        {
            intensidades = new float[luces.Length];
            for (int i = 0; i < luces.Length; i++) intensidades[i] = luces[i] != null ? luces[i].intensity : 0f;
        }

        void OnEnable()
        {
            if (diaNoche != null) diaNoche.Cambiado += Aplicar;
            factor = diaNoche != null && diaNoche.EsDeNoche ? 1f : 0f;
            Fijar(factor);
            foreach (var p in particulas)
                if (p != null) { if (factor > 0f) p.Play(); else p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
        }

        void OnDisable()
        {
            if (diaNoche != null) diaNoche.Cambiado -= Aplicar;
        }

        void Aplicar(bool deNoche)
        {
            if (rutina != null) StopCoroutine(rutina);
            rutina = StartCoroutine(Fundido(deNoche ? 1f : 0f));
            foreach (var p in particulas)
                if (p != null) { if (deNoche) p.Play(); else p.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
        }

        IEnumerator Fundido(float destino)
        {
            float inicio = factor;
            for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, duracionFundido))
            {
                Fijar(Mathf.Lerp(inicio, destino, t));
                yield return null;
            }
            Fijar(destino);
            rutina = null;
        }

        void Update()
        {
            if (factor <= 0f || parpadeo <= 0f) return;
            for (int i = 0; i < luces.Length; i++)
            {
                if (luces[i] == null) continue;
                float ruido = Mathf.PerlinNoise(Time.time * 3f, i * 7.3f) - 0.5f;
                luces[i].intensity = intensidades[i] * factor * (1f + ruido * 2f * parpadeo);
            }
        }

        void Fijar(float f)
        {
            factor = f;
            for (int i = 0; i < luces.Length; i++)
            {
                if (luces[i] == null) continue;
                luces[i].intensity = intensidades[i] * f;
                luces[i].enabled = f > 0.001f;
            }
            foreach (var r in brillos)
                if (r != null) r.enabled = f > 0.5f;
        }
    }
}
