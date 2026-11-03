// Funciones compartidas por los Lit Shader Graph de Cenote Vivo (corte y sección del subsuelo).
// La iluminación (sol, sombras, faroles, reflejos, niebla) la resuelve el Lit Shader Graph;
// los archivos SG_*.hlsl solo calculan la superficie: color base, normal, suavidad, emisión y el corte.
#ifndef CENOTE_GRAPH_COMUN_INCLUDED
#define CENOTE_GRAPH_COMUN_INCLUDED

#include "../CenoteComun.hlsl"

// Umbral de Alpha Clip: 2 descarta el píxel (está del lado de la cámara en el corte), 0 lo conserva.
float UmbralCorte(float3 pw)
{
    return (_CenoteCorteActivo > 0.5 && dot(pw, _CenotePlanoCorte.xyz) > _CenotePlanoCorte.w) ? 2.0 : 0.0;
}

// Interior del sólido visto a través del corte → sección del subsuelo (estratos + pluma).
half3 SeccionInterior(float3 pw, half p, half variacion, half3 estratoA, half3 estratoB, half3 colorPluma, out half pluma)
{
    half3 albedo = half3(0.78h, 0.74h, 0.64h);
    pluma = SeccionSubsuelo(albedo, PuntoEnCorte(pw), p, variacion, estratoA, estratoB,
                            6.0 / max(1.0, _CenoteProfundidad), colorPluma);
    return albedo;
}

#endif
