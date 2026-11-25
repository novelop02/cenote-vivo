// Custom Function de SG_Vegetacion (Lit Shader Graph). Usa las propiedades del Blackboard de SG_Vegetacion.
#ifndef CENOTE_SG_VEGETACION_INCLUDED
#define CENOTE_SG_VEGETACION_INCLUDED

#include "CenoteGraphComun.hlsl"

// Viento en el vértice: el alfa del color de vértice indica cuánto se mueve (0 = base fija).
void VegetacionViento_float(float3 PosOS, float4 ColorVertice, out float3 PosSalida)
{
    // En el espacio del cenote: en AR la miniatura mide centímetros y el viento debe escalar con ella.
    float3 pw = ACenote(TransformObjectToWorld(PosOS));
    float peso = ColorVertice.a;
    float fase = _Time.y * _VelocidadViento + dot(pw.xz, float2(0.13, 0.17)) * _EscalaViento * 10.0;
    float rafaga = 0.6 + 0.4 * sin(_Time.y * 0.35 + pw.x * _EscalaViento);
    pw.x += sin(fase) * _Viento * peso * rafaga;
    pw.z += cos(fase * 0.83) * _Viento * peso * rafaga * 0.7;
    pw.y += sin(fase * 1.7) * _Viento * peso * 0.15;
    PosSalida = TransformWorldToObject(DeCenote(pw));
}

void VegetacionSuperficie_float(float3 PosWS, float3 NormalWS, float4 ColorVertice, bool EsFrente,
    out float3 BaseColor, out float3 Normal, out float Suavidad, out float3 Emision, out float Umbral)
{
    float3 pw = ACenote(PosWS); // espacio del cenote (igual al mundo fuera de AR)
    Umbral = UmbralCorte(pw);
    half p = saturate(_CenotePollution);
    half3 n = normalize(NormalWS);
    // Los colores de vértice se definen en sRGB; el proyecto renderiza en espacio lineal.
    half3 albedo = ColorVertice.rgb;
#if !defined(UNITY_COLORSPACE_GAMMA)
    albedo = FastSRGBToLinear(albedo);
#endif
    albedo *= _Tinte.rgb;
    bool seccion = false;
    Emision = 0;

    if (_Seccion > 0.5)
    {
        half variacion = Fbm3(pw.xz * _EscalaManchas * 2.0);
        if (!EsFrente)
        {
            // Interior del terreno expuesto por el corte → estratos + pluma.
            half pluma;
            albedo = SeccionInterior(pw, p, variacion, _ColorEstratoA.rgb, _ColorEstratoB.rgb, _ColorPluma.rgb, pluma);
            n = DireccionAMundo(_CenotePlanoCorte.xyz);
            Emision = albedo * 0.25;
            seccion = true;
        }
        else
        {
            float derrame = Fbm3(pw.xz * _EscalaManchas);
            float umbral = 1.0 - p * 0.45;
            half mancha = smoothstep(umbral, umbral + 0.1, derrame) * saturate(p * 4.0h);
            albedo = lerp(albedo, _ColorFiltracion.rgb * (0.7h + variacion * 0.5h), mancha * 0.85h);
        }
    }
    else if (!EsFrente)
    {
        n = -n; // hojas de doble cara
    }

    // Marchitez: las plantas pierden su verde con la contaminación.
    if (!seccion)
    {
        half gris = dot(albedo, half3(0.3h, 0.59h, 0.11h));
        half3 marchito = lerp(half3(gris, gris, gris), _ColorMarchito.rgb, 0.6h) * 1.1h;
        albedo = lerp(albedo, marchito, p * _Marchitez);
    }

    BaseColor = albedo;
    Normal = n;
    Suavidad = _Suavidad;
}

#endif
