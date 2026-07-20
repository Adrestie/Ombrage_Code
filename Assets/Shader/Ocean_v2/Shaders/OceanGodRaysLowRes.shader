// OceanGodRaysLowRes.shader  (Ocean_v2)
// Rendu des god-rays en BASSE RÉSOLUTION (perf) → RT demi-résolution FLOUTÉE, bindée en global (_OceanGodRayTex).
//   Pass "GodRays" : calcule les god-rays (ComputeOceanGodRays) → RGB, dans une RT demi-résolution.
//                    Résolution-INDÉPENDANT : direction de vue reconstruite depuis positionNDC (0..1),
//                    AUCUNE lecture de depth (v1 sans occlusion géométrie → pas de plomberie résolution).
//   Pass "Blur"    : flou gaussien SÉPARABLE (H puis V, ping-pong par la passe scriptée). La courbure FFT est
//                    HAUTE fréquence → en demi-res + dither IGN elle grène en quadrillage ; le flou lisse ça
//                    en faisceaux DOUX (recette V1 : low-res → BLUR → upscale). God-rays basse fréquence → invisible.
// Le COMPOSITE (échantillonnage bilinéaire + ajout additif) est fait À LA FIN de OceanUnderwater.shader —
// PAS ici — pour garantir l'ordre vs le fog underwater (les deux ne se courent plus après).
// Piloté par un CustomPass scripté (OceanGodRayLowResPass) : RT demi-res, 3 draws (rays + blur H + blur V), bind global.
Shader "Hidden/Ocean/GodRaysLowRes"
{
    HLSLINCLUDE
    #pragma vertex Vert
    #pragma target 4.5
    #pragma only_renderers d3d11 d3d12 vulkan metal

    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"
    #include "Assets/Shader/Ocean_v2/Shaders/OceanSurfaceCascadeSampling.hlsl"
    #include "Assets/Shader/Ocean_v2/Shaders/OceanCaustics.hlsl"   // _OceanSunDirection
    #include "Assets/Shader/Ocean_v2/Shaders/OceanGodRays.hlsl"    // ComputeOceanGodRays + globals god-rays

    float  _OceanWaterLevel;          // Y absolu du plan d'eau
    float4 _OceanGRTargetSize;        // (w,h,1/w,1/h) de la RT god-rays demi-res (poussé par la passe scriptée)
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        // ── Rendu des god-rays (dans la RT demi-res) ──
        Pass
        {
            Name "GodRays"
            ZWrite Off ZTest Always Blend Off Cull Off
            HLSLPROGRAM
            #pragma fragment Frag
            float4 Frag(Varyings varyings) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

                // Gate : caméra IMMERGÉE (submersion in-shader par-caméra). L'interrupteur god-ray
                // (_OceanGodRaysEnabled) + l'intensité sont testés DANS ComputeOceanGodRays.
                float camAbsY = GetAbsolutePositionWS(float3(0.0, 0.0, 0.0)).y;
                if (camAbsY >= _OceanWaterLevel)
                    return float4(0.0, 0.0, 0.0, 1.0);

                // NDC de ce pixel : positionCS.xy = pixel de la RT DEMI-RES, mais _ScreenSize est PLEIN écran
                // → on divise par la taille RÉELLE de la RT (poussée par la passe) pour un NDC 0..1 correct.
                float2 positionNDC = varyings.positionCS.xy * _OceanGRTargetSize.zw;

                // Direction de vue reconstruite via un point sur le plan LOINTAIN (aucune depth scène requise).
                float3 farWS   = ComputeWorldSpacePosition(positionNDC, UNITY_RAW_FAR_CLIP_VALUE, UNITY_MATRIX_I_VP);
                float3 viewDir = normalize(farWS);                    // camera-relative → direction monde

                float  camDepth = max(_OceanWaterLevel - camAbsY, 0.0);
                // marchDist = sortie de l'eau par la surface (rayon montant), sinon grande (cap dans la fonction).
                float  dExit    = (viewDir.y > 1e-3) ? (_OceanWaterLevel - camAbsY) / viewDir.y : 1e9;

                float3 camAbsPos = GetAbsolutePositionWS(float3(0.0, 0.0, 0.0));
                float3 gr = ComputeOceanGodRays(camAbsPos, viewDir, dExit, camDepth, _OceanWaterLevel, varyings.positionCS.xy);
                return float4(gr, 1.0);
            }
            ENDHLSL
        }

        // ── Flou gaussien séparable (une direction par draw : la passe scriptée l'appelle H puis V) ──
        Pass
        {
            Name "Blur"
            ZWrite Off ZTest Always Blend Off Cull Off
            HLSLPROGRAM
            #pragma fragment FragBlur
            TEXTURE2D_X(_OceanGRSource);      // RT source à flouter (poussée par la passe scriptée)
            float2 _OceanGRBlurDir;           // (1,0) = horizontal · (0,1) = vertical (en TEXELS de la RT demi-res)

            float4 FragBlur(Varyings varyings) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);
                // NDC 0..1 de la RT demi-res, puis mise à l'échelle RTHandle (la RT scaled n'occupe que
                // _RTHandleScale de son allocation). Pas d'échantillonnage en TEXELS × cette même échelle.
                float2 uv    = varyings.positionCS.xy * _OceanGRTargetSize.zw;
                float2 sc    = _RTHandleScale.xy;
                float2 suv   = uv * sc;
                float2 step  = _OceanGRBlurDir * _OceanGRTargetSize.zw * sc;

                // Gaussien 9 taps (σ ≈ 2 texels), poids normalisés.
                const float w0 = 0.227027, w1 = 0.194594, w2 = 0.121621, w3 = 0.054054, w4 = 0.016216;
                float3 c = SAMPLE_TEXTURE2D_X_LOD(_OceanGRSource, s_linear_clamp_sampler, suv, 0).rgb * w0;
                c += SAMPLE_TEXTURE2D_X_LOD(_OceanGRSource, s_linear_clamp_sampler, suv + step * 1.0, 0).rgb * w1;
                c += SAMPLE_TEXTURE2D_X_LOD(_OceanGRSource, s_linear_clamp_sampler, suv - step * 1.0, 0).rgb * w1;
                c += SAMPLE_TEXTURE2D_X_LOD(_OceanGRSource, s_linear_clamp_sampler, suv + step * 2.0, 0).rgb * w2;
                c += SAMPLE_TEXTURE2D_X_LOD(_OceanGRSource, s_linear_clamp_sampler, suv - step * 2.0, 0).rgb * w2;
                c += SAMPLE_TEXTURE2D_X_LOD(_OceanGRSource, s_linear_clamp_sampler, suv + step * 3.0, 0).rgb * w3;
                c += SAMPLE_TEXTURE2D_X_LOD(_OceanGRSource, s_linear_clamp_sampler, suv - step * 3.0, 0).rgb * w3;
                c += SAMPLE_TEXTURE2D_X_LOD(_OceanGRSource, s_linear_clamp_sampler, suv + step * 4.0, 0).rgb * w4;
                c += SAMPLE_TEXTURE2D_X_LOD(_OceanGRSource, s_linear_clamp_sampler, suv - step * 4.0, 0).rgb * w4;
                return float4(c, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
