Shader "Hidden/Ombrage/FoamFootprint"
{
    // Empreinte d'écume : rend la TRANCHE de l'objet au niveau de l'eau dans une RT
    // coverage (R8). La projection ortho top-down (near/far serrés autour du niveau
    // d'eau, réglés côté C#) clippe tout ce qui est au-dessus/en-dessous de la dalle :
    // seule la coupe au plan de l'eau est rasterisée -> contour fidèle à la jonction,
    // indépendant de la largeur de l'objet plus haut.
    //
    // Sortie = 1.0 (présence). BlendOp Max => recouvrements et faces multiples => 1.
    // UnityCG + UnityObjectToClipPos : matrices bindées par le moteur (SetViewProjection
    // Matrices + DrawMesh côté CommandBuffer), aucune matrice manuelle.
    SubShader
    {
        Pass
        {
            Name "FoamFootprint"
            ZWrite Off
            ZTest Always
            Cull Off
            BlendOp Max
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f { float4 pos : SV_POSITION; };

            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                return o;
            }

            float frag(v2f i) : SV_Target { return 1.0; }
            ENDCG
        }
    }
    Fallback Off
}
