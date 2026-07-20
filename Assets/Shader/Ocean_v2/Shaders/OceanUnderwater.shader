// OceanUnderwater.shader  (Ocean_v2)
// CustomPass FULLSCREEN du sous-marin (compositing post-GBuffer, injection BeforePostProcess).
// MILIEU SOUS-MARIN (single-scattering), σ PARTAGÉ (_WaterAbsorption) + couleur d'eau (_OceanScatterColor,
// même look que la surface) : L = L_fond·T + couleurEau·(1−T), T = exp(−σ·d). La géométrie lointaine se
// FOND dans la couleur d'eau (fog), plus vers le noir. Les pixels sans géométrie (depth==far) horizontaux/
// descendants (l'horizon sous l'eau) → couleur d'eau pleine (colonne infinie). Le fog volumétrique HDRP
// (module Volumetrics) ajoute PAR-DESSUS le glow LITÉ (même teinte).
//
// FENÊTRE DE SNELL sur les pixels de surface isolés par le tag stencil UserBit0 (posé au GBuffer
// d'OceanSurface.shader) : dans le cône (θ<θc réglable) on échantillonne le ciel HDRP
// (_SkyTexture) dans la direction réfractée (loi de Snell) ; hors cône = réflexion totale interne (TIR,
// approximation « eau sombre »). Le résultat est ensuite absorbé par la colonne d'eau.
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
    // Caustiques immergées : SampleOceanNormal (cascades) + ComputeOceanCaustics, partagés avec la
    // surface (globals _OceanCaustics*/_OceanSunDirection poussés par le module Caustics). Include-guards
    // propres, aucune redéclaration de _WaterAbsorption. Cascade sampling AVANT caustics (dépendance).
    #include "Assets/Shader/Ocean_v2/Shaders/OceanSurfaceCascadeSampling.hlsl"
    #include "Assets/Shader/Ocean_v2/Shaders/OceanCaustics.hlsl"

    // Globaux (poussés par les modules ; _WaterAbsorption est le MÊME que la surface).
    float4 _WaterAbsorption;         // σ (m⁻¹) en .rgb (extinction spectrale)
    float4 _OceanScatterColor;       // couleur AFFICHÉE de l'eau = in-scattering du milieu (poussée par OceanAbsorptionModule)
    float  _OceanUnderwaterEnabled;  // 0/1 (caméra immergée)
    float  _OceanUnderwaterDistScale;// échelle artistique de densité (défaut 1)
    float  _OceanWaterLevel;         // Y absolu du plan d'eau (poussé par OceanSurfaceModule)

    // Rôle de cette passe (post-flip forward) : effets de COLONNE D'EAU sur la géométrie OPAQUE immergée
    // (fond + objets) — absorption Beer-Lambert + caustiques. La FENÊTRE DE SNELL (surface vue de dessous)
    // est désormais rendue par le SHADER DE SURFACE (forward double-face) : plus de tag stencil ni de lecture
    // du normal/sky buffer ici. La séparation « surface vs géométrie immergée » est GÉOMÉTRIQUE (worldY vs
    // niveau d'eau), robuste quel que soit le comportement de depth des transparents.
    float4 FullScreenPass(Varyings varyings) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

        float depth = LoadCameraDepth(varyings.positionCS.xy);
        PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ScreenSize.zw, depth,
                                                   UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
        float4 color = float4(CustomPassLoadCameraColor(varyings.positionCS.xy, 0), 1.0);

        // Gate : module actif ET caméra IMMERGÉE (submersion calculée in-shader par-caméra — robuste
        // Scene view/Play, contrairement à l'ancien Camera.main). Sinon la passe n'a rien à faire.
        float camAbsY = GetAbsolutePositionWS(float3(0.0, 0.0, 0.0)).y;
        if (_OceanUnderwaterEnabled < 0.5 || camAbsY >= _OceanWaterLevel)
            return color;

        float3 sigma    = max(_WaterAbsorption.rgb, 0.0);
        float3 waterCol = _OceanScatterColor.rgb;          // in-scattering du milieu = couleur d'eau (look)

        // EAU LIBRE / HORIZON : pixel SANS géométrie (depth == far). Sous l'eau, un rayon HORIZONTAL ou
        // DESCENDANT (rayDir.y ≤ 0) ne ressort JAMAIS de l'eau → colonne INFINIE → couleur d'eau PLEINE
        // (fog l'horizon, remplace le ciel qu'on voyait net). Un rayon MONTANT (rayDir.y > 0) va vers la
        // surface (fenêtre de Snell) → possédé par le SHADER DE SURFACE → on NE touche PAS (zéro double).
        if (depth == UNITY_RAW_FAR_CLIP_VALUE)
        {
            float3 rayDir = normalize(posInput.positionWS);   // camera-relative = direction monde (Y = up)
            if (rayDir.y <= 0.001)
            {
                float3 T = exp(-sigma * 400.0);               // colonne infinie → T≈0 → couleur d'eau
                color.rgb = color.rgb * T + waterCol * (1.0 - T);
            }
            return color;
        }

        // GÉOMÉTRIE : seulement l'opaque IMMERGÉE (worldY < niveau). Au-dessus = surface (Snell + sa colonne).
        float3 pAbs = GetAbsolutePositionWS(posInput.positionWS);
        if (pAbs.y >= _OceanWaterLevel)
            return color;

        // CAUSTIQUES (avant le milieu). Marge 0.1 m pour ne pas frôler le plan d'eau.
        if (pAbs.y < _OceanWaterLevel - 0.1)
            color.rgb *= 1.0 + ComputeOceanCaustics(pAbs, _OceanWaterLevel);

        // MILIEU SOUS-MARIN (single-scattering) : extinction spectrale T = exp(−σ·d) + IN-SCATTERING vers la
        // couleur d'eau → la géométrie lointaine se FOND dans la couleur d'eau (fog), plus vers le noir.
        float  d = min(length(posInput.positionWS) * _OceanUnderwaterDistScale, 400.0);
        float3 T = exp(-sigma * d);
        color.rgb = color.rgb * T + waterCol * (1.0 - T);
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
