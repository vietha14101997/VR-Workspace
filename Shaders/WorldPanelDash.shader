Shader "Unlit/WorldPanelDash"
{
    Properties
    {
        _Color     ("Color", Color) = (1,1,1,1)
        _DashSize  ("Dash Size", Float) = 0.1
        _GapSize   ("Gap Size", Float) = 0.1
        _Thickness ("Thickness", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            float4 _Color;
            float  _DashSize, _GapSize, _Thickness;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;           // 0..1
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // dashed pattern along U
                float period = max(1e-4, _DashSize + _GapSize);
                float m = frac(i.uv.x / period);
                float inDash = step(m, _DashSize / period);

                // line thickness around V = 0.5
                float v = abs(i.uv.y - 0.5) * 2.0;
                float lineMask = saturate(1.0 - smoothstep(_Thickness, _Thickness + 0.01, v));

                float alpha = inDash * lineMask;
                return float4(_Color.rgb, _Color.a * alpha);
            }
            ENDCG
        }
    }
}
