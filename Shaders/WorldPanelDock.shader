Shader "Unlit/WorldPanelDock"
{
    Properties
    {
        _FillColor    ("Fill Color", Color) = (0.2,0.2,0.2,0.25)
        _BorderColor  ("Border Color", Color) = (1,1,1,0.95)
        _Radius       ("Corner Radius (m)", Float) = 0.04   // Bo tròn 2 bên, đơn vị thực
        _Border       ("Border (m)", Float) = 0.050
        _Feather      ("Feather", Float) = 0.006
        _BorderFade   ("Border Fade", Float) = 0.4
        [Toggle] _DebugPill ("Debug Pill Shape", Float) = 0  // Để debug hình dạng
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

            float4 _FillColor, _BorderColor;
            float  _Radius, _Border, _Feather, _BorderFade;
            float _DebugPill;

            v2f vert (appdata v)
            { 
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Signed distance to rounded rect (bo tròn cả 4 góc)
            float sdRoundedRect(float2 p, float2 b, float r)
            {
                p = abs(p);
                float2 q = p - b + float2(r, r);
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
            }

            // Signed distance to pill-shaped rect (chỉ bo tròn 2 bên)
            float sdPillRect(float2 p, float2 b, float r)
            {
                p = abs(p);
                // Xử lý riêng cho phần góc
                if (p.x > b.x - r)
                {
                    return length(float2(p.x - (b.x - r), p.y)) - r;
                }
                // Phần thẳng
                return max(p.x - (b.x - r), abs(p.y) - b.y);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * 2.0;            // -1..1
                float2 halfSize = float2(1.0, 1.0);
                
                // Kiểm tra tỷ lệ khung hình để quyết định dùng hình tròn hay pill
                float aspectRatio = length(halfSize.x / halfSize.y);
                bool useCircle = aspectRatio > 0.95 && aspectRatio < 1.05; // Gần vuông thì dùng hình tròn
                
                float dOuter, dInner;
                
                if (useCircle)
                {
                    // Sử dụng rounded rect với 4 góc bo tròn khi thu nhỏ (gần vuông)
                    dOuter = sdRoundedRect(p, halfSize, _Radius*2.0);
                    dInner = sdRoundedRect(p, halfSize - _Border*2.0, max(_Radius*2.0 - _Border*2.0, 0));
                }
                else
                {
                    // Sử dụng pill rect với 2 bên bo tròn khi mở rộng (hình chữ nhật)
                    dOuter = sdPillRect(p, halfSize, _Radius*2.0);
                    dInner = sdPillRect(p, halfSize - _Border*2.0, max(_Radius*2.0 - _Border*2.0, 0));
                }

                // Alpha cho phần fill và border
                float alphaFill = saturate(1.0 - smoothstep(0.0, _Feather, dInner));
                float alphaBorder = saturate(1.0 - smoothstep(0.0, _Feather, dOuter)) - alphaFill;
                
                // Thêm gradient alpha cho border (đậm ở giữa, nhạt dần ra)
                float borderGradient = 1.0 - smoothstep(0.0, _BorderFade, abs(p.x));
                alphaBorder *= lerp(0.5, 1.0, borderGradient);  // Giữ ít nhất 50% alpha

                float4 col = _FillColor * alphaFill + _BorderColor * alphaBorder;
                col.a = saturate(col.a);
                
                // Debug mode để kiểm tra hình dạng
                if (_DebugPill > 0.5)
                {
                    float debug = 1 - saturate(-dOuter/_Feather);
                    return float4(debug.xxx, 1);
                }
                
                return col;
            }
            ENDCG
        }
    }
}
