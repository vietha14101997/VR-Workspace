Shader "VRWorkspace/UI/StereoUIPanel"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _StereoOffset ("Stereo Depth Offset", Range(0, 0.05)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _StereoOffset;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // Stereo parallax offset in view space:
                // Shifts vertices horizontally per eye to reduce binocular disparity.
                // This makes the panel appear at a greater virtual depth than its
                // physical position, matching the stereo video depth and reducing
                // vergence-accommodation conflict.
                float4 viewPos = mul(UNITY_MATRIX_MV, v.vertex);
                float eyeSign = (unity_StereoEyeIndex == 0) ? -1.0 : 1.0;
                viewPos.x += eyeSign * _StereoOffset;
                o.pos = mul(UNITY_MATRIX_P, viewPos);

                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * i.color;
            }
            ENDCG
        }
    }

    FallBack "Sprites/Default"
}
