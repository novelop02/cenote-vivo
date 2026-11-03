// Custom Function de SG_Particulas (Lit Shader Graph). Usa las propiedades del Blackboard de SG_Particulas.
#ifndef CENOTE_SG_PARTICULAS_INCLUDED
#define CENOTE_SG_PARTICULAS_INCLUDED

#include "CenoteGraphComun.hlsl"

// Color del sistema de partículas × textura × tinte; la emisión hace brillar las luciérnagas.
void Particula_float(float4 ColorVertice, float4 UV, out float3 BaseColor, out float Alfa, out float3 Emision)
{
    half4 c = ColorVertice * SAMPLE_TEXTURE2D(_TexturaBase, sampler_TexturaBase, UV.xy) * _ColorBase;
    BaseColor = c.rgb;
    Alfa = c.a;
    Emision = c.rgb * _Emision;
}

#endif
