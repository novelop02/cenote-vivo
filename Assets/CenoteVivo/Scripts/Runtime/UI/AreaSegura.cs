using UnityEngine;

namespace CenoteVivo.UI
{
    /// <summary>
    /// Ajusta el RectTransform al área segura de la pantalla (muescas, cámaras perforadas y
    /// barras de gestos de Android) para que ningún botón quede tapado.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class AreaSegura : MonoBehaviour
    {
        RectTransform rect;
        Rect ultima;
        Vector2Int ultimaPantalla;

        void Awake()
        {
            rect = (RectTransform)transform;
            Aplicar();
        }

        void Update()
        {
            if (Screen.safeArea != ultima || ultimaPantalla.x != Screen.width || ultimaPantalla.y != Screen.height)
                Aplicar();
        }

        void Aplicar()
        {
            ultima = Screen.safeArea;
            ultimaPantalla = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0) return;

            var min = ultima.position;
            var max = ultima.position + ultima.size;
            min.x /= Screen.width; min.y /= Screen.height;
            max.x /= Screen.width; max.y /= Screen.height;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
