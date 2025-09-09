Shader "Unlit/WorldPanelDock"
{
    Properties
    {
        _FillColor   ("Fill Color", Color) = (0,0,0,0.22)
        _BorderColor ("Border Color", Color) = (1,1,1,0.35)
        _RectWH      ("Rect Size (W,H)", Vector) = (1,1,0,0)
        _Radius      ("Corner Radius (units)", Float) = 0.06
        _Border      ("Border Thickness (units)", Float) = 0.004
        _Feather     ("Edge Feather (units)", Float) = 0.006
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 100
        Cull Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        // ---- PREPASS: ghi depth để che hoàn toàn lớp dưới ----
        Pass
        {
            ZWrite On
            ColorMask 0
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            v2f vert(appdata v){ v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            fixed4 frag(v2f i):SV_Target { return 0; }
            ENDCG
        }

        // ---- MAIN PASS: vẽ fill + border bằng SDF ----
        Pass
        {
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f     { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            float4 _FillColor, _BorderColor;
            float4 _RectWH;
            float  _Radius, _Border, _Feather;

            v2f vert (appdata v){ v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            float sdRoundRect(float2 p, float2 a, float r){
                float2 q = abs(p) - a + r;
                return length(max(q,0)) + min(max(q.x,q.y),0) - r;
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 wh = _RectWH.xy;
                float2 p  = (i.uv - 0.5) * wh;

                float2 aOuter = 0.5*wh;
                float  rOuter = min(_Radius, min(aOuter.x,aOuter.y));
                float  feather = max(_Feather, 1e-5);

                float dOuter = sdRoundRect(p, aOuter, rOuter);
                float2 aInner = max(aOuter - _Border, 0);
                float  rInner = max(rOuter - _Border, 0);
                float dInner = sdRoundRect(p, aInner, rInner);

                float outerMask = 1 - smoothstep(0, feather, dOuter);
                float innerMask = 1 - smoothstep(0, feather, -dInner);
                float borderMask = outerMask * (1 - innerMask);
                float fillMask   = innerMask;

                float4 col = _FillColor * fillMask + _BorderColor * borderMask;
                col.a = saturate(col.a);
                return col;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
