Shader "Custom/RoundedRawImage"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _CornerRadius ("Corner Radius (UV)", Range(0.01, 0.3)) = 0.12
        _EdgePadding ("Edge Padding (UV)", Range(0, 0.1)) = 0
        _Aspect ("Aspect Ratio (W/H)", Float) = 1.78
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

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
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _CornerRadius;
            float _EdgePadding;
            float _Aspect;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            // SDF for rounded box with aspect ratio correction
            float sdRoundedBox(float2 uv, float aspect, float radius, float padding)
            {
                float2 center = float2(0.5, 0.5);
                float2 pos = (uv - center);
                pos.x *= aspect;

                float2 halfSize = float2(0.5 * aspect - padding * aspect, 0.5 - padding);

                float2 d = abs(pos) - halfSize + radius;
                return min(max(d.x, d.y), 0.0) + length(max(d, 0.0)) - radius;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample texture
                fixed4 col = tex2D(_MainTex, i.uv) * i.color;

                // Calculate rounded rect mask
                float aspect = (_Aspect > 0.0) ? _Aspect : 1.0;
                float dist = sdRoundedBox(i.uv, aspect, _CornerRadius, _EdgePadding);

                // Smooth edge
                float alphaMask = 1.0 - smoothstep(-0.005, 0.005, dist);

                col.a *= alphaMask;

                return col;
            }
            ENDCG
        }
    }
}
