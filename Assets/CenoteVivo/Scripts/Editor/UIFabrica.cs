using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CenoteVivo.EditorTools
{
    /// <summary>
    /// Utilidades para construir la interfaz (uGUI) desde código con un estilo consistente.
    /// </summary>
    static class UIFabrica
    {
        public static readonly Color FondoPanel = new Color(0.03f, 0.11f, 0.13f, 0.86f);
        public static readonly Color Acento = new Color(0.18f, 0.72f, 0.66f);
        public static readonly Color AcentoOscuro = new Color(0.05f, 0.3f, 0.3f, 0.95f);
        public static readonly Color Alerta = new Color(0.86f, 0.5f, 0.18f);
        public static readonly Color TextoClaro = new Color(0.93f, 0.97f, 0.96f);
        public static readonly Color TextoSecundario = new Color(0.62f, 0.8f, 0.78f);
        public static readonly Color TextoOscuro = new Color(0.02f, 0.1f, 0.1f);

        const string CarpetaTexturas = "Assets/CenoteVivo/Texturas";

        static Font fuente;
        static Sprite redondeado;
        static Sprite circulo;

        public static Font Fuente => fuente != null ? fuente : (fuente = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        public static Sprite Redondeado => redondeado != null ? redondeado : (redondeado = CrearSprite("UI_Redondeado.png", 96, 30f, 0f, 34));
        public static Sprite Circulo => circulo != null ? circulo : (circulo = CrearSprite("UI_Circulo.png", 96, 48f, 0f, 0));

        /// <summary>Textura suave para partículas (círculo con borde difuso).</summary>
        public static Texture2D TexturaParticula()
        {
            var sprite = CrearSprite("Particula_Suave.png", 64, 32f, 30f, 0);
            return sprite != null ? sprite.texture : null;
        }

        // Genera (una sola vez) una textura de forma redondeada y la importa como Sprite.
        static Sprite CrearSprite(string nombre, int tam, float radio, float difuminado, int borde)
        {
            string ruta = $"{CarpetaTexturas}/{nombre}";
            if (!File.Exists(ruta))
            {
                Directory.CreateDirectory(CarpetaTexturas);
                var tex = new Texture2D(tam, tam, TextureFormat.RGBA32, false);
                float c = (tam - 1) * 0.5f;
                for (int y = 0; y < tam; y++)
                for (int x = 0; x < tam; x++)
                {
                    // Distancia a un rectángulo redondeado (o círculo si radio = tam/2).
                    float dx = Mathf.Max(Mathf.Abs(x - c) - (c - radio), 0f);
                    float dy = Mathf.Max(Mathf.Abs(y - c) - (c - radio), 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) - radio;
                    float a = difuminado > 0f
                        ? Mathf.Clamp01(-d / difuminado)
                        : Mathf.Clamp01(0.5f - d);
                    if (difuminado > 0f) a *= a;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
                File.WriteAllBytes(ruta, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(ruta);

                var imp = (TextureImporter)AssetImporter.GetAtPath(ruta);
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.spriteBorder = new Vector4(borde, borde, borde, borde);
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(ruta);
        }

        public static RectTransform Crear(string nombre, Transform padre)
        {
            var go = new GameObject(nombre, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            var rt = (RectTransform)go.transform;
            rt.SetParent(padre, false);
            return rt;
        }

        public static void Anclar(RectTransform rt, Vector2 ancla, Vector2 posicion, Vector2 tam)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = ancla;
            rt.anchoredPosition = posicion;
            rt.sizeDelta = tam;
        }

        public static void Estirar(RectTransform rt, float izq = 0, float der = 0, float arriba = 0, float abajo = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(izq, abajo);
            rt.offsetMax = new Vector2(-der, -arriba);
        }

        public static Image Panel(string nombre, Transform padre, Color color, bool redondo = true)
        {
            var rt = Crear(nombre, padre);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (redondo)
            {
                img.sprite = Redondeado;
                img.type = Image.Type.Sliced;
            }
            return img;
        }

        public static Text Texto(string nombre, Transform padre, string contenido, int tam, Color color,
            TextAnchor alineacion = TextAnchor.MiddleCenter, FontStyle estilo = FontStyle.Normal)
        {
            var rt = Crear(nombre, padre);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Fuente;
            t.text = contenido;
            t.fontSize = tam;
            t.color = color;
            t.alignment = alineacion;
            t.fontStyle = estilo;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            return t;
        }

        public static Button Boton(string nombre, Transform padre, string etiqueta, Color fondo, Color colorTexto,
            int tamTexto, out Text texto)
        {
            var img = Panel(nombre, padre, fondo);
            var boton = img.gameObject.AddComponent<Button>();
            boton.targetGraphic = img;
            var colores = boton.colors;
            colores.normalColor = Color.white;
            colores.highlightedColor = new Color(0.92f, 0.92f, 0.92f);
            colores.pressedColor = new Color(0.72f, 0.72f, 0.72f);
            colores.selectedColor = Color.white;
            colores.fadeDuration = 0.08f;
            boton.colors = colores;
            // Sin navegación por teclado: evita que el botón quede "seleccionado" tras tocarlo.
            boton.navigation = new Navigation { mode = Navigation.Mode.None };

            texto = Texto("Texto", img.transform, etiqueta, tamTexto, colorTexto, TextAnchor.MiddleCenter, FontStyle.Bold);
            Estirar(texto.rectTransform, 16, 16, 6, 6);
            return boton;
        }

        /// <summary>Botón de dos líneas: un rótulo pequeño y un valor grande (p. ej. "ILUMINACIÓN / Día").</summary>
        public static Button BotonConRotulo(string nombre, Transform padre, string rotulo, string valor, out Text textoValor)
        {
            var boton = Boton(nombre, padre, valor, FondoPanel, TextoClaro, 34, out textoValor);
            Estirar(textoValor.rectTransform, 16, 16, 38, 8);
            var r = Texto("Rotulo", boton.transform, rotulo, 20, TextoSecundario, TextAnchor.UpperCenter, FontStyle.Bold);
            Estirar(r.rectTransform, 16, 16, 12, 60);
            return boton;
        }

        public static Slider Deslizador(string nombre, Transform padre, Color relleno)
        {
            var raiz = Crear(nombre, padre);
            var slider = raiz.gameObject.AddComponent<Slider>();

            var fondo = Panel("Fondo", raiz, new Color(1f, 1f, 1f, 0.15f));
            fondo.rectTransform.anchorMin = new Vector2(0f, 0.3f);
            fondo.rectTransform.anchorMax = new Vector2(1f, 0.7f);
            fondo.rectTransform.offsetMin = fondo.rectTransform.offsetMax = Vector2.zero;

            var areaRelleno = Crear("Area Relleno", raiz);
            areaRelleno.anchorMin = new Vector2(0f, 0.3f);
            areaRelleno.anchorMax = new Vector2(1f, 0.7f);
            areaRelleno.offsetMin = new Vector2(8f, 0f);
            areaRelleno.offsetMax = new Vector2(-8f, 0f);
            var rellenoImg = Panel("Relleno", areaRelleno, relleno);
            rellenoImg.rectTransform.offsetMin = new Vector2(-8f, 0f);
            rellenoImg.rectTransform.offsetMax = new Vector2(8f, 0f);

            var areaManija = Crear("Area Manija", raiz);
            Estirar(areaManija, 24, 24);
            var manija = Panel("Manija", areaManija, Color.white);
            manija.sprite = Circulo;
            manija.type = Image.Type.Simple;
            manija.rectTransform.sizeDelta = new Vector2(48f, 0f);

            slider.fillRect = rellenoImg.rectTransform;
            slider.handleRect = manija.rectTransform;
            slider.targetGraphic = manija;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            return slider;
        }

        /// <summary>
        /// Crea un Canvas (Screen Space Overlay) por pantalla. Separar pantallas en Canvas distintos
        /// evita que un cambio en una (p. ej. abrir el panel de información) reconstruya las demás.
        /// </summary>
        public static Canvas Lienzo(string nombre, int orden, out RectTransform areaSegura)
        {
            var go = new GameObject(nombre, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = orden;
            canvas.pixelPerfect = false;
            var escalador = go.AddComponent<CanvasScaler>();
            escalador.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            escalador.referenceResolution = new Vector2(1920f, 1080f);
            escalador.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            escalador.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            areaSegura = Crear("Area Segura", go.transform);
            Estirar(areaSegura);
            areaSegura.gameObject.AddComponent<CenoteVivo.UI.AreaSegura>();
            return canvas;
        }

        public static void SistemaDeEventos()
        {
            var go = new GameObject("EventSystem", typeof(EventSystem));
            var modulo = go.AddComponent<InputSystemUIInputModule>();
            modulo.AssignDefaultActions();
        }

        /// <summary>Panel modal a pantalla completa con tarjeta, título, texto desplazable y botón Cerrar.</summary>
        public static CenoteVivo.UI.PanelDesvanecible Modal(string nombre, Transform padre, Vector2 tamTarjeta,
            out Text titulo, out Text cuerpo, out Button cerrar)
        {
            var velo = Panel(nombre, padre, new Color(0f, 0.02f, 0.03f, 0.7f), false);
            Estirar(velo.rectTransform);
            // Oculto en el editor también, para no tapar la escena mientras se edita.
            var grupo = velo.gameObject.AddComponent<CanvasGroup>();
            grupo.alpha = 0f;
            grupo.blocksRaycasts = false;
            grupo.interactable = false;
            var modal = velo.gameObject.AddComponent<CenoteVivo.UI.PanelDesvanecible>();

            var tarjeta = Panel("Tarjeta", velo.transform, new Color(0.04f, 0.14f, 0.16f, 0.97f));
            Anclar(tarjeta.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, tamTarjeta);

            titulo = Texto("Titulo", tarjeta.transform, "", 44, TextoClaro, TextAnchor.MiddleLeft, FontStyle.Bold);
            var tr = titulo.rectTransform;
            tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f); tr.pivot = new Vector2(0.5f, 1f);
            tr.offsetMin = new Vector2(56f, -120f); tr.offsetMax = new Vector2(-260f, -30f);

            cerrar = Boton("Cerrar", tarjeta.transform, "Cerrar", Acento, TextoOscuro, 30, out _);
            var cr = (RectTransform)cerrar.transform;
            Anclar(cr, new Vector2(1f, 1f), new Vector2(-40f, -36f), new Vector2(190f, 80f));

            var linea = Panel("Separador", tarjeta.transform, new Color(1f, 1f, 1f, 0.12f), false);
            var lr = linea.rectTransform;
            lr.anchorMin = new Vector2(0f, 1f); lr.anchorMax = new Vector2(1f, 1f); lr.pivot = new Vector2(0.5f, 1f);
            lr.offsetMin = new Vector2(56f, -138f); lr.offsetMax = new Vector2(-56f, -136f);

            // Área desplazable (RectMask2D es más barato que Mask en móviles).
            var vista = Crear("Vista", tarjeta.transform);
            vista.anchorMin = Vector2.zero; vista.anchorMax = Vector2.one;
            vista.offsetMin = new Vector2(56f, 40f); vista.offsetMax = new Vector2(-56f, -160f);
            vista.gameObject.AddComponent<RectMask2D>();
            var zonaToque = vista.gameObject.AddComponent<Image>();
            zonaToque.color = new Color(0f, 0f, 0f, 0f);

            var contenido = Crear("Contenido", vista);
            contenido.anchorMin = new Vector2(0f, 1f); contenido.anchorMax = new Vector2(1f, 1f); contenido.pivot = new Vector2(0.5f, 1f);
            contenido.offsetMin = contenido.offsetMax = Vector2.zero;
            var ajuste = contenido.gameObject.AddComponent<VerticalLayoutGroup>();
            ajuste.childControlHeight = true; ajuste.childControlWidth = true;
            ajuste.childForceExpandHeight = false; ajuste.padding = new RectOffset(0, 16, 0, 24);
            contenido.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            cuerpo = Texto("Cuerpo", contenido, "", 30, TextoClaro, TextAnchor.UpperLeft);
            cuerpo.verticalOverflow = VerticalWrapMode.Overflow;
            cuerpo.lineSpacing = 1.15f;

            var scroll = vista.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = vista;
            scroll.content = contenido;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 40f;
            return modal;
        }
    }
}
