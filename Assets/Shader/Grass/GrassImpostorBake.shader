// GrassImpostorBake.shader
// ---------------------------------------------------------------------------------
// Utility shader (NOT part of the HDRP pipeline). Renders a CPU-built grass TUFT mesh
// into an offscreen RenderTexture to bake a horizon impostor card:
//   RGB = neutral grass shading (root->tip gradient * simple lambert + base AO),
//   A   = coverage (1 where a blade is, 0 elsewhere = the tuft silhouette).
// The horizon billboards (étape C) sample this and tint it by the species colour.
// Rendered via CommandBuffer.DrawMesh with a manual ortho VP, so it uses plain
// object->clip math (UnityCG), independent of SRP.
Shader "Hidden/Ombrage/GrassImpostorBake"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Cull Off            // blades are double-sided
            ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float3 nrm : TEXCOORD0; float2 uv : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.nrm = UnityObjectToWorldNormal(v.normal);
                o.uv  = v.uv;            // uv.y = 0 root -> 1 tip
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Neutral grass gradient (kept fairly desaturated so per-species tint reads well at draw).
                float3 root = float3(0.16, 0.20, 0.06);
                float3 tip  = float3(0.40, 0.58, 0.20);
                float3 col  = lerp(root, tip, saturate(i.uv.y));
                // Simple lambert from a fixed key light + ambient, so the tuft has form.
                float3 L  = normalize(float3(0.35, 0.75, 0.45));
                float  nl = saturate(dot(normalize(i.nrm), L)) * 0.65 + 0.35;
                // Base AO: slightly darker near the root for depth.
                float ao = lerp(0.7, 1.0, saturate(i.uv.y * 1.5));
                col *= nl * ao;
                return fixed4(col, 1.0);   // A = coverage (RT cleared to 0)
            }
            ENDCG
        }
    }
    Fallback Off
}
