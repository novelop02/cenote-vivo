#ifndef CENOTE_COMUN_INCLUDED
#define CENOTE_COMUN_INCLUDED

// ── Variables globales (Shader.SetGlobal*) ──
// Nivel de contaminación 0–1. Lo fija PollutionController.
float _CenotePollution;
// Corte transversal: plano xyz = normal (apunta a la cámara), w = distancia. Lo fija CorteTransversal.
float4 _CenotePlanoCorte;
float _CenoteCorteActivo;
// Datos del terreno para estratos y pluma de infiltración (los fija CorteTransversal).
float _CenoteAlturaSuperficie;
float _CenoteProfundidad;
float _CenoteNivelAgua;

// ── Realidad aumentada ──
// En AR el cenote es una miniatura que el usuario mueve, gira y escala. Los datos de arriba
// (plano de corte, alturas, nivel del agua) están en el "espacio del cenote" (el mundo original
// de la escena 3D). EspacioCenoteAR publica la matriz mundo → cenote y activa _CenoteEspacioAR.
// Fuera de AR vale 0 y las posiciones se usan tal cual (la escena 3D no cambia).
float _CenoteEspacioAR;
float4x4 _CenoteMundoALocal;
float4x4 _CenoteLocalAMundo;

// Posición de mundo → espacio del cenote.
float3 ACenote(float3 posWS)
{
    return _CenoteEspacioAR > 0.5 ? mul(_CenoteMundoALocal, float4(posWS, 1.0)).xyz : posWS;
}

// Posición del espacio del cenote → mundo.
float3 DeCenote(float3 posCenote)
{
    return _CenoteEspacioAR > 0.5 ? mul(_CenoteLocalAMundo, float4(posCenote, 1.0)).xyz : posCenote;
}

// Dirección del espacio del cenote → mundo (para devolver normales al Lit Shader Graph).
float3 DireccionAMundo(float3 dirLocal)
{
    return _CenoteEspacioAR > 0.5 ? normalize(mul((float3x3)_CenoteLocalAMundo, dirLocal)) : dirLocal;
}

float Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

// Ruido de valor barato (4 hashes, sin texturas).
float ValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = Hash21(i);
    float b = Hash21(i + float2(1, 0));
    float c = Hash21(i + float2(0, 1));
    float d = Hash21(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float Fbm3(float2 p)
{
    return ValueNoise(p) * 0.55 + ValueNoise(p * 2.03 + 17.1) * 0.3 + ValueNoise(p * 4.01 + 3.7) * 0.15;
}

// Descarta la mitad del modelo que queda del lado de la cámara cuando el corte está activo.
void AplicarCorte(float3 posWS)
{
    if (_CenoteCorteActivo > 0.5)
        clip(_CenotePlanoCorte.w - dot(ACenote(posWS), _CenotePlanoCorte.xyz));
}

// Para una cara interior vista a través del corte: punto donde el rayo de la cámara cruza el plano.
// Así la sección se dibuja plana, como un corte real, y no según la cara trasera (más lejana).
// pw ya está en el espacio del cenote; la cámara se lleva al mismo espacio.
float3 PuntoEnCorte(float3 pw)
{
    float3 cam = ACenote(_WorldSpaceCameraPos); // disponible también en las vistas previas de Shader Graph
    float3 dir = normalize(pw - cam);
    float den = dot(_CenotePlanoCorte.xyz, dir);
    float t = (_CenotePlanoCorte.w - dot(_CenotePlanoCorte.xyz, cam)) / (abs(den) > 1e-4 ? den : 1e-4);
    return t > 0.0 ? cam + dir * t : pw;
}

// Sección del subsuelo: estratos ondulados + pluma de contaminación que avanza hacia abajo
// con "dedos" irregulares. Modifica albedo y devuelve la máscara de la pluma (0–1).
half SeccionSubsuelo(inout half3 albedo, float3 pw, half p, half variacion,
                     half3 estratoA, half3 estratoB, float frecuencia, half3 colorPluma)
{
    half onda = ValueNoise(pw.xz * frecuencia * 0.5) * 2.0;
    half banda = frac(pw.y * frecuencia + onda);
    albedo *= lerp(estratoA, estratoB, smoothstep(0.35, 0.65, banda)) * 1.4h;

    float profundidad = _CenoteAlturaSuperficie - pw.y;
    float frente = p * _CenoteProfundidad * 0.85;
    float dedos = (Fbm3(float2((pw.x + pw.z) * 0.15, pw.y * 0.05)) - 0.5) * _CenoteProfundidad * 0.35;
    half pluma = 1.0h - smoothstep(frente - 2.0, frente + 2.0, profundidad + dedos);
    pluma *= saturate(p * 6.0h);
    albedo = lerp(albedo, colorPluma * (0.4h + variacion * 0.5h), pluma * 0.9h);
    return pluma;
}

// Color del agua para "rellenar" la cavidad en la vista de corte.
half3 ColorAguaCorte(half p)
{
    return lerp(half3(0.1, 0.55, 0.6), half3(0.22, 0.18, 0.07), p);
}

#endif
