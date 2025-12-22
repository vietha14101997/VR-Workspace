Shader "Custom/GlassNoiseBackground"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

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

                return o;
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
                float3 finalColor = _ThemeColor.rgb * brightness;

                // Alpha
                float alpha = _BaseAlpha;
                alpha += _HoverAmount * 0.05;
                alpha *= alphaMask;

                fixed4 result = fixed4(finalColor, alpha);
                result *= i.color;

                return result;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
