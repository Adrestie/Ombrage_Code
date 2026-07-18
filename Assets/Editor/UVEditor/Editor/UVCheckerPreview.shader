Shader "Hidden/UVEditor/CheckerPreview"
{
    // Shader unlit minimal, utilisé UNIQUEMENT pour le rendu GL immediate-mode
    // de la preview dans la SceneView (GL.SetPass). Indépendant du pipeline
    // (HDRP inclus) : un shader CG unlit fonctionne avec GL.SetPass comme
    // "Hidden/Internal-Colored". Le canal UV à visualiser est sélectionné côté
    // C# en émettant l'UV correspondante via GL.TexCoord.
    Properties
    {
        _Tiling ("Checker Tiling", Float) = 8
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "IgnoreProjector" = "True" }

        Pass
        {
            Name "UV_CHECKER_PREVIEW"
            ZWrite On
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _Tiling;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float tiling = max(_Tiling, 1.0);

                // Damier en espace UV.
                float2 cell   = floor(i.uv * tiling);
                float  parity = fmod(cell.x + cell.y, 2.0);
                float3 checker = lerp(float3(0.32, 0.32, 0.34),
                                      float3(0.78, 0.78, 0.80),
                                      parity);

                // Lignes fines aux frontières de cellule.
                float2 f = frac(i.uv * tiling);
                float2 d = fwidth(i.uv * tiling);
                float2 lineMask = smoothstep(0.0, d * 1.5, f) *
                                  smoothstep(0.0, d * 1.5, 1.0 - f);
                float grid = min(lineMask.x, lineMask.y);
                checker = lerp(float3(0.12, 0.12, 0.13), checker, grid);

                // Teinte légère hors de l'intervalle [0,1] pour repérer le
                // débordement du layout UV.
                bool outside = i.uv.x < 0.0 || i.uv.x > 1.0 ||
                               i.uv.y < 0.0 || i.uv.y > 1.0;
                if (outside)
                    checker = lerp(checker, float3(0.85, 0.45, 0.30), 0.35);

                return fixed4(checker, 1.0);
            }
            ENDCG
        }
    }
}
