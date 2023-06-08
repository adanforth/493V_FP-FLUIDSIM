Shader "CustomRenderTexture/rendertex"
{
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)
        _Tex("InputTex", 2D) = "white" {}
        _ScreenWidth("ScreenWidth", float) = 1280
        _ScreenHeight("ScreenHeight", float) = 720
    }

        SubShader
    {
        Lighting Off
        Blend One Zero

        Pass
        {
            CGPROGRAM
            #include "UnityCustomRenderTexture.cginc"

            #pragma vertex InitCustomRenderTextureVertexShader
            #pragma fragment frag
            #pragma target 3.0

            sampler2D _CameraDepthTexture;

            float4      _Color;
            sampler2D   _Tex;
            float       _ScreenHeight;
            float       _ScreenWidth;


            float3 calculateViewPos(float2 coord, float depth) 
            {    
                //float3 p_ndc = float3((2 * coord.x / _ScreenWidth - 1), (2 * coord.y / _ScreenHeight - 1), (2 * depth - 1));
                float3 p_ndc = float3(coord, 2 * depth - 1);

                float w_clip = UNITY_MATRIX_P[2][3] / (p_ndc[2] + UNITY_MATRIX_P[2][2]);

                return p_ndc * w_clip;
            }

            float4 frag(v2f_init_customrendertexture IN) : COLOR
            {
                
                //if (tex2D(_Tex, IN.texcoord.xy).w == 1)
                //{
                //    return tex2D(_Tex, IN.texcoord.xy);
                //}
                float4 sum = float4(0.0,0,0,0);

                float2 u_texture_size_inv = (1 / _ScreenWidth, 1 / _ScreenHeight);

  
                int M = 15;

                for (int i = 0; i < M; ++i)
                {
                    for (int j = 0; j < M; ++j)
                    {
                        float2 tc = IN.texcoord.xy + u_texture_size_inv * float2(float(i - M), float(j - M));
                        sum += tex2D(_Tex, tc) / (M * M);
                    }
                }

                

                return sum;


                //float depth = tex2D(_CameraDepthTexture , IN.texcoord.xy).x;

            }
            ENDCG
        }
    }
}