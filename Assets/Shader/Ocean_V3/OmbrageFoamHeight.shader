Shader "Hidden/Ombrage/FoamHeight"
{
    // Capture top-down de la HAUTEUR monde du décor (edge foam d'empreinte).
    // Rendu via CommandBuffer.DrawRenderer + VP orthographique (SetViewProjectionMatrices).
    // Volontairement SANS include : on utilise les matrices built-in absolues
    // (unity_ObjectToWorld / unity_MatrixVP) pour éviter le camera-relative HDRP et
    // la surcharge de UNITY_MATRIX_VP par les ShaderVariables HDRP.
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

            float4x4 unity_ObjectToWorld; // alimenté par DrawRenderer (absolu)
            float4x4 unity_MatrixVP;      // alimenté par SetViewProjectionMatrices

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float worldY : TEXCOORD0; };

            Varyings Vert(Attributes IN)
            {
                float3 posWS = mul(unity_ObjectToWorld, IN.positionOS).xyz;
                Varyings OUT;
                OUT.positionCS = mul(unity_MatrixVP, float4(posWS, 1.0));
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
