using UnityEngine;
using UnityEngine.UI;

namespace CenoteVivo.UI
{
    /// <summary>
    /// Genera un degradado vertical en una RawImage (sin texturas importadas: una textura de 1×64 px).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RawImage))]
    public class FondoDegradado : MonoBehaviour
    {
        [SerializeField] Color arriba = new Color(0.02f, 0.2f, 0.25f);
        [SerializeField] Color abajo = new Color(0.01f, 0.05f, 0.08f);

        Texture2D textura;

        void Awake()
        {
            textura = new Texture2D(1, 64, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            for (int y = 0; y < 64; y++)
                textura.SetPixel(0, y, Color.Lerp(abajo, arriba, y / 63f));
            textura.Apply(false, true);
            GetComponent<RawImage>().texture = textura;
        }

        void OnDestroy()
        {
            if (textura == null) return;
            if (Application.isPlaying) Destroy(textura);
            else DestroyImmediate(textura);
        }
    }
}
