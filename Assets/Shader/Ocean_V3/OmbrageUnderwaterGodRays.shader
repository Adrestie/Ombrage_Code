Shader "Hidden/Ombrage/UnderwaterGodRays"
{
    // =========================================================================
    // Ombrage — God-rays underwater V3 (portage de l'approche V1 : screen-space,
    // beam depuis la COURBURE de la surface, décorrélé du froxel volumétrique).
    // -------------------------------------------------------------------------
    // ÉTAPE 1 — SPIKE : on affiche UNIQUEMENT le signal de beam brut (courbure
    // -> smoothstep), en niveaux de gris, pour valider que la source (gradient
    // du Water System HDRP) est lisse et suit les vagues. AUCUN march ici.
    //
    // Source : _WaterAdditionalDataBuffer (gradient de surface, .xy = dh/dx,dh/dz)
    // + _Band0_ScaleOffset_AmplitudeMultiplier (CB per-surface), tous deux exposés
    // en global par WaterSurface.SetGlobalTextures() appelé côté C# (pass).
    // Boilerplate custom-pass calqué sur la V1 (Hidden/Ocean/UnderwaterEffect).
    // =========================================================================
    HLSLINCLUDE

    #pragma target 4.5
    #pragma editor_sync_compilation

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassSampling.hlsl"

    // CB per-surface du Water System (donne _Band0_ScaleOffset_AmplitudeMultiplier, _BandResolution).
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Water/ShaderVariablesWater.cs.hlsl"

    // Buffer de gradient de surface (exposé en global par SetGlobalTextures()).
    Texture2DArray<float4> _WaterAdditionalDataBuffer;

    // Params poussés par le pass.
    float  _WaterLevel;
    float  _BeamScale;
    float  _BeamThresholdLo;
    float  _BeamThresholdHi;
    float4 _UnderwaterCamPixelSize; // (camW, camH, 0, 0)

    struct Attributes { uint vertexID : SV_VertexID; };
    struct Varyings { float4 positionCS : SV_POSITION; float2 texcoord : TEXCOORD0; };

    Varyings Vert(Attributes IN)
    {
        Varyings OUT;
        OUT.positionCS = GetFullScreenTriangleVertexPosition(IN.vertexID);
        OUT.texcoord   = GetFullScreenTriangleTexCoord(IN.vertexID);
        return OUT;
    }

    // Gradient de surface (dh/dx, dh/dz) à une position monde absolue (bande 0).
    float2 SampleWaterGradient(float2 posXZ)
    {
        float4 so = _Band0_ScaleOffset_AmplitudeMultiplier; // xyz = scale, offset.x, offset.y
        float2 uv = posXZ * so.x - so.yz;                   // = TransformWaterUV(posXZ, band0)
        return _WaterAdditionalDataBuffer.SampleLevel(s_linear_repeat_sampler, float3(uv, 0.0), 0).xy;
    }

    // Signal de beam depuis la courbure (divergence du gradient). Concave = focalise = beam.
    float OceanBeamPattern(float2 surfXZ)
    {
        float eps = 1.0 / max(_BeamScale, 0.01);
        float2 gC = SampleWaterGradient(surfXZ);
        float2 gX = SampleWaterGradient(surfXZ + float2(eps, 0));
        float2 gZ = SampleWaterGradient(surfXZ + float2(0, eps));
        float divN = (gX.x - gC.x + gZ.y - gC.y) / eps;     // ~ Laplacien (courbure)
        return smoothstep(_BeamThresholdLo, _BeamThresholdHi, -divN);
    }

    float4 Frag(Varyings IN) : SV_Target
    {
        float2 uv = IN.texcoord;
        uint2 pixelCoord = uint2(uv * _UnderwaterCamPixelSize.xy);

        float3 camPosAWS = _WorldSpaceCameraPos;
        float camDepthBelow = _WaterLevel - camPosAWS.y;

        // Hors de l'eau : passthrough.
        if (camDepthBelow <= 0.0)
            return float4(CustomPassSampleCameraColor(uv, 0), 1.0);

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
        float3 fragAWS = camPosAWS + posRWS;

        // SPIKE : courbure échantillonnée à la colonne du pixel. Sortie niveaux de gris.
        float beam = OceanBeamPattern(fragAWS.xz);
        return float4(beam.xxx, 1.0);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            Name "Ombrage Underwater God Rays (Spike)"
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
