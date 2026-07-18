Shader "Hidden/VertexColorEditor/Preview"
{
    // Shader unlit minimal, utilisé UNIQUEMENT pour le rendu GL immediate-mode
    // de la preview dans la SceneView. Indépendant du pipeline (HDRP inclus) :
    // un shader CG unlit fonctionne avec GL.SetPass comme "Hidden/Internal-Colored".
    Properties
    {
        _CheckerSize ("Checker Size (px)", Float) = 8
        _UseChecker  ("Use Checker", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "IgnoreProjector" = "True" }

        Pass
        {
            Name "VERTEXCOLOR_PREVIEW"
            ZWrite On
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _CheckerSize;
            float _UseChecker;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // rgb = couleur de contenu, a = alpha du vertex.
                float3 content = i.color.rgb;
                float  alpha   = saturate(i.color.a);

                // Damier en espace écran, taille fixe en pixels (i.pos.xy = coords pixel).
                float2 cell   = floor(i.pos.xy / max(_CheckerSize, 1.0));
                float  parity = fmod(cell.x + cell.y, 2.0);
                float3 checker = lerp(float3(0.34, 0.34, 0.34),
                                      float3(0.60, 0.60, 0.60),
                                      parity);

                // Fond : damier (RGB/R/G/B) ou noir (canal A).
                float3 background = lerp(float3(0.0, 0.0, 0.0),
                                         checker,
                                         saturate(_UseChecker));

                // Fondu linéaire fond -> contenu piloté par l'alpha.
                float3 final = lerp(background, content, alpha);
                return fixed4(final, 1.0);
            }
            ENDCG
        }
    }
}
