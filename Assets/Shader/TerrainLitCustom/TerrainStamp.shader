Shader "Hidden/TerrainStamp"
{
    Properties
    {
        _SegmentA("Segment Start UV", Vector) = (0.5, 0.5, 0, 0)
        _SegmentB("Segment End UV", Vector) = (0.5, 0.5, 0, 0)
        _Radius("Radius", Float) = 0.01
        _Intensity("Intensity", Float) = 0.1
        _ToroidalWrap("Toroidal Wrap", Float) = 1.0
    }
    SubShader
    {
        Tags { "Queue"="Overlay" }
        ZTest Always ZWrite Off Cull Off
        BlendOp Max
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float2 _SegmentA;
            float2 _SegmentB;
            float _Radius;
            float _Intensity;
            float _ToroidalWrap;

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
                float2 ab = _SegmentB - _SegmentA;
                float2 ap = i.uv - _SegmentA;

                if (_ToroidalWrap > 0.5)
                {
                    ab -= round(ab);
                    ap -= round(ap);
                }

                float t = saturate(dot(ap, ab) / max(dot(ab, ab), 1e-8));
                float dist = length(ap - t * ab);

                float falloff = 1.0 - saturate(dist / _Radius);
                falloff = falloff * falloff;
                return falloff * _Intensity;
            }
            ENDCG
        }
    }
}
