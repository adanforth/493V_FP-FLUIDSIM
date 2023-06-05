Shader "FLIP/ParticleInstancedDepth"
{
    Properties
    {
        //_InteractionRadius("Interaction Radius",float) = 30
        //_InactiveColor("Inactive color", Color) = (.2, .2, .2, 1)
        _ActiveColor("Active color", Color) = (1, .7, .0, 1)
    }
        SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            //float4 _InactiveColor;
            float4 _ActiveColor;
            //float _InteractionRadius;

            struct mesh_data
            {
                float4x4 mat;
                float4 color;
            };

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2_f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            StructuredBuffer<mesh_data> data;
            //the depth texture
            sampler2D _CameraDepthTexture;

            v2_f vert(const appdata_t i, const uint instance_id: SV_InstanceID)
            {
                v2_f o;
                const float4 pos = mul(data[instance_id].mat, i.vertex);
                o.vertex = UnityObjectToClipPos(pos);
                //o.color = lerp(_InactiveColor, _ActiveColor, data[instance_id].amount);
                //o.color = data[instance_id].color;
                o.color = .01 * pos.z / pos.w;
                o.uv = i.uv;
                return o;
            }

            fixed4 frag(v2_f i) : SV_Target
            {
                float depth = tex2D(_CameraDepthTexture, i.uv).r;
                return depth;
                depth = Linear01Depth(depth);
            }
            ENDCG
        }
    }
}