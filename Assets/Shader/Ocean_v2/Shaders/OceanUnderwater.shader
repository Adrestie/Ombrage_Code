// OceanUnderwater.shader  (Ocean_v2 / P6)
// CustomPass FULLSCREEN du sous-marin (Q3.1 : compositing post-GBuffer, injection BeforePostProcess).
// G2 = ABSORPTION Beer-Lambert de la colonne d'eau traversée, avec le σ PARTAGÉ (_WaterAbsorption,
// source unique P3/Q6.1) : color *= exp(−σ·d). Le « glow » de single-scattering (bleu-vert) et les
// god-rays viendront du VOLUMETRIC HDRP natif (G4).
//
// G3 = FENÊTRE DE SNELL sur les pixels de surface isolés par le tag stencil UserBit0 (posé au GBuffer
// d'OceanSurface.shader, G3.0) : dans le cône (θ<θc réglable) on échantillonne le ciel HDRP
// (_SkyTexture) dans la direction réfractée (loi de Snell) ; hors cône = réflexion totale interne (TIR,
// approximation « eau sombre »). Le résultat est ensuite absorbé par la colonne d'eau (G2, Q-G3.3).
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
    float  _OceanSnellCosThetaC;     // cos(demi-angle du cône de Snell), poussé par le module (réglable)

    // ---------------------------------------------------------------------------------
    // Ressources lues par la fenêtre de Snell (G3), déclarées ICI car ABSENTES de la chaîne d'includes
    // d'un FullScreen CustomPass :
    //  • _StencilTexture : stencil caméra HDRP. CustomPassSampling.hlsl ne l'expose pas → rebindé par
    //    BindCameraStencilPass (OceanUnderwaterModule.cs) AVANT ce pass, gaté immersion. Isole la surface
    //    (tag UserBit0=64 posé au GBuffer d'OceanSurface.shader). Lu via GetStencilValue (core
    //    Common.hlsl) qui choisit le bon canal selon la plateforme (D3D12 inclus).
    //  • _SkyTexture : cubemap de ciel HDRP (global de frame bindé en UpdateEnvironment → dispo à
    //    l'injection BeforePostProcess), échantillonné dans la direction réfractée pour remplir la fenêtre.
    TYPED_TEXTURE2D_X(uint2, _StencilTexture);
    TEXTURECUBE_ARRAY(_SkyTexture);
    // ---------------------------------------------------------------------------------

    float4 FullScreenPass(Varyings varyings) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

        float depth = LoadCameraDepth(varyings.positionCS.xy);
        PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ScreenSize.zw, depth,
                                                   UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
        float4 color = float4(CustomPassLoadCameraColor(varyings.positionCS.xy, 0), 1.0);

        if (_OceanUnderwaterEnabled < 0.5)
            return color;

        // Absorption Beer-Lambert de la colonne d'eau traversée (σ partagé, Q6.1). Distance caméra→pixel :
        // en camera-relative HDRP, |positionWS| EST la distance. Skybox (depth ~ far) clampé (pas d'exp(−∞)).
        float  d = min(length(posInput.positionWS) * _OceanUnderwaterDistScale, 400.0);
        float3 transmittance = exp(-max(_WaterAbsorption.rgb, 0.0) * d);

        // FENÊTRE DE SNELL — uniquement sur les pixels de surface océan vus de dessous (tag UserBit0, G3.0).
        // On REMPLACE la couleur de surface par la lumière venant du dessus ; elle est ensuite absorbée par
        // la colonne d'eau (color.rgb *= transmittance plus bas → fenêtre d'autant plus sombre que la caméra
        // est profonde, cohérent Q6.1 / Q-G3.3).
        uint stencil = GetStencilValue(LOAD_TEXTURE2D_X(_StencilTexture, posInput.positionSS));
        if ((stencil & 64u) != 0u)                        // 64 = StencilUsage.UserBit0 (surface)
        {
            // V = rayon de vue caméra→pixel (camera-relative : les axes monde sont conservés). cosθ = angle
            // vs verticale +Y (normale du plan d'eau — approximation plan, cf. cadrage). θc = demi-angle
            // réglable du cône ; on en dérive l'indice de l'eau pour une réfraction COHÉRENTE avec le bord.
            float3 V         = normalize(posInput.positionWS);
            float  cosTheta  = V.y;
            float  cosThetaC = _OceanSnellCosThetaC;
            float  sinThetaC = sqrt(saturate(1.0 - cosThetaC * cosThetaC));
            float  eta       = 1.0 / max(sinThetaC, 1e-3); // n_eau (n_air=1), = 1/sin(θc)

            // Réfraction eau→air (normale face à l'eau = -Y). refract() renvoie 0 en réflexion totale
            // interne (θ > θc) → détection TIR gratuite. La direction réfractée dit où lire le ciel émergé.
            float3 refr   = refract(V, float3(0.0, -1.0, 0.0), eta);
            bool   isTIR  = dot(refr, refr) < 1e-6;
            float3 skyDir = isTIR ? normalize(float3(V.x, 1e-3, V.z)) : refr;   // repli horizon près du bord
            float3 skyCol = SAMPLE_TEXTURECUBE_ARRAY_LOD(_SkyTexture, s_trilinear_clamp_sampler, skyDir, 0, 0).rgb;

            // TIR (hors cône) : approximation « eau sombre » (Q-G3.2) — la réflexion réelle de l'environnement
            // sous-marin viendra plus tard. Bord adouci (smoothstep) entre fenêtre (ciel) et TIR.
            const float3 colTIR = float3(0.004, 0.020, 0.030);
            const float  edge   = 0.03;
            float  inWindow = smoothstep(cosThetaC - edge, cosThetaC + edge, cosTheta);
            color.rgb = lerp(colTIR, skyCol, inWindow);
        }

        color.rgb *= transmittance;   // absorption colonne (surface : ciel réfracté / TIR ; sinon : scène immergée)
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
