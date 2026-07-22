Shader "Hidden/Ombrage/FoamHeight"
{
    // Capture top-down de la HAUTEUR monde du décor (pour l'edge foam d'empreinte).
    // Rendu via CommandBuffer.DrawRenderer avec une VP orthographique par au-dessus :
    // ZTest LEqual + proj ortho (haut = proche) => le sommet du décor gagne à chaque
    // texel. Sortie = worldY. Cleared à heightMin en dehors du décor.
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            Name "FoamHeight"
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float worldY : TEXCOORD0; };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = mul(UNITY_MATRIX_M, float4(IN.positionOS, 1.0)).xyz;
                OUT.positionCS = mul(UNITY_MATRIX_VP, float4(posWS, 1.0));
                OUT.worldY = posWS.y;
                return OUT;
            }

            float Frag(Varyings IN) : SV_Target
            {
                return IN.worldY;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
