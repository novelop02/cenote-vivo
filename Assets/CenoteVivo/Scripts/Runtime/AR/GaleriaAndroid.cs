using System.IO;
using UnityEngine;

namespace CenoteVivo.AR
{
    /// <summary>
    /// Guardar una imagen en la galería del teléfono y abrir el menú "Compartir" de Android,
    /// usando solo llamadas JNI (sin plugins externos).
    ///  - Android 10 o superior: MediaStore con RELATIVE_PATH (no pide permisos).
    ///  - Android 8 y 9: se escribe en Pictures/Cenote Vivo (pide permiso de almacenamiento) y se registra en MediaStore.
    /// </summary>
    public static class GaleriaAndroid
    {
        public const string Carpeta = "Cenote Vivo";

        public static int VersionSdk
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using var version = new AndroidJavaClass("android.os.Build$VERSION");
                return version.GetStatic<int>("SDK_INT");
#else
                return 0;
#endif
            }
        }

        /// <summary>Android 8 y 9 necesitan permiso de escritura para la galería.</summary>
        public static bool NecesitaPermiso => VersionSdk > 0 && VersionSdk < 29;

        /// <summary>Guarda el PNG en la galería. Devuelve el content:// URI (para compartir) o null.</summary>
        public static AndroidJavaObject Guardar(byte[] png, string nombreArchivo)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            int sdk = VersionSdk;
            using var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var actividad = unity.GetStatic<AndroidJavaObject>("currentActivity");
            using var resolver = actividad.Call<AndroidJavaObject>("getContentResolver");
            using var media = new AndroidJavaClass("android.provider.MediaStore$Images$Media");
            using var destino = media.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");
            using var valores = new AndroidJavaObject("android.content.ContentValues");
            valores.Call("put", "_display_name", nombreArchivo);
            valores.Call("put", "title", Path.GetFileNameWithoutExtension(nombreArchivo));
            valores.Call("put", "mime_type", "image/png");

            if (sdk >= 29)
            {
                valores.Call("put", "relative_path", "Pictures/" + Carpeta);
                var uri = resolver.Call<AndroidJavaObject>("insert", destino, valores);
                if (uri == null) return null;
                using (var salida = resolver.Call<AndroidJavaObject>("openOutputStream", uri))
                {
                    salida.Call("write", png);
                    salida.Call("flush");
                    salida.Call("close");
                }
                return uri;
            }
            else
            {
                using var entorno = new AndroidJavaClass("android.os.Environment");
                using var imagenes = entorno.CallStatic<AndroidJavaObject>("getExternalStoragePublicDirectory",
                    entorno.GetStatic<string>("DIRECTORY_PICTURES"));
                string carpeta = Path.Combine(imagenes.Call<string>("getAbsolutePath"), Carpeta);
                Directory.CreateDirectory(carpeta);
                string ruta = Path.Combine(carpeta, nombreArchivo);
                File.WriteAllBytes(ruta, png);
                valores.Call("put", "_data", ruta);
                return resolver.Call<AndroidJavaObject>("insert", destino, valores);
            }
#else
            return null;
#endif
        }

        /// <summary>Abre el selector de Android para compartir la imagen (WhatsApp, Instagram, Facebook…).</summary>
        public static void Compartir(AndroidJavaObject uri, string texto)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (uri == null) return;
            using var claseIntent = new AndroidJavaClass("android.content.Intent");
            using var intent = new AndroidJavaObject("android.content.Intent", claseIntent.GetStatic<string>("ACTION_SEND"));
            intent.Call<AndroidJavaObject>("setType", "image/png");
            intent.Call<AndroidJavaObject>("putExtra", claseIntent.GetStatic<string>("EXTRA_STREAM"), uri);
            intent.Call<AndroidJavaObject>("putExtra", claseIntent.GetStatic<string>("EXTRA_TEXT"), texto);
            intent.Call<AndroidJavaObject>("addFlags", claseIntent.GetStatic<int>("FLAG_GRANT_READ_URI_PERMISSION"));
            using var selector = claseIntent.CallStatic<AndroidJavaObject>("createChooser", intent, "Compartir foto");
            using var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var actividad = unity.GetStatic<AndroidJavaObject>("currentActivity");
            actividad.Call("startActivity", selector);
#endif
        }
    }
}
