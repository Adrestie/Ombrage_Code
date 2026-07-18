// GrassBRGSurface.hlsl
// ---------------------------------------------------------------------------------
// PHASE 1b — Custom surface description for the BRG grass shader.
//
// This file REPLACES HDRP's LitData.hlsl. It provides the one function every HDRP
// shading/depth pass calls: GetSurfaceAndBuiltinData(). Everything else (properties,
// DOTS-instancing caches, GBuffer encoding, the pass drivers) is reused verbatim from
// the HDRP Lit framework — so the only hand-authored HLSL is this ~70-line surface.
//
// What it does (1b, deliberately minimal):
//   - baseColor   : read from the per-instance DOTS property _BaseColor (proves the
//                   BRG -> DOTS per-instance plumbing that Phase 1c's compute feed needs)
//   - normalWS    : interpolated geometric normal, flipped to face the viewer (Cull Off)
//   - smoothness  : from material _Smoothness (constant for now)
//   - metallic    : from material _Metallic   (constant; 0 for grass)
//   - GI/APV      : filled by InitBuiltinData/PostInitBuiltinData (the standard helpers)
//
// Phase 1c will extend this (translucency via a diffusion profile, procedural AO,
// rounded normals) and add the vertex hook (ApplyVertexModification) for the Bézier blade.
//
// Included AFTER Material.hlsl + Lit.hlsl in every pass, so SurfaceData/BuiltinData,
// ENCODE_INTO_GBUFFER, MATERIALFEATUREFLAGS_LIT_STANDARD, InitBuiltinData,
// PostInitBuiltinData and Orthonormalize are all already defined.
// ---------------------------------------------------------------------------------

#ifndef GRASS_BRG_SURFACE_INCLUDED
#define GRASS_BRG_SURFACE_INCLUDED

// InitBuiltinData / PostInitBuiltinData live here. The stock Lit path pulls this in
// transitively via LitData.hlsl -> LitBuiltinData.hlsl; since we replace LitData.hlsl we
// must include it ourselves (this is exactly what UnlitData.hlsl does). Material.hlsl does
// NOT include it. Safe in every pass (depth/shadow included), like Unlit.
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"
// InterleavedGradientNoise (screen-door dither). Usually pulled in transitively by Common.hlsl, but
// include it explicitly (guarded, so a no-op if already in) so the dither always resolves.
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"
// Shared grass field: GrassFakeShadowMul (fake far canopy shadow) + the _Grass* globals.
#include "Assets/Shader/Grass/GrassWind.hlsl"

// Stylized-grass surface controls (global; Shader.SetGlobalFloat).
float _GrassNormalUp;    // 0..1 lift the shaded normal toward world up
float _GrassNormalRound; // 0..1 cylindrical cross-section (rounded blade across its width)
float _GrassAO;          // 0..1 procedural ambient occlusion darkening at the blade base
float _GrassAOHeight;    // fraction of the blade (from base) over which AO fades out
float _GrassThickness;   // 0..1 thickness (remapped by the diffusion profile); thin = more transmission
float _GrassTransmission;// transmission strength (backlit glow)
float _GrassSpecularAA;  // 0..1 strength: roughen (lower smoothness) with distance to kill far sparkle
float _GrassSpecularAADist; // distance (m) at which the specular-AA roughening is full
float _GrassDitherStrength; // 0..1 far-edge screen-door dissolve (0 = pure height melt = validated look)
float _GrassRingFeather;    // clipmap ring overflow feather (= lodTransition) for the ring anti-pop dither
float _GrassRingDitherMax;  // ring dither applies ONLY to rings whose outer radius < this (the inner n-2 rings)

// Animated interleaved-gradient screen-door (1b primitive). Per-pixel threshold that shifts each
// frame (via the TAA frame index) so TAA integrates the clipped pixels into a smooth fade — deferred
// has no alpha blending, so "dissolve" = clip() + temporal accumulation. _TaaFrameInfo.z is HDRP's
// own dither frame counter (the same one its LOD/shadow dithering uses).
float GrassScreenDoor(float2 positionSS)
{
    return InterleavedGradientNoise(positionSS, (int)_TaaFrameInfo.z);
}

