Shader "Unlit/WorldPanelBoard"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Texture", 2D) = "white" {}
        _Color       ("Tint", Color) = (1,1,1,1)
        _EdgeFade    ("Edge Fade", Range(0.01, 0.1)) = 0.03  // 3% rìa làm mờ
        _FadeAmount  ("Fade Amount", Range(0, 1)) = 0.4      // Giảm xuống 40% alpha
        _PanelSize   ("Panel Size", Vector) = (1.2,0.72,0,0) // Width, Height của panel
        [Toggle] _DebugFade ("Debug Fade", Float) = 0        // Để debug trên mobile
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

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _EdgeFade;
            float _FadeAmount;
            float4 _PanelSize;
            float _DebugFade;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample texture
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;
                
                // Đơn giản hóa: Tính khoảng cách UV đến rìa gần nhất
                float2 uvDist = min(i.uv, 1 - i.uv);  // Khoảng cách đến cả 4 rìa
                float minDist = min(uvDist.x, uvDist.y);  // Lấy khoảng cách nhỏ nhất
                
                // Vùng fade dựa trên _EdgeFade
                float fadeAmount = saturate(minDist / _EdgeFade);
                
                // Lerp từ _FadeAmount (25%) đến 1.0
                fadeAmount = lerp(_FadeAmount, 1.0, fadeAmount);
                
                // Debug mode để kiểm tra trên mobile
                if (_DebugFade > 0.5)
                {
                    return float4(fadeAmount.xxx, 1.0);
                }
                
                // Áp dụng fade cho cả màu và alpha
                col.rgb *= fadeAmount;
                col.a *= fadeAmount;
                
                return col;
            }
            ENDCG
        }
    }
}
