Shader "BoardGameKit/CardTwoSided"
{
    Properties
    {
        _MainTex ("Front Texture (表面)", 2D) = "white" {}
        _BackTex ("Back Texture (裏面)", 2D) = "white" {}
        _Color ("Tint Color", Color) = (1, 1, 1, 1)
        [Toggle] _FlipBackUV ("Flip Back UV (裏面左右反転補正)", Float) = 1
        [Toggle] _ShowFront ("Show Front (0=全員裏面表示, 1=表面表示)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _BackTex;
            float4 _BackTex_ST;
            fixed4 _Color;
            float _FlipBackUV;
            float _ShowFront;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                // 表裏判定: カメラ正面(facing > 0) かつ 表面表示が許可されている場合のみ表面を描画
                if (facing > 0 && _ShowFront > 0.5)
                {
                    fixed4 col = tex2D(_MainTex, i.uv) * _Color;
                    return col;
                }
                else
                {
                    // 裏面は左右反転（鏡像）を防ぐためにUVのX座標を反転補正して正読化
                    float2 backUV = (_FlipBackUV > 0.5) ? float2(1.0 - i.uv.x, i.uv.y) : i.uv;
                    fixed4 col = tex2D(_BackTex, backUV) * _Color;
                    return col;
                }
            }
            ENDCG
        }
    }
    FallBack "Unlit/Texture"
}
