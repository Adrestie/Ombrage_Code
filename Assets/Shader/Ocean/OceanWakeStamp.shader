Shader "Hidden/OceanWakeStamp"
{
    Properties
    {
        _MainTex ("Existing wake RT", 2D) = "black" {}
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

            // Set per stamp by OceanWakeManager
            float2 _StampCenter;     // UV center [0,1]
            float2 _StampDirection;  // normalized direction the vehicle came FROM (wake trails this way)
            float  _StampSpeed;      // [0,1] normalized speed
            float  _StampUVRadius;   // stamp radius in UV space

            float  _StampIntensity;
            float  _WakeAngle;       // degrees
            float  _WakeLength;
            float  _WakeWidth;
            float  _BowWaveRadius;
            float  _BowWaveIntensity;

            #define PI 3.14159265358979

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
                // Standard fullscreen triangle/quad for Blit
                OUT.positionCS = float4(IN.positionOS.xy * 2.0 - 1.0, 0, 1);
                #if UNITY_UV_STARTS_AT_TOP
                OUT.positionCS.y = -OUT.positionCS.y;
                #endif
                OUT.uv = IN.uv;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float existing = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).r;

                // Distance from stamp center in UV space (toroidal wrap)
                float2 delta = IN.uv - _StampCenter;
                // Toroidal wrap: shortest distance on torus
                delta = delta - round(delta);

                // Normalize to stamp radius → [-1, 1] local space
                float2 local = delta / max(_StampUVRadius, 0.001);
                float localDist = length(local);

                // Early out: skip pixels far from stamp
                if (localDist > 1.5)
                    return float4(existing, 0, 0, 1);

                // Rotate local space: Y axis = direction wake trails behind vehicle
                float2 dir = _StampDirection;
                float2 perp = float2(-dir.y, dir.x);
                float2 rotated = float2(dot(local, perp), dot(local, dir));
                // rotated.y > 0 = behind vehicle (where wake trails)
                // rotated.x = lateral offset from center line

                float wake = 0.0;

                // ── Bow wave (circular, at vehicle position) ────────
                float bowDist = length(local);
                float bow = 1.0 - smoothstep(0.0, _BowWaveRadius, bowDist);
                bow *= _BowWaveIntensity;

                // ── V-shape Kelvin wake arms ────────────────────────
                float halfAngleRad = _WakeAngle * (PI / 180.0);
                float tanAngle = tan(halfAngleRad);

                // Only behind the vehicle
                float behindMask = smoothstep(0.0, 0.08, rotated.y);

                // Progress along wake [0,1]
                float wakeProgress = saturate(rotated.y / _WakeLength);

                // Expected lateral position of wake arm
                float expectedX = rotated.y * tanAngle;

                // Distance from each arm
                float distLeft  = abs(rotated.x - expectedX);
                float distRight = abs(rotated.x + expectedX);
                float distFromArm = min(distLeft, distRight);

                // Arm width widens with distance
                float armWidth = _WakeWidth * (1.0 + wakeProgress * 2.0);
                float arm = 1.0 - smoothstep(0.0, armWidth, distFromArm);

                // Fade with distance
                float distanceFade = 1.0 - wakeProgress * wakeProgress;

                arm *= behindMask * distanceFade;

                // ── Combine ─────────────────────────────────────────
                wake = max(bow, arm);
                wake *= _StampIntensity * _StampSpeed;

                // Output: max of existing wake and new stamp
                float result = max(existing, wake);
                return float4(result, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
