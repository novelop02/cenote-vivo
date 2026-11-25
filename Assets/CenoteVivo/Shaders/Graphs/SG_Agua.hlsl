// Custom Function de SG_Agua (Lit Shader Graph). Usa las propiedades del Blackboard de SG_Agua.
#ifndef CENOTE_SG_AGUA_INCLUDED
#define CENOTE_SG_AGUA_INCLUDED

#include "CenoteGraphComun.hlsl"

// Ondas procedurales: dos capas de senos cruzados + ruido (sin texturas).
half3 NormalOndas(float2 xz, half3 normalBase, half calma)
{
    float t = _Time.y * _VelocidadOndas;
    float2 q = xz * _EscalaOndas;
    float2 d = float2(0, 0);
    d += float2(0.8, 0.6)   * cos(dot(q, float2(0.8, 0.6))   * 1.0 + t * 1.3);
    d += float2(-0.5, 0.85) * cos(dot(q, float2(-0.5, 0.85)) * 1.7 + t * 1.7) * 0.6;
    d += float2(0.3, -0.95) * cos(dot(q, float2(0.3, -0.95)) * 3.1 + t * 2.3) * 0.3;
    d += (ValueNoise(q * 2.3 + t) - 0.5) * 0.15;
    d *= _FuerzaOndas * calma;
    return normalize(normalBase + half3(-d.x, 0, -d.y));
}

// Paso 1: normal de las ondas y coordenadas distorsionadas para el nodo Scene Color (refracción).
void AguaRefraccion_float(float3 PosWS, float3 NormalWS, float4 PosPantalla, out float3 Normal, out float4 UVEscena)
{
    half p = saturate(_CenotePollution);
    // El agua contaminada es más pesada/espesa: las ondas se calman.
    half3 n = NormalOndas(ACenote(PosWS).xz, normalize(NormalWS), lerp(1.0h, 0.35h, p));
    Normal = n;
    float2 uv = PosPantalla.xy + n.xz * _Refraccion * (1.0 - p);
    UVEscena = float4(uv, 0, 1);
}

// Paso 2: mezcla del fondo refractado con el cuerpo de agua (limpio → turbio) y película de aceite.
void AguaSuperficie_float(float3 PosWS, float3 Normal, float3 ColorEscena,
    out float3 BaseColor, out float Suavidad, out float3 Emision, out float Alfa, out float Umbral)
{
    float3 pc = ACenote(PosWS); // espacio del cenote (igual al mundo fuera de AR)
    Umbral = UmbralCorte(pc);
    half p = saturate(_CenotePollution);
    half3 v = normalize(_WorldSpaceCameraPos - PosWS);
    half nv = saturate(dot(Normal, v));
    half fresnel = pow(1.0h - nv, 4.0h);

    // Más profundo al mirar perpendicular (se ve más columna de agua).
    half4 limpio = lerp(_ColorProfundo, _ColorSuperficie, fresnel);
    float manchas = ValueNoise(pc.xz * _EscalaTurbidez + _Time.y * 0.03);
    manchas = manchas * 0.6 + ValueNoise(pc.xz * _EscalaTurbidez * 3.1 - _Time.y * 0.05) * 0.4;
    half4 sucio = lerp(_ColorContaminado, _ColorTurbio, smoothstep(0.35, 0.75, manchas));
    half4 cuerpo = lerp(limpio, sucio, smoothstep(0.0, 1.0, p));
    half opacidad = cuerpo.a;

    // El cuerpo de agua recibe la luz (Lit); el fondo refractado se suma como emisión.
    BaseColor = cuerpo.rgb * opacidad;
    Emision = ColorEscena * lerp(half3(1, 1, 1), limpio.rgb * 1.6h, 0.5h) * (1.0h - opacidad);

    // Película de aceite iridiscente a contaminación media/alta.
    half aceite = smoothstep(0.55, 0.8, manchas) * smoothstep(0.3, 0.9, p) * _PeliculaAceite;
    half3 iris = 0.5h + 0.5h * cos(6.2831h * (manchas * 3.0h + nv + half3(0.0h, 0.33h, 0.67h)));
    Emision += iris * 0.25h * aceite * fresnel * 2.0h;

    Suavidad = lerp(0.95h, 0.55h, p);
    Alfa = 1.0;
}

#endif
