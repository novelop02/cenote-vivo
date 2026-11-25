using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace CenoteVivo.AR
{
    /// <summary>
    /// Funcionalidad 3, "Foto con el cenote".
    ///
    /// El botón de cámara oculta la interfaz, muestra un marco con el lema de Cenote Vivo, captura la
    /// pantalla (cámara del teléfono + cenote en AR) y la guarda en la galería, en Imágenes/Cenote Vivo.
    /// Después se puede compartir con el menú de Android (WhatsApp, Instagram, Facebook…).
    /// </summary>
    public class FotoAR : MonoBehaviour
    {
        public const string TextoCompartir =
            "Así se ve un cenote cuando su agua se contamina. El agua que no ves, también se contamina. #CenoteVivo";

        [Tooltip("Marco que solo aparece en la foto (lema y logo).")]
        [SerializeField] GameObject marcoFoto;

        bool capturando;
        AndroidJavaObject ultimaUri;

        public Texture2D UltimaFoto { get; private set; }
        public bool PuedeCompartir => ultimaUri != null;

        /// <summary>true = guardada en la galería; el texto explica el resultado.</summary>
        public event Action<bool, string> FotoTomada;

        public void Tomar()
        {
            if (!capturando) StartCoroutine(Capturar());
        }

        IEnumerator Capturar()
        {
            capturando = true;

#if UNITY_ANDROID && !UNITY_EDITOR
            if (GaleriaAndroid.NecesitaPermiso && !Permission.HasUserAuthorizedPermission(Permission.ExternalStorageWrite))
            {
                bool? concedido = null;
                var respuesta = new PermissionCallbacks();
                respuesta.PermissionGranted += _ => concedido = true;
                respuesta.PermissionDenied += _ => concedido = false;
                respuesta.PermissionDeniedAndDontAskAgain += _ => concedido = false;
                Permission.RequestUserPermission(Permission.ExternalStorageWrite, respuesta);
                // Espera la respuesta del diálogo de permisos.
                float limite = Time.realtimeSinceStartup + 60f;
                while (concedido == null && Time.realtimeSinceStartup < limite) yield return null;
                if (concedido != true)
                {
                    capturando = false;
                    FotoTomada?.Invoke(false, "Para guardar la foto, permite el acceso a tus fotos.");
                    yield break;
                }
            }
#endif

            // Oculta toda la interfaz excepto el marco de la foto.
            var ocultos = new List<Canvas>();
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || !c.enabled) continue;
                if (marcoFoto != null && c.transform.IsChildOf(marcoFoto.transform)) continue;
                c.enabled = false;
                ocultos.Add(c);
            }
            if (marcoFoto != null) marcoFoto.SetActive(true);

            yield return new WaitForEndOfFrame();
            var captura = ScreenCapture.CaptureScreenshotAsTexture();

            if (marcoFoto != null) marcoFoto.SetActive(false);
            foreach (var c in ocultos) if (c != null) c.enabled = true;

            byte[] png = captura.EncodeToPNG();
            if (UltimaFoto != null) Destroy(UltimaFoto);
            UltimaFoto = captura;

            string nombre = $"CenoteVivo_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            bool guardada;
            string mensaje;
            try
            {
                ultimaUri?.Dispose();
                ultimaUri = null;
#if UNITY_ANDROID && !UNITY_EDITOR
                ultimaUri = GaleriaAndroid.Guardar(png, nombre);
                guardada = ultimaUri != null;
                mensaje = guardada ? "Foto guardada en tu galería (Imágenes › Cenote Vivo)." : "No se pudo guardar la foto.";
#else
                string ruta = Path.Combine(Application.persistentDataPath, nombre);
                File.WriteAllBytes(ruta, png);
                guardada = true;
                mensaje = "Foto guardada en " + ruta;
#endif
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                guardada = false;
                mensaje = "No se pudo guardar la foto.";
            }

            capturando = false;
            FotoTomada?.Invoke(guardada, mensaje);
        }

        public void Compartir() => GaleriaAndroid.Compartir(ultimaUri, TextoCompartir);

        void OnDestroy()
        {
            ultimaUri?.Dispose();
            if (UltimaFoto != null) Destroy(UltimaFoto);
        }
    }
}
