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
        
        [Header(Glowing Stroke)]
        _StrokeEnabled ("Enable Stroke", Range(0, 1)) = 1
        _StrokeSpeed ("Stroke Speed", Range(0, 1)) = 0.2
        _StrokeLength ("Stroke Length", Range(0.02, 0.2)) = 0.125
        _StrokeIntensity ("Stroke Intensity", Range(0, 2)) = 1.0
        _StrokeGlow ("Stroke Glow Width", Range(0.01, 0.1)) = 0.03

        [Header(Hologram Effect)]
        _HoloEnabled ("Enable Hologram", Range(0, 1)) = 0
        _ScanlineIntensity ("Scanline Intensity", Range(0, 1)) = 0.15
        _ScanlineCount ("Scanline Count", Range(50, 500)) = 150
        _ScanlineSpeed ("Scanline Speed", Range(0, 2)) = 0.3
        _ChromaticAberration ("Chromatic Aberration", Range(0, 0.01)) = 0.002
        _HoloFlicker ("Flicker Intensity", Range(0, 0.3)) = 0.05
        _HoloNoise ("Noise Intensity", Range(0, 0.2)) = 0.03

        [Header(Vertical Separators)]
        _SeparatorCount ("Separator Count", Range(0, 4)) = 0
        _SeparatorPositions ("Separator X Positions (UV)", Vector) = (0, 0, 0, 0)
        _SeparatorWidth ("Separator Width", Range(0.001, 0.02)) = 0.004
        _SeparatorGlowWidth ("Separator Glow Width", Range(0.001, 0.05)) = 0.015
        _SeparatorAlpha ("Separator Alpha", Range(0, 1)) = 0.8

        [Header(Horizontal Separators)]
        _HSeparatorCount ("H Separator Count", Range(0, 4)) = 0
        _HSeparatorPositions ("H Separator Y Positions (UV)", Vector) = (0, 0, 0, 0)
        _HSeparatorWidth ("H Separator Width", Range(0.001, 0.02)) = 0.004
        _HSeparatorGlowWidth ("H Separator Glow Width", Range(0.001, 0.05)) = 0.015
        _HSeparatorAlpha ("H Separator Alpha", Range(0, 1)) = 0.8
        _HSeparatorLength ("H Separator Length", Range(0, 1)) = 1.0

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

            // Glowing Stroke properties
            float _StrokeEnabled;
            float _StrokeSpeed;
            float _StrokeLength;
            float _StrokeIntensity;
            float _StrokeGlow;

            // Hologram properties
            float _HoloEnabled;
            float _ScanlineIntensity;
            float _ScanlineCount;
            float _ScanlineSpeed;
            float _ChromaticAberration;
            float _HoloFlicker;
            float _HoloNoise;

            // Vertical Separator properties
            float _SeparatorCount;
            float4 _SeparatorPositions;
            float _SeparatorWidth;
            float _SeparatorGlowWidth;
            float _SeparatorAlpha;

            // Horizontal Separator properties
            float _HSeparatorCount;
            float4 _HSeparatorPositions;
            float _HSeparatorWidth;
            float _HSeparatorGlowWidth;
            float _HSeparatorAlpha;
            float _HSeparatorLength;

            float _Aspect; // Aspect Ratio

            // Hash function for noise generation
            float hash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // Hologram effect function
            float3 applyHologramEffect(float3 color, float2 uv, float borderMask)
            {
                if (_HoloEnabled < 0.5) return color;

                float time = _Time.y;

                // === SCANLINES ===
                float scanline = sin((uv.y + time * _ScanlineSpeed) * _ScanlineCount * 3.14159) * 0.5 + 0.5;
                scanline = pow(scanline, 0.8);
                float scanlineEffect = 1.0 - (scanline * _ScanlineIntensity * borderMask);

                // === CHROMATIC ABERRATION ===
                float caOffset = _ChromaticAberration * borderMask;
                // Shift red and blue channels slightly
                float3 caColor = color;
                caColor.r = color.r * (1.0 + caOffset * 2.0);
                caColor.b = color.b * (1.0 - caOffset);

                // === FLICKER ===
                float flicker = 1.0 - (hash(float2(floor(time * 15.0), 0.0)) * _HoloFlicker * borderMask);

                // === NOISE ===
                float noise = hash(uv * 500.0 + time * 10.0);
                noise = (noise - 0.5) * _HoloNoise * borderMask;

                // Combine effects
                float3 result = caColor * scanlineEffect * flicker;
                result += noise;

                return result;
            }

            // Calculate perimeter position (0-1) for glowing stroke
            float getPerimeterPosition(float2 uv, float aspect, float padding)
            {
                float2 center = float2(0.5, 0.5);
                float2 pos = uv - center;

                // Calculate angle from center
                float angle = atan2(pos.y, pos.x * (1.0 / aspect));

                // Normalize to 0-1 range (starting from right, going counter-clockwise)
                float t = (angle + 3.14159) / (2.0 * 3.14159);

                return t;
            }

            // Calculate glowing stroke intensity for dual strokes
            // Returns float2: x = stroke1 intensity, y = stroke2 intensity
            // Two strokes running opposite directions (offset by 0.5)
            // Alpha fades smoothly from center to both ends
            float2 getStrokeIntensity(float2 uv, float aspect, float padding, float borderMask)
            {
                if (_StrokeEnabled < 0.5) return float2(0, 0);

                // Get current pixel's position along the perimeter (0-1)
                float perimPos = getPerimeterPosition(uv, aspect, padding);

                // Animate - both strokes run full perimeter cycle
                float time = _Time.y * _StrokeSpeed;

                // Stroke positions (opposite sides, offset by 0.5)
                float stroke1Pos = frac(time);
                float stroke2Pos = frac(time + 0.5);

                // Stroke length for fade
                float halfLength = _StrokeLength * 0.5;

                // === STROKE 1 ===
                // Calculate signed distance from stroke center (-halfLength to +halfLength)
                float signedDist1 = perimPos - stroke1Pos;
                // Handle wrap-around
                if (signedDist1 > 0.5) signedDist1 -= 1.0;
                if (signedDist1 < -0.5) signedDist1 += 1.0;

                // Normalize to -1 to 1 range within stroke length
                float normalizedDist1 = signedDist1 / halfLength;
                // Alpha gradient: 1.0 at center, 0.0 at edges (using smooth cosine curve)
                float intensity1 = 0.0;
                if (abs(normalizedDist1) < 1.0)
                {
                    // Cosine falloff: smooth from center to edges
                    intensity1 = 0.5 + 0.5 * cos(normalizedDist1 * 3.14159);
                }
                intensity1 *= borderMask;

                // === STROKE 2 ===
                float signedDist2 = perimPos - stroke2Pos;
                if (signedDist2 > 0.5) signedDist2 -= 1.0;
                if (signedDist2 < -0.5) signedDist2 += 1.0;

                float normalizedDist2 = signedDist2 / halfLength;
                float intensity2 = 0.0;
                if (abs(normalizedDist2) < 1.0)
                {
                    intensity2 = 0.5 + 0.5 * cos(normalizedDist2 * 3.14159);
                }
                intensity2 *= borderMask;

                return float2(intensity1, intensity2);
            }

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

            // Calculate horizontal separator intensity at a given UV position
            // Returns float4: x = layer1 (core), y = layer2, z = layer3, w = layer4 (ambient)
            float4 getHSeparatorIntensity(float2 uv, float aspect, float padding, float dist)
            {
                // Only draw inside the rounded rect
                if (dist > 0.0) return float4(0, 0, 0, 0);

                // Check if separator count is 0
                if (_HSeparatorCount < 0.5) return float4(0, 0, 0, 0);

                // Calculate separator extent based on length (0-1)
                // length = 1 means full width, length = 0.5 means half width, centered at x = 0.5
                float centerX = 0.5;
                float fullHalfWidth = centerX - padding; // Full half-width of content area
                float sepHalfWidth = fullHalfWidth * _HSeparatorLength; // Actual separator half-width
                float distFromCenter = abs(uv.x - centerX);

                // Check if outside separator extent
                if (distFromCenter > sepHalfWidth) return float4(0, 0, 0, 0);

                // Horizontal edge fade: alpha = 1 at center, smoothly fading to 0 at edges
                // Smooth fade from center (1.0) to edge (0.0)
                float normalizedDist = saturate(distFromCenter / sepHalfWidth);
                float hEdgeFade = 1.0 - pow(normalizedDist, 2.0); // Quadratic falloff

                float4 totalLayers = float4(0, 0, 0, 0);

                // Check each horizontal separator
                for (int i = 0; i < 4; i++)
                {
                    if (i >= (int)_HSeparatorCount) break;

                    // Get separator position from vector component
                    float sepPos = 0.0;
                    if (i == 0) sepPos = _HSeparatorPositions.x;
                    else if (i == 1) sepPos = _HSeparatorPositions.y;
                    else if (i == 2) sepPos = _HSeparatorPositions.z;
                    else sepPos = _HSeparatorPositions.w;

                    // Skip if position is 0 (unset)
                    if (sepPos < 0.01) continue;

                    // Calculate distance to separator (Y axis for horizontal line)
                    float distToSep = abs(uv.y - sepPos);

                    // Multi-layer glow matching border style
                    // Layer 1 (Core) - sharpest
                    float l1 = 1.0 - saturate(distToSep / _HSeparatorWidth);
                    l1 = pow(l1, 0.5);

                    // Layer 2 (Mid glow)
                    float l2 = 1.0 - saturate(distToSep / (_HSeparatorWidth * 2.0));
                    l2 = pow(l2, 1.5);

                    // Layer 3 (Outer glow)
                    float l3 = 1.0 - saturate(distToSep / _HSeparatorGlowWidth);
                    l3 = pow(l3, 2.0);

                    // Layer 4 (Ambient)
                    float l4 = 1.0 - saturate(distToSep / (_HSeparatorGlowWidth * 2.0));
                    l4 = pow(l4, 2.0);

                    // Apply horizontal edge fade to all layers
                    totalLayers = max(totalLayers, float4(l1, l2, l3, l4) * hEdgeFade);
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

                // === ANAMORPHIC LENS FLARE STROKE ===
                // Stroke runs along border, with perpendicular glow spreading inward/outward
                float strokeBorderMask = saturate(layer1 * 2.0 + layer2 + layer3 * 0.5);
                float2 strokeIntensities = getStrokeIntensity(uv, aspect, _EdgePadding, strokeBorderMask);
                float strokeIntensity = max(strokeIntensities.x, strokeIntensities.y);

                if (strokeIntensity > 0.001)
                {
                    // Perpendicular glow: use distance from border (dist) for spread
                    // absDist is already calculated above
                    float perpGlowWidth = _StrokeGlow;

                    // Multiple glow layers spreading perpendicular to border
                    // Layer 5: Ultra-wide ambient glow
                    float perpLayer5 = 1.0 - saturate(absDist / (perpGlowWidth * 5.0));
                    perpLayer5 = pow(perpLayer5, 3.0);

                    // Layer 4: Wide soft glow
                    float perpLayer4 = 1.0 - saturate(absDist / (perpGlowWidth * 3.0));
                    perpLayer4 = pow(perpLayer4, 2.5);

                    // Layer 3: Medium glow
                    float perpLayer3 = 1.0 - saturate(absDist / (perpGlowWidth * 1.5));
                    perpLayer3 = pow(perpLayer3, 2.0);

                    // Layer 2: Inner glow
                    float perpLayer2 = 1.0 - saturate(absDist / perpGlowWidth);
                    perpLayer2 = pow(perpLayer2, 1.5);

                    // Layer 1: Sharp core
                    float perpLayer1 = 1.0 - saturate(absDist / (perpGlowWidth * 0.3));
                    perpLayer1 = pow(perpLayer1, 0.8);

                    // Combine stroke intensity with perpendicular layers
                    float intensity = strokeIntensity * _StrokeIntensity;

                    // Layer 5: Ultra ambient - gradient color, very soft
                    finalColor.rgb += borderColor.rgb * perpLayer5 * intensity * 0.15;
                    finalColor.a = max(finalColor.a, perpLayer5 * intensity * 0.1);

                    // Layer 4: Wide glow - gradient color
                    finalColor.rgb += borderColor.rgb * perpLayer4 * intensity * 0.3;
                    finalColor.a = max(finalColor.a, perpLayer4 * intensity * 0.2);

                    // Layer 3: Medium glow - slightly brighter gradient
                    finalColor.rgb += borderColor.rgb * 1.2 * perpLayer3 * intensity * 0.5;
                    finalColor.a = max(finalColor.a, perpLayer3 * intensity * 0.4);

                    // Layer 2: Inner glow - bright gradient with white mix
                    fixed3 innerCol = lerp(borderColor.rgb * 1.5, fixed3(1, 1, 1), 0.3);
                    finalColor.rgb += innerCol * perpLayer2 * intensity * 0.8;
                    finalColor.a = max(finalColor.a, perpLayer2 * intensity * 0.6);

                    // Layer 1: Sharp white-hot core
                    fixed3 coreCol = lerp(borderColor.rgb * 2.0, fixed3(1, 1, 1), 0.7);
                    finalColor.rgb += coreCol * perpLayer1 * intensity * 1.2;
                    finalColor.a = max(finalColor.a, perpLayer1 * intensity * 0.9);
                }

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

                // === HORIZONTAL SEPARATORS (multi-layer glow matching border) ===
                float4 hSepLayers = getHSeparatorIntensity(uv, aspect, _EdgePadding, dist);
                if (hSepLayers.x > 0.0 || hSepLayers.w > 0.0)
                {
                    // Use gradient color at separator position
                    fixed3 hSepColor = borderColor.rgb;

                    // Layer 4 (Ambient) - widest, softest
                    finalColor.rgb += hSepColor * hSepLayers.w * _Layer4Alpha * _HSeparatorAlpha * 0.5;
                    finalColor.a = max(finalColor.a, hSepLayers.w * _Layer4Alpha * _HSeparatorAlpha * 0.3);

                    // Layer 3 (Outer glow)
                    finalColor.rgb += hSepColor * hSepLayers.z * _Layer3Alpha * _HSeparatorAlpha;
                    finalColor.a = max(finalColor.a, hSepLayers.z * _Layer3Alpha * _HSeparatorAlpha * 0.5);

                    // Layer 2 (Mid glow)
                    finalColor.rgb = lerp(finalColor.rgb, hSepColor * 1.2, hSepLayers.y * _Layer2Alpha * _HSeparatorAlpha);
                    finalColor.a = max(finalColor.a, hSepLayers.y * _Layer2Alpha * _HSeparatorAlpha);

                    // Layer 1 (Core) - brightest center
                    fixed3 hSepWhiteCore = fixed3(1, 1, 1);
                    finalColor.rgb = lerp(finalColor.rgb, hSepWhiteCore, hSepLayers.x * _Layer1Alpha * _HSeparatorAlpha * 0.5);
                    finalColor.a = max(finalColor.a, hSepLayers.x * _Layer1Alpha * _HSeparatorAlpha);
                }

                // === HOLOGRAM EFFECT ===
                // Calculate border mask for hologram (stronger effect on border areas)
                float borderMask = saturate(layer1 + layer2 * 0.8 + layer3 * 0.5 + layer4 * 0.3);
                finalColor.rgb = applyHologramEffect(finalColor.rgb, uv, borderMask);

                finalColor *= i.color;

                return finalColor;
            }
            ENDCG
        }
    }
    
    FallBack "UI/Default"
}
