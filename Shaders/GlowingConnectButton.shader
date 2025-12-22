Shader "Custom/GlowingConnectButton"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Border Settings)]
        _EdgePadding ("Edge Padding (UV)", Range(0, 0.2)) = 0.02
        _BorderWidth ("Border Width (UV)", Range(0.005, 0.08)) = 0.02
        _CornerRadius ("Corner Radius (UV)", Range(0.01, 0.4)) = 0.15

        [Header(Glow Layers)]
        _Layer1Width ("Layer 1 (Inner)", Range(0.005, 0.05)) = 0.015
        _Layer1Alpha ("Layer 1 Alpha", Range(0, 2)) = 1.8
        _Layer2Width ("Layer 2 (Mid)", Range(0.01, 0.1)) = 0.045
        _Layer2Alpha ("Layer 2 Alpha", Range(0, 2)) = 1.2
        _Layer3Width ("Layer 3 (Outer)", Range(0.02, 0.2)) = 0.09
        _Layer3Alpha ("Layer 3 Alpha", Range(0, 1.5)) = 0.8
        _Layer4Width ("Layer 4 (Ambient)", Range(0.05, 0.4)) = 0.16
        _Layer4Alpha ("Layer 4 Alpha", Range(0, 1)) = 0.4

        [Header(Gradient Colors)]
        _ColorA ("Color A (Cyan)", Color) = (0.2, 0.9, 1, 1)
        _ColorB ("Color B (Deep Sea Blue)", Color) = (0.1, 0.4, 0.8, 1)
        _ColorC ("Color C (Purple)", Color) = (0.7, 0.3, 1, 1)
        _GradientAngle ("Gradient Angle", Range(-180, 180)) = 0
        _MidPoint1 ("Mid Point 1", Range(0.1, 0.5)) = 0.35
        _MidPoint2 ("Mid Point 2", Range(0.5, 0.9)) = 0.70

        [Header(Background)]
        _BgAlpha ("Background Alpha", Range(0, 1)) = 0.5
        _BgGradientStrength ("BG Gradient Strength", Range(0, 1)) = 1.0
        _BgInnerMargin ("BG Inner Margin", Range(0, 0.1)) = 0.025

        [Header(Pulse Animation)]
        _PulseEnabled ("Enable Pulse", Range(0, 1)) = 1
        _PulseSpeed ("Pulse Speed", Range(0.5, 5)) = 2.0
        _PulseIntensity ("Pulse Intensity", Range(0, 0.5)) = 0.2

        [Header(Shimmer Effect)]
        _ShimmerEnabled ("Enable Shimmer", Range(0, 1)) = 1
        _ShimmerSpeed ("Shimmer Speed", Range(0.1, 2)) = 0.5
        _ShimmerWidth ("Shimmer Width", Range(0.05, 0.3)) = 0.15
        _ShimmerIntensity ("Shimmer Intensity", Range(0, 2)) = 1.0

        [Header(Inner Glow)]
        _InnerGlowEnabled ("Enable Inner Glow", Range(0, 1)) = 1
        _InnerGlowWidth ("Inner Glow Width", Range(0.01, 0.2)) = 0.08
        _InnerGlowAlpha ("Inner Glow Alpha", Range(0, 1)) = 0.3

        [Header(Hover State)]
        _HoverAmount ("Hover Amount", Range(0, 1)) = 0

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
            Name "GlowingConnectButton"
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
            fixed4 _ColorC;
            float _GradientAngle;
            float _MidPoint1;
            float _MidPoint2;

            float _BgAlpha;
            float _BgGradientStrength;
            float _BgInnerMargin;

            float _PulseEnabled;
            float _PulseSpeed;
            float _PulseIntensity;

            float _ShimmerEnabled;
            float _ShimmerSpeed;
            float _ShimmerWidth;
            float _ShimmerIntensity;

            float _InnerGlowEnabled;
            float _InnerGlowWidth;
            float _InnerGlowAlpha;

            float _HoverAmount;
            float _Aspect;

            float sdRoundedBoxAspect(float2 uv, float aspect, float radius, float padding)
            {
                float2 center = float2(0.5, 0.5);
                float2 pos = (uv - center);
                pos.x *= aspect;
                float2 halfSize = float2(0.5 * aspect - padding * aspect, 0.5 - padding);
                float2 d = abs(pos) - halfSize + radius;
                return min(max(d.x, d.y), 0.0) + length(max(d, 0.0)) - radius;
            }

            float getPerimeterPosition(float2 uv, float aspect)
            {
                float2 center = float2(0.5, 0.5);
                float2 pos = uv - center;
                float angle = atan2(pos.y, pos.x * (1.0 / aspect));
                float t = (angle + 3.14159) / (2.0 * 3.14159);
                return t;
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
                float time = _Time.y;
                float aspect = (_Aspect > 0.0) ? _Aspect : 1.0;
                float dist = sdRoundedBoxAspect(uv, aspect, _CornerRadius, _EdgePadding);

                float angleRad = _GradientAngle * 3.14159 / 180.0;
                float2 centeredUV = uv - 0.5;
                float2 rotatedUV;
                rotatedUV.x = centeredUV.x * cos(angleRad) - centeredUV.y * sin(angleRad);
                rotatedUV.y = centeredUV.x * sin(angleRad) + centeredUV.y * cos(angleRad);
                float gradientT = saturate((rotatedUV.x + 0.5));

                fixed4 borderColor;
                float cyanWeight = 1.0 - smoothstep(0.0, _MidPoint1 * 1.5, gradientT);
                float purpleWeight = smoothstep(_MidPoint2 * 0.85, 1.0, gradientT);
                float blueWeight = 1.0 - cyanWeight - purpleWeight;
                blueWeight = max(blueWeight, 0.0);

                float totalWeight = cyanWeight + blueWeight + purpleWeight;
                cyanWeight /= totalWeight;
                blueWeight /= totalWeight;
                purpleWeight /= totalWeight;

                borderColor = _ColorA * cyanWeight + _ColorB * blueWeight + _ColorC * purpleWeight;

                float pulse = 1.0;
                if (_PulseEnabled > 0.5)
                {
                    pulse = 1.0 + sin(time * _PulseSpeed) * _PulseIntensity;
                }

                // Background starts after inner margin to leave room for border glow
                float bgDist = -dist - _BgInnerMargin;
                float insideMask = saturate(bgDist / 0.008);
                fixed4 bgColor = _ColorA * cyanWeight + _ColorB * blueWeight + _ColorC * purpleWeight;
                bgColor.rgb = lerp(bgColor.rgb, (bgColor.r + bgColor.g + bgColor.b) / 3.0, 0.1);
                bgColor.a = _BgAlpha * insideMask;

                fixed4 finalColor = bgColor;

                if (_InnerGlowEnabled > 0.5 && dist < 0)
                {
                    float innerDist = -dist;
                    float innerGlow = 1.0 - saturate(innerDist / _InnerGlowWidth);
                    innerGlow = pow(innerGlow, 1.5);
                    finalColor.rgb += borderColor.rgb * innerGlow * _InnerGlowAlpha * pulse;
                    finalColor.a = max(finalColor.a, innerGlow * _InnerGlowAlpha * 0.5);
                }

                float absDist = abs(dist);
                float layer4 = 1.0 - saturate(absDist / _Layer4Width);
                layer4 = pow(layer4, 2.0);
                float layer3 = 1.0 - saturate(absDist / _Layer3Width);
                layer3 = pow(layer3, 2.0);
                float layer2 = 1.0 - saturate(absDist / _Layer2Width);
                layer2 = pow(layer2, 1.5);
                float layer1 = 1.0 - saturate(absDist / _Layer1Width);
                layer1 = pow(layer1, 0.5);

                // Hover boost: intensity +60%, glow width +30% on hover
                float hoverBoost = 1.0 + _HoverAmount * 0.6;
                float hoverWidthBoost = 1.0 + _HoverAmount * 0.3;

                fixed3 glowColor = borderColor.rgb * pulse * hoverBoost;

                // Apply wider glow on hover
                float layer4H = 1.0 - saturate(absDist / (_Layer4Width * hoverWidthBoost));
                layer4H = pow(layer4H, 2.0);
                float layer3H = 1.0 - saturate(absDist / (_Layer3Width * hoverWidthBoost));
                layer3H = pow(layer3H, 2.0);

                finalColor.rgb += glowColor * layer4H * _Layer4Alpha * 0.8;
                finalColor.a = max(finalColor.a, layer4H * _Layer4Alpha * 0.5);

                finalColor.rgb += glowColor * layer3H * _Layer3Alpha * 1.2;
                finalColor.a = max(finalColor.a, layer3H * _Layer3Alpha * 0.7);

                finalColor.rgb = lerp(finalColor.rgb, glowColor * 1.2, layer2 * _Layer2Alpha);
                finalColor.a = max(finalColor.a, layer2 * _Layer2Alpha);

                fixed3 whiteCore = lerp(glowColor * 1.5, fixed3(1,1,1), 0.6);
                float coreMix = layer1 * _Layer1Alpha * 0.5 * hoverBoost;
                finalColor.rgb = lerp(finalColor.rgb, whiteCore, coreMix);
                finalColor.a = max(finalColor.a, layer1 * _Layer1Alpha);

                if (_ShimmerEnabled > 0.5 && _HoverAmount > 0.01)
                {
                    float perimPos = getPerimeterPosition(uv, aspect);
                    float shimmerPos = frac(time * _ShimmerSpeed);
                    float shimmerDist = abs(perimPos - shimmerPos);
                    if (shimmerDist > 0.5) shimmerDist = 1.0 - shimmerDist;
                    float shimmer = 1.0 - saturate(shimmerDist / _ShimmerWidth);
                    shimmer = pow(shimmer, 2.0);
                    float borderMask = saturate(layer1 * 2.0 + layer2 + layer3 * 0.5);
                    shimmer *= borderMask * _ShimmerIntensity * _HoverAmount;
                    finalColor.rgb += fixed3(1, 1, 1) * shimmer * 0.8;
                    finalColor.a = max(finalColor.a, shimmer * 0.5);
                }

                // Top highlight only inside the shape (not in the glow margin)
                float shapeMask = saturate(-dist / 0.005);
                float topHighlight = saturate(1.0 - uv.y) * 0.15;
                finalColor.rgb += fixed3(1, 1, 1) * topHighlight * shapeMask * 0.3;

                finalColor *= i.color;
                return finalColor;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
