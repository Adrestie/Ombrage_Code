// OceanSurfaceCascadeSampling.hlsl  (Ocean_v2 / P2)
// Échantillonnage des cascades de simulation P1 par la surface :
//   - déplacement xyz (domain shader)   : _OceanDisp512/256  (.x=Dx, .y=hauteur, .z=Dz, .w=Jacobien)
//   - déplacement xyz N-1 (passe MV)     : _OceanDispPrev512/256 (positions seules ; dérivées NON dupliquées)
//   - pentes → normales analytiques (frag): _OceanDeriv512/256 (.x=slopeX, .y=slopeZ, .z=Jxz, .w=J)
//
// Métadonnées de cascade (globaux poussés par P1) : _OceanCascade{i} = (length, group, slice, res),
// group 0 = array 512², group 1 = array 256². On lit PUREMENT ces globaux (aucune écriture ici).
//
// Anti-bug n°2 : la normale est recomposée à partir des PENTES analytiques (jamais de différences
// finies sur le déplacement). Le Jacobien (P1) est disponible mais NON consommé en P2 (écume = P3).
#ifndef OCEAN_SURFACE_CASCADE_SAMPLING_INCLUDED
#define OCEAN_SURFACE_CASCADE_SAMPLING_INCLUDED

// Arrays de simulation (Texture2DArray RGBAFloat). Samplers hérités des RT (Repeat / Bilinear).
TEXTURE2D_ARRAY(_OceanDisp512);     SAMPLER(sampler_OceanDisp512);
TEXTURE2D_ARRAY(_OceanDisp256);     SAMPLER(sampler_OceanDisp256);
TEXTURE2D_ARRAY(_OceanDeriv512);    SAMPLER(sampler_OceanDeriv512);
TEXTURE2D_ARRAY(_OceanDeriv256);    SAMPLER(sampler_OceanDeriv256);
// Tampons N-1 fournis par le coordinator (OceanMotionVectorPass), bindés via ctx.globals (anti-bug n°1).
TEXTURE2D_ARRAY(_OceanDispPrev512); SAMPLER(sampler_OceanDispPrev512);
TEXTURE2D_ARRAY(_OceanDispPrev256); SAMPLER(sampler_OceanDispPrev256);

float4 _OceanCascade0;
float4 _OceanCascade1;
float4 _OceanCascade2;
float4 _OceanCascade3;
float  _OceanCascadeCount;
// 0 le frame où le tampon N-1 vient d'être (ré)alloué → échantillonner prev=current (MV nuls, pas de flash).
float  _OceanMVValid;

float4 OceanGetCascade(int i)
{
    if (i == 0) return _OceanCascade0;
    if (i == 1) return _OceanCascade1;
    if (i == 2) return _OceanCascade2;
    return _OceanCascade3;
}

// Déplacement xyz total (somme des 4 cascades) à la position monde XZ donnée.
// usePrev=true → échantillonne les tampons N-1 (passe MotionVectors), sinon les arrays courants.
float3 SampleOceanDisplacement(float2 worldXZ, bool usePrev)
{
    float3 disp = float3(0.0, 0.0, 0.0);
    int count = (int)_OceanCascadeCount;

    [unroll]
    for (int i = 0; i < 4; i++)
    {
        if (i >= count) break;
        float4 c = OceanGetCascade(i);          // (length, group, slice, res)
        float2 uv = worldXZ / max(c.x, 1e-3);
        float slice = c.z;

        float4 s;
        if (c.y < 0.5) // group 0 → array 512²
            s = usePrev ? SAMPLE_TEXTURE2D_ARRAY_LOD(_OceanDispPrev512, sampler_OceanDispPrev512, uv, slice, 0)
                        : SAMPLE_TEXTURE2D_ARRAY_LOD(_OceanDisp512,     sampler_OceanDisp512,     uv, slice, 0);
        else           // group 1 → array 256²
            s = usePrev ? SAMPLE_TEXTURE2D_ARRAY_LOD(_OceanDispPrev256, sampler_OceanDispPrev256, uv, slice, 0)
                        : SAMPLE_TEXTURE2D_ARRAY_LOD(_OceanDisp256,     sampler_OceanDisp256,     uv, slice, 0);

        disp += s.xyz;   // Dx, hauteur, Dz
    }
    return disp;
}

// Normale monde recomposée à partir des PENTES analytiques sommées (anti-bug n°2).
float3 SampleOceanNormal(float2 worldXZ)
{
    float2 slope = float2(0.0, 0.0);
    int count = (int)_OceanCascadeCount;

    [unroll]
    for (int i = 0; i < 4; i++)
    {
        if (i >= count) break;
        float4 c = OceanGetCascade(i);
        float2 uv = worldXZ / max(c.x, 1e-3);
        float slice = c.z;

        float4 d = (c.y < 0.5)
            ? SAMPLE_TEXTURE2D_ARRAY_LOD(_OceanDeriv512, sampler_OceanDeriv512, uv, slice, 0)
            : SAMPLE_TEXTURE2D_ARRAY_LOD(_OceanDeriv256, sampler_OceanDeriv256, uv, slice, 0);

        slope += d.xy;   // slopeX, slopeZ
    }
    // n = normalize(-∂h/∂x, 1, -∂h/∂z)
    return normalize(float3(-slope.x, 1.0, -slope.y));
}

#endif // OCEAN_SURFACE_CASCADE_SAMPLING_INCLUDED