// Signature MUST match the call site in the ShaderPass*.hlsl drivers exactly.
// RAY_TRACING_OPTIONAL_PARAMETERS expands to nothing in the (non ray-tracing) passes we build.
void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput,
                              out SurfaceData surfaceData, out BuiltinData builtinData RAY_TRACING_OPTIONAL_PARAMETERS)
{
    ZERO_INITIALIZE(SurfaceData, surfaceData);

    // ---- Screen-door dither (1b primitive) ----
    // fadeOut01 = how much this blade should disappear (1 = fully gone). Established here on the FAR
    // band as a dissolve complementing the height melt; the SAME machinery covers the frustum edge
    // (Step 2) and the impostor crossfade later. clip() runs in EVERY pass (GBuffer/Depth/Shadow/MV)
    // — same camDist + screen pos + frame everywhere — so depth, shadows and colour agree on which
    // fragments exist (no z-fighting between the depth prepass and the GBuffer pass).
    // Distances to the GAME / cull camera (_GrassCullCamPos), NOT the rendering camera, so the far-crossfade +
    // ring dither match the VERTEX melt (also _GrassCullCamPos) and the game-camera-centred clipmap, and stay
    // stable when inspected from the Scene view. In the GAME view this equals length(positionWS) -> no change.
    float3 fragAbs = GetAbsolutePositionWS(posInput.positionWS);
    float camDist = distance(fragAbs, _GrassCullCamPos);
    {
        float fadeOut = (_GrassCrossfadeEnd > _GrassCrossfadeStart)
            ? smoothstep(_GrassCrossfadeStart, _GrassCrossfadeEnd, camDist) * _GrassDitherStrength
            : 0.0;
        // Clipmap ring overflow dither for the INNER rings ONLY (ringOuter < _GrassRingDitherMax = the second-
        // outermost ring's radius): it hides the LOD density steps / re-scatter pops there, and those inner ring
        // radii are STABLE (independent of Max Blade Distance) so it stays coherent. The OUTER 2 rings are left to
        // the distance-based height MELT (they hand off to the impostors) -> the disappearance no longer keys off
        // the outermost ring radius (which jumps in powers of two with Max Blade Distance). Skipped in the shadow
        // pass (ring-edge shadows = harmless).
#if SHADERPASS != SHADERPASS_SHADOWS
        float ringOuter = _GrassParams2.w;
        if (ringOuter > 0.0 && ringOuter < _GrassRingDitherMax)
        {
            float mCam = length(fragAbs.xz - _GrassCullCamPos.xz);
            float nom  = ringOuter * (1.0 - _GrassRingFeather);
            fadeOut = max(fadeOut, smoothstep(nom, ringOuter, mCam));
        }
#endif
        if (fadeOut > 1e-4)
            clip(GrassScreenDoor((float2)posInput.positionSS) - fadeOut);
    }

    // ---- Normal ----
    // tangentToWorld[2] is the interpolated world-space vertex normal (identity in pure
    // depth passes, which is fine — they don't use it).
    float3 normalWS = normalize(input.tangentToWorld[2]);
    // Thin double-sided blade (Cull Off): shade whichever side faces the camera. This is
    // winding/isFrontFace-independent and robust, and fixes inverted-looking lighting.
    // (1c.4 will revisit this when adding proper translucency/transmission.)
    if (dot(normalWS, V) < 0.0)
        normalWS = -normalWS;

    // Mesh-kind species (flower/tuft, _GrassParams2.y >= 0.5) keep their AUTHORED normal/UVs — the
    // blade-specific cylindrical round + along-blade AO (which assume a vertical strip) are skipped.
    bool isMesh = _GrassParams2.y >= 0.5;

    // Rounded (cylindrical) cross-section: bend the normal across the blade width (texCoord0.x
    // = 0..1 across the strip) so the blade reads as a rounded volume, not a flat card.
    float curvature  = (input.texCoord0.x - 0.5) * 2.0; // -1..1 across the width
    float3 roundAxis = normalize(cross(float3(0.0, 1.0, 0.0), normalWS) + float3(1e-4, 0.0, 0.0));
    normalWS = normalize(normalWS + roundAxis * curvature * (isMesh ? 0.0 : _GrassNormalRound));

    // Lift toward world up so blades catch sky/sun light and never go fully black.
    normalWS = normalize(lerp(normalWS, float3(0.0, 1.0, 0.0), _GrassNormalUp));

    // ---- Material (Lit Standard) ----
    // Procedural AO along the blade (dark base -> light tip). HDRP's ambientOcclusion only
    // affects INDIRECT light, so we ALSO fold it into baseColor to darken the base under direct light.
    // Skipped for mesh species (their UVs aren't the blade's base->tip).
    float ao = isMesh ? 1.0 : lerp(1.0 - _GrassAO, 1.0, saturate(input.texCoord0.y / max(_GrassAOHeight, 1e-3)));

    // Specular AA: far blades sub-pixel-alias the highlight (many tilted normals + the rounded
    // cross-section), making it sparkle/flicker. Roughen the surface with distance so the specular
    // lobe widens and the sparkle integrates away (TAA finishes it). Near blades keep their sheen.
    float specAA    = _GrassSpecularAA * saturate(camDist / max(_GrassSpecularAADist, 1.0));
    float smoothness = lerp(_Smoothness, _Smoothness * 0.15, specAA);

    surfaceData.materialFeatures     = MATERIALFEATUREFLAGS_LIT_TRANSMISSION; // backlit grass (translucent)
    surfaceData.baseColor            = _BaseColor.rgb * ao;   // per-instance via DOTS instancing
#if SHADERPASS == SHADERPASS_GBUFFER
    // Fake far shadow: blades beyond the real shadow sphere lose cast shadows -> darken their albedo
    // to fake the canopy self-shadowing (ramps in as the real shadows ramp out -> no disc edge).
    surfaceData.baseColor *= GrassFakeShadowMul(fragAbs.xz, camDist);
#endif
    surfaceData.perceptualSmoothness = smoothness;
    surfaceData.metallic             = _Metallic;
    surfaceData.ambientOcclusion     = ao;
    surfaceData.specularOcclusion    = 1.0;
    surfaceData.normalWS             = normalWS;
    surfaceData.geomNormalWS         = normalWS;

    // Tangent only matters for anisotropy (off here) but Lit expects it orthonormalized.
    surfaceData.tangentWS            = normalize(input.tangentToWorld[0]);
    surfaceData.tangentWS            = Orthonormalize(surfaceData.tangentWS, surfaceData.normalWS);

    surfaceData.specularColor        = 0.0;

    // Transmission (backlit grass) via the assigned diffusion profile (_DiffusionProfileHash set
    // from C# by HDMaterial.SetDiffusionProfile). thickness is remapped by the profile.
    surfaceData.diffusionProfileHash = asuint(_DiffusionProfileHash);
    surfaceData.subsurfaceMask       = 0.0;              // transmission only, no SSS blur
    surfaceData.thickness            = _GrassThickness;  // thin -> more light passes through
    surfaceData.transmissionMask     = _GrassTransmission;

    float alpha = _BaseColor.a;

    // ---- Builtin data (GI / APV / emissive) ----
    // No lightmaps on BRG grass -> pass 0 lightmap UVs; APV uses world position internally.
    float3 bentNormalWS = surfaceData.normalWS;
    InitBuiltinData(posInput, alpha, bentNormalWS, -surfaceData.normalWS,
                    /*texCoord1*/ (float4)0.0, /*texCoord2*/ (float4)0.0, builtinData);

    builtinData.emissiveColor = 0.0;

    PostInitBuiltinData(V, posInput, surfaceData, builtinData);
}

#endif // GRASS_BRG_SURFACE_INCLUDED
