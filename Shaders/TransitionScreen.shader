Shader "Custom/TransitionScreen"
{
    Properties
    {
        _Color ("Color", Color) = (0,0,0,1)
        _Progress ("Progress", Range(0, 1)) = 0
        _SmoothWidth ("Smooth Width", Range(0, 1)) = 0.1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+1000" }
        LOD 100

        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            float4 _Color;
            float _Progress;
            float _SmoothWidth;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float threshold = _Progress * (1 + _SmoothWidth) - _SmoothWidth * i.uv.y;
                float alpha = smoothstep(threshold - _SmoothWidth, threshold, i.uv.y);
                return float4(_Color.rgb, _Color.a * alpha);
            }
            ENDCG
        }
    }
}
