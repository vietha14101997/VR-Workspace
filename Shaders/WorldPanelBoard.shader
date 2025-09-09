Shader "Unlit/WorldPanelBoard"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color   ("Tint", Color) = (1,1,1,1)

        // Viền mờ độc lập theo trục:
        _EdgeFadeX ("Edge Fade X (0..0.25)", Range(0,0.25)) = 0.06  // ngang (trái/phải)
        _EdgeFadeY ("Edge Fade Y (0..0.25)", Range(0,0.25)) = 0.03  // dọc (trên/dưới)
        _EdgeMinAlpha ("Edge Min Alpha (0..1)", Range(0,1)) = 0.4

        _PanelSize ("Panel Size (W,H)", Vector) = (1,1,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            fixed4 _Color;

            float _EdgeFadeX;
            float _EdgeFadeY;
            float _EdgeMinAlpha;
            float4 _PanelSize; // (W,H,0,0) — giữ tương thích

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f     { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            v2f vert (appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;

                // khoảng cách tới mép dọc/ngang
                float dx = min(i.uv.x, 1.0 - i.uv.x);
                float dy = min(i.uv.y, 1.0 - i.uv.y);

                // tránh chia 0
                float ex = max(_EdgeFadeX, 1e-6);
                float ey = max(_EdgeFadeY, 1e-6);

                // Chuẩn hoá khoảng cách theo mỗi trục -> lấy trục "gần mép hơn"
                float nx = dx / ex;   // 0 ở sát mép X, 1 ở trong rìa mờ X
                float ny = dy / ey;   // 0 ở sát mép Y, 1 ở trong rìa mờ Y
                float t  = saturate(min(nx, ny));
                t = smoothstep(0.0, 1.0, t);

                // Alpha: mép ngoài -> _EdgeMinAlpha, vào trong -> 1
                float alphaMul = lerp(_EdgeMinAlpha, 1.0, t);
                col.a *= alphaMul;

                return col;
            }
            ENDCG
        }
    }
    FallBack Off
}
