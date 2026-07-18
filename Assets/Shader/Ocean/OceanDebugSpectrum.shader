Shader "Hidden/OceanDebugSpectrum"
{
    Properties
    {
        _MainTex ("Spectrum RT", 2D)  = "black" {}
        _Amplify ("Amplify",  Float)  = 5000
        _DebugMode ("Debug Mode", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float _Amplify;
            float _DebugMode;

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
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float4 raw = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);

                if (_DebugMode > 0.5)
                {
                    // Shore map: R=waterY, G=foam, B=groundY, A=signedDist
                    float sd = raw.a;
                    float foam = raw.g;
                    // Blue = water above ground, red = ground above water
                    float3 vis = sd > 0
                        ? float3(0, 0.15, 0.6) * saturate(sd * 0.5)
                        : float3(0.7, 0.3, 0.05) * saturate(-sd * 0.5);
                    // Green = foam overlay
                    vis += float3(0, foam * 0.8, 0);
                    // White line at the junction (signedDist ≈ 0)
                    vis = lerp(vis, float3(1, 1, 1), smoothstep(0.3, 0.0, abs(sd)));
                    return float4(saturate(vis), 1.0);
                }

                float3 vis = float3(
                    abs(raw.r),
                    abs(raw.g),
                    max(abs(raw.r), abs(raw.g)) * 0.5
                ) * _Amplify;

                return float4(saturate(vis), 1.0);
            }
            ENDHLSL
        }

        // Depth-only pass required by HDRP to avoid rendering artifacts
        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }

            Cull Back
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex   vertDepth
            #pragma fragment fragDepth
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vertDepth(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            float4 fragDepth(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
