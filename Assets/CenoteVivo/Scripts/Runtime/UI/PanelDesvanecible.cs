using System.Collections;
using UnityEngine;

namespace CenoteVivo.UI
{
    /// <summary>Muestra u oculta un panel con un fundido suave (CanvasGroup).</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class PanelDesvanecible : MonoBehaviour
    {
        [SerializeField] float duracion = 0.25f;
        [SerializeField] bool visibleAlInicio;

        CanvasGroup grupo;
        Coroutine rutina;

        public bool Visible { get; private set; }

        void Awake()
        {
            grupo = GetComponent<CanvasGroup>();
            Fijar(visibleAlInicio);
        }

        public void Mostrar() => Animar(true);
        public void Ocultar() => Animar(false);
        public void Alternar() => Animar(!Visible);

        void Animar(bool visible)
        {
            if (!gameObject.activeInHierarchy) { Fijar(visible); return; }
            Visible = visible;
            grupo.blocksRaycasts = visible;
            grupo.interactable = visible;
            if (rutina != null) StopCoroutine(rutina);
            rutina = StartCoroutine(Fundido(visible ? 1f : 0f));
        }

        IEnumerator Fundido(float destino)
        {
            float inicio = grupo.alpha;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / Mathf.Max(0.01f, duracion))
            {
                grupo.alpha = Mathf.Lerp(inicio, destino, t);
                yield return null;
            }
            grupo.alpha = destino;
            rutina = null;
        }

        void Fijar(bool visible)
        {
            Visible = visible;
            grupo.alpha = visible ? 1f : 0f;
            grupo.blocksRaycasts = visible;
            grupo.interactable = visible;
        }
    }
}
