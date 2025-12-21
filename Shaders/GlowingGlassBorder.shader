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
        
        [Header(Running Light)]
        _ShimmerSpeed ("Light Speed", Range(0, 1)) = 0.15
        _ShimmerIntensity ("Light Intensity", Range(0, 1)) = 0.8
        _LightSize ("Light Size", Range(0.01, 0.1)) = 0.018
        _LightGlow ("Light Glow Spread", Range(0.005, 0.05)) = 0.012
        
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
            
            float _ShimmerSpeed;
            float _ShimmerIntensity;
            float _LightSize;
            float _LightGlow;

            float _Aspect; // Aspect Ratio

            // Calculate perimeter position (0-1) for a point on or near the rounded rect border
            float getPerimeterPosition(float2 uv, float aspect, float padding)
            {
                float2 center = float2(0.5, 0.5);
                float2 pos = uv - center;

                // Get the effective box dimensions
                float halfW = 0.5 - padding;
                float halfH = 0.5 - padding;

                // Calculate angle from center
                float angle = atan2(pos.y, pos.x * (1.0 / aspect));

                // Normalize to 0-1 range (starting from right, going counter-clockwise)
                float t = (angle + 3.14159) / (2.0 * 3.14159);

                return t;
            }

            // Get the UV position on the border for a given perimeter t (0-1)
            float2 getBorderUV(float t, float aspect, float padding)
            {
                float halfW = 0.5 - padding;
                float halfH = 0.5 - padding;

                // Convert t to angle
                float angle = t * 2.0 * 3.14159 - 3.14159;

                // Calculate direction
                float2 dir = float2(cos(angle) * aspect, sin(angle));
                dir = normalize(dir);

                // Scale to hit the box edge (approximate for rounded rect)
                float scaleX = halfW / max(abs(dir.x), 0.001);
                float scaleY = halfH / max(abs(dir.y), 0.001);
                float scale = min(scaleX, scaleY);

                return float2(0.5, 0.5) + dir * scale * 0.95;
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
                
                // === RUNNING LIGHT (single dot running along border) ===
                fixed3 runningLightColor = fixed3(0, 0, 0);
                float runningLightAlpha = 0.0;

                if (_ShimmerSpeed > 0.01)
                {
                    // Get current pixel's position along the perimeter (0-1)
                    float perimPos = getPerimeterPosition(uv, aspect, _EdgePadding);

                    // Start from top-left corner (offset ~0.625 in perimeter space)
                    float startOffset = 0.625;

                    // Single light moving counter-clockwise from top-left
                    float timeVal = _Time.y * _ShimmerSpeed;
                    float lightPos = frac(-timeVal + startOffset);

                    // Calculate distance to the light (with wrap-around)
                    float shimmerDist = abs(perimPos - lightPos);
                    shimmerDist = min(shimmerDist, 1.0 - shimmerDist);

                    // Create soft glow
                    float glow = 1.0 - saturate(shimmerDist / _LightSize);
                    glow = pow(glow, 2.5); // Tighter shimmer core

                    // Get color at light's position based on the gradient
                    float2 lightUV = getBorderUV(lightPos, aspect, _EdgePadding);
                    float2 centered = lightUV - 0.5;
                    float2 rotated;
                    rotated.x = centered.x * cos(angleRad) - centered.y * sin(angleRad);
                    rotated.y = centered.x * sin(angleRad) + centered.y * cos(angleRad);
                    float tLight = saturate(rotated.x + 0.5);
                    tLight = pow(tLight, 1.0 / _CyanRatio);
                    fixed3 lightColor = lerp(_ColorA.rgb, _ColorB.rgb, tLight);

                    // Boost saturation of shimmer color
                    lightColor = saturate(lightColor * 1.4);

                    runningLightColor = lightColor * glow;
                    runningLightAlpha = glow;

                    // Mask to border area only
                    float borderMask = saturate(layer1 * 2.0 + layer2 + layer3);
                    runningLightColor *= borderMask * _ShimmerIntensity;
                    runningLightAlpha *= borderMask;
                }
                
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
                // Suppress white core where shimmer is active to let shimmer color shine through
                float shimmerFactor = runningLightAlpha * _ShimmerIntensity;
                float coreMix = layer1 * _Layer1Alpha * (0.5 * (1.0 - shimmerFactor));
                
                finalColor.rgb = lerp(finalColor.rgb, whiteCore, coreMix); 
                finalColor.a = max(finalColor.a, layer1 * _Layer1Alpha);

                // Add Running Lights (positioned over the suppressed core)
                finalColor.rgb += runningLightColor * 2.5; 
                finalColor.a = max(finalColor.a, runningLightAlpha * _ShimmerIntensity);
                
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
