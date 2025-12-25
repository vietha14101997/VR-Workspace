Shader "Custom/GlowingWideElementBorder"
{
    // Specialized shader for wide elements (like Space key) with correct aspect ratio handling
    // The key difference from GlowingElementBorder is that padding is applied uniformly
    // relative to the height dimension, not scaled by aspect ratio.

    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Border Settings)]
        _EdgePadding ("Edge Padding (UV)", Range(0, 0.2)) = 0.05
        _BorderWidth ("Border Width (UV)", Range(0.01, 0.1)) = 0.045
        _CornerRadius ("Corner Radius (UV)", Range(0.02, 0.3)) = 0.15
        _Aspect ("Aspect Ratio", Float) = 1.0

        [Header(Glow Settings)]
        _GlowColor ("Glow Color", Color) = (0.3, 1, 1, 1)
        _GlowIntensity ("Glow Intensity", Range(0.5, 3)) = 1.5
        _GlowWidth ("Glow Width (UV)", Range(0.02, 0.2)) = 0.1

        [Header(Background)]
        _BackgroundAlpha ("Background Alpha", Range(0, 0.3)) = 0.08
        _BackgroundColor ("Background Color", Color) = (0, 0.3, 0.5, 1)

        [Header(Pulse Animation)]
        _PulseEnabled ("Pulse Enabled", Float) = 1
        _PulseSpeed ("Pulse Speed", Range(0.5, 4)) = 2
        _PulseIntensity ("Pulse Intensity", Range(0, 0.3)) = 0.1

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
            Name "GlowingWideElementBorder"
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

            float _EdgePadding;
            float _BorderWidth;
            float _CornerRadius;
            float _Aspect;

            fixed4 _GlowColor;
            float _GlowIntensity;
            float _GlowWidth;

            float _BackgroundAlpha;
            fixed4 _BackgroundColor;

            float _PulseEnabled;
            float _PulseSpeed;
            float _PulseIntensity;

            float _HoverAmount;

            // SDF for rounded box - CORRECTED for wide elements
            // Padding is applied uniformly relative to height (the shorter dimension)
            // This ensures corners look circular regardless of aspect ratio
            float sdRoundedBoxWide(float2 uv, float aspect, float radius, float padding)
            {
                float2 center = float2(0.5, 0.5);
                float2 pos = (uv - center);

                // Scale to square space for correct corner calculation
                pos.x *= aspect;

                // CRITICAL FIX: Padding is uniform in visual space
                // In the scaled space, X padding should be same as Y padding (not scaled by aspect)
                // This means corners will be circular and padding will look even on all sides
                float paddingX = padding;  // Same absolute visual padding
                float paddingY = padding;

                // Half size of the box in scaled space
                // X range goes from -0.5*aspect to +0.5*aspect, so half = 0.5*aspect
                // Y range goes from -0.5 to +0.5, so half = 0.5
                float2 halfSize = float2(0.5 * aspect - paddingX, 0.5 - paddingY);

                // Corner radius scaled to match the Y dimension (height)
                // This ensures circular corners
                float r = radius;

                float2 d = abs(pos) - halfSize + r;
                return min(max(d.x, d.y), 0.0) + length(max(d, 0.0)) - r;
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

                // Aspect Ratio
                float aspect = (_Aspect > 0.0) ? _Aspect : 1.0;

                // SDF with corrected wide element handling
                float dist = sdRoundedBoxWide(uv, aspect, _CornerRadius, _EdgePadding);

                // === PULSE ===
                float pulse = 1.0;
                if (_PulseEnabled > 0.5)
                {
                    float wave = sin(_Time.y * _PulseSpeed) * 0.5 + 0.5;
                    pulse = 1.0 + wave * _PulseIntensity * (1.0 + _HoverAmount);
                }

                // === HOVER ===
                float hoverIntensityBoost = 1.0 + _HoverAmount * 0.6;
                float hoverGlowBoost = 1.0 + _HoverAmount * 0.3;
                float intensity = _GlowIntensity * hoverIntensityBoost * pulse;

                // === BACKGROUND ===
                float insideMask = saturate(-dist / 0.01);
                fixed4 bgColor = _BackgroundColor;
                bgColor.a = _BackgroundAlpha * insideMask;

                // === GLOW LAYERS ===
                float distAbs = abs(dist);
                float effectiveGlowWidth = _GlowWidth * hoverGlowBoost;

                float outerGlow = 1.0 - saturate(distAbs / effectiveGlowWidth);
                outerGlow = pow(outerGlow, 2.0);

                float midGlow = 1.0 - saturate(distAbs / (effectiveGlowWidth * 0.5));
                midGlow = pow(midGlow, 1.5);

                float borderMask = 1.0 - saturate(distAbs / _BorderWidth);
                borderMask = pow(borderMask, 0.8);

                // === COMBINE ===
                fixed4 finalColor = bgColor;

                // Outer glow
                fixed4 outer = _GlowColor;
                outer.a = outerGlow * intensity * 0.4;
                finalColor.rgb = lerp(finalColor.rgb, outer.rgb, saturate(outer.a));
                finalColor.a = max(finalColor.a, saturate(outer.a));

                // Mid glow
                fixed4 mid = _GlowColor;
                mid.rgb *= 1.15;
                mid.a = midGlow * intensity * 0.6;
                finalColor.rgb = lerp(finalColor.rgb, mid.rgb, saturate(mid.a));
                finalColor.a = max(finalColor.a, saturate(mid.a));

                // Border
                fixed4 border = _GlowColor;
                border.rgb *= 1.25;
                border.rgb += fixed3(0.08, 0.08, 0.08);
                border.a = borderMask * 0.9;
                finalColor.rgb = lerp(finalColor.rgb, border.rgb, saturate(border.a));
                finalColor.a = max(finalColor.a, saturate(border.a));

                finalColor *= i.color;

                return finalColor;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
