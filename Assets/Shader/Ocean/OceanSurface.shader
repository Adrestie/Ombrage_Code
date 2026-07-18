Shader "Ocean/OceanSurface"
{
    Properties
    {
        [Header(Colors)]
        _ShallowColor ("Shallow color", Color)          = (0.1, 0.75, 0.65, 1)
        _DeepColor    ("Deep color",    Color)          = (0.02, 0.12, 0.25, 1)
        _SSSColor     ("SSS color",     Color)          = (0.1, 0.6, 0.4, 1)
        _FoamColor    ("Foam color",    Color)          = (0.95, 0.97, 1, 1)

        [Header(Foam Textures)]
        _FoamTexHigh  ("Foam high-freq (crests)", 2D)   = "white" {}
        _FoamTexLow   ("Foam low-freq (dissipating)", 2D) = "white" {}
        _FoamTexScale ("Foam texture tiling", Range(0.5, 20)) = 8
        _FoamTexBlend ("Hi/Lo blend sharpness", Range(0.1, 5)) = 1.5

        [Header(Shading)]
        _FresnelPower      ("Fresnel power",        Range(1, 10))   = 5
        _SSSIntensity      ("SSS intensity",        Range(0, 5))    = 1.5
        _SSSPower          ("SSS falloff power",    Range(1, 8))    = 3
        _SSSSpread         ("SSS spread",           Range(0.1, 10)) = 1.0
        _SpecularIntensity ("Specular intensity",   Range(0, 5))    = 1.0
        _OceanRoughness    ("Ocean roughness",      Range(0.01, 1)) = 0.05
        _HeightScale       ("Height scale",         Range(0.01, 10))= 1.0
        _AmbientStrength   ("Ambient strength",     Range(0, 1))    = 0.15
        _WrapDiffuse       ("Wrap diffuse",         Range(0, 1))    = 0.3

        [Header(Depth Coloring)]
        _DepthAbsorption   ("Absorption rate",      Range(0.01, 2)) = 0.3
        _DepthMaxDistance  ("Max visible depth",     Range(1, 100))  = 20
        _AbsorptionColor   ("Absorption tint",      Color)          = (0.01, 0.04, 0.08, 1)

        [Header(Refraction)]
        _RefractionStrength ("Distortion strength",  Range(0, 1))   = 0.3
        _RefractionDepthFade ("Depth fade",          Range(0.1, 20))= 5

        [Header(Reflection)]
        _HorizonColor      ("Horizon color",        Color)          = (0.35, 0.45, 0.55, 1)
        _ZenithColor       ("Zenith color",         Color)          = (0.15, 0.3, 0.6, 1)
        _ReflectionIntensity ("Reflection intensity", Range(0, 2))  = 0.8

        [Header(Planar Reflection)]
        [Toggle] _UsePlanarReflection ("Use planar reflection", Float) = 0
        _PlanarReflectionBlend ("Planar/Sky blend", Range(0, 1)) = 0.8

        [Header(Tessellation)]
        _TessMaxFactor     ("Max tess factor",       Range(1, 64))  = 16
        _TessMaxDistance   ("Max tess distance",     Range(10, 500)) = 150

        [Header(Light)]
        _SunDirection      ("Sun direction (auto)",  Vector)        = (0, -1, 0, 0)

        [Header(Shore Foam)]
        _ShoreFoamDistance ("Shore foam distance", Range(0.1, 20)) = 3
        _ShoreFoamStrength ("Shore foam strength", Range(0, 3)) = 1.2
        _ShoreFoamFalloff  ("Shore foam falloff",  Range(0.5, 5)) = 1.5

        [Header(Debug)]
        [Toggle] _DebugFoam ("Show raw foam values", Float) = 0
        [Toggle] _DebugWake ("Show wake displacement", Float) = 0
        [Toggle] _DebugShoreAtten ("Show shore attenuation", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent-10"
        }

        // ════════════════════════════════════════════════════════════════
        //  Pass 0 — ForwardOnly
        // ════════════════════════════════════════════════════════════════
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex   OceanVertTess
            #pragma hull     OceanHull
            #pragma domain   OceanDomain
            #pragma fragment OceanFrag
            #pragma target 5.0

            #define SCREEN_SPACE_SHADOWS_OFF
            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_LOW AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH

            #define SHADERPASS SHADERPASS_FORWARD
            #define HAS_LIGHTLOOP

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/FragInputs.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoop.hlsl"

            #include "OceanInput.hlsl"
            #include "OceanCaustics.hlsl"

            float4 _SunDirection;
            float  _OceanWaterLevel;
            float  _ShoreFoamDistance;
            float  _ShoreFoamStrength;
            float  _ShoreFoamFalloff;
            float  _DebugShoreAtten;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float2 oceanUV     : TEXCOORD0;
                float3 positionRWS : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float3 posAWS      : TEXCOORD3;
                float  waveHeight  : TEXCOORD4;
                float  choppiness  : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct VaryingsDepth
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            #include "OceanTessellation.hlsl"

            float4 OceanFrag(Varyings IN) : SV_Target
            {
                // Discard surface when camera is underwater — UnderwaterPass handles the view from below
                float3 camPosAWS = _WorldSpaceCameraPos;
                if (camPosAWS.y < _OceanWaterLevel)
                    discard;

                float2 uv = IN.oceanUV;

                // ── Sample maps ─────────────────────────────────────
                float3 N    = normalize(SampleOceanNormal(IN.posAWS));
                float  foam = SampleOceanFoam(IN.posAWS);

                // Attenuate FFT foam near shore (same logic as domain shader)
                float shoreAttenFrag = 1.0;
                if (_OceanShoreMapSize > 0.0)
                {
                    float2 shoreUVF = (IN.posAWS.xz - _OceanShoreMapCenter.xy) / _OceanShoreMapSize + 0.5;
                    if (all(shoreUVF > 0.01) && all(shoreUVF < 0.99))
                    {
                        float4 shoreDataFrag = SampleOceanShoreMap(IN.posAWS);
                        if (shoreDataFrag.b > -9000.0)
                        {
                            shoreAttenFrag = smoothstep(0.0, _WaveShoreAttenuationDist, shoreDataFrag.a);
                            shoreAttenFrag = lerp(_WaveShoreMinAmplitude, 1.0, shoreAttenFrag);
                        }
                    }
                }
                foam *= shoreAttenFrag;

                float wakeFoam = SampleWakeFoam(IN.posAWS);
                foam = max(foam, wakeFoam);

                [branch]
                if (_DebugWake > 0.5)
                {
                    float wv = SampleWakeDisplacement(IN.posAWS);
                    return float4(saturate(-wv * 10.0), saturate(wv * 10.0), 0, 1);
                }

                [branch]
                if (_DebugShoreAtten > 0.5)
                {
                    float4 dbgShore = SampleOceanShoreMap(IN.posAWS);
                    // R = groundY / 200 (terrain height in world)
                    // G = signedDist mapped: green near waterline, black far below/above
                    // B = signedDist < 0 (terrain above water)
                    float groundY = dbgShore.b;
                    float signedDist = dbgShore.a;
                    return float4(saturate(groundY / 200.0), 1.0 - saturate(abs(signedDist) / 20.0), signedDist < 0.0 ? 1.0 : 0.0, 1.0);
                }

                float choppiness = IN.choppiness;

                // ── HDRP light data ─────────────────────────────────
                float preExposure = GetCurrentExposureMultiplier();
                float3 sunRadiance = float3(0, 0, 0);
                float3 sunFwd = float3(0, -1, 0);
                if (_DirectionalLightCount > 0)
                {
                    sunRadiance = _DirectionalLightDatas[0].color;
                    sunFwd = _DirectionalLightDatas[0].forward;
                }
                float3 exposedSun = sunRadiance * preExposure;

                float shadow = 1.0;
                float fragDist = length(IN.positionRWS);
                PositionInputs posInput = GetPositionInput(
                    IN.positionCS.xy, _ScreenSize.zw,
                    IN.positionCS.z, fragDist,
                    IN.positionRWS, uint2(0, 0));

                // ── Directions ──────────────────────────────────────
                float3 V = normalize(-IN.positionRWS);
                float3 L = normalize(-sunFwd);
                float3 H = normalize(V + L);

                float NdotL = saturate(dot(N, L));
                float NdotV = saturate(dot(N, V));
                float NdotH = saturate(dot(N, H));
                float VdotH = saturate(dot(V, H));
                float VdotL = dot(V, L);

                // ── Depth-based coloring (deferred until world-space depth is known)
                float3 waterBodyColor = _ShallowColor.rgb;

                // ── Fresnel (Schlick, F0 ~ 0.02 for water IOR 1.33)
                float F0 = 0.02;
                float fresnel = F0 + (1.0 - F0) * pow(1.0 - NdotV, _FresnelPower);

                // ── Refraction + Caustics + Shore Foam ──────────────
                float3 sceneColor = float3(0, 0, 0);
                float refractionMask = 0;
                float depthBelowSurface = 0;
                float edgeFade = 1.0;

                #ifdef SHADER_STAGE_FRAGMENT
                {
                    uint2 pixelCoord = uint2(IN.positionCS.xy);
                    float2 refrOffset = N.xz * _RefractionStrength * _ScreenSize.x * 0.02 * NdotV;
                    uint2 refractedPixel = uint2(clamp(
                        float2(pixelCoord) + refrOffset,
                        float2(0, 0), _ScreenSize.xy - 1));

                    sceneColor = LOAD_TEXTURE2D_X(_ColorPyramidTexture, refractedPixel).rgb;

                    float rawDepth = LOAD_TEXTURE2D_X(_CameraDepthTexture, pixelCoord).r;

                    // Reconstruct scene world position for caustics + shore foam
                    [branch] if (rawDepth != UNITY_RAW_FAR_CLIP_VALUE)
                    {
                        float2 sceneNDC = IN.positionCS.xy * _ScreenSize.zw * 2.0 - 1.0;
                        #if UNITY_UV_STARTS_AT_TOP
                        sceneNDC.y = -sceneNDC.y;
                        #endif
                        float4 hScene = mul(UNITY_MATRIX_I_VP, float4(sceneNDC, rawDepth, 1.0));
                        float3 sceneAWS = GetAbsolutePositionWS(hScene.xyz / hScene.w);

                        float surfaceY = _OceanWaterLevel + SampleOceanHeightCorrected(sceneAWS);
                        depthBelowSurface = max(0, surfaceY - sceneAWS.y);
                        edgeFade = smoothstep(0.0, 0.5, depthBelowSurface);

                        // ── Caustics ──────────────────────────────
                        [branch] if (depthBelowSurface > 0.1)
                        {
                            float3 surfacePos = float3(sceneAWS.x, surfaceY, sceneAWS.z);
                            sceneColor *= 1.0 + ComputeOceanCaustics(surfacePos, depthBelowSurface);
                        }

                    }
                }
                #endif

                // ── Shadow attenuation ──────────────────────────────
                {
                    int shadowIdx = _DirectionalLightDatas[0].shadowIndex;
                    if (shadowIdx >= 0)
                    {
                        HDShadowContext shadowContext = InitShadowContext();
                        shadow = GetDirectionalShadowAttenuation(shadowContext,
                            posInput.positionSS, IN.positionRWS, float3(0, 1, 0),
                            shadowIdx, -sunFwd);
                    }
                }
                exposedSun *= shadow;

                // ── Depth absorption + refraction mask (world-space) ──
                float apparentDepth = depthBelowSurface > 0.0
                    ? depthBelowSurface
                    : _DepthMaxDistance;
                float absorption = 1.0 - exp(-apparentDepth * _DepthAbsorption);
                waterBodyColor = lerp(_ShallowColor.rgb, _AbsorptionColor.rgb, absorption);
                refractionMask = exp(-apparentDepth / max(_RefractionDepthFade, 0.1));

                // ── Shore / intersection foam ───────────────────────
                float shoreFoamAmount = 0;
                [branch] if (_ShoreFoamStrength > 0.001 && depthBelowSurface > 0.0)
                {
                    shoreFoamAmount = 1.0 - saturate(depthBelowSurface / _ShoreFoamDistance);
                    shoreFoamAmount = pow(shoreFoamAmount, _ShoreFoamFalloff) * _ShoreFoamStrength;
                    foam = max(foam, shoreFoamAmount);
                }

                // ── Refracted light (transmitted through surface) ───
                // Cap sun contribution to body color so artist colors don't blow out.
                // Specular/SSS/foam keep full intensity — only body color is capped.
                float sunLum = dot(exposedSun, float3(0.2126, 0.7152, 0.0722));
                float3 bodySun = exposedSun * rcp(max(sunLum, 1.0));

                float3 underwaterColor = lerp(waterBodyColor * bodySun, sceneColor, refractionMask);
                float diffuse = saturate((NdotL + _WrapDiffuse) / (1.0 + _WrapDiffuse));
                float3 diffuseLight = underwaterColor * diffuse;

                // ── SSS ─────────────────────────────────────────────
                float sssThreshold = 1.0 - _SSSSpread;
                float transitionWidth = max(0.3, _SSSSpread * 0.3);
                float backlitGradient = smoothstep(sssThreshold - transitionWidth,
                                                   sssThreshold + transitionWidth, -VdotL);
                float flankMask = smoothstep(0.0, 0.5 + 0.5 * _SSSSpread, choppiness);
                float thinEdge = pow(1.0 - NdotV, _SSSPower);
                float sssBacklit = backlitGradient * flankMask * thinEdge;
                float sssEdge = pow(1.0 - NdotV, _SSSPower + 2.0) * 0.3;
                float sssMask = saturate(sssBacklit + sssEdge);
                float3 sssLight = _SSSColor.rgb * _SSSIntensity * sssMask * exposedSun;

                float3 refractedLight = diffuseLight + sssLight;
                float3 ambient = waterBodyColor * _AmbientStrength * min(preExposure, 1.0);

                // ── Reflected light (sky gradient / planar + GGX sun specular)
                float3 reflDir = reflect(-V, N);
                float upFactor = saturate(reflDir.y * 0.5 + 0.5);
                float3 skyRefl = lerp(_HorizonColor.rgb, _ZenithColor.rgb, upFactor) * _ReflectionIntensity * min(preExposure, 1.0);

                float3 envRefl = skyRefl;
                [branch]
                if (_UsePlanarReflection > 0.5)
                {
                    float2 screenUV = IN.positionCS.xy * (_ScreenSize.zw);
                    screenUV.x = 1.0 - screenUV.x;
                    float2 reflUV = screenUV + N.xz * 0.03;
                    float3 planarRefl = SAMPLE_TEXTURE2D(_OceanPlanarReflectionTex, sampler_linear_clamp, reflUV).rgb;
                    envRefl = lerp(skyRefl, planarRefl * _ReflectionIntensity, _PlanarReflectionBlend);
                }

                float a = max(_OceanRoughness * _OceanRoughness, 0.001);
                float a2 = a * a;
                float denom = NdotH * NdotH * (a2 - 1.0) + 1.0;
                float D_ggx = a2 / (PI * denom * denom);
                float F_spec = F0 + (1.0 - F0) * pow(1.0 - VdotH, 5.0);
                float3 sunSpec = exposedSun * D_ggx * F_spec * _SpecularIntensity * NdotL;

                float3 reflectedLight = envRefl + sunSpec;

                // ── Combine via Fresnel (attenuated in shallow water) ─
                fresnel *= saturate(1.0 - refractionMask);
                float3 color = lerp(refractedLight + ambient, reflectedLight, fresnel);

                // ── Foam ────────────────────────────────────────────
                [branch]
                if (_DebugFoam > 0.5)
                    return float4(foam, foam, foam, 1.0);

                float foamMask = saturate(foam);
                float2 foamUV = uv * _FoamTexScale;
                float texHigh = SAMPLE_TEXTURE2D(_FoamTexHigh, sampler_linear_repeat, foamUV).r;
                float texLow  = SAMPLE_TEXTURE2D(_FoamTexLow,  sampler_linear_repeat, foamUV).r;
                float foamPattern = lerp(texLow, texHigh, saturate(foamMask * _FoamTexBlend));
                float foamDetail = lerp(0.3, 1.0, foamPattern);
                float foamAlpha = saturate(foamMask * foamDetail);
                float3 foamLit = _FoamColor.rgb * (diffuse * 0.6 + 0.4) * bodySun;
                color = lerp(color, foamLit, foamAlpha);

                // At shallow edges, blend toward foam so the ocean never
                // reveals bare terrain — the shore wave pass handles terrain.
                color = lerp(foamLit, color, edgeFade);

                return float4(color, 1.0);
            }
            ENDHLSL
        }

        // ════════════════════════════════════════════════════════════════
        //  Pass 1 — DepthForwardOnly
        // ════════════════════════════════════════════════════════════════
        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }
            Cull Back
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex   OceanVertTess
            #pragma hull     OceanHull
            #pragma domain   OceanDomainDepth
            #pragma fragment OceanFragDepth
            #pragma target 5.0

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            #include "OceanInput.hlsl"

            float _OceanWaterLevel;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Must match ForwardOnly Varyings (shared OceanTessellation.hlsl)
            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float2 oceanUV     : TEXCOORD0;
                float3 positionRWS : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float3 posAWS      : TEXCOORD3;
                float  waveHeight  : TEXCOORD4;
                float  choppiness  : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct VaryingsDepth
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            #include "OceanTessellation.hlsl"

            float4 OceanFragDepth(VaryingsDepth IN) : SV_Target
            {
                if (_WorldSpaceCameraPos.y < _OceanWaterLevel)
                    discard;
                return 0;
            }
            ENDHLSL
        }
    }
}
