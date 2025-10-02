Shader "Unlit/WorldPanelRounded"
{
    Properties
    {
        _FillColor    ("Fill Color", Color) = (0.2,0.2,0.2,0.25)
        _BorderColor  ("Border Color", Color) = (1,1,1,0.85) // Trắng hơn một chút
        _Radius       ("Corner Radius (m)", Float) = 0.06
        _Border       ("Border (m)", Float) = 0.009         // Viền dày gấp 1.5 lần
        _Feather      ("Feather", Float) = 0.005          // Độ mờ viền tăng theo
        _BorderFade   ("Border Fade", Float) = 0.6        // Độ mờ từ tâm ra

        // --- New: gaze mask ---
        _MaskEnable   ("Mask Enable (0/1)", Float) = 1
        _MaskCenter   ("Mask Center (UV)", Vector) = (0.5, 0.5, 0, 0)
        _MaskSize     ("Mask Half-Size (UV)", Vector) = (0.125, 0.08, 0, 0) // ≈ 1/8 theo trục U
        _MaskFeather  ("Mask Feather (UV)", Float) = 0.12
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

            // mask
            float  _MaskEnable;
            float4 _MaskCenter;     // xy
            float4 _MaskSize;       // xy
            float  _MaskFeather;

            v2f vert (appdata v){ v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.uv; return o; }

            // signed distance to rounded rect in NDC quad space (-1..1)
            float sdRoundRect(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q,0.0)) + min(max(q.x,q.y),0.0) - r;
            }

            // rectangular (elliptical) smooth mask around UV center
            float maskRect(float2 uv, float2 center, float2 halfSize, float feather)
            {
                float2 d = abs(uv - center);
                // normalized distance: 0 at center, 1 at edge of halfSize
                float m = max(d.x / max(halfSize.x, 1e-5), d.y / max(halfSize.y, 1e-5));
                // 1 inside, 0 outside with feather soft edge
                return saturate(1.0 - smoothstep(1.0, 1.0 + max(feather, 1e-5), m));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // base rounded box (same như trước)
                float2 p = (i.uv - 0.5) * 2.0;            // -1..1
                float2 halfSize = float2(1.0, 1.0);
                float dOuter = sdRoundRect(p, halfSize, _Radius*2.0);
                float dInner = sdRoundRect(p, halfSize - _Border*2.0, max(_Radius*2.0 - _Border*2.0, 0));

                float alphaFill = saturate(1.0 - smoothstep(0.0, _Feather, dInner));
                float alphaBorder = saturate(1.0 - smoothstep(0.0, _Feather, dOuter)) - alphaFill;
                
                // Thêm gradient alpha cho border (đậm ở giữa, nhạt dần ra)
                float2 center = abs(p);
                float dist = length(center);
                float borderGradient = 1.0 - smoothstep(0.0, _BorderFade, dist);
                alphaBorder *= lerp(0.6, 1.0, borderGradient);  // Giữ ít nhất 60% alpha

                float4 col = _FillColor * alphaFill + _BorderColor * alphaBorder;
                col.a = saturate(col.a);

                // apply gaze mask (multiply alpha)
                float m = (_MaskEnable <= 0.0) ? 1.0 :
                          maskRect(i.uv, _MaskCenter.xy, _MaskSize.xy, _MaskFeather);
                col.rgb *= m;
                col.a   *= m;

                return col;
            }
            ENDCG
        }
    }
}
