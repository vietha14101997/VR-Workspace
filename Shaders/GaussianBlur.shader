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

        // 9-tap Gaussian weights (sigma ~= 2.5)
        static const half weights[5] = {
            0.227027, // center
            0.194594, // offset 1
            0.121622, // offset 2
            0.054054, // offset 3
            0.016216  // offset 4
        };

        // Optimized Gaussian blur using linear sampling
        // Samples at offset positions to get 2 texels with 1 sample
        static const half offsets[5] = {
            0.0,
            1.3846153846,
            3.2307692308,
            5.0769230769,
            6.9230769231
        };

        half4 GaussianBlurHorizontal(float2 uv)
        {
            half2 texelSize = _MainTex_TexelSize.xy * _BlurRadius;
            half4 color = tex2D(_MainTex, uv) * weights[0];

            // Horizontal samples
            [unroll]
            for (int i = 1; i < 5; i++)
            {
                float2 offset = float2(texelSize.x * offsets[i], 0);
                color += tex2D(_MainTex, uv + offset) * weights[i];
                color += tex2D(_MainTex, uv - offset) * weights[i];
            }

            return color;
        }

        half4 GaussianBlurVertical(float2 uv)
        {
            half2 texelSize = _MainTex_TexelSize.xy * _BlurRadius;
            half4 color = tex2D(_MainTex, uv) * weights[0];

            // Vertical samples
            [unroll]
            for (int i = 1; i < 5; i++)
            {
                float2 offset = float2(0, texelSize.y * offsets[i]);
                color += tex2D(_MainTex, uv + offset) * weights[i];
                color += tex2D(_MainTex, uv - offset) * weights[i];
            }

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
            #pragma target 3.0

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
            #pragma target 3.0

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
            #pragma target 3.0

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
