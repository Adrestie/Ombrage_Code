Shader "Hidden/Ombrage/UnderwaterGodRays"
{
    // =========================================================================
    // Ombrage — God-rays underwater V3 (portage de l'approche V1 : screen-space,
    // beam depuis la COURBURE de la surface, décorrélé du froxel volumétrique).
    // -------------------------------------------------------------------------
    // ÉTAPE 2 — MARCH COMPLET. Port fidèle de ComputeGodRays / _OceanBeamPattern
    // (V1 Hidden/Ocean/UnderwaterEffect), en sourçant la courbure depuis le
    // gradient du Water System HDRP (_WaterAdditionalDataBuffer, .xy = dh/dx,dh/dz)
    // exposé en global par WaterSurface.SetGlobalTextures() (appel côté pass).
    // Boilerplate custom-pass calqué sur la V1.
    // =========================================================================
    HLSLINCLUDE

    #pragma target 4.5
    #pragma editor_sync_compilation

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassSampling.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Water/ShaderVariablesWater.cs.hlsl"

    Texture2DArray<float4> _WaterAdditionalDataBuffer;

    // Params (poussés par le pass)
    float  _WaterLevel;
    float  _BeamScale;
    float  _BeamGain;            // amplifie la courbure (magnitude gradient HDRP << V1)
    float  _BeamThresholdLo;
    float  _BeamThresholdHi;
    float  _BeamSunFollow;
    float  _BeamDepthFade;
    float  _BeamExtinction;
    float  _GodRayIntensity;
    float4 _GodRayColor;
    float  _GodRayMaxDist;
    float  _GodRayFadeInDepth;
    int    _GodRaySteps;
    int    _GodRayDebug;          // 0 = composite, 1 = god rays seuls
    float3 _SunDirWS;

    struct Attributes { uint vertexID : SV_VertexID; };
    struct Varyings { float4 positionCS : SV_POSITION; float2 texcoord : TEXCOORD0; };

    Varyings Vert(Attributes IN)
    {
        Varyings OUT;
        OUT.positionCS = GetFullScreenTriangleVertexPosition(IN.vertexID);
        OUT.texcoord   = GetFullScreenTriangleTexCoord(IN.vertexID);
        return OUT;
    }

    float _IGN(float2 p)   // interleaved gradient noise (anti-banding)
    {
        float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
        return frac(magic.z * frac(dot(p, magic.xy)));
    }

    float _BeamHash(float2 p)
    {
        float3 p3 = frac(float3(p.xyx) * 0.1031);
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
    }

    float2 SampleBandGradient(float2 posXZ, float4 scaleOffset, int band)
    {
        float2 uv = posXZ * scaleOffset.x - scaleOffset.yz;  // = TransformWaterUV(posXZ, band)
        return _WaterAdditionalDataBuffer.SampleLevel(s_linear_repeat_sampler, float3(uv, band), 0).xy;
    }

    // Gradient de surface (dh/dx, dh/dz) : somme des bandes actives (comme la V1),
    // ce qui apporte le détail fin (ripples) -> faisceaux plus étroits.
    // Les bandes inactives ont un amplitudeMultiplier (.w) nul et sont ignorées.
    float2 SampleWaterGradient(float2 posXZ)
    {
        float2 g = SampleBandGradient(posXZ, _Band0_ScaleOffset_AmplitudeMultiplier, 0);
        if (_Band1_ScaleOffset_AmplitudeMultiplier.w > 0.0)
            g += SampleBandGradient(posXZ, _Band1_ScaleOffset_AmplitudeMultiplier, 1);
        if (_Band2_ScaleOffset_AmplitudeMultiplier.w > 0.0)
            g += SampleBandGradient(posXZ, _Band2_ScaleOffset_AmplitudeMultiplier, 2);
        return g;
    }

    // Beam depuis la courbure (divergence du gradient). Concave = focalise = beam.
    // Rescale interne des seuils repris de la V1 (lo*0.3, hi*3.0).
    float OceanBeamPattern(float2 surfXZ)
    {
        float eps = 1.0 / max(_BeamScale, 0.01);
        float2 gC = SampleWaterGradient(surfXZ);
        float2 gX = SampleWaterGradient(surfXZ + float2(eps, 0));
        float2 gZ = SampleWaterGradient(surfXZ + float2(0, eps));
        float divN = (gX.x - gC.x + gZ.y - gC.y) / eps;

        // Courbure amplifiée (le gradient HDRP a une magnitude bien plus faible que
        // la normal-map V1) puis seuillée. Concave (-divN > 0) = focalise = beam.
        float curv = saturate(-divN * _BeamGain);
        float beam = smoothstep(_BeamThresholdLo, _BeamThresholdHi, curv);

        float2 cell = floor(surfXZ * _BeamScale * 0.5);
        float sizeVar = lerp(0.4, 1.0, _BeamHash(cell));
        return beam * sizeVar;
    }

    // March screen-space projeté sur la surface (port de ComputeGodRays V1).
    // positionSS = coordonnée pixel (SV_Position), déjà en pixels du buffer courant
    // (robuste sous dynamic resolution — cf. C3 audit).
    float3 ComputeGodRays(float2 positionSS, float3 posRWS, float3 camPosAWS, float linearDist, bool isSky)
    {
        float camDepthBelow = _WaterLevel - camPosAWS.y;
        if (camDepthBelow <= 0.0) return float3(0, 0, 0);

        float l = length(posRWS);
        float3 rayDir = (l > 1e-4) ? posRWS / l : float3(0, 1, 0);
        float maxDist = isSky ? _GodRayMaxDist : min(linearDist, _GodRayMaxDist);
        int   steps   = clamp(_GodRaySteps, 4, 64);
        float stepSize = maxDist / (float)steps;

        float3 sunDir = float3(-_SunDirWS.x, -abs(_SunDirWS.y), -_SunDirWS.z);
        float sunLen = length(sunDir);
        sunDir = (sunLen > 1e-4) ? sunDir / sunLen : float3(0, -1, 0);

        float3 beamDir = normalize(lerp(float3(0, -1, 0), sunDir, _BeamSunFollow));
        beamDir.y = min(beamDir.y, -0.1);
        beamDir = normalize(beamDir);

        float jitter = _IGN(positionSS);
        float accum = 0.0;

        [loop]
        for (int i = 0; i < steps; i++)
        {
            float t = stepSize * ((float)i + 0.5 + jitter);
            float3 sampleAWS = camPosAWS + rayDir * t;

            float depthBelow = _WaterLevel - sampleAWS.y;
            if (depthBelow < 0.0) break;

            float tUp = depthBelow / (-beamDir.y);
            float3 surfaceHit = sampleAWS - beamDir * tUp;

            float beam = OceanBeamPattern(surfaceHit.xz);
            float surfaceProximity = exp(-depthBelow * _BeamDepthFade);
            float distanceAtten = exp(-t * _BeamExtinction);

            accum += beam * surfaceProximity * distanceAtten * stepSize;
        }

        float depthFadeIn = smoothstep(0.0, _GodRayFadeInDepth, camDepthBelow);
        float horizonFade = smoothstep(0.1, 0.4, -beamDir.y);
        return accum * _GodRayColor.rgb * _GodRayIntensity * depthFadeIn * horizonFade;
    }

    float3 SafeColor(float3 c)
    {
        return (any(isnan(c)) || any(isinf(c))) ? float3(0, 0, 0) : max(c, 0.0);
    }

    float4 Frag(Varyings IN) : SV_Target
    {
        float2 uv = IN.texcoord;
        float2 positionSS = IN.positionCS.xy;      // pixels du buffer courant (SV_Position)
        uint2 pixelCoord = uint2(positionSS);

        float3 camPosAWS = _WorldSpaceCameraPos;
        float camDepthBelow = _WaterLevel - camPosAWS.y;

        float3 scene = SafeColor(CustomPassSampleCameraColor(uv, 0));

        // Hors de l'eau : passthrough.
        if (camDepthBelow <= 0.0)
            return float4(scene, 1.0);

        // Reconstruction de la position monde du pixel (calqué V1).
        float rawDepth = LOAD_TEXTURE2D_X(_CameraDepthTexture, pixelCoord).r;
        bool isSky = rawDepth < 0.0001;
        float2 posNDC = uv * 2.0 - 1.0;
        #if UNITY_UV_STARTS_AT_TOP
        posNDC.y = -posNDC.y;
        #endif
        float safeDepth = isSky ? 0.001 : rawDepth;
        float4 viewPos = mul(UNITY_MATRIX_I_VP, float4(posNDC, safeDepth, 1.0));
        float3 posRWS = (abs(viewPos.w) > 1e-5) ? viewPos.xyz / viewPos.w : float3(0, 0, 1);

        float len = length(posRWS);
        float linearDist = isSky ? 500.0 : len;

        float3 godRays = SafeColor(ComputeGodRays(positionSS, posRWS, camPosAWS, linearDist, isSky));

        if (_GodRayDebug == 1)
            return float4(godRays, 1.0);

        return float4(scene + godRays, 1.0);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            Name "Ombrage Underwater God Rays"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
