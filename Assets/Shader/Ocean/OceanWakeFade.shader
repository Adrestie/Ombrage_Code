Shader "Hidden/OceanWakeFade"
{
    Properties
    {
        _FadeAmount ("Fade amount per frame", Float) = 0.01
    }

    SubShader
    {
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float _FadeAmount;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = float4(IN.positionOS.xy * 2.0 - 1.0, 0, 1);
                OUT.uv = IN.uv;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float current = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).r;
                float faded = max(0.0, current - _FadeAmount);
                return float4(faded, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
