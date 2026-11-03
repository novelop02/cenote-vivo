using System;
using System.Collections;
using UnityEngine;

namespace CenoteVivo
{
    /// <summary>
    /// Selector de perspectiva: alterna entre puntos de vista fijos con una transición suave.
    /// Cuando la cámara queda por debajo del nivel del agua activa una niebla densa cuyo color
    /// depende del nivel de contaminación (agua cristalina vs. turbia).
    /// </summary>
    public class CameraViewController : MonoBehaviour
    {
        [Serializable]
        public class Vista
        {
            public string nombre = "Vista";
            [TextArea] public string descripcion;
            public Transform punto;
            [Range(20f, 90f)] public float campoVision = 55f;
            [Tooltip("Activa el corte transversal mientras esta vista está seleccionada.")]
            public bool corte;
        }

        [SerializeField] Camera camara;
        [SerializeField] Vista[] vistas = Array.Empty<Vista>();
        [SerializeField] float duracionTransicion = 1.4f;
        [SerializeField] CorteTransversal corte;

        [Header("Bajo el agua")]
        [Tooltip("Malla de la superficie del agua: la cámara está sumergida si queda debajo de ella y dentro de su área.")]
        [SerializeField] Renderer superficieAgua;
        [SerializeField] PollutionController contaminacion;
        [SerializeField] DayNightController diaNoche;
        [SerializeField] float densidadNieblaLimpia = 0.02f;
        [SerializeField] float densidadNieblaContaminada = 0.12f;

        int indice;
        Coroutine transicion;
        bool sumergida;
        bool nieblaOriginal;
        FogMode modoNieblaOriginal;
        float densidadOriginal;

        public int Indice => indice;
        public Vista VistaActual => vistas.Length > 0 ? vistas[indice] : null;
        public event Action<Vista> VistaCambiada;

        void Awake()
        {
            if (camara == null) camara = Camera.main;
            nieblaOriginal = RenderSettings.fog;
            modoNieblaOriginal = RenderSettings.fogMode;
            densidadOriginal = RenderSettings.fogDensity;
        }

        void Start()
        {
            if (vistas.Length == 0) return;
            Colocar(vistas[indice]);
            corte?.Activar(vistas[indice].corte);
            VistaCambiada?.Invoke(vistas[indice]);
        }

        public void Siguiente() => IrA((indice + 1) % Mathf.Max(1, vistas.Length));

        public void IrA(int nuevo)
        {
            if (vistas.Length == 0 || nuevo < 0 || nuevo >= vistas.Length) return;
            indice = nuevo;
            if (transicion != null) StopCoroutine(transicion);
            transicion = StartCoroutine(Transicion(vistas[indice]));
            corte?.Activar(vistas[indice].corte);
            VistaCambiada?.Invoke(vistas[indice]);
        }

        IEnumerator Transicion(Vista destino)
        {
            var t0 = camara.transform;
            Vector3 p0 = t0.position;
            Quaternion r0 = t0.rotation;
            float f0 = camara.fieldOfView;

            for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, duracionTransicion))
            {
                float e = Mathf.SmoothStep(0f, 1f, t);
                t0.SetPositionAndRotation(Vector3.Lerp(p0, destino.punto.position, e), Quaternion.Slerp(r0, destino.punto.rotation, e));
                camara.fieldOfView = Mathf.Lerp(f0, destino.campoVision, e);
                yield return null;
            }
            Colocar(destino);
            transicion = null;
        }

        void Colocar(Vista v)
        {
            if (v.punto == null) return;
            camara.transform.SetPositionAndRotation(v.punto.position, v.punto.rotation);
            camara.fieldOfView = v.campoVision;
        }

        void LateUpdate()
        {
            if (superficieAgua == null) return;

            var agua = superficieAgua.bounds;
            var pos = camara.transform.position;
            bool bajoAgua = pos.y < agua.max.y
                && pos.x > agua.min.x && pos.x < agua.max.x
                && pos.z > agua.min.z && pos.z < agua.max.z;
            if (bajoAgua)
            {
                float p = contaminacion != null ? contaminacion.Actual : 0f;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = Mathf.Lerp(densidadNieblaLimpia, densidadNieblaContaminada, p);
                RenderSettings.fogColor = contaminacion != null ? contaminacion.ColorNieblaAgua : new Color(0.1f, 0.4f, 0.45f);
            }
            else if (sumergida)
            {
                RenderSettings.fog = nieblaOriginal;
                RenderSettings.fogMode = modoNieblaOriginal;
                RenderSettings.fogDensity = densidadOriginal;
                if (diaNoche != null) RenderSettings.fogColor = diaNoche.ColorNiebla;
            }
            sumergida = bajoAgua;
        }
    }
}
