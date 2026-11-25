using UnityEngine;

namespace CenoteVivo
{
    /// <summary>
    /// Optimización para móviles: la cámara deja de dibujar la vegetación pequeña (capa
    /// "VegetacionDetalle": pasto, helechos, arbustos, lianas) cuando está más lejos que la distancia
    /// indicada. Unity la descarta en el culling, así que no gasta draw calls ni triángulos.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CullingPorDistancia : MonoBehaviour
    {
        [SerializeField] string capa = "VegetacionDetalle";
        [Tooltip("Distancia (m) a partir de la cual no se dibuja la capa.")]
        [SerializeField] float distancia = 80f;

        void OnEnable()
        {
            int indice = LayerMask.NameToLayer(capa);
            if (indice < 0) return;
            var cam = GetComponent<Camera>();
            var distancias = new float[32]; // 0 = sin límite (se usa el plano lejano de la cámara)
            distancias[indice] = distancia;
            cam.layerCullDistances = distancias;
            cam.layerCullSpherical = true; // distancia real, no solo en profundidad
        }
    }
}
