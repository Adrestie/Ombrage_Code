Shader "Hidden/Ocean_v2/SpectrumDebug"
{
    // Shader de DEBUG : visualise sur un quad les slices des Texture2DArray
    // de sortie du module spectre (déplacement / dérivées), poussées en globaux.
    // Permet la validation VISUELLE sans asmdef de test (smoke test EditMode différé).
    // Pattern HDRP éprouvé du projet (ForwardOnly + DepthForwardOnly).
    Properties
    {
        // 0 = _OceanDisp512, 1 = _OceanDeriv512, 2 = _OceanDisp256, 3 = _OceanDeriv256
        _DebugArray ("Array (0=Disp512 1=Deriv512 2=Disp256 3=Deriv256)", Float) = 0
        _DebugSlice ("Slice (cascade dans le groupe)", Float) = 0
        // 0 = hauteur (G), 1 = déplacement XZ (RB), 2 = normale analytique, 3 = Jacobien/écume
        // Mode 2 (normale) auto-force l'array Deriv du même groupe de résolution.
        _DebugMode  ("Mode (0=height 1=dispXZ 2=normal[auto-Deriv] 3=jacobian)", Float) = 0
        _Amplify    ("Amplify", Float) = 1.0
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

            // Globaux poussés par OceanSpectrumModule (anti-bug n°1 via ctx.globals).
            TEXTURE2D_ARRAY(_OceanDisp512);
            TEXTURE2D_ARRAY(_OceanDeriv512);
            TEXTURE2D_ARRAY(_OceanDisp256);
            TEXTURE2D_ARRAY(_OceanDeriv256);
            SAMPLER(sampler_linear_repeat);

            float _DebugArray;
            float _DebugSlice;
            float _DebugMode;
            float _Amplify;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            float4 SampleArray(float arr, float2 uv, float slice)
            {
                if (arr < 0.5)      return SAMPLE_TEXTURE2D_ARRAY(_OceanDisp512,  sampler_linear_repeat, uv, slice);
                else if (arr < 1.5) return SAMPLE_TEXTURE2D_ARRAY(_OceanDeriv512, sampler_linear_repeat, uv, slice);
                else if (arr < 2.5) return SAMPLE_TEXTURE2D_ARRAY(_OceanDisp256,  sampler_linear_repeat, uv, slice);
                else                return SAMPLE_TEXTURE2D_ARRAY(_OceanDeriv256, sampler_linear_repeat, uv, slice);
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float4 d = SampleArray(_DebugArray, IN.uv, _DebugSlice);
                float3 vis;

                if (_DebugMode < 0.5)
                {
                    // Hauteur (Disp.y) en niveaux de gris signés.
                    float h = d.y * _Amplify;
                    vis = float3(0.5 + 0.5 * h, 0.5 + 0.5 * h, 0.5 + 0.5 * h);
                }
                else if (_DebugMode < 1.5)
                {
                    // Déplacement horizontal Dx/Dz (Disp.x / Disp.z).
                    vis = float3(0.5 + 0.5 * d.x * _Amplify, 0.0, 0.5 + 0.5 * d.z * _Amplify);
                }
                else if (_DebugMode < 2.5)
                {
                    // Normale analytique : n'a de sens que sur un array Deriv (slopeX/slopeZ).
                    // Auto-force le Deriv du MÊME groupe de résolution si un array Disp est
                    // sélectionné (Disp512=0 -> Deriv512=1, Disp256=2 -> Deriv256=3), pour
                    // éviter d'interpréter Dx/hauteur comme des pentes.
                    float derivArr = (_DebugArray < 1.5) ? 1.0 : 3.0;
                    float4 g = SampleArray(derivArr, IN.uv, _DebugSlice);
                    float3 n = normalize(float3(-g.x * _Amplify, 1.0, -g.y * _Amplify));
                    vis = 0.5 + 0.5 * n;
                }
                else
                {
                    // Jacobien / écume (Deriv.w ou Disp.w). J<1 => crête déferlante.
                    float j = d.w;
                    float foam = saturate((1.0 - j) * _Amplify);
                    vis = float3(foam, foam, foam);
                }

                return float4(saturate(vis), 1.0);
            }
            ENDHLSL
        }

        // Passe depth-only requise par HDRP.
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

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vertDepth(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            float4 fragDepth(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
