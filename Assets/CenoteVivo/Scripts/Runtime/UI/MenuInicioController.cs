using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CenoteVivo.UI
{
    /// <summary>
    /// Menú de inicio: título, frases de impacto que rotan con fundido y acceso a la escena 3D.
    /// </summary>
    public class MenuInicioController : MonoBehaviour
    {
        [SerializeField] string escenaCenote = "CenoteVivo";
        [SerializeField] Text titulo;
        [SerializeField] Text subtitulo;
        [SerializeField] Text frase;
        [SerializeField] CanvasGroup grupoFrase;
        [SerializeField] float segundosPorFrase = 5f;
        [SerializeField] Button botonExplorar;
        [SerializeField] Button botonAcerca;
        [SerializeField] Button botonSalir;
        [SerializeField] PanelDesvanecible panelAcerca;
        [SerializeField] Button botonCerrarAcerca;
        [SerializeField] Text cuerpoAcerca;

        int indiceFrase;

        void Start()
        {
            if (titulo != null) titulo.text = TextosCenote.Titulo;
            if (subtitulo != null) subtitulo.text = TextosCenote.Subtitulo;
            if (cuerpoAcerca != null)
            {
                var secciones = TextosCenote.SeccionesInfo;
                var (t1, c1) = secciones[0];
                var (t2, c2) = secciones[secciones.Length - 1];
                cuerpoAcerca.text = $"<b><color=#7FE3D3>{t1}</color></b>\n{c1}\n\n<b><color=#7FE3D3>{t2}</color></b>\n{c2}";
            }

            botonExplorar?.onClick.AddListener(Explorar);
            botonAcerca?.onClick.AddListener(() => panelAcerca?.Mostrar());
            botonCerrarAcerca?.onClick.AddListener(() => panelAcerca?.Ocultar());
            botonSalir?.onClick.AddListener(Application.Quit);

            if (frase != null) StartCoroutine(RotarFrases());
        }

        void Update()
        {
            // El botón "Atrás" de Android llega como Escape.
            var teclado = Keyboard.current;
            if (teclado == null || !teclado.escapeKey.wasPressedThisFrame) return;
            if (panelAcerca != null && panelAcerca.Visible) panelAcerca.Ocultar();
            else Application.Quit();
        }

        public void Explorar() => SceneManager.LoadSceneAsync(escenaCenote);

        IEnumerator RotarFrases()
        {
            var frases = TextosCenote.FrasesImpacto;
            while (true)
            {
                frase.text = frases[indiceFrase];
                yield return Fundir(0f, 1f, 0.6f);
                yield return new WaitForSeconds(segundosPorFrase);
                yield return Fundir(1f, 0f, 0.6f);
                indiceFrase = (indiceFrase + 1) % frases.Length;
            }
        }

        IEnumerator Fundir(float desde, float hasta, float duracion)
        {
            if (grupoFrase == null) yield break;
            for (float t = 0f; t < 1f; t += Time.deltaTime / duracion)
            {
                grupoFrase.alpha = Mathf.Lerp(desde, hasta, t);
                yield return null;
            }
            grupoFrase.alpha = hasta;
        }
    }
}
