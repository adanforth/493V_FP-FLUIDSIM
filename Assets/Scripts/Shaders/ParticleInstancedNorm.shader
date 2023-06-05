Shader "FLIP/ParticleInstancedNorm"
{
    Properties
    {
        //_InteractionRadius("Interaction Radius",float) = 30
        //_InactiveColor("Inactive color", Color) = (.2, .2, .2, 1)
        _ActiveColor("Active color", Color) = (1, .7, .0, 1)
    }
        SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalRenderPipeline" }


        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"            

            //#include "UnityCG.cginc"

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
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                half3 normal    : NORMAL;
            };

            struct v2_f
            {
                float4 vertex   : SV_POSITION;
                float4 color    : COLOR;
                half3 normal    : TEXCOORD0;

            };

            StructuredBuffer<mesh_data> data;

            float4 UnityObjectToClipPos(float3 pos)
            {
                float4 temp = TransformObjectToHClip(pos);
                return  temp;
            }

            v2_f vert(const appdata_t i, const uint instance_id: SV_InstanceID)
            {
                v2_f o;


                const float4 pos = mul(data[instance_id].mat, i.vertex);
                o.vertex = UnityObjectToClipPos(pos);
                //o.color = lerp(_InactiveColor, _ActiveColor, data[instance_id].amount);
                //o.color = data[instance_id].color;
                o.normal = TransformObjectToWorldNormal(i.normal);

                return o;
            }

            float4 frag(v2_f i) : SV_Target
            {
                half4 color = 0;
                color.rgb = i.normal * 0.5 + 0.5;
                return color;
            }
            ENDHLSL
        }
    }
}