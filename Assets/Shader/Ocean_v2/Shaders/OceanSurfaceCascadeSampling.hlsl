// OceanSurfaceCascadeSampling.hlsl  (Ocean_v2)
// Échantillonnage des cascades de simulation par la surface :
//   - déplacement xyz (domain shader)   : _OceanDisp512/256  (.x=Dx, .y=hauteur, .z=Dz, .w=Jacobien)
//   - déplacement xyz N-1 (passe MV)     : _OceanDispPrev512/256 (positions seules ; dérivées NON dupliquées)
//   - pentes → normales analytiques (frag): _OceanDeriv512/256 (.x=slopeX, .y=slopeZ, .z=Jxz, .w=J)
//
// Métadonnées de cascade (globaux poussés par la simulation) : _OceanCascade{i} = (length, group, slice, res),
// group 0 = array 512², group 1 = array 256². On lit PUREMENT ces globaux (aucune écriture ici).
//
// Anti-bug n°2 : la normale est recomposée à partir des PENTES analytiques (jamais de différences
// finies sur le déplacement). Le Jacobien est disponible mais NON consommé ici (écume ultérieure).
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
// Carte d'écume WORLD-LOCKED (OceanFoam.compute) : couverture accumulée [0..1], mippée (AA distance),
// résolution/étendue PROPRES (découplée de la longueur de tuile). Échantillonnée comme un DÉCAL.
TEXTURE2D(_OceanFoam); SAMPLER(sampler_OceanFoam);
float _OceanFoamExtent;   // demi-étendue monde de la carte (= gridExtent)

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

// ── Écume — décal world-locked ─────────────────────────────────────────────
// La couverture (crêtes + persistance) est PRÉ-CALCULÉE par OceanFoam.compute dans la carte
// _OceanFoam (résolution/étendue propres). Échantillonnage décal à la position monde NON-DÉPLACÉE,
// avec LOD EXPLICITE dérivé de la DISTANCE CAMÉRA (lisse) : les dérivées implicites de la
// coordonnée inversée du déplacement sont en marches d'escalier (bilinéaire) → le LOD hardware
// sautait vers des mips grossiers → paliers durs en parallélogramme constatés.
// viewDist = length(posInput.positionWS) (camera-relative). Mips = AA distance (valeurs [0..1]).
float SampleOceanFoamCoverage(float2 worldXZ, float viewDist)
{
    float2 uv = worldXZ / (2.0 * max(_OceanFoamExtent, 1e-3)) + 0.5;   // [-extent,+extent] → [0,1]
    float w, h;
    _OceanFoam.GetDimensions(w, h);
    float mapTexel = (2.0 * max(_OceanFoamExtent, 1e-3)) / max(w, 1.0);      // m / texel de carte
    float pixWorld = viewDist * (2.0 / UNITY_MATRIX_P._m11) * _ScreenSize.w; // m / pixel écran (vertical)
    float lod = log2(max(pixWorld / mapTexel, 1e-6));                        // <0 → clampé mip 0
    return saturate(SAMPLE_TEXTURE2D_LOD(_OceanFoam, sampler_OceanFoam, uv, lod).r);
}

#endif // OCEAN_SURFACE_CASCADE_SAMPLING_INCLUDED
