// Custom Function de SG_Roca (Lit Shader Graph). Usa las propiedades del Blackboard de SG_Roca.
#ifndef CENOTE_SG_ROCA_INCLUDED
#define CENOTE_SG_ROCA_INCLUDED

#include "CenoteGraphComun.hlsl"

void RocaSuperficie_float(float3 PosWS, float3 NormalWS, bool EsFrente,
    out float3 BaseColor, out float3 Normal, out float Suavidad, out float3 Emision, out float Umbral)
{
    float3 pw = ACenote(PosWS); // espacio del cenote (igual al mundo fuera de AR)
    Umbral = UmbralCorte(pw);
    half p = saturate(_CenotePollution);
    half variacion = Fbm3(pw.xz * _EscalaVariacion + pw.y * _EscalaVariacion * 0.7);
    half humedad = _Humedad;
    half3 nGeo = normalize(NormalWS);
    half3 albedo;
    half3 n;
    Emision = 0;

    if (!EsFrente)
    {
        half pluma;
        albedo = SeccionInterior(pw, p, variacion, _ColorEstratoA.rgb, _ColorEstratoB.rgb, _ColorPluma.rgb, pluma);
        humedad = lerp(humedad, 0.6h, pluma);
        n = DireccionAMundo(_CenotePlanoCorte.xyz);
        Emision = albedo * 0.25; // la sección se lee también de noche
    }
    else
    {
        // Proyección triplanar (el modelo no necesita UV) con mezcla "whiteout" de normales.
        float3 pos = pw * _EscalaTriplanar;
        half3 w = pow(abs(nGeo), 4.0h);
        w /= (w.x + w.y + w.z);
        albedo = SAMPLE_TEXTURE2D(_TexturaBase, sampler_TexturaBase, pos.zy).rgb * w.x
               + SAMPLE_TEXTURE2D(_TexturaBase, sampler_TexturaBase, pos.xz).rgb * w.y
               + SAMPLE_TEXTURE2D(_TexturaBase, sampler_TexturaBase, pos.xy).rgb * w.z;
        half3 tx = UnpackNormalScale(SAMPLE_TEXTURE2D(_TexturaNormal, sampler_TexturaNormal, pos.zy), _IntensidadNormal);
        half3 ty = UnpackNormalScale(SAMPLE_TEXTURE2D(_TexturaNormal, sampler_TexturaNormal, pos.xz), _IntensidadNormal);
        half3 tz = UnpackNormalScale(SAMPLE_TEXTURE2D(_TexturaNormal, sampler_TexturaNormal, pos.xy), _IntensidadNormal);
        tx = half3(tx.xy + nGeo.zy, abs(tx.z) * nGeo.x);
        ty = half3(ty.xy + nGeo.xz, abs(ty.z) * nGeo.y);
        tz = half3(tz.xy + nGeo.xy, abs(tz.z) * nGeo.z);
        n = normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
        albedo *= _ColorBase.rgb;

        if (_Subsuelo > 0.5)
        {
            half pluma = SeccionSubsuelo(albedo, pw, p, variacion, _ColorEstratoA.rgb, _ColorEstratoB.rgb,
                                         _FrecuenciaEstratos, _ColorPluma.rgb);
            humedad = lerp(humedad, 0.6h, pluma);
        }
        else
        {
            // Musgo y pasto en superficies horizontales, en parches irregulares.
            half musgo = smoothstep(0.55h, 0.9h, n.y) * _Musgo
                       * smoothstep(0.22h, 0.42h, Fbm3(pw.xz * _EscalaVariacion * 3.0));
            albedo = lerp(albedo, _ColorMusgo.rgb * (0.7h + variacion * 0.6h), musgo);

            // Paredes: escurrimientos de filtración. Suelo: manchas de derrames.
            half pared = saturate(1.0h - abs(n.y) * 1.3h);
            float escurrimiento = Fbm3(float2((pw.x + pw.z) * _EscalaManchas * 2.0, pw.y * _EscalaManchas * 0.2));
            float derrame = Fbm3(pw.xz * _EscalaManchas * 0.8);
            float ruido = lerp(derrame, escurrimiento, pared);
            float umbral = 1.0 - p * _CoberturaManchas * lerp(0.65, 1.0, pared);
            half mancha = smoothstep(umbral, umbral + 0.12, ruido) * saturate(p * 4.0h);
            albedo = lerp(albedo, _ColorFiltracion.rgb * (0.7h + variacion * 0.5h), mancha * 0.9h);
            humedad = lerp(humedad, 0.7h, mancha);
        }
        albedo *= lerp(1.0h, 0.7h + variacion * 0.6h, _VariacionColor);

        // En el corte, la cavidad bajo el nivel del agua se ve "llena" de agua.
        if (_CenoteCorteActivo > 0.5 && pw.y < _CenoteNivelAgua)
        {
            half prof = saturate((_CenoteNivelAgua - pw.y) / max(1.0, _CenoteProfundidad * 0.3));
            half3 agua = ColorAguaCorte(p) * (0.4h + 0.6h * (1.0h - prof));
            half k = lerp(0.45h, 0.9h, p);
            albedo = lerp(albedo, agua, k);
            Emision += agua * k * 0.3h;
        }
    }

    BaseColor = albedo;
    Normal = n;
    Suavidad = saturate(humedad) * 0.85;
}

#endif
