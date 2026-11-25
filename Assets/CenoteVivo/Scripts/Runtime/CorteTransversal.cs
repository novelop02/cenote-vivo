using UnityEngine;

namespace CenoteVivo
{
    /// <summary>
    /// Corte transversal del cenote para la vista de subsuelo. Los Lit Shader Graph (SG_Roca,
    /// SG_Vegetacion, SG_Agua) recortan con Alpha Clip todo lo que queda del lado de la cámara y
    /// pintan las caras interiores como la sección: estratos de roca, la cavidad con agua y la pluma
    /// de contaminación infiltrándose. No modifica mallas ni materiales.
    /// También publica los datos del terreno que usan los shaders (altura, profundidad, nivel del agua).
    /// </summary>
    [ExecuteAlways]
    public class CorteTransversal : MonoBehaviour
    {
        static readonly int PlanoId = Shader.PropertyToID("_CenotePlanoCorte");
        static readonly int ActivoId = Shader.PropertyToID("_CenoteCorteActivo");
        static readonly int AlturaId = Shader.PropertyToID("_CenoteAlturaSuperficie");
        static readonly int ProfundidadId = Shader.PropertyToID("_CenoteProfundidad");
        static readonly int NivelAguaId = Shader.PropertyToID("_CenoteNivelAgua");

        [Tooltip("Normal del plano de corte (apunta hacia la cámara de la vista de corte).")]
        [SerializeField] Vector3 normal = Vector3.back;
        [SerializeField] Vector3 puntoDelPlano;
        [SerializeField] float alturaSuperficie;
        [SerializeField] float profundidad = 30f;
        [SerializeField] float nivelAgua;

        bool nieblaAntes;

        public bool Activo { get; private set; }

        void OnEnable()
        {
            Shader.SetGlobalFloat(AlturaId, alturaSuperficie);
            Shader.SetGlobalFloat(ProfundidadId, profundidad);
            Shader.SetGlobalFloat(NivelAguaId, nivelAgua);
            Activar(false);
        }

        void OnDisable() => Activar(false);

        public void Activar(bool activo)
        {
            if (activo == Activo && Application.isPlaying) return;

            // La sección se ve a través del corte: sin niebla para que los estratos lejanos no se deslaven.
            if (Application.isPlaying)
            {
                if (activo) { nieblaAntes = RenderSettings.fog; RenderSettings.fog = false; }
                else if (Activo) RenderSettings.fog = nieblaAntes;
            }

            Activo = activo;
            PublicarPlano();
            Shader.SetGlobalFloat(ActivoId, activo ? 1f : 0f);
        }

        /// <summary>Punto por el que pasa el plano (espacio del cenote).</summary>
        public Vector3 PuntoDelPlano => puntoDelPlano;

        /// <summary>
        /// Cambia la orientación del plano (espacio del cenote). En AR se llama cada cuadro para que
        /// el corte quede siempre de frente al usuario, camine por donde camine alrededor de la mesa.
        /// </summary>
        public void FijarNormal(Vector3 nuevaNormal)
        {
            if (nuevaNormal.sqrMagnitude < 1e-6f) return;
            normal = nuevaNormal.normalized;
            PublicarPlano();
        }

        void PublicarPlano()
        {
            var n = normal.normalized;
            Shader.SetGlobalVector(PlanoId, new Vector4(n.x, n.y, n.z, Vector3.Dot(n, puntoDelPlano)));
        }
    }
}
