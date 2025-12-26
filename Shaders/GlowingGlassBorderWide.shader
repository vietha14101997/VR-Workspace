Shader "Custom/GlowingGlassBorderWide"
{
    // Combines the multi-layer glow effect of GlowingGlassBorder
    // with correct aspect ratio handling for wide elements (like Space key)

    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Border Settings)]
        _EdgePadding ("Edge Padding (UV)", Range(0, 0.2)) = 0.05
        _BorderWidth ("Border Width (UV)", Range(0.005, 0.08)) = 0.025
        _CornerRadius ("Corner Radius (UV)", Range(0.01, 0.25)) = 0.07
        _Aspect ("Aspect Ratio", Float) = 1.0

        [Header(Multi Layer Glow)]
        _Layer1Width ("Layer 1 (Inner)", Range(0.005, 0.05)) = 0.015
        _Layer1Alpha ("Layer 1 Alpha", Range(0, 2)) = 1.2
        _Layer2Width ("Layer 2 (Mid)", Range(0.01, 0.1)) = 0.04
        _Layer2Alpha ("Layer 2 Alpha", Range(0, 2)) = 0.8
        _Layer3Width ("Layer 3 (Outer)", Range(0.02, 0.15)) = 0.08
        _Layer3Alpha ("Layer 3 Alpha", Range(0, 1)) = 0.4
        _Layer4Width ("Layer 4 (Ambient)", Range(0.05, 0.25)) = 0.15
        _Layer4Alpha ("Layer 4 Alpha", Range(0, 1)) = 0.2

        [Header(Gradient Colors)]
        _ColorA ("Color A (Cyan)", Color) = (0.3, 1, 1, 1)
        _ColorB ("Color B (Purple)", Color) = (1, 0.4, 1, 1)
        _GradientAngle ("Gradient Angle", Range(-180, 180)) = 45
        _CyanRatio ("Cyan Ratio", Range(0.1, 0.9)) = 0.6

        [Header(Simple Glow Fallback)]
        _GlowColor ("Glow Color", Color) = (0.3, 1, 1, 1)
        _GlowIntensity ("Glow Intensity", Range(0.5, 3)) = 1.5
        _GlowWidth ("Glow Width (UV)", Range(0.02, 0.2)) = 0.1

        [Header(Glass Background)]
        _GlassAlpha ("Glass Alpha", Range(0, 0.2)) = 0.02
        _GlassTint ("Glass Tint", Color) = (0.9, 0.95, 1, 1)

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
            Name "GlowingGlassBorderWide"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

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

            float _Layer1Width;
            float _Layer1Alpha;
            float _Layer2Width;
            float _Layer2Alpha;
            float _Layer3Width;
            float _Layer3Alpha;
            float _Layer4Width;
            float _Layer4Alpha;

            fixed4 _ColorA;
            fixed4 _ColorB;
            float _GradientAngle;
            float _CyanRatio;

            fixed4 _GlowColor;
            float _GlowIntensity;
            float _GlowWidth;

            float _GlassAlpha;
            fixed4 _GlassTint;

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
                float2 halfSize = float2(0.5 * aspect - paddingX, 0.5 - paddingY);

                // Corner radius scaled to match the Y dimension (height)
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

                // Aspect Ratio Logic
                float aspect = (_Aspect > 0.0) ? _Aspect : 1.0;

                // SDF with wide element handling (uniform padding)
                float dist = sdRoundedBoxWide(uv, aspect, _CornerRadius, _EdgePadding);

                // === GRADIENT ===
                float angleRad = _GradientAngle * 3.14159 / 180.0;
                float2 centeredUV = uv - 0.5;
                float2 rotatedUV;
                rotatedUV.x = centeredUV.x * cos(angleRad) - centeredUV.y * sin(angleRad);
                rotatedUV.y = centeredUV.x * sin(angleRad) + centeredUV.y * cos(angleRad);

                float t = saturate((rotatedUV.x + 0.5));
                t = pow(t, 1.0 / _CyanRatio);

                fixed4 borderColor = lerp(_ColorA, _ColorB, t);

                // === GLASS BACKGROUND ===
                float insideMask = saturate(-dist / 0.005);
                fixed4 glassColor = _GlassTint;
                glassColor.a = _GlassAlpha * insideMask;

                // === MULTI-LAYER BORDER ===
                float absDist = abs(dist);

                // Layer 4 (Ambient Outer)
                float layer4 = 1.0 - saturate(absDist / _Layer4Width);
                layer4 = pow(layer4, 2.0);

                // Layer 3 (Outer Glow)
                float layer3 = 1.0 - saturate(absDist / _Layer3Width);
                layer3 = pow(layer3, 2.0);

                // Layer 2 (Mid Glow)
                float layer2 = 1.0 - saturate(absDist / _Layer2Width);
                layer2 = pow(layer2, 1.5);

                // Layer 1 (Core Line)
                float layer1 = 1.0 - saturate(absDist / _Layer1Width);
                layer1 = pow(layer1, 0.5);

                // === COMPOSITE ===
                fixed4 finalColor = glassColor;

                fixed3 glowColor = borderColor.rgb;

                // Base ambient
                finalColor.rgb += glowColor * layer4 * _Layer4Alpha * 0.5;
                finalColor.a = max(finalColor.a, layer4 * _Layer4Alpha * 0.3);

                // Main Highlight
                finalColor.rgb += glowColor * layer3 * _Layer3Alpha;
                finalColor.a = max(finalColor.a, layer3 * _Layer3Alpha * 0.5);

                // Core Definition
                finalColor.rgb = lerp(finalColor.rgb, glowColor * 1.2, layer2 * _Layer2Alpha);
                finalColor.a = max(finalColor.a, layer2 * _Layer2Alpha);

                // Bright Core
                fixed3 whiteCore = fixed3(1,1,1);
                float coreMix = layer1 * _Layer1Alpha * 0.5;
                finalColor.rgb = lerp(finalColor.rgb, whiteCore, coreMix);
                finalColor.a = max(finalColor.a, layer1 * _Layer1Alpha);

                finalColor *= i.color;

                return finalColor;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
