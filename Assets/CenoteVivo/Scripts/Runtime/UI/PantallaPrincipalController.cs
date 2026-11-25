using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CenoteVivo.UI
{
    /// <summary>
    /// Conecta la interfaz de la escena 3D con los controladores de contaminación,
    /// día/noche y cámaras.
    /// </summary>
    public class PantallaPrincipalController : MonoBehaviour
    {
        [Header("Sistemas")]
        [SerializeField] PollutionController contaminacion;
        [SerializeField] DayNightController diaNoche;
        [SerializeField] CameraViewController camaras;
        [SerializeField] string escenaMenu = "MenuInicio";
        [Tooltip("Desactivado en la escena AR: ahí el botón Atrás lo maneja PantallaARController.")]
        [SerializeField] bool manejarBotonAtras = true;

        [Header("Banner guía")]
        [SerializeField] Text textoBanner;
        [SerializeField] PanelDesvanecible banner;
        [SerializeField] Button botonCerrarBanner;

        [Header("Contaminación")]
        [SerializeField] Slider sliderContaminacion;
        [SerializeField] Text textoEstado;
        [SerializeField] Text textoPorcentaje;
        [SerializeField] Button botonContaminar;
        [SerializeField] Text textoBotonContaminar;
        [SerializeField] Color colorLimpio = new Color(0.35f, 0.9f, 0.8f);
        [SerializeField] Color colorContaminado = new Color(0.85f, 0.55f, 0.2f);

        [Header("Controles")]
        [SerializeField] Button botonDiaNoche;
        [SerializeField] Text textoDiaNoche;
        [SerializeField] Button botonCamara;
        [SerializeField] Text textoCamara;
        [SerializeField] Text textoDescripcionVista;
        [SerializeField] Button botonMenu;

        [Header("Panel informativo")]
        [SerializeField] Button botonInfo;
        [SerializeField] PanelDesvanecible panelInfo;
        [SerializeField] Text tituloInfo;
        [SerializeField] Text cuerpoInfo;
        [SerializeField] Button botonCerrarInfo;

        void Start()
        {
            if (textoBanner != null) textoBanner.text = TextosCenote.BannerGuia;
            botonCerrarBanner?.onClick.AddListener(() => banner?.Ocultar());

            if (contaminacion != null)
            {
                sliderContaminacion?.SetValueWithoutNotify(contaminacion.Objetivo);
                sliderContaminacion?.onValueChanged.AddListener(contaminacion.FijarObjetivo);
                botonContaminar?.onClick.AddListener(() =>
                {
                    contaminacion.Alternar();
                    sliderContaminacion?.SetValueWithoutNotify(contaminacion.Objetivo);
                });
                contaminacion.NivelCambiado += ActualizarContaminacion;
                ActualizarContaminacion(contaminacion.Actual);
            }

            if (diaNoche != null)
            {
                botonDiaNoche?.onClick.AddListener(diaNoche.Alternar);
                diaNoche.Cambiado += ActualizarDiaNoche;
                ActualizarDiaNoche(diaNoche.EsDeNoche);
            }

            if (camaras != null)
            {
                botonCamara?.onClick.AddListener(camaras.Siguiente);
                camaras.VistaCambiada += ActualizarVista;
                if (camaras.VistaActual != null) ActualizarVista(camaras.VistaActual);
            }

            botonMenu?.onClick.AddListener(VolverAlMenu);
            botonInfo?.onClick.AddListener(() => panelInfo?.Mostrar());
            botonCerrarInfo?.onClick.AddListener(() => panelInfo?.Ocultar());
            LlenarInfo();
        }

        void OnDestroy()
        {
            if (contaminacion != null) contaminacion.NivelCambiado -= ActualizarContaminacion;
            if (diaNoche != null) diaNoche.Cambiado -= ActualizarDiaNoche;
            if (camaras != null) camaras.VistaCambiada -= ActualizarVista;
        }

        void Update()
        {
            if (!manejarBotonAtras) return;
            var teclado = Keyboard.current;
            if (teclado == null || !teclado.escapeKey.wasPressedThisFrame) return;
            if (panelInfo != null && panelInfo.Visible) panelInfo.Ocultar();
            else VolverAlMenu();
        }

        void VolverAlMenu() => SceneManager.LoadSceneAsync(escenaMenu);

        void ActualizarContaminacion(float nivel)
        {
            var color = Color.Lerp(colorLimpio, colorContaminado, nivel);
            if (textoEstado != null)
            {
                textoEstado.text = PollutionController.Estado(nivel);
                textoEstado.color = color;
            }
            if (textoPorcentaje != null) textoPorcentaje.text = Mathf.RoundToInt(nivel * 100f) + "%";
            if (textoBotonContaminar != null)
                textoBotonContaminar.text = contaminacion.EstaContaminado ? "Restaurar cenote" : "Contaminar subsuelo";
        }

        void ActualizarDiaNoche(bool deNoche)
        {
            if (textoDiaNoche != null) textoDiaNoche.text = deNoche ? "Noche" : "Día";
        }

        void ActualizarVista(CameraViewController.Vista vista)
        {
            if (textoCamara != null) textoCamara.text = vista.nombre;
            if (textoDescripcionVista != null) textoDescripcionVista.text = vista.descripcion;
        }

        void LlenarInfo()
        {
            if (tituloInfo != null) tituloInfo.text = TextosCenote.TituloInfo;
            if (cuerpoInfo == null) return;

            var sb = new StringBuilder();
            foreach (var (titulo, cuerpo) in TextosCenote.SeccionesInfo)
            {
                sb.Append("<b><color=#7FE3D3>").Append(titulo).Append("</color></b>\n");
                sb.Append(cuerpo).Append("\n\n");
            }
            cuerpoInfo.text = sb.ToString().TrimEnd();
        }
    }
}
