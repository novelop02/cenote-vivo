using System;
using UnityEngine;
using UnityEngine.UI;
using CenoteVivo.UI;

namespace CenoteVivo.AR
{
    /// <summary>
    /// Funcionalidad 2, "Etiquetas informativas".
    ///
    /// Con el cenote colocado, unas etiquetas flotantes señalan sus partes (roca caliza, agua,
    /// raíces, contaminación). Al tocar una se abre una tarjeta con una explicación corta.
    ///
    /// Para teléfonos modestos las etiquetas no son objetos 3D ni Canvas en el mundo: son botones
    /// de un único Canvas de pantalla que se colocan cada cuadro sobre la proyección de su punto 3D.
    /// </summary>
    public class EtiquetasAR : MonoBehaviour
    {
        [Serializable]
        public class Etiqueta
        {
            public string titulo;
            [TextArea(3, 6)] public string explicacion;
            [Tooltip("Punto del cenote que señala (hijo del espacio del cenote).")]
            public Transform punto;
            public Button boton;
            [NonSerialized] public RectTransform rect;
            [NonSerialized] public CanvasGroup grupo;
        }

        [SerializeField] ColocadorAR colocador;
        [SerializeField] Etiqueta[] etiquetas = Array.Empty<Etiqueta>();
        [Tooltip("RectTransform que contiene las etiquetas (pantalla completa).")]
        [SerializeField] RectTransform contenedor;

        [Header("Tarjeta")]
        [SerializeField] PanelDesvanecible tarjeta;
        [SerializeField] Text tituloTarjeta;
        [SerializeField] Text textoTarjeta;
        [SerializeField] Button cerrarTarjeta;

        Camera camara;
        public bool Mostrar { get; private set; } = true;
        public event Action<bool> MostrarCambiado;

        void Start()
        {
            camara = Camera.main;
            foreach (var e in etiquetas)
            {
                if (e.boton == null) continue;
                e.rect = (RectTransform)e.boton.transform;
                e.grupo = e.boton.GetComponent<CanvasGroup>();
                if (e.grupo == null) e.grupo = e.boton.gameObject.AddComponent<CanvasGroup>();
                var actual = e;
                e.boton.onClick.AddListener(() => Abrir(actual));
            }
            cerrarTarjeta?.onClick.AddListener(Cerrar);
        }

        public void Alternar()
        {
            Mostrar = !Mostrar;
            if (!Mostrar) Cerrar();
            MostrarCambiado?.Invoke(Mostrar);
        }

        public bool TarjetaAbierta => tarjeta != null && tarjeta.Visible;

        public void Cerrar() => tarjeta?.Ocultar();

        void Abrir(Etiqueta e)
        {
            if (tituloTarjeta != null) tituloTarjeta.text = e.titulo;
            if (textoTarjeta != null) textoTarjeta.text = e.explicacion;
            tarjeta?.Mostrar();
        }

        void LateUpdate()
        {
            bool visibles = Mostrar && colocador != null && colocador.ContenidoVisible && camara != null;
            if (!visibles && tarjeta != null && tarjeta.Visible) tarjeta.Ocultar();

            foreach (var e in etiquetas)
            {
                if (e.rect == null) continue;
                bool ver = visibles && e.punto != null;
                Vector3 pantalla = default;
                if (ver)
                {
                    pantalla = camara.WorldToScreenPoint(e.punto.position);
                    // Detrás de la cámara o fuera de la pantalla: no se dibuja.
                    ver = pantalla.z > 0f && pantalla.x > -50f && pantalla.x < Screen.width + 50f
                          && pantalla.y > -50f && pantalla.y < Screen.height + 50f;
                }

                if (e.rect.gameObject.activeSelf != ver) e.rect.gameObject.SetActive(ver);
                if (!ver) continue;

                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(contenedor, pantalla, null, out var local))
                    e.rect.anchoredPosition = local;
                // Las etiquetas lejanas se ven más tenues para no saturar la vista.
                e.grupo.alpha = Mathf.Lerp(1f, 0.6f, Mathf.InverseLerp(0.4f, 2.5f, pantalla.z));
            }
        }
    }
}
