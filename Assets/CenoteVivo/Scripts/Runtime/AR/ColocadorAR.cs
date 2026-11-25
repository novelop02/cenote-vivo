using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Vuforia;
using Toque = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace CenoteVivo.AR
{
    public enum EstadoAR
    {
        Iniciando,
        BuscandoSuperficie,   // Ground Plane: aún no se detecta la mesa/piso
        ListoParaColocar,     // Ground Plane: hay superficie bajo el indicador
        Colocado,             // Ground Plane: cenote anclado
        BuscandoMarcador,     // Marcador impreso: no está a la vista
        SobreMarcador,        // Marcador impreso: cenote visible
        Error,
    }

    /// <summary>
    /// Funcionalidad 1, "El cenote sobre la mesa".
    ///
    /// Elige el método de AR según el teléfono:
    ///  - Superficie (Vuforia Ground Plane): si el equipo admite anclas (ARCore u otro rastreo de
    ///    dispositivo), se detecta la mesa o el piso y el usuario toca para colocar el cenote.
    ///  - Marcador impreso (Vuforia Image Target): en teléfonos sin ese soporte (gama baja), el cenote
    ///    aparece sobre la imagen impresa de Cenote Vivo, que el usuario pone en la mesa o el piso.
    ///
    /// Gestos sobre el cenote ya colocado (ambos métodos):
    ///  - Un dedo: lo arrastra por la superficie (traslación).
    ///  - Dos dedos: pellizcar cambia su tamaño (escalado) y girarlos lo rota.
    ///  - En el editor: clic izquierdo arrastra, rueda escala, clic derecho gira.
    /// </summary>
    public class ColocadorAR : MonoBehaviour
    {
        [Header("Contenido")]
        [Tooltip("Raíz de la miniatura (se mueve, gira y escala). Su hijo es el espacio del cenote.")]
        [SerializeField] Transform contenido;
        [Tooltip("Escala de la miniatura al colocarla (el cenote mide unos 35 cm de diámetro).")]
        [SerializeField] float escalaBase = 0.01f;
        [SerializeField] float escalaMinima = 0.35f;
        [SerializeField] float escalaMaxima = 3f;

        [Header("Superficie (Ground Plane)")]
        [SerializeField] PlaneFinderBehaviour buscadorPlanos;
        [SerializeField] GameObject indicador;
        [Tooltip("Segundos que se considera válido el último impacto del buscador de planos.")]
        [SerializeField] float vigenciaImpacto = 0.6f;

        [Header("Marcador impreso (Image Target)")]
        [SerializeField] Texture2D imagenMarcador;
        [Tooltip("Ancho real del marcador impreso, en metros.")]
        [SerializeField] float anchoMarcador = 0.2f;

        public EstadoAR Estado { get; private set; } = EstadoAR.Iniciando;
        public bool UsandoMarcador { get; private set; }
        public bool SuperficieDisponible { get; private set; }
        /// <summary>El cenote está en pantalla y se puede manipular.</summary>
        public bool ContenidoVisible => Estado == EstadoAR.Colocado || Estado == EstadoAR.SobreMarcador;
        /// <summary>Escala actual respecto a la inicial (1 = tamaño al colocarlo).</summary>
        public float FactorEscala { get; private set; } = 1f;

        public event Action<EstadoAR> EstadoCambiado;

        HitTestResult ultimoImpacto;
        float tiempoImpacto = -10f;
        AnchorBehaviour ancla;
        ImageTargetBehaviour marcador;
        Camera camara;

        // Gestos
        bool arrastrando;
        Vector3 desplazamientoArrastre;
        float distanciaInicial, escalaInicialGesto, anguloAnterior;
        bool gestoDosDedos;
        Vector2 inicioToque;
        float tiempoInicioToque;
        bool toqueSobreUI;
        static readonly List<RaycastResult> resultadosUI = new List<RaycastResult>();

        void OnEnable()
        {
            EnhancedTouchSupport.Enable();
            VuforiaApplication.Instance.OnVuforiaStarted += AlIniciarVuforia;
            VuforiaApplication.Instance.OnVuforiaError += AlFallarVuforia;
        }

        void OnDisable()
        {
            VuforiaApplication.Instance.OnVuforiaStarted -= AlIniciarVuforia;
            VuforiaApplication.Instance.OnVuforiaError -= AlFallarVuforia;
            if (buscadorPlanos != null) buscadorPlanos.OnAutomaticHitTest.RemoveListener(AlDetectarSuperficie);
            if (marcador != null) marcador.OnTargetStatusChanged -= AlCambiarMarcador;
            EnhancedTouchSupport.Disable();
        }

        void Start()
        {
            camara = Camera.main;
            // Un teléfono modesto no necesita más de 30 cuadros por segundo para esta experiencia.
            Application.targetFrameRate = 30;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            contenido.gameObject.SetActive(false);
            if (indicador != null) indicador.SetActive(false);
            if (buscadorPlanos != null) buscadorPlanos.gameObject.SetActive(false);
            if (VuforiaApplication.Instance.IsRunning) AlIniciarVuforia();
        }

        void OnDestroy() => Screen.sleepTimeout = SleepTimeout.SystemSetting;

        // ───────────────────────────── Elección del método ─────────────────────────────

        void AlIniciarVuforia()
        {
            if (Estado != EstadoAR.Iniciando) return;
            var vuforia = VuforiaBehaviour.Instance;
            SuperficieDisponible = vuforia != null && vuforia.World != null && vuforia.World.AnchorsSupported
                                   && buscadorPlanos != null;
            if (SuperficieDisponible) UsarSuperficie();
            else UsarMarcador();
        }

        void AlFallarVuforia(VuforiaEngineError error)
        {
            Debug.LogWarning($"[Cenote Vivo AR] Error de Vuforia: {error}");
            CambiarEstado(EstadoAR.Error);
        }

        void UsarSuperficie()
        {
            UsandoMarcador = false;
            buscadorPlanos.gameObject.SetActive(true);
            buscadorPlanos.HitTestMode = HitTestMode.AUTOMATIC;
            buscadorPlanos.PlaneIndicator = indicador;
            buscadorPlanos.OnAutomaticHitTest.AddListener(AlDetectarSuperficie);
            CambiarEstado(EstadoAR.BuscandoSuperficie);
        }

        /// <summary>Cambia al marcador impreso (automático en teléfonos sin Ground Plane, o a petición del usuario).</summary>
        public void UsarMarcador()
        {
            if (UsandoMarcador) return;
            QuitarAncla();
            if (buscadorPlanos != null)
            {
                buscadorPlanos.OnAutomaticHitTest.RemoveListener(AlDetectarSuperficie);
                buscadorPlanos.gameObject.SetActive(false);
            }
            if (indicador != null) indicador.SetActive(false);
            UsandoMarcador = true;

            if (marcador == null)
            {
                if (imagenMarcador == null || VuforiaBehaviour.Instance == null)
                {
                    Debug.LogError("[Cenote Vivo AR] No hay imagen de marcador.");
                    CambiarEstado(EstadoAR.Error);
                    return;
                }
                marcador = VuforiaBehaviour.Instance.ObserverFactory.CreateImageTarget(imagenMarcador, anchoMarcador, "CenoteVivoMarcador");
                marcador.OnTargetStatusChanged += AlCambiarMarcador;
            }
            contenido.SetParent(marcador.transform, false);
            ReiniciarPose();
            contenido.gameObject.SetActive(false);
            CambiarEstado(EstadoAR.BuscandoMarcador);
        }

        /// <summary>Vuelve a la detección de superficies (solo si el teléfono la admite).</summary>
        public void UsarSuperficieDeNuevo()
        {
            if (!SuperficieDisponible || !UsandoMarcador) return;
            if (marcador != null) { marcador.OnTargetStatusChanged -= AlCambiarMarcador; Destroy(marcador.gameObject); marcador = null; }
            contenido.SetParent(null, false);
            contenido.gameObject.SetActive(false);
            UsarSuperficie();
        }

        // ───────────────────────────── Superficie ─────────────────────────────

        void AlDetectarSuperficie(HitTestResult impacto)
        {
            if (impacto == null || Estado == EstadoAR.Colocado) return;
            ultimoImpacto = impacto;
            tiempoImpacto = Time.time;
        }

        bool HayImpactoReciente => ultimoImpacto != null && Time.time - tiempoImpacto < vigenciaImpacto;

        void Colocar()
        {
            ancla = VuforiaBehaviour.Instance.ObserverFactory.CreateAnchorBehaviour("Ancla Cenote", ultimoImpacto);
            contenido.SetParent(ancla.transform, false);
            ReiniciarPose();
            contenido.gameObject.SetActive(true);

            // Deja de buscar planos: ahorra CPU y batería en teléfonos modestos.
            buscadorPlanos.OnAutomaticHitTest.RemoveListener(AlDetectarSuperficie);
            buscadorPlanos.gameObject.SetActive(false);
            if (indicador != null) indicador.SetActive(false);
            CambiarEstado(EstadoAR.Colocado);
        }

        void QuitarAncla()
        {
            if (ancla == null) return;
            contenido.SetParent(null, false);
            Destroy(ancla.gameObject);
            ancla = null;
        }

        /// <summary>Botón "Recolocar": en superficie vuelve a buscar dónde ponerlo; con marcador, restablece tamaño y posición.</summary>
        public void Recolocar()
        {
            if (UsandoMarcador) { ReiniciarPose(); return; }
            if (!SuperficieDisponible) return;
            QuitarAncla();
            contenido.gameObject.SetActive(false);
            ultimoImpacto = null;
            buscadorPlanos.gameObject.SetActive(true);
            buscadorPlanos.OnAutomaticHitTest.RemoveListener(AlDetectarSuperficie);
            buscadorPlanos.OnAutomaticHitTest.AddListener(AlDetectarSuperficie);
            CambiarEstado(EstadoAR.BuscandoSuperficie);
        }

        // ───────────────────────────── Marcador ─────────────────────────────

        void AlCambiarMarcador(ObserverBehaviour observador, TargetStatus estado)
        {
            bool visible = estado.Status == Status.TRACKED || estado.Status == Status.EXTENDED_TRACKED;
            contenido.gameObject.SetActive(visible);
            CambiarEstado(visible ? EstadoAR.SobreMarcador : EstadoAR.BuscandoMarcador);
        }

        // ───────────────────────────── Pose y gestos ─────────────────────────────

        void ReiniciarPose()
        {
            FactorEscala = 1f;
            contenido.localPosition = Vector3.zero;
            AplicarEscala();
            // De frente al usuario: el frente del cenote (el lado -Z, el de la "Vista general" de la
            // escena 3D) mira hacia la cámara, así el corte y las etiquetas se leen de inmediato.
            // Con el marcador, el frente del cenote sigue al borde inferior de la imagen impresa.
            var padre = contenido.parent;
            if (UsandoMarcador) contenido.localRotation = Quaternion.identity;
            else if (camara != null && padre != null)
            {
                var haciaCamara = padre.InverseTransformDirection(camara.transform.position - padre.position);
                haciaCamara.y = 0f;
                contenido.localRotation = haciaCamara.sqrMagnitude > 1e-6f
                    ? Quaternion.LookRotation(-haciaCamara.normalized, Vector3.up)
                    : Quaternion.identity;
            }
            else contenido.localRotation = Quaternion.identity;
        }

        void AplicarEscala()
        {
            // El Image Target de Vuforia tiene su propia escala: se compensa para que el tamaño sea el mismo.
            float padre = contenido.parent != null ? Mathf.Max(1e-4f, contenido.parent.lossyScale.x) : 1f;
            contenido.localScale = Vector3.one * (escalaBase * FactorEscala / padre);
        }

        void Update()
        {
            switch (Estado)
            {
                case EstadoAR.BuscandoSuperficie when HayImpactoReciente:
                    CambiarEstado(EstadoAR.ListoParaColocar);
                    break;
                case EstadoAR.ListoParaColocar when !HayImpactoReciente:
                    CambiarEstado(EstadoAR.BuscandoSuperficie);
                    break;
            }

            if (Touchscreen.current != null && Toque.activeTouches.Count > 0) ProcesarToques();
            else
            {
                if (gestoDosDedos || arrastrando) { gestoDosDedos = false; arrastrando = false; }
                ProcesarRaton();
            }
        }

        void ProcesarToques()
        {
            var toques = Toque.activeTouches;

            if (toques.Count == 1)
            {
                var t = toques[0];
                if (t.phase == UnityEngine.InputSystem.TouchPhase.Began)
                {
                    toqueSobreUI = SobreUI(t.screenPosition);
                    inicioToque = t.screenPosition;
                    tiempoInicioToque = Time.time;
                    gestoDosDedos = false;
                    if (!toqueSobreUI && ContenidoVisible) arrastrando = EmpezarArrastre(t.screenPosition);
                }
                else if (t.phase == UnityEngine.InputSystem.TouchPhase.Moved && arrastrando && !gestoDosDedos)
                {
                    Arrastrar(t.screenPosition);
                }
                else if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended)
                {
                    bool esToqueCorto = Time.time - tiempoInicioToque < 0.35f && (t.screenPosition - inicioToque).magnitude < 40f;
                    if (esToqueCorto && !toqueSobreUI && !gestoDosDedos) AlTocar();
                    arrastrando = false;
                }
                return;
            }

            // Dos dedos: pellizcar = escala, girar = rotación.
            if (!ContenidoVisible || toqueSobreUI) return;
            var a = toques[0].screenPosition;
            var b = toques[1].screenPosition;
            float distancia = Vector2.Distance(a, b);
            float angulo = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            if (!gestoDosDedos)
            {
                gestoDosDedos = true;
                arrastrando = false;
                distanciaInicial = Mathf.Max(1f, distancia);
                escalaInicialGesto = FactorEscala;
                anguloAnterior = angulo;
                return;
            }
            Escalar(escalaInicialGesto * distancia / distanciaInicial);
            Girar(Mathf.DeltaAngle(anguloAnterior, angulo));
            anguloAnterior = angulo;
        }

        void ProcesarRaton()
        {
            var raton = Mouse.current;
            if (raton == null) return;
            var pos = raton.position.ReadValue();

            if (raton.leftButton.wasPressedThisFrame)
            {
                toqueSobreUI = SobreUI(pos);
                inicioToque = pos;
                tiempoInicioToque = Time.time;
                if (!toqueSobreUI && ContenidoVisible) arrastrando = EmpezarArrastre(pos);
            }
            else if (raton.leftButton.isPressed && arrastrando) Arrastrar(pos);
            else if (raton.leftButton.wasReleasedThisFrame)
            {
                if (!toqueSobreUI && (pos - inicioToque).magnitude < 10f) AlTocar();
                arrastrando = false;
            }

            if (!ContenidoVisible || SobreUI(pos)) return;
            float rueda = raton.scroll.ReadValue().y;
            if (Mathf.Abs(rueda) > 0.01f) Escalar(FactorEscala * (1f + Mathf.Sign(rueda) * 0.1f));
            if (raton.rightButton.isPressed) Girar(raton.delta.ReadValue().x * 0.4f);
        }

        void AlTocar()
        {
            if (Estado == EstadoAR.ListoParaColocar && HayImpactoReciente) Colocar();
        }

        // La traslación ocurre sobre el plano de la superficie (o del marcador) donde está apoyado el cenote.
        bool RayoAlPlano(Vector2 pantalla, out Vector3 punto)
        {
            punto = default;
            var padre = contenido.parent;
            if (camara == null || padre == null) return false;
            var plano = new Plane(padre.up, padre.position);
            var rayo = camara.ScreenPointToRay(pantalla);
            if (!plano.Raycast(rayo, out float d)) return false;
            punto = rayo.GetPoint(d);
            return true;
        }

        bool EmpezarArrastre(Vector2 pantalla)
        {
            if (!RayoAlPlano(pantalla, out var punto)) return false;
            desplazamientoArrastre = contenido.position - punto;
            return true;
        }

        void Arrastrar(Vector2 pantalla)
        {
            if (!RayoAlPlano(pantalla, out var punto)) return;
            contenido.position = punto + desplazamientoArrastre;
            var local = contenido.localPosition;
            local.y = 0f; // siempre apoyado
            // Con marcador no se aleja demasiado: si sale del campo de la cámara se pierde el rastreo.
            float limite = UsandoMarcador ? anchoMarcador * 4f / Mathf.Max(1e-4f, contenido.parent.lossyScale.x) : float.MaxValue;
            if (local.magnitude > limite) local = local.normalized * limite;
            contenido.localPosition = local;
        }

        void Escalar(float factor)
        {
            FactorEscala = Mathf.Clamp(factor, escalaMinima, escalaMaxima);
            AplicarEscala();
        }

        void Girar(float grados) => contenido.Rotate(Vector3.up, -grados, Space.Self);

        static bool SobreUI(Vector2 pantalla)
        {
            var sistema = EventSystem.current;
            if (sistema == null) return false;
            resultadosUI.Clear();
            sistema.RaycastAll(new PointerEventData(sistema) { position = pantalla }, resultadosUI);
            return resultadosUI.Count > 0;
        }

        void CambiarEstado(EstadoAR nuevo)
        {
            if (Estado == nuevo) return;
            Estado = nuevo;
            EstadoCambiado?.Invoke(nuevo);
        }
    }
}
