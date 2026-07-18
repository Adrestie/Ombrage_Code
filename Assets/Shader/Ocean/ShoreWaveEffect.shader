Shader "Hidden/Ocean/ShoreWaveEffect"
{
    HLSLINCLUDE

    #pragma target 4.5
    #pragma editor_sync_compilation

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

    TEXTURE2D(_OceanDisplacementY);
    TEXTURE2D(_OceanDisplacementY1);
    TEXTURE2D(_OceanDisplacementY2);
    TEXTURE2D(_OceanDisplacementX);
    TEXTURE2D(_OceanDisplacementX1);
    TEXTURE2D(_OceanDisplacementX2);
    TEXTURE2D(_OceanDisplacementZ);
    TEXTURE2D(_OceanDisplacementZ1);
    TEXTURE2D(_OceanDisplacementZ2);
    TEXTURE2D(_OceanFoamMap);
    TEXTURE2D(_OceanFoamMap1);
    TEXTURE2D(_OceanFoamMap2);
    SAMPLER(sampler_linear_repeat);

    float _OceanPatchSize;
    float _OceanPatchSize1;
    float _OceanPatchSize2;
    int   _OceanCascadeCount;

    float  _WaterLevel;
    float  _ShoreHeightScale;
    float  _ShoreWashHeight;
    float  _ShoreWashFoamWidth;
    float  _ShoreWetDarkening;
    float  _ShoreFoamScale;
    float4 _ShoreFoamColor;
    float  _ShoreWashPower;
    float  _ShoreWashFadeTime;

    // Tracking RT from previous frame (R = prevWaveH, G = washMark)
    TEXTURE2D(_ShoreTrackingPrev);
    // Tracking RT from current frame (written by pass 0, read by pass 1)
    TEXTURE2D(_ShoreTrackingCurr);

    void _ShoreOceanSample(float3 posAWS, out float height, out float foam)
    {
        float2 gridXZ = posAWS.xz;

        [unroll]
        for (int iter = 0; iter < 6; iter++)
        {
            float2 dx_total = 0;

            float2 uv0 = gridXZ / _OceanPatchSize;
            dx_total.x += SAMPLE_TEXTURE2D_LOD(_OceanDisplacementX, sampler_linear_repeat, uv0, 0).r;
            dx_total.y += SAMPLE_TEXTURE2D_LOD(_OceanDisplacementZ, sampler_linear_repeat, uv0, 0).r;

            [branch] if (_OceanCascadeCount >= 2)
            {
                float2 uv1 = gridXZ / _OceanPatchSize1;
                dx_total.x += SAMPLE_TEXTURE2D_LOD(_OceanDisplacementX1, sampler_linear_repeat, uv1, 0).r;
                dx_total.y += SAMPLE_TEXTURE2D_LOD(_OceanDisplacementZ1, sampler_linear_repeat, uv1, 0).r;
            }

            [branch] if (_OceanCascadeCount >= 3)
            {
                float2 uv2 = gridXZ / _OceanPatchSize2;
                dx_total.x += SAMPLE_TEXTURE2D_LOD(_OceanDisplacementX2, sampler_linear_repeat, uv2, 0).r;
                dx_total.y += SAMPLE_TEXTURE2D_LOD(_OceanDisplacementZ2, sampler_linear_repeat, uv2, 0).r;
            }

            gridXZ = posAWS.xz + dx_total;
        }

        float h = 0;
        float f = 0;

        float2 uv0 = gridXZ / _OceanPatchSize;
        h += SAMPLE_TEXTURE2D_LOD(_OceanDisplacementY, sampler_linear_repeat, uv0, 0).r;
        f = SAMPLE_TEXTURE2D_LOD(_OceanFoamMap, sampler_linear_repeat, uv0, 0).r;

        [branch] if (_OceanCascadeCount >= 2)
        {
            float2 uv1 = gridXZ / _OceanPatchSize1;
            h += SAMPLE_TEXTURE2D_LOD(_OceanDisplacementY1, sampler_linear_repeat, uv1, 0).r;
            f = max(f, SAMPLE_TEXTURE2D_LOD(_OceanFoamMap1, sampler_linear_repeat, uv1, 0).r);
        }

        [branch] if (_OceanCascadeCount >= 3)
        {
            float2 uv2 = gridXZ / _OceanPatchSize2;
            h += SAMPLE_TEXTURE2D_LOD(_OceanDisplacementY2, sampler_linear_repeat, uv2, 0).r;
            f = max(f, SAMPLE_TEXTURE2D_LOD(_OceanFoamMap2, sampler_linear_repeat, uv2, 0).r);
        }

        height = h * _ShoreHeightScale;
        foam = f;
    }

    float2 _shoreGrad(float2 p)
    {
        float2 h = float2(dot(p, float2(127.1, 311.7)),
                          dot(p, float2(269.5, 183.3)));
        h = frac(sin(h) * 43758.5453);
        float a = h.x * 6.2831853;
        return float2(cos(a), sin(a));
    }

    float _shoreNoise(float2 p)
    {
        float2 i = floor(p);
        float2 f = frac(p);
        float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);

        float va = dot(_shoreGrad(i),                f);
        float vb = dot(_shoreGrad(i + float2(1, 0)), f - float2(1, 0));
        float vc = dot(_shoreGrad(i + float2(0, 1)), f - float2(0, 1));
        float vd = dot(_shoreGrad(i + float2(1, 1)), f - float2(1, 1));

        return lerp(lerp(va, vb, u.x), lerp(vc, vd, u.x), u.y) * 0.5 + 0.5;
    }

    struct Attributes { uint vertexID : SV_VertexID; };

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

    // ---------------------------------------------------------------
    // Pass 0: Update tracking
    // Output: R = current waveH, G = accumulated washMark
    // ---------------------------------------------------------------

    float4 FragTrack(Varyings IN) : SV_Target
    {
        uint2 pixelCoord = uint2(IN.positionCS.xy);
        float rawDepth = LOAD_TEXTURE2D_X(_CameraDepthTexture, pixelCoord).r;

        float4 prevData = LOAD_TEXTURE2D(_ShoreTrackingPrev, pixelCoord);
        float prevWaveH = prevData.r;
        float prevWashMark = prevData.g;

        if (rawDepth == UNITY_RAW_FAR_CLIP_VALUE)
            return float4(0, 0, 0, 0);

        float2 uv = IN.texcoord;
        float2 posNDC = uv * 2.0 - 1.0;
        #if UNITY_UV_STARTS_AT_TOP
        posNDC.y = -posNDC.y;
        #endif

        float4 posWS = mul(UNITY_MATRIX_I_VP, float4(posNDC, rawDepth, 1.0));
        float3 posAWS = GetAbsolutePositionWS(posWS.xyz / posWS.w);

        float roughAbove = posAWS.y - _WaterLevel;
        if (roughAbove > _ShoreWashHeight * 3.0 || roughAbove < -3.0)
            return float4(0, 0, 0, 0);

        float waveH, fftFoam;
        _ShoreOceanSample(posAWS, waveH, fftFoam);

        float dt = unity_DeltaTime.x;

        // First frame guard: if no valid previous data, just store current
        bool firstFrame = (prevWaveH == 0.0 && prevWashMark == 0.0);
        if (firstFrame || abs(prevWaveH) > 50.0 || prevWashMark > 100.0)
            return float4(waveH, 0, 0, 0);

        // Compute descent (positive = wave is dropping)
        float descent = prevWaveH - waveH;

        float washMark = prevWashMark;
        if (descent > 0.0)
            washMark += descent;

        // Decay
        float decayRate = 1.0 / max(_ShoreWashFadeTime, 0.5);
        washMark *= saturate(1.0 - decayRate * dt);
        washMark = min(washMark, _ShoreWashHeight * 2.0);

        return float4(waveH, washMark, 0, 0);
    }

    // ---------------------------------------------------------------
    // Pass 1: Render foam
    // ---------------------------------------------------------------

    float4 FragFoam(Varyings IN) : SV_Target
    {
        uint2 pixelCoord = uint2(IN.positionCS.xy);
        float rawDepth = LOAD_TEXTURE2D_X(_CameraDepthTexture, pixelCoord).r;

        if (rawDepth == UNITY_RAW_FAR_CLIP_VALUE) discard;

        float2 uv = IN.texcoord;
        float2 posNDC = uv * 2.0 - 1.0;
        #if UNITY_UV_STARTS_AT_TOP
        posNDC.y = -posNDC.y;
        #endif

        float4 posWS = mul(UNITY_MATRIX_I_VP, float4(posNDC, rawDepth, 1.0));
        float3 posAWS = GetAbsolutePositionWS(posWS.xyz / posWS.w);

        float waveH, fftFoam;
        _ShoreOceanSample(posAWS, waveH, fftFoam);

        float surfaceLevel = _WaterLevel + waveH;
        float above = posAWS.y - surfaceLevel;

        float4 trackData = LOAD_TEXTURE2D(_ShoreTrackingCurr, pixelCoord);
        float washMark = trackData.g;

        if (washMark < 0.005) discard;
        if (above < -0.1 || above > washMark) discard;

        // 0 at water, 1 at top of wash mark
        float bandPos = saturate(above / max(washMark, 0.01));

        // Stronger near water, weaker at top
        float spatialIntensity = pow(1.0 - bandPos, _ShoreWashPower);
        float topFade = smoothstep(1.0, 0.6, bandPos);
        float washGradient = spatialIntensity * topFade;

        // Foam noise
        float time = _Time.y;
        static const float2x2 rot = float2x2(0.80, 0.60, -0.60, 0.80);
        float2 noiseUV = posAWS.xz * _ShoreFoamScale;
        float n1 = _shoreNoise(noiseUV + time * float2(0.08, 0.03));
        noiseUV = mul(rot, noiseUV) * 2.1 + 5.3;
        float n2 = _shoreNoise(noiseUV - time * float2(0.12, 0.05));
        noiseUV = mul(rot, noiseUV) * 2.3 + 13.7;
        float n3 = _shoreNoise(noiseUV + time * float2(0.05, -0.07));
        noiseUV = mul(rot, noiseUV) * 2.0 + 27.1;
        float n4 = _shoreNoise(noiseUV - time * float2(0.03, 0.04));
        float noiseRaw = n1 * 0.4 + n2 * 0.3 + n3 * 0.2 + n4 * 0.1;

        float edgeThreshold = lerp(0.35, 0.55, bandPos);
        float foamDetail = smoothstep(edgeThreshold - 0.08, edgeThreshold + 0.08, noiseRaw);

        float solidEdge = smoothstep(0.2, 0.0, bandPos);
        float foamMask = washGradient * lerp(foamDetail, 1.0, solidEdge);

        // Wet sand darkening
        float wetMask = washGradient * _ShoreWetDarkening;

        // Premultiplied alpha output
        float foamA = foamMask * 0.85;
        float wetA  = wetMask * 0.5 * (1.0 - foamA);
        float totalA = saturate(foamA + wetA);

        if (totalA < 0.001) discard;

        float3 premultColor = _ShoreFoamColor.rgb * foamA;
        return float4(premultColor, totalA);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            Name "Shore Track"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragTrack
            ENDHLSL
        }

        Pass
        {
            Name "Shore Foam"
            ZWrite Off
            ZTest Always
            Blend One OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragFoam
            ENDHLSL
        }
    }
}
