Shader "Custom/GlassNoiseBackground"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Blur Effect)]
        _BlurRadius ("Blur Radius", Range(0, 50)) = 25
        _BlurIterations ("Blur Iterations", Range(1, 8)) = 4
        _BlurAmount ("Blur Strength", Range(0, 1)) = 1.0

        [Header(Rounded Corners)]
        _CornerRadius ("Corner Radius (UV)", Range(0.01, 0.2)) = 0.07
        _EdgePadding ("Edge Padding (UV)", Range(0, 0.2)) = 0.05

        [Header(Theme Color)]
        _ThemeColor ("Theme Color", Color) = (0.2627, 0.4902, 0.7529, 1)
        _BaseAlpha ("Base Alpha", Range(0, 1)) = 0.15
        _Darkness ("Darkness", Range(0, 1)) = 0.7

        [Header(Center Dark Spread)]
        _CenterDarkness ("Center Darkness", Range(0, 1)) = 0.6
        _CenterSpread ("Center Spread", Range(0.1, 2)) = 0.8
        _CenterPower ("Center Falloff Power", Range(0.5, 4)) = 1.5

        [Header(Noise Variation)]
        _NoiseScale ("Noise Scale", Range(1, 10)) = 3
        _NoiseStrength ("Noise Strength", Range(0, 0.3)) = 0.1

        [Header(Edge Glow)]
        _EdgeGlowWidth ("Edge Glow Width", Range(0, 0.5)) = 0.15
        _EdgeGlowIntensity ("Edge Glow Intensity", Range(0, 0.5)) = 0.2

        [Header(Corner Highlights)]
        _CornerHighlight ("Corner Highlight Intensity", Range(0, 0.5)) = 0.15

        [Header(Hover State)]
        _HoverAmount ("Hover Amount", Range(0, 1)) = 0

        // UI Masking
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        // GrabPass để capture background cho blur effect
        GrabPass
        {
            "_GlassGrabTexture"
        }

        Pass
        {
            Name "GlassNoiseBackground"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 grabPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            // Blur variables
            sampler2D _GlassGrabTexture;
            float4 _GlassGrabTexture_TexelSize;
            float _BlurRadius;
            float _BlurIterations;
            float _BlurAmount;

            float _CornerRadius;
            float _EdgePadding;
            fixed4 _ThemeColor;
            float _BaseAlpha;
            float _Darkness;

            float _CenterDarkness;
            float _CenterSpread;
            float _CenterPower;

            float _NoiseScale;
            float _NoiseStrength;

            float _EdgeGlowWidth;
            float _EdgeGlowIntensity;

            float _CornerHighlight;

            float _HoverAmount;

            float _Aspect;

            // ========== NOISE ==========
            float2 hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            float valueNoise(float2 uv)
            {
                float2 iuv = floor(uv);
                float2 fuv = frac(uv);
                float2 u = fuv * fuv * (3.0 - 2.0 * fuv);

                float a = dot(hash2(iuv), float2(1, 1)) * 0.5;
                float b = dot(hash2(iuv + float2(1, 0)), float2(1, 1)) * 0.5;
                float c = dot(hash2(iuv + float2(0, 1)), float2(1, 1)) * 0.5;
                float dd = dot(hash2(iuv + float2(1, 1)), float2(1, 1)) * 0.5;

                return lerp(lerp(a, b, u.x), lerp(c, dd, u.x), u.y);
            }

            // SDF for rounded box
            float sdRoundedBoxAspect(float2 uv, float aspect, float radius, float padding)
            {
                float2 center = float2(0.5, 0.5);
                float2 pos = (uv - center);
                pos.x *= aspect;

                float2 halfSize = float2(0.5 * aspect - padding * aspect, 0.5 - padding);

                float2 dd = abs(pos) - halfSize + radius;
                return min(max(dd.x, dd.y), 0.0) + length(max(dd, 0.0)) - radius;
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                o.grabPos = ComputeGrabScreenPos(o.vertex);

                return o;
            }

            // ========== EXTREME BLUR FUNCTION ==========
            // Dual Kawase blur - extremely efficient and strong blur
            half4 KawaseBlurSample(float4 grabPos, float2 texelSize, float offset)
            {
                half4 col = half4(0, 0, 0, 0);

                // Sample center
                col += tex2Dproj(_GlassGrabTexture, grabPos);

                // Sample 4 diagonal corners with offset
                col += tex2Dproj(_GlassGrabTexture, grabPos + float4(texelSize.x * offset, texelSize.y * offset, 0, 0));
                col += tex2Dproj(_GlassGrabTexture, grabPos + float4(-texelSize.x * offset, texelSize.y * offset, 0, 0));
                col += tex2Dproj(_GlassGrabTexture, grabPos + float4(texelSize.x * offset, -texelSize.y * offset, 0, 0));
                col += tex2Dproj(_GlassGrabTexture, grabPos + float4(-texelSize.x * offset, -texelSize.y * offset, 0, 0));

                return col / 5.0;
            }

            // Multi-pass extreme blur
            half4 ExtremeBlur(float4 grabPos)
            {
                float2 texelSize = _GlassGrabTexture_TexelSize.xy * _BlurRadius;
                half4 blurColor = half4(0, 0, 0, 0);
                float totalWeight = 0;

                // Gaussian-like weights for smooth blur
                float weights[8] = {1.0, 0.9, 0.8, 0.65, 0.5, 0.35, 0.2, 0.1};

                int iterations = (int)_BlurIterations;

                // Multi-ring sampling for extreme blur
                for (int ring = 0; ring < iterations; ring++)
                {
                    float offset = (ring + 1) * 1.5;
                    float weight = weights[ring];

                    // 8 samples per ring (cardinal + diagonal)
                    // Cardinals
                    blurColor += tex2Dproj(_GlassGrabTexture, grabPos + float4(texelSize.x * offset, 0, 0, 0)) * weight;
                    blurColor += tex2Dproj(_GlassGrabTexture, grabPos + float4(-texelSize.x * offset, 0, 0, 0)) * weight;
                    blurColor += tex2Dproj(_GlassGrabTexture, grabPos + float4(0, texelSize.y * offset, 0, 0)) * weight;
                    blurColor += tex2Dproj(_GlassGrabTexture, grabPos + float4(0, -texelSize.y * offset, 0, 0)) * weight;

                    // Diagonals
                    float diagOffset = offset * 0.707; // sqrt(2)/2
                    blurColor += tex2Dproj(_GlassGrabTexture, grabPos + float4(texelSize.x * diagOffset, texelSize.y * diagOffset, 0, 0)) * weight;
                    blurColor += tex2Dproj(_GlassGrabTexture, grabPos + float4(-texelSize.x * diagOffset, texelSize.y * diagOffset, 0, 0)) * weight;
                    blurColor += tex2Dproj(_GlassGrabTexture, grabPos + float4(texelSize.x * diagOffset, -texelSize.y * diagOffset, 0, 0)) * weight;
                    blurColor += tex2Dproj(_GlassGrabTexture, grabPos + float4(-texelSize.x * diagOffset, -texelSize.y * diagOffset, 0, 0)) * weight;

                    totalWeight += weight * 8;
                }

                // Add center sample
                blurColor += tex2Dproj(_GlassGrabTexture, grabPos) * 1.0;
                totalWeight += 1.0;

                return blurColor / totalWeight;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float aspect = (_Aspect > 0.0) ? _Aspect : 1.0;

                // SDF for shape
                float dist = sdRoundedBoxAspect(uv, aspect, _CornerRadius, _EdgePadding);

                // Alpha mask
                float alphaMask = 1.0 - smoothstep(-0.01, 0.0, dist);
                if (alphaMask <= 0.001) clip(-1);

                // ========== EXTREME BLUR BACKGROUND ==========
                half4 blurredBg = ExtremeBlur(i.grabPos);

                // ========== CENTER DARKNESS ==========
                // Distance from center (accounting for aspect ratio)
                float2 centerOffset = uv - 0.5;
                centerOffset.x *= aspect;
                float centerDist = length(centerOffset) / _CenterSpread;

                // Dark in center, lighter at edges
                float centerDark = 1.0 - pow(saturate(centerDist), _CenterPower);
                centerDark *= _CenterDarkness;

                // ========== NOISE VARIATION ==========
                float2 noiseUV = uv * _NoiseScale;
                noiseUV.x *= aspect * 0.5;
                float noise = valueNoise(noiseUV);
                float noiseVar = (noise - 0.5) * _NoiseStrength;

                // ========== EDGE GLOW ==========
                // Brighter near the edges (inside the shape)
                float edgeDist = saturate(-dist / _EdgeGlowWidth);
                float edgeGlow = edgeDist * _EdgeGlowIntensity;

                // ========== CORNER HIGHLIGHTS ==========
                // Subtle highlights in corners
                float cornerTL = (1.0 - uv.x) * uv.y; // top-left
                float cornerBR = uv.x * (1.0 - uv.y); // bottom-right
                float corners = (pow(cornerTL, 2.0) + pow(cornerBR, 2.0) * 0.5) * _CornerHighlight;

                // ========== HOVER ==========
                float hoverBoost = _HoverAmount * 0.2;

                // ========== COMBINE ==========
                // Start with base darkness
                float brightness = 1.0 - _Darkness;

                // Subtract center darkness (makes center darker)
                brightness -= centerDark;

                // Add subtle noise variation
                brightness += noiseVar;

                // Add edge glow (brightens edges)
                brightness += edgeGlow;

                // Add corner highlights
                brightness += corners;

                // Add hover
                brightness += hoverBoost;

                // Clamp
                brightness = saturate(brightness);

                // Apply to theme color
                float3 themeColor = _ThemeColor.rgb * brightness;

                // ========== BLEND BLUR WITH THEME ==========
                // Mix blurred background với theme color overlay
                // Blur làm nền, theme color phủ lên với độ trong suốt
                float3 finalColor = lerp(blurredBg.rgb, themeColor, _BaseAlpha * _BlurAmount);

                // Thêm một lớp tint màu nhẹ
                finalColor = lerp(finalColor, blurredBg.rgb * _ThemeColor.rgb, 0.3);

                // Alpha - giữ shape rõ ràng
                float alpha = alphaMask;
                alpha *= i.color.a;

                fixed4 result = fixed4(finalColor, alpha);
                result.rgb *= i.color.rgb;

                return result;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
