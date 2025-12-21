Shader "Custom/GlowingGlassBorder"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        
        [Header(Border Settings)]
        _EdgePadding ("Edge Padding (UV)", Range(0, 0.2)) = 0.05
        _BorderWidth ("Border Width (UV)", Range(0.005, 0.08)) = 0.025
        _CornerRadius ("Corner Radius (UV)", Range(0.01, 0.25)) = 0.07
        
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
        
        [Header(Glass Background)]
        _GlassAlpha ("Glass Alpha", Range(0, 0.2)) = 0.02
        _GlassTint ("Glass Tint", Color) = (0.9, 0.95, 1, 1)
        
        [Header(Vertical Separators)]
        _SeparatorCount ("Separator Count", Range(0, 4)) = 0
        _SeparatorPositions ("Separator X Positions (UV)", Vector) = (0, 0, 0, 0)
        _SeparatorWidth ("Separator Width", Range(0.001, 0.02)) = 0.004
        _SeparatorGlowWidth ("Separator Glow Width", Range(0.001, 0.05)) = 0.015
        _SeparatorAlpha ("Separator Alpha", Range(0, 1)) = 0.8

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
            Name "GlowingGlassBorder"
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
            float _GradientAngle;
            float _CyanRatio;
            
            float _GlassAlpha;
            fixed4 _GlassTint;

            // Separator properties
            float _SeparatorCount;
            float4 _SeparatorPositions;
            float _SeparatorWidth;
            float _SeparatorGlowWidth;
            float _SeparatorAlpha;

            float _Aspect; // Aspect Ratio

            // Calculate separator intensity at a given UV position
            // Returns float4: x = layer1 (core), y = layer2, z = layer3, w = layer4 (ambient)
            float4 getSeparatorIntensity(float2 uv, float aspect, float padding, float dist)
            {
                // Only draw inside the rounded rect
                if (dist > 0.0) return float4(0, 0, 0, 0);

                // Check if separator count is 0
                if (_SeparatorCount < 0.5) return float4(0, 0, 0, 0);

                float4 totalLayers = float4(0, 0, 0, 0);

                // Check each separator
                for (int i = 0; i < 4; i++)
                {
                    if (i >= (int)_SeparatorCount) break;

                    // Get separator position from vector component
                    float sepPos = 0.0;
                    if (i == 0) sepPos = _SeparatorPositions.x;
                    else if (i == 1) sepPos = _SeparatorPositions.y;
                    else if (i == 2) sepPos = _SeparatorPositions.z;
                    else sepPos = _SeparatorPositions.w;

                    // Skip if position is 0 (unset)
                    if (sepPos < 0.01) continue;

                    // Calculate distance to separator
                    float distToSep = abs(uv.x - sepPos);

                    // Multi-layer glow matching border style
                    // Layer 1 (Core) - sharpest
                    float l1 = 1.0 - saturate(distToSep / _SeparatorWidth);
                    l1 = pow(l1, 0.5);

                    // Layer 2 (Mid glow)
                    float l2 = 1.0 - saturate(distToSep / (_SeparatorWidth * 2.0));
                    l2 = pow(l2, 1.5);

                    // Layer 3 (Outer glow)
                    float l3 = 1.0 - saturate(distToSep / _SeparatorGlowWidth);
                    l3 = pow(l3, 2.0);

                    // Layer 4 (Ambient)
                    float l4 = 1.0 - saturate(distToSep / (_SeparatorGlowWidth * 2.0));
                    l4 = pow(l4, 2.0);

                    totalLayers = max(totalLayers, float4(l1, l2, l3, l4));
                }

                return totalLayers;
            }

            // SDF for rounded box with Aspect Ratio correction
            // radius is for the corner
            float sdRoundedBoxAspect(float2 uv, float aspect, float radius, float padding)
            {
                float2 center = float2(0.5, 0.5);
                float2 pos = (uv - center);
                pos.x *= aspect; // Correct X scale
                
                // Effective size is reduced by padding to allow glow space
                // Padding is in UV space (Vertical), so we scale it for X too if needed, 
                // but usually padding is uniform distance.
                // Box Half Size = (0.5 - padding)
                float2 halfSize = float2(0.5 * aspect - padding * aspect, 0.5 - padding); // Scale padding on X? 
                
                // Let's keep padding conceptually uniform in Y height terms.
                // If Aspect > 1, horizontal padding in UV should be smaller? 
                // Wait, if halfSize.y is 0.4 (0.5 - 0.1), halfSize.x should be 0.4 * aspect.
                // This maintains the aspect ratio of the inner box.
                
                // Let's re-verify:
                // We want the visual box to be smaller than the quad.
                
                float2 d = abs(pos) - halfSize + radius;
                return min(max(d.x, d.y), 0.0) + length(max(d, 0.0)) - radius;
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
                
                // SDF with aspect correction and padding
                float dist = sdRoundedBoxAspect(uv, aspect, _CornerRadius, _EdgePadding);
                
                // === GRADIENT ===
                float angleRad = _GradientAngle * 3.14159 / 180.0;
                // Rotate UV around center for gradient
                float2 centeredUV = uv - 0.5;
                float2 rotatedUV;
                rotatedUV.x = centeredUV.x * cos(angleRad) - centeredUV.y * sin(angleRad);
                rotatedUV.y = centeredUV.x * sin(angleRad) + centeredUV.y * cos(angleRad);
                
                float t = saturate((rotatedUV.x + 0.5)); 
                // Adjust curve based on CyanRatio
                t = pow(t, 1.0 / _CyanRatio); // simple bias
                
                fixed4 borderColor = lerp(_ColorA, _ColorB, t);
                
                // === GLASS BACKGROUND ===
                // Inside the box (dist < 0)
                float insideMask = saturate(-dist / 0.005);
                fixed4 glassColor = _GlassTint;
                glassColor.a = _GlassAlpha * insideMask;
                
                // === MULTI-LAYER BORDER (all in UV space) ===
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
                layer1 = pow(layer1, 0.5); // Sharper falloff
                
                // === COMPOSITE ===
                fixed4 finalColor = glassColor;
                
                // Additive Glow Layers
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

                // === VERTICAL SEPARATORS (multi-layer glow matching border) ===
                float4 sepLayers = getSeparatorIntensity(uv, aspect, _EdgePadding, dist);
                if (sepLayers.x > 0.0 || sepLayers.w > 0.0)
                {
                    // Use gradient color at separator position
                    fixed3 sepColor = borderColor.rgb;

                    // Layer 4 (Ambient) - widest, softest
                    finalColor.rgb += sepColor * sepLayers.w * _Layer4Alpha * _SeparatorAlpha * 0.5;
                    finalColor.a = max(finalColor.a, sepLayers.w * _Layer4Alpha * _SeparatorAlpha * 0.3);

                    // Layer 3 (Outer glow)
                    finalColor.rgb += sepColor * sepLayers.z * _Layer3Alpha * _SeparatorAlpha;
                    finalColor.a = max(finalColor.a, sepLayers.z * _Layer3Alpha * _SeparatorAlpha * 0.5);

                    // Layer 2 (Mid glow)
                    finalColor.rgb = lerp(finalColor.rgb, sepColor * 1.2, sepLayers.y * _Layer2Alpha * _SeparatorAlpha);
                    finalColor.a = max(finalColor.a, sepLayers.y * _Layer2Alpha * _SeparatorAlpha);

                    // Layer 1 (Core) - brightest center
                    fixed3 sepWhiteCore = fixed3(1, 1, 1);
                    finalColor.rgb = lerp(finalColor.rgb, sepWhiteCore, sepLayers.x * _Layer1Alpha * _SeparatorAlpha * 0.5);
                    finalColor.a = max(finalColor.a, sepLayers.x * _Layer1Alpha * _SeparatorAlpha);
                }

                finalColor *= i.color;
                
                // Clipping: if visual alpha is too low, we might want to clip or not.
                // Since we rely on transparency, we just return finalColor.
                // However, if we wanted to be super efficient regarding fillrate we could clip, 
                // but for soft glow, better to just let it fade.
                 
                return finalColor;
            }
            ENDCG
        }
    }
    
    FallBack "UI/Default"
}
