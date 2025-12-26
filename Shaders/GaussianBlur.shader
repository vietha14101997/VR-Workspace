Shader "Hidden/RTT/GaussianBlur"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BlurRadius ("Blur Radius", Float) = 4.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        // Common settings for all passes
        Cull Off
        ZWrite Off
        ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        half _BlurRadius;

        struct appdata
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct v2f
        {
            float4 vertex : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        v2f vert(appdata v)
        {
            v2f o;
            o.vertex = UnityObjectToClipPos(v.vertex);
            o.uv = v.uv;
            return o;
        }

        // Mobile-compatible 5-tap blur (unrolled, no arrays)
        half4 GaussianBlurHorizontal(float2 uv)
        {
            half2 texelSize = _MainTex_TexelSize.xy * _BlurRadius;

            // Center sample (weight 0.227027)
            half4 color = tex2D(_MainTex, uv) * 0.227027;

            // Offset 1 (weight 0.194594, offset 1.385)
            color += tex2D(_MainTex, uv + float2(texelSize.x * 1.385, 0)) * 0.194594;
            color += tex2D(_MainTex, uv - float2(texelSize.x * 1.385, 0)) * 0.194594;

            // Offset 2 (weight 0.121622, offset 3.231)
            color += tex2D(_MainTex, uv + float2(texelSize.x * 3.231, 0)) * 0.121622;
            color += tex2D(_MainTex, uv - float2(texelSize.x * 3.231, 0)) * 0.121622;

            // Offset 3 (weight 0.054054, offset 5.077)
            color += tex2D(_MainTex, uv + float2(texelSize.x * 5.077, 0)) * 0.054054;
            color += tex2D(_MainTex, uv - float2(texelSize.x * 5.077, 0)) * 0.054054;

            // Offset 4 (weight 0.016216, offset 6.923)
            color += tex2D(_MainTex, uv + float2(texelSize.x * 6.923, 0)) * 0.016216;
            color += tex2D(_MainTex, uv - float2(texelSize.x * 6.923, 0)) * 0.016216;

            return color;
        }

        half4 GaussianBlurVertical(float2 uv)
        {
            half2 texelSize = _MainTex_TexelSize.xy * _BlurRadius;

            // Center sample (weight 0.227027)
            half4 color = tex2D(_MainTex, uv) * 0.227027;

            // Offset 1 (weight 0.194594, offset 1.385)
            color += tex2D(_MainTex, uv + float2(0, texelSize.y * 1.385)) * 0.194594;
            color += tex2D(_MainTex, uv - float2(0, texelSize.y * 1.385)) * 0.194594;

            // Offset 2 (weight 0.121622, offset 3.231)
            color += tex2D(_MainTex, uv + float2(0, texelSize.y * 3.231)) * 0.121622;
            color += tex2D(_MainTex, uv - float2(0, texelSize.y * 3.231)) * 0.121622;

            // Offset 3 (weight 0.054054, offset 5.077)
            color += tex2D(_MainTex, uv + float2(0, texelSize.y * 5.077)) * 0.054054;
            color += tex2D(_MainTex, uv - float2(0, texelSize.y * 5.077)) * 0.054054;

            // Offset 4 (weight 0.016216, offset 6.923)
            color += tex2D(_MainTex, uv + float2(0, texelSize.y * 6.923)) * 0.016216;
            color += tex2D(_MainTex, uv - float2(0, texelSize.y * 6.923)) * 0.016216;

            return color;
        }
        ENDCG

        // Pass 0: Horizontal blur
        Pass
        {
            Name "BLUR_HORIZONTAL"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            half4 frag(v2f i) : SV_Target
            {
                return GaussianBlurHorizontal(i.uv);
            }
            ENDCG
        }

        // Pass 1: Vertical blur
        Pass
        {
            Name "BLUR_VERTICAL"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            half4 frag(v2f i) : SV_Target
            {
                return GaussianBlurVertical(i.uv);
            }
            ENDCG
        }

        // Pass 2: Simple downsample (4-tap box filter)
        Pass
        {
            Name "DOWNSAMPLE"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            half4 frag(v2f i) : SV_Target
            {
                half2 texelSize = _MainTex_TexelSize.xy * 0.5;

                half4 color = tex2D(_MainTex, i.uv + float2(-texelSize.x, -texelSize.y));
                color += tex2D(_MainTex, i.uv + float2(texelSize.x, -texelSize.y));
                color += tex2D(_MainTex, i.uv + float2(-texelSize.x, texelSize.y));
                color += tex2D(_MainTex, i.uv + float2(texelSize.x, texelSize.y));

                return color * 0.25;
            }
            ENDCG
        }
    }

    FallBack Off
}
