Shader "Hidden/Ombrage/FoamStamp"
{
    // Stamp d'empreinte (approche V1 OceanWakeStamp) : un Blit plein écran qui
    // inscrit un disque à falloff centré sur l'objet (UV région), accumulé en
    // BlendOp Max dans la RT de foam. Aucune capture de mesh, aucune matrice
    // per-objet -> fiable. Sampled ensuite par le shader d'eau -> collier.
    SubShader
    {
        Pass
        {
            Name "FoamStamp"
            ZWrite Off
            ZTest Always
            Cull Off
            BlendOp Max
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            float2 _OmbrageStampCenter;    // centre en UV [0,1]
            float  _OmbrageStampRadiusUV;  // rayon de l'objet (UV)
            float  _OmbrageStampWidthUV;   // largeur du fondu du collier (UV)

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = GetFullScreenTriangleVertexPosition(IN.vertexID);
                OUT.uv         = GetFullScreenTriangleTexCoord(IN.vertexID);
                return OUT;
            }

            float Frag(Varyings IN) : SV_Target
            {
                float d = distance(IN.uv, _OmbrageStampCenter);
                // 1 à l'intérieur du rayon, fondu vers 0 sur la largeur du collier.
                return 1.0 - smoothstep(_OmbrageStampRadiusUV, _OmbrageStampRadiusUV + _OmbrageStampWidthUV, d);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
