using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using CenoteVivo.UI;

namespace CenoteVivo.AR
{
    /// <summary>
    /// Interfaz propia de la escena AR: guía paso a paso, corte del subsuelo, etiquetas, foto,
    /// recolocar y cambio a marcador impreso. Lo común con la escena 3D (contaminación, día/noche,
    /// información y menú) lo sigue manejando PantallaPrincipalController.
    /// </summary>
    public class PantallaARController : MonoBehaviour
    {
        [Header("Sistemas")]
        [SerializeField] ColocadorAR colocador;
        [SerializeField] EtiquetasAR etiquetas;
        [SerializeField] FotoAR foto;
        [SerializeField] CorteTransversal corte;
        [SerializeField] PanelDesvanecible panelInfo;
        [SerializeField] string escenaMenu = "MenuInicio";

        [Header("Guía")]
        [SerializeField] PanelDesvanecible guia;
        [SerializeField] Text textoGuia;

        [Header("Botones")]
        [SerializeField] Button botonSubsuelo;
        [SerializeField] Text textoSubsuelo;
        [SerializeField] Button botonEtiquetas;
        [SerializeField] Text textoEtiquetas;
        [SerializeField] Button botonFoto;
        [SerializeField] Button botonRecolocar;
        [SerializeField] Button botonMarcador;
        [SerializeField] Text textoMarcador;
        [Tooltip("Elementos que solo tienen sentido con el cenote en pantalla (controles, botones).")]
        [SerializeField] GameObject[] soloConCenote = System.Array.Empty<GameObject>();

        [Header("Foto")]
        [SerializeField] PanelDesvanecible vistaPrevia;
        [SerializeField] RawImage imagenPrevia;
        [SerializeField] Text textoPrevia;
        [SerializeField] Button botonCompartir;
        [SerializeField] Button botonCerrarPrevia;

        Coroutine ocultarGuia;
        float tiempoBuscando;

        void Start()
        {
            botonSubsuelo?.onClick.AddListener(AlternarSubsuelo);
            botonEtiquetas?.onClick.AddListener(() => etiquetas?.Alternar());
            botonFoto?.onClick.AddListener(() => foto?.Tomar());
            botonRecolocar?.onClick.AddListener(() => colocador?.Recolocar());
            botonMarcador?.onClick.AddListener(CambiarMetodo);
            botonCompartir?.onClick.AddListener(() => foto?.Compartir());
            botonCerrarPrevia?.onClick.AddListener(() => vistaPrevia?.Ocultar());

            if (etiquetas != null) { etiquetas.MostrarCambiado += ActualizarEtiquetas; ActualizarEtiquetas(etiquetas.Mostrar); }
            if (foto != null) foto.FotoTomada += AlTomarFoto;
            if (colocador != null) { colocador.EstadoCambiado += AlCambiarEstado; AlCambiarEstado(colocador.Estado); }
            ActualizarSubsuelo();
        }

        void OnDestroy()
        {
            if (etiquetas != null) etiquetas.MostrarCambiado -= ActualizarEtiquetas;
            if (foto != null) foto.FotoTomada -= AlTomarFoto;
            if (colocador != null) colocador.EstadoCambiado -= AlCambiarEstado;
        }

        void Update()
        {
            // En superficie: si tras unos segundos no aparece la mesa, se ofrece el marcador impreso.
            if (colocador != null && botonMarcador != null)
            {
                bool buscando = colocador.Estado == EstadoAR.BuscandoSuperficie;
                tiempoBuscando = buscando ? tiempoBuscando + Time.deltaTime : 0f;
                bool ofrecer = (buscando && tiempoBuscando > 10f) || (colocador.UsandoMarcador && colocador.SuperficieDisponible);
                if (botonMarcador.gameObject.activeSelf != ofrecer) botonMarcador.gameObject.SetActive(ofrecer);
            }

            // El botón "Atrás" de Android llega como Escape: cierra lo que esté abierto o vuelve al menú.
            var teclado = Keyboard.current;
            if (teclado == null || !teclado.escapeKey.wasPressedThisFrame) return;
            if (vistaPrevia != null && vistaPrevia.Visible) vistaPrevia.Ocultar();
            else if (etiquetas != null && etiquetas.TarjetaAbierta) etiquetas.Cerrar();
            else if (panelInfo != null && panelInfo.Visible) panelInfo.Ocultar();
            else SceneManager.LoadSceneAsync(escenaMenu);
        }

        void AlCambiarEstado(EstadoAR estado)
        {
            bool visible = colocador.ContenidoVisible;
            foreach (var go in soloConCenote) if (go != null) go.SetActive(visible);
            if (botonRecolocar != null)
                botonRecolocar.GetComponentInChildren<Text>().text = colocador.UsandoMarcador ? "Restablecer" : "Recolocar";
            if (textoMarcador != null)
                textoMarcador.text = colocador.UsandoMarcador ? "Usar la mesa" : "Usar marcador impreso";

            switch (estado)
            {
                case EstadoAR.Iniciando:
                    Guia("Iniciando la cámara…", false); break;
                case EstadoAR.BuscandoSuperficie:
                    Guia("Mueve el teléfono despacio sobre una mesa o el piso para encontrar la superficie.", false); break;
                case EstadoAR.ListoParaColocar:
                    Guia("¡Listo! Toca la pantalla para colocar el cenote.", false); break;
                case EstadoAR.BuscandoMarcador:
                    Guia("Apunta la cámara al marcador impreso de Cenote Vivo, sobre una mesa o el piso.", false); break;
                case EstadoAR.Colocado:
                case EstadoAR.SobreMarcador:
                    Guia("Arrastra con un dedo para moverlo. Pellizca para cambiar su tamaño y gira dos dedos para rotarlo. Toca las etiquetas para aprender.", true); break;
                case EstadoAR.Error:
                    Guia("No se pudo iniciar la realidad aumentada. Revisa el permiso de la cámara.", false); break;
            }
        }

        void Guia(string texto, bool ocultarDespues)
        {
            if (textoGuia != null) textoGuia.text = texto;
            guia?.Mostrar();
            if (ocultarGuia != null) StopCoroutine(ocultarGuia);
            ocultarGuia = ocultarDespues ? StartCoroutine(OcultarGuia(7f)) : null;
        }

        IEnumerator OcultarGuia(float segundos)
        {
            yield return new WaitForSeconds(segundos);
            guia?.Ocultar();
            ocultarGuia = null;
        }

        void CambiarMetodo()
        {
            if (colocador == null) return;
            if (colocador.UsandoMarcador) colocador.UsarSuperficieDeNuevo();
            else colocador.UsarMarcador();
        }

        void AlternarSubsuelo()
        {
            if (corte == null) return;
            corte.Activar(!corte.Activo);
            ActualizarSubsuelo();
        }

        void ActualizarSubsuelo()
        {
            if (textoSubsuelo != null) textoSubsuelo.text = corte != null && corte.Activo ? "Ver completo" : "Ver subsuelo";
        }

        void ActualizarEtiquetas(bool mostrar)
        {
            if (textoEtiquetas != null) textoEtiquetas.text = mostrar ? "Ocultar etiquetas" : "Mostrar etiquetas";
        }

        void AlTomarFoto(bool guardada, string mensaje)
        {
            if (imagenPrevia != null) imagenPrevia.texture = foto.UltimaFoto;
            if (textoPrevia != null) textoPrevia.text = mensaje;
            if (botonCompartir != null) botonCompartir.gameObject.SetActive(guardada && foto.PuedeCompartir);
            vistaPrevia?.Mostrar();
        }
    }
}
