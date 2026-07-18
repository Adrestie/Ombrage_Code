// GrassImpostorSurface.hlsl
// ---------------------------------------------------------------------------------
// Horizon impostor surface (Step 2) — REPLACES HDRP's LitData.hlsl, like the blade's
// GrassBRGSurface.hlsl. Provides GetSurfaceAndBuiltinData():
//   - samples the baked horizon card _GrassImpostorTex (RGB = neutral grass shading,
//     A = silhouette coverage)
//   - alpha-cutouts the silhouette (deferred = no alpha blend -> clip(); runs in EVERY
//     pass so depth/shadow/GBuffer agree on which fragments exist)
//   - tints the baked shading by the per-instance/material colour (_BaseColor)
//   - shades it lit through the deferred GBuffer (same material setup as the blade so the
//     GBuffer encoding + stencil match the cloned passes; transmission feature compiled but
//     left at 0 strength = standard-looking grass for now).
//
// Included AFTER Material.hlsl + Lit.hlsl in every pass, so SurfaceData/BuiltinData,
// MATERIALFEATUREFLAGS_*, InitBuiltinData, PostInitBuiltinData, Orthonormalize are defined.
// Reuses GrassBRGProperties.hlsl (HLSLINCLUDE) for _BaseColor/_Smoothness/_Metallic/_Diffusion*.
// ---------------------------------------------------------------------------------

#ifndef GRASS_IMPOSTOR_SURFACE_INCLUDED
#define GRASS_IMPOSTOR_SURFACE_INCLUDED

// InitBuiltinData / PostInitBuiltinData (we replace LitData.hlsl, so include it ourselves like Unlit).
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"
// InterleavedGradientNoise — the screen-door dither for the Step 4 crossfade (guarded, no-op if already in).
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"

TEXTURE2D(_GrassImpostorTex);          // baked horizon card (GLOBAL, bound by the runtime EnsureImpostorBake)
float _GrassImpostorNormalUp;          // 0..1 lift the shaded normal toward world up (soft canopy look)
// (_AlphaCutoff is the silhouette clip threshold — a material property in GrassImpostorProperties.hlsl.)
// Step 4 crossfade band (distance from camera): x = fade-in start (blade-melt start), y = fade-in end (= max
// blade dist), z = fade-out start, w = fade-out end (= impostor outer). w <= 0 means "not set" (no fade).
float4 _GrassImpBand;
float4x4 _GrassCullCamVP;     // GAME camera view-projection (absolute world -> clip), for the dither below
float4 _GrassCullCamScreen;   // GAME camera pixel size (xy)

// Animated screen-door (the same primitive the blades use) -> TAA resolves the dithered dissolve.
float GrassImpostorScreenDoor(float2 positionSS)
{
    return InterleavedGradientNoise(positionSS, (int)_TaaFrameInfo.z);
}

// Signature MUST match the ShaderPass*.hlsl call site exactly (same as the blade surface).
void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput,
                              out SurfaceData surfaceData, out BuiltinData builtinData RAY_TRACING_OPTIONAL_PARAMETERS)
{
    ZERO_INITIALIZE(SurfaceData, surfaceData);

    // ---- Silhouette cutout ----
    float2 uv  = input.texCoord0.xy;            // baked in the quad mesh (u across, v root->top)
    // HDRP's predefined linear-clamp sampler — the texture is a global RT, which has no auto sampler_* state.
    float4 tex = SAMPLE_TEXTURE2D(_GrassImpostorTex, s_linear_clamp_sampler, uv);
    clip(tex.a - _AlphaCutoff);                 // deferred has no blend -> clip the coverage (all passes)

    // Step 4 — crossfade: dither the card IN over the blade-melt band (cross-dissolve with the melting blades)
    // and OUT toward the L2 tint ground, so blades -> impostors -> tint read as one continuous gradient.
    if (_GrassImpBand.w > 0.0)
    {
        // Everything relative to the GAME camera (_GrassCullCamPos / its VP), NOT the rendering view, so the
        // fade AND the dither pattern are identical from the Game view or when inspected from the Scene view.
        float3 absPos = GetAbsolutePositionWS(posInput.positionWS);
        float dist    = distance(absPos, _GrassCullCamPos);
        // Fade IN over [x, y]: cards reach 100% AT y (= the blade-melt start), having faded in over the band
        // BEFORE it while still hidden under the full blades. Fade OUT over [z, w] toward the L2 tint ground.
        float fadeIn  = 1.0 - smoothstep(_GrassImpBand.x, _GrassImpBand.y, dist);
        float fadeOut = smoothstep(_GrassImpBand.z, _GrassImpBand.w, dist);
        float fade    = max(fadeIn, fadeOut);
        if (fade > 1e-4)
        {
            // Screen-door evaluated in the GAME camera's screen space (project the fragment through its VP) so
            // the dissolve pattern is stable relative to the game camera, not the Scene-view pixels.
            float4 gClip = mul(_GrassCullCamVP, float4(absPos, 1.0));
            float2 gSS   = (gClip.xy / max(gClip.w, 1e-4) * 0.5 + 0.5) * _GrassCullCamScreen.xy;
            clip(GrassImpostorScreenDoor(gSS) - fade);
        }
    }

    // ---- Normal ----
    // The billboard faces the camera (vertex normal). Flip to the viewer (double-sided card) and lift
    // toward world up so the card reads as a soft grass canopy instead of a flat lit panel.
    float3 normalWS = normalize(input.tangentToWorld[2]);
    if (dot(normalWS, V) < 0.0) normalWS = -normalWS;
    normalWS = normalize(lerp(normalWS, float3(0.0, 1.0, 0.0), _GrassImpostorNormalUp));

    // ---- Material (mirror the blade GBuffer path: transmission feature compiled, 0 strength here) ----
    surfaceData.materialFeatures     = MATERIALFEATUREFLAGS_LIT_TRANSMISSION;
    surfaceData.baseColor            = tex.rgb * _BaseColor.rgb;   // baked grass shading * tint
    surfaceData.perceptualSmoothness = _Smoothness;
    surfaceData.metallic             = _Metallic;
    surfaceData.ambientOcclusion     = 1.0;
    surfaceData.specularOcclusion    = 1.0;
    surfaceData.normalWS             = normalWS;
    surfaceData.geomNormalWS         = normalWS;
    surfaceData.tangentWS            = normalize(input.tangentToWorld[0]);
    surfaceData.tangentWS            = Orthonormalize(surfaceData.tangentWS, surfaceData.normalWS);
    surfaceData.specularColor        = 0.0;
    surfaceData.diffusionProfileHash = asuint(_DiffusionProfileHash);
    surfaceData.subsurfaceMask       = 0.0;
    surfaceData.thickness            = 0.0;     // transmission compiled (stencil match) but OFF for now
    surfaceData.transmissionMask     = 0.0;

    // ---- Builtin data (GI / APV) ----
    float alpha = tex.a;                          // keep coverage in builtin alpha (consistent if HDRP re-tests)
    float3 bentNormalWS = surfaceData.normalWS;
    InitBuiltinData(posInput, alpha, bentNormalWS, -surfaceData.normalWS,
                    /*texCoord1*/ (float4)0.0, /*texCoord2*/ (float4)0.0, builtinData);
    builtinData.emissiveColor = 0.0;
    PostInitBuiltinData(V, posInput, surfaceData, builtinData);
}

#endif // GRASS_IMPOSTOR_SURFACE_INCLUDED
