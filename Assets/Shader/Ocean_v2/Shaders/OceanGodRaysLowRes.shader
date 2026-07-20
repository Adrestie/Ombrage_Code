// OceanGodRaysLowRes.shader  (Ocean_v2)
// Rendu des god-rays en BASSE RÉSOLUTION (perf) → RT demi-résolution bindée en global (_OceanGodRayTex).
//   Pass "GodRays" : calcule les god-rays (ComputeOceanGodRays) → RGB, dans une RT demi-résolution.
//                    Résolution-INDÉPENDANT : direction de vue reconstruite depuis positionNDC (0..1),
//                    AUCUNE lecture de depth (v1 sans occlusion géométrie → pas de plomberie résolution).
// Le COMPOSITE (échantillonnage bilinéaire + ajout additif) est fait À LA FIN de OceanUnderwater.shader —
// PAS ici — pour garantir l'ordre vs le fog underwater (les deux ne se courent plus après).
// Piloté par un CustomPass scripté (OceanGodRayLowResPass) : RT demi-res, un seul draw, bind global.
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
    }
    Fallback Off
}
