Shader "Hidden/TerrainDeformFade"
{
    Properties
    {
        _MainTex("Tex", 2D) = "black" {}
        _FadeAmount("Fade", Float) = 0.001
        _DiffusionStrength("Diffusion", Range(0, 1)) = 0.3
        _TexelSize("Texel Size", Vector) = (0.00048828125, 0.00048828125, 0, 0)
    }
    SubShader
    {
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _FadeAmount;
            float _DiffusionStrength;
            float4 _TexelSize; // xy = 1/resolution

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(float4 vertex : POSITION, float2 uv : TEXCOORD0)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.uv = uv;
                return o;
            }

            float frag(v2f i) : SV_Target
            {
                float2 tx = _TexelSize.xy;
                float center = tex2D(_MainTex, i.uv).r;

                // 3x3 weighted average (cross pattern + diagonals)
                // Cross neighbors (weight 2)
                float n  = tex2D(_MainTex, i.uv + float2( 0,  tx.y)).r;
                float s  = tex2D(_MainTex, i.uv + float2( 0, -tx.y)).r;
                float e  = tex2D(_MainTex, i.uv + float2( tx.x,  0)).r;
                float w  = tex2D(_MainTex, i.uv + float2(-tx.x,  0)).r;
                // Diagonal neighbors (weight 1)
                float ne = tex2D(_MainTex, i.uv + float2( tx.x,  tx.y)).r;
                float nw = tex2D(_MainTex, i.uv + float2(-tx.x,  tx.y)).r;
                float se = tex2D(_MainTex, i.uv + float2( tx.x, -tx.y)).r;
                float sw = tex2D(_MainTex, i.uv + float2(-tx.x, -tx.y)).r;

                // Weighted average: center=4, cross=2, diag=1 → total=16
                float avg = (center * 4.0
                           + (n + s + e + w) * 2.0
                           + (ne + nw + se + sw) * 1.0) / 16.0;

                // Blend between sharp (center) and smooth (avg)
                float diffused = lerp(center, avg, _DiffusionStrength);

                // Apply fade
                return max(0, diffused - _FadeAmount);
            }
            ENDCG
        }
    }
}
