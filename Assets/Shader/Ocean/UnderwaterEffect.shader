Shader "Hidden/Ocean/UnderwaterEffect"
{
    HLSLINCLUDE

    #pragma target 4.5
    #pragma editor_sync_compilation

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassSampling.hlsl"
    #include "UnderwaterInput.hlsl"
    #include "OceanCaustics.hlsl"

    float4 _UnderwaterCamPixelSize;   // (camWidth, camHeight, 0, 0)

    struct Attributes
    {
        uint vertexID : SV_VertexID;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 texcoord   : TEXCOORD0;
    };

    Varyings Vert(Attributes IN)
    {
        Varyings OUT;
        OUT.positionCS = GetFullScreenTriangleVertexPosition(IN.vertexID);
        OUT.texcoord   = GetFullScreenTriangleTexCoord(IN.vertexID);
        return OUT;
    }

    float3 SafeColor(float3 c)
    {
        return (any(isnan(c)) || any(isinf(c))) ? float3(0, 0, 0) : max(c, 0.0);
    }

    float3 SafeVec(float3 v)
    {
        return (any(isnan(v)) || any(isinf(v))) ? float3(0, 0, 1) : v;
    }

    float4 Frag(Varyings IN) : SV_Target
    {
        float2 uv = IN.texcoord;
        uint2 pixelCoord = uint2(uv * _UnderwaterCamPixelSize.xy);

        // Debug 5: solid red — tests that the CustomPass pipeline works at all
        if (_UnderwaterDebug == 5)
            return float4(1, 0, 0, 1);

        // _WorldSpaceCameraPos is the absolute position; GetCurrentViewPosition()
        // returns (0,0,0) in HDRP camera-relative rendering mode.
        float3 camPosAWS = _WorldSpaceCameraPos;
        float camDepthBelow = _WaterLevel - camPosAWS.y;

        if (camDepthBelow <= 0.0)
        {
            float3 aboveColor = SafeColor(CustomPassSampleCameraColor(uv, 0));
            return float4(aboveColor, 1.0);
        }

        // FFT-based underwater distortion
        float rawDepth = LOAD_TEXTURE2D_X(_CameraDepthTexture, pixelCoord).r;

        float2 sceneUV = uv;
        if (_ScreenDistStrength > 0.0001 && rawDepth > 0.0001)
        {
            float4 clipEarly = float4((uv * 2.0 - 1.0) * float2(1, -1), rawDepth, 1.0);
            float4 viewEarly = mul(UNITY_MATRIX_I_VP, clipEarly);
            float3 fragAWSEarly = camPosAWS + viewEarly.xyz / viewEarly.w;

            if (fragAWSEarly.y < _WaterLevel)
            {
                float waveHRef = _UW_SampleOceanHeight(fragAWSEarly);
                float surfYRef = _WaterLevel + waveHRef;
                float3 surfPosRef = float3(fragAWSEarly.x, surfYRef, fragAWSEarly.z);
                float3 surfN = _UW_SampleOceanNormal(surfPosRef);
                float refrDepth = surfYRef - fragAWSEarly.y;
                float depthFade = saturate(refrDepth * _ScreenDistScale * 0.1);
                sceneUV = clamp(uv + surfN.xz * depthFade * _ScreenDistStrength, 0.001, 0.999);
            }
        }

        float3 sceneColor = SafeColor(CustomPassSampleCameraColor(sceneUV, 0));

        float2 posNDC = uv * 2.0 - 1.0;
        #if UNITY_UV_STARTS_AT_TOP
        posNDC.y = -posNDC.y;
        #endif

        bool isSky = rawDepth < 0.0001;
        float safeDepth = isSky ? 0.001 : rawDepth;
        float4 clipPos = float4(posNDC, safeDepth, 1.0);
        float4 viewPos = mul(UNITY_MATRIX_I_VP, clipPos);
        float3 posRWS = (abs(viewPos.w) > 0.00001)
            ? viewPos.xyz / viewPos.w
            : float3(0, 0, 1);
        posRWS = SafeVec(posRWS);

        float len = length(posRWS);
        float3 rayDir = (len > 0.0001) ? posRWS / len : float3(0, 1, 0);
        float linearDist = isSky ? 500.0 : len;

        float3 fragAWS = camPosAWS + posRWS;
        float fragDepthBelow = max(0.0, _WaterLevel - fragAWS.y);

        // ── Surface from below (sky or geometry above water level) ──
        bool fragAboveSurface = fragAWS.y > _WaterLevel;
        if (isSky || fragAboveSurface)
        {
            sceneColor = SafeColor(ComputeSurfaceFromBelow(rayDir, camPosAWS, camDepthBelow, uv));
            linearDist = (rayDir.y > 0.01) ? (camDepthBelow / rayDir.y) : 500.0;
            fragDepthBelow = 0.0;
        }

        // ── Caustics (applied before fog so fog attenuates them) ──
        [branch] if (!isSky && !fragAboveSurface && _CausticsIntensity > 0.001)
        {
            float waveH = _UW_SampleOceanHeight(fragAWS);
            float surfaceY = _WaterLevel + waveH;
            float depthBelowWave = surfaceY - fragAWS.y;

            [branch] if (depthBelowWave > 0.0)
            {
                float3 surfacePos = float3(fragAWS.x, surfaceY, fragAWS.z);
                sceneColor *= 1.0 + ComputeOceanCaustics(surfacePos, depthBelowWave);
            }
        }

        // ── Fog (Subnautica-style decoupled attenuation) ──
        UnderwaterFogResult fog = ComputeUnderwaterFog(sceneColor, linearDist,
                                                        fragDepthBelow, camDepthBelow);
        fog.color = SafeColor(fog.color);

        // ── Debug modes ───────────────────────────────────
        if (_UnderwaterDebug == 2)
            return float4(sceneColor, 1.0);
        if (_UnderwaterDebug == 3)
            return float4(fog.fogAmount, fog.fogAmount, fog.fogAmount, 1.0);
        if (_UnderwaterDebug == 4)
        {
            float dVis = saturate(fragDepthBelow / 30.0);
            return float4(dVis, dVis * 0.5, 0.0, 1.0);
        }

        // ── God rays ──────────────────────────────────────
        float3 godRays = float3(0, 0, 0);
        if (_GodRayIntensity > 0.001)
        {
            godRays = SafeColor(ComputeGodRays(uv, posRWS, camPosAWS, linearDist, isSky));
        }

        if (_UnderwaterDebug == 1)
            return float4(godRays, 1.0);

        float3 finalColor = SafeColor(fog.color + godRays);
        return float4(finalColor, 1.0);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            Name "Underwater Fog + God Rays"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "Underwater Upscale Blit"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlit

            TEXTURE2D(_UnderwaterTempRT);

            float4 FragBlit(Varyings IN) : SV_Target
            {
                return SAMPLE_TEXTURE2D_LOD(_UnderwaterTempRT, sampler_linear_repeat, IN.texcoord, 0);
            }
            ENDHLSL
        }
    }
}
