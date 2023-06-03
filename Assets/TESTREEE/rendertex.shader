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
                float depth = tex2D(_CameraDepthTexture , IN.texcoord.xy).x;

                return tex2D(_Tex, IN.texcoord.xy);


                if (depth > .9999) {
                    return _Color * tex2D(_Tex, IN.texcoord.xy);
                }

                float3 viewPos = calculateViewPos(IN.texcoord.xy, depth);

                float offset = 0.05 / _ScreenWidth;
                float2 uv = float2(IN.texcoord.x + offset, IN.texcoord.y);
                float2 uv2 = float2(IN.texcoord.x - offset, IN.texcoord.y);

                float3 ddx = calculateViewPos(uv, tex2D(_Tex, uv)) - viewPos;
                float3 ddx2 = viewPos - calculateViewPos(uv2, tex2D(_Tex, uv2));
                if (abs(ddx.z) > abs(ddx2.z)) {
                    ddx = ddx2;
                }

                offset = 0.05 / _ScreenHeight;
                uv = float2(IN.texcoord.x, IN.texcoord.y + offset);
                uv2 = float2(IN.texcoord.x, IN.texcoord.y - offset);

                float3 ddy = calculateViewPos(uv, tex2D(_Tex, uv)) - viewPos;
                float3 ddy2 = viewPos - calculateViewPos(uv2, tex2D(_Tex, uv2));
                if (abs(ddy.z) > abs(ddy2.z)) {
                    ddy = ddy2;
                }

                float3 n = normalize(cross(ddx, ddy));

                //return _Color * tex2D(_Tex, IN.texcoord.xy);
                return float4(n, 1.0);
            }
            ENDCG
        }
    }
}