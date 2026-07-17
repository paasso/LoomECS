Shader "Loom/SpeedDemoInstanced"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"

            struct InstanceData
            {
                float4 posSize;
                float4 color;
                float4 misc;
            };

            StructuredBuffer<InstanceData> _Instances;

            struct appdata
            {
                float3 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR0;
            };

            v2f vert(appdata v, uint instanceID : SV_InstanceID)
            {
                InstanceData data = _Instances[instanceID];
                float2 local = v.vertex.xy;
                float2 world = data.posSize.xy + local * data.posSize.zw;

                v2f o;
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 0.0, 1.0));
                o.uv = v.uv;
                o.color = data.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 d = i.uv - 0.5;
                if (dot(d, d) > 0.25)
                    discard;
                return i.color;
            }
            ENDCG
        }
    }
    FallBack Off
}
