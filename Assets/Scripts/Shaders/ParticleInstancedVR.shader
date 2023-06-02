Shader "FLIP/ParticleInstancedVR"
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

                UNITY_VERTEX_INPUT_INSTANCE_ID //Insert
            };

            struct v2_f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;

                UNITY_VERTEX_OUTPUT_STEREO //Insert
            };

            StructuredBuffer<mesh_data> data;

            v2_f vert(const appdata_t i/*, const uint instance_id: SV_InstanceID*/)
            {


                v2_f o;


                UNITY_SETUP_INSTANCE_ID(i); //Insert
                UNITY_INITIALIZE_OUTPUT(v2_f, o); //Insert
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); //Insert
                //UNITY_TRANSFER_INSTANCE_ID(i, o);


                const float4 pos = mul(data[unity_InstanceID].mat, i.vertex);
                o.vertex = UnityObjectToClipPos(pos);
                //o.color = lerp(_InactiveColor, _ActiveColor, data[instance_id].amount);
                o.color = data[unity_InstanceID].color;

                return o;
            }

            fixed4 frag(v2_f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}