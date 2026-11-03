// Custom Function de SG_Farol (Lit Shader Graph). Usa las propiedades del Blackboard de SG_Farol.
#ifndef CENOTE_SG_FAROL_INCLUDED
#define CENOTE_SG_FAROL_INCLUDED

// Pantalla del farol: casi negra a la luz, con emisión cálida (HDR) que la hace brillar.
void Farol_float(out float3 BaseColor, out float3 Emision)
{
    BaseColor = _ColorBase.rgb;
    Emision = _ColorEmision.rgb;
}

#endif
