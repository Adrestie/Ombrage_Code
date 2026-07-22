Shader "Hidden/Ombrage/FoamHeight"
{
    // Capture top-down de la HAUTEUR monde du décor (edge foam d'empreinte).
    // Matrices EXPLICITES (aucune dépendance au binding built-in HDRP) :
    //   _OmbrageVP             : view-proj GPU (global, posé sur le CommandBuffer)
    //   _OmbrageObjectToWorld  : matrice objet->monde (par draw, via MaterialPropertyBlock)
    // ZTest LEqual + proj ortho (haut = proche) => le sommet du décor gagne par texel.
    SubShader
    {
        Pass
        {
            Name "FoamHeight"
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            float4x4 _OmbrageVP;
            float4x4 _OmbrageObjectToWorld;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float worldY : TEXCOORD0; };

            Varyings Vert(Attributes IN)
            {
                float3 posWS = mul(_OmbrageObjectToWorld, IN.positionOS).xyz;
                Varyings OUT;
                OUT.positionCS = mul(_OmbrageVP, float4(posWS, 1.0));
                OUT.worldY = posWS.y;
                return OUT;
            }

            float Frag(Varyings IN) : SV_Target
            {
                return IN.worldY;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
