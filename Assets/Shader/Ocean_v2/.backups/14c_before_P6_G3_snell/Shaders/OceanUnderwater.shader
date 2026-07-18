// OceanUnderwater.shader  (Ocean_v2 / P6)
// CustomPass FULLSCREEN du sous-marin (Q3.1 : compositing post-GBuffer, injection BeforePostProcess).
// G2 = ABSORPTION Beer-Lambert de la colonne d'eau traversée, avec le σ PARTAGÉ (_WaterAbsorption,
// source unique P3/Q6.1) : color *= exp(−σ·d). Le « glow » de single-scattering (bleu-vert) et les
// god-rays viendront du VOLUMETRIC HDRP natif (G4) ; la fenêtre de Snell = G3. Ici, transmittance pure.
//
// Gaté par _OceanUnderwaterEnabled (1 quand la caméra est immergée, poussé par OceanUnderwaterModule).
// N'écrit RIEN d'autre que la couleur (pas de mutation d'état partagé — anti-bug n°1 respecté côté C#).
Shader "Hidden/Ocean/Underwater"
{
    HLSLINCLUDE

    #pragma vertex Vert
    #pragma target 4.5
    #pragma only_renderers d3d11 d3d12 vulkan metal

    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

    // Globaux (poussés par le module ; _WaterAbsorption est le MÊME que la surface, Q6.1).
    float4 _WaterAbsorption;         // σ (m⁻¹) en .rgb
    float  _OceanUnderwaterEnabled;  // 0/1
    float  _OceanUnderwaterDistScale;// échelle artistique de densité (défaut 1)

    float4 FullScreenPass(Varyings varyings) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

        float depth = LoadCameraDepth(varyings.positionCS.xy);
        PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ScreenSize.zw, depth,
                                                   UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
        float4 color = float4(CustomPassLoadCameraColor(varyings.positionCS.xy, 0), 1.0);

        if (_OceanUnderwaterEnabled < 0.5)
            return color;

        // Distance caméra → pixel. En rendu camera-relative HDRP, positionWS est relatif caméra →
        // sa longueur EST la distance. Le skybox (depth ~ far) est clampé pour éviter un exp(−∞) exact
        // (on garde une teinte d'horizon plutôt qu'un noir dur ; le fog volumétrique G4 le comblera).
        float d = min(length(posInput.positionWS) * _OceanUnderwaterDistScale, 400.0);
        float3 sigma = max(_WaterAbsorption.rgb, 0.0);
        float3 transmittance = exp(-sigma * d);

        color.rgb *= transmittance;   // Beer-Lambert (absorption pure ; scatter = volumetric HDRP G4)
        return color;
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "Underwater"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
            #pragma fragment FullScreenPass
            ENDHLSL
        }
    }
    Fallback Off
}
