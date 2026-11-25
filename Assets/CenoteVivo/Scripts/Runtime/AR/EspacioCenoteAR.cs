using UnityEngine;
using UnityEngine.Rendering;

namespace CenoteVivo.AR
{
    /// <summary>
    /// Mantiene coherentes los efectos del cenote cuando es una miniatura en realidad aumentada.
    ///
    /// Los shaders calculan el corte del subsuelo, los estratos y la pluma de contaminación con
    /// alturas y planos definidos en el "espacio del cenote" (el mundo original de la escena 3D).
    /// En AR el cenote se mueve, gira y escala sobre la mesa, así que antes de dibujar cada cámara
    /// se publica la matriz mundo → espacio del cenote. Además:
    ///  - escala el alcance de las luces puntuales (faroles) con el tamaño de la miniatura;
    ///  - con el corte activo, gira el plano para que quede siempre de frente al usuario.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class EspacioCenoteAR : MonoBehaviour
    {
        static readonly int ActivoId = Shader.PropertyToID("_CenoteEspacioAR");
        static readonly int MundoALocalId = Shader.PropertyToID("_CenoteMundoALocal");
        static readonly int LocalAMundoId = Shader.PropertyToID("_CenoteLocalAMundo");

        [Tooltip("Objeto cuyo espacio local es el mundo original del cenote (padre del modelo, la selva y las partículas).")]
        [SerializeField] Transform espacioCenote;
        [SerializeField] CorteTransversal corte;
        [Tooltip("Faroles: su alcance se escala con la miniatura.")]
        [SerializeField] Light[] lucesPuntuales = System.Array.Empty<Light>();

        float[] alcancesBase;
        Camera camara;

        public Transform Espacio => espacioCenote;

        void Awake()
        {
            alcancesBase = new float[lucesPuntuales.Length];
            for (int i = 0; i < lucesPuntuales.Length; i++)
                alcancesBase[i] = lucesPuntuales[i] != null ? lucesPuntuales[i].range : 0f;
        }

        void OnEnable()
        {
            Shader.SetGlobalFloat(ActivoId, 1f);
            RenderPipelineManager.beginCameraRendering += AntesDeDibujar;
            Publicar();
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= AntesDeDibujar;
            // Las otras escenas (3D y menú) usan las posiciones de mundo tal cual.
            Shader.SetGlobalFloat(ActivoId, 0f);
        }

        void LateUpdate()
        {
            Publicar();

            float escala = espacioCenote != null ? espacioCenote.lossyScale.x : 1f;
            for (int i = 0; i < lucesPuntuales.Length; i++)
                if (lucesPuntuales[i] != null) lucesPuntuales[i].range = alcancesBase[i] * escala;

            if (corte != null && corte.Activo) OrientarCorte();
        }

        // Vuforia actualiza las poses justo antes de dibujar: se publica otra vez para no tener un cuadro de retraso.
        void AntesDeDibujar(ScriptableRenderContext contexto, Camera cam) => Publicar();

        void Publicar()
        {
            if (espacioCenote == null) return;
            Shader.SetGlobalMatrix(MundoALocalId, espacioCenote.worldToLocalMatrix);
            Shader.SetGlobalMatrix(LocalAMundoId, espacioCenote.localToWorldMatrix);
        }

        void OrientarCorte()
        {
            if (camara == null) camara = Camera.main;
            if (camara == null || espacioCenote == null) return;
            // Dirección horizontal del centro del corte hacia el usuario, en el espacio del cenote.
            var haciaUsuario = espacioCenote.InverseTransformPoint(camara.transform.position) - corte.PuntoDelPlano;
            haciaUsuario.y = 0f;
            corte.FijarNormal(haciaUsuario);
        }
    }
}
