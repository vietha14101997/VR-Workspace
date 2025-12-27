Shader "Custom/GlowingGlassBorderPanel"
{
    // Panel variant with per-edge control for seamless cluster arrangement
    // EdgeMask allows disabling border/corners on specific edges
    // Use for WorldPanelCluster where panels need to appear connected

    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Border Settings)]
        _EdgePadding ("Edge Padding (UV)", Range(0, 0.2)) = 0.05
        _BorderWidth ("Border Width (UV)", Range(0.005, 0.08)) = 0.025
        _CornerRadius ("Corner Radius (UV)", Range(0.01, 0.25)) = 0.07

        [Header(Edge Masking for Cluster)]
        _EdgeMask ("Edge Mask (L,R,T,B)", Vector) = (1,1,1,1)
        // x = Left edge:   1 = show corner/padding/border, 0 = hide
        // y = Right edge:  1 = show, 0 = hide
        // z = Top edge:    1 = show, 0 = hide
        // w = Bottom edge: 1 = show, 0 = hide

        [Header(Content Bounds for Expanded Quads)]
        _ContentBounds ("Content Bounds (L,R,B,T)", Vector) = (0,1,0,1)
        // UV bounds of actual content area when quad is expanded for glow overflow

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
        _GlassAlpha ("Glass Alpha", Range(0, 0.2)) = 0
        _GlassTint ("Glass Tint", Color) = (0.9, 0.95, 1, 1)

        [Header(Aspect Ratio)]
        _Aspect ("Aspect Ratio", Float) = 1.0

        [Header(Cluster Positioning)]
        _ClusterUVOffset ("Cluster UV Offset X", Float) = 0
        _ClusterUVScale ("Cluster UV Scale X", Float) = 1

        [Header(Content Margins)]
        _MarginH ("Horizontal Margin", Float) = 0.04
        _MarginV ("Vertical Margin", Float) = 0.045

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
            Name "GlowingGlassBorderPanel"
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
            float4 _EdgeMask; // L, R, T, B
            float4 _ContentBounds; // L, R, B, T in UV space

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

            float _Aspect;
            float _ClusterUVOffset;
            float _ClusterUVScale;
            float _MarginH;
            float _MarginV;

            // SDF for rounded box with per-corner radius based on EdgeMask
            // contentBounds: (left, right, bottom, top) in UV space
            float sdRoundedBoxPanel(float2 uv, float aspect, float radius, float padding, float4 edgeMask, float4 contentBounds)
            {
                // Remap UV from quad space to content-normalized space (0-1)
                float contentWidth = contentBounds.y - contentBounds.x;
                float contentHeight = contentBounds.w - contentBounds.z;

                float2 contentUV;
                if (contentWidth > 0.001 && contentHeight > 0.001)
                {
                    contentUV.x = (uv.x - contentBounds.x) / contentWidth;
                    contentUV.y = (uv.y - contentBounds.z) / contentHeight;
                }
                else
                {
                    contentUV = uv;
                }

                float2 center = float2(0.5, 0.5);
                float2 pos = (contentUV - center);
                pos.x *= aspect;

                // Per-edge padding
                float padL = padding * edgeMask.x;
                float padR = padding * edgeMask.y;
                float padT = padding * edgeMask.z;
                float padB = padding * edgeMask.w;

                float halfW = 0.5 * aspect;
                float halfH = 0.5;

                float left = -halfW + padL;
                float right = halfW - padR;
                float bottom = -halfH + padB;
                float top = halfH - padT;

                // Per-corner radius
                float rBL = radius * edgeMask.x * edgeMask.w;
                float rBR = radius * edgeMask.y * edgeMask.w;
                float rTR = radius * edgeMask.y * edgeMask.z;
                float rTL = radius * edgeMask.x * edgeMask.z;

                float r;
                if (pos.x < 0.0)
                {
                    r = (pos.y < 0.0) ? rBL : rTL;
                }
                else
                {
                    r = (pos.y < 0.0) ? rBR : rTR;
                }

                float2 halfSize = float2((right - left) * 0.5, (top - bottom) * 0.5);
                float2 boxCenter = float2((left + right) * 0.5, (bottom + top) * 0.5);

                float2 d = abs(pos - boxCenter) - halfSize + r;
                return min(max(d.x, d.y), 0.0) + length(max(d, 0.0)) - r;
            }

            // Calculate edge visibility mask - hard clips from panel edge to Board edge on masked edges
            // Uses margin values to determine Board boundary
            float getEdgeBorderMask(float2 uv, float4 edgeMask, float edgePadding, float aspect, float4 contentBounds, float cornerRadius, float marginH, float marginV)
            {
                // Remap UV to content-normalized space
                float contentWidth = contentBounds.y - contentBounds.x;
                float contentHeight = contentBounds.w - contentBounds.z;

                float2 contentUV;
                if (contentWidth > 0.001 && contentHeight > 0.001)
                {
                    contentUV.x = (uv.x - contentBounds.x) / contentWidth;
                    contentUV.y = (uv.y - contentBounds.z) / contentHeight;
                }
                else
                {
                    contentUV = uv;
                }

                // Board boundaries in contentUV space
                float boardLeft = marginH;
                float boardRight = 1.0 - marginH;

                // On masked left edge, clip PAST boardLeft to avoid overlap
                // This panel yields to the adjacent panel's right edge
                if (edgeMask.x < 0.5 && contentUV.x < boardLeft + 0.02)
                {
                    return 0.0;
                }

                // On masked right edge, clip exactly at boardRight
                if (edgeMask.y < 0.5 && contentUV.x > boardRight)
                {
                    return 0.0;
                }

                return 1.0;
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

                // Calculate content-normalized UV for effects
                float contentWidth = _ContentBounds.y - _ContentBounds.x;
                float contentHeight = _ContentBounds.w - _ContentBounds.z;
                float2 contentUV;
                if (contentWidth > 0.001 && contentHeight > 0.001)
                {
                    contentUV.x = (uv.x - _ContentBounds.x) / contentWidth;
                    contentUV.y = (uv.y - _ContentBounds.z) / contentHeight;
                }
                else
                {
                    contentUV = uv;
                }

                // SDF with per-edge control
                // On masked edges: padding=0, cornerRadius=0, so border extends to full boundary
                float dist = sdRoundedBoxPanel(uv, aspect, _CornerRadius, _EdgePadding, _EdgeMask, _ContentBounds);

                // === GRADIENT (use cluster-wide UV for seamless gradient across panels) ===
                // Map local contentUV.x to cluster-wide position
                float clusterX = contentUV.x * _ClusterUVScale + _ClusterUVOffset;

                float angleRad = _GradientAngle * 3.14159 / 180.0;
                float2 centeredUV = float2(clusterX, contentUV.y) - 0.5;
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

                // Layers use SDF naturally - no special masking needed
                // SDF handles masked edges with padding=0 and cornerRadius=0

                // === SHIMMER EFFECT (use content-normalized UV) ===
                float time = _Time.y * _ShimmerSpeed;
                float shimmerPos = frac(time);

                // Calculate position along top and bottom edges (using contentUV)
                float topDist = abs(contentUV.y - (1.0 - _EdgePadding));
                float bottomDist = abs(contentUV.y - _EdgePadding);
                float isOnHorizontalEdge = step(topDist, 0.02) + step(bottomDist, 0.02);

                // Shimmer light moving along horizontal edges
                float shimmerX = abs(contentUV.x - shimmerPos);
                shimmerX = min(shimmerX, 1.0 - shimmerX); // Wrap around
                float shimmerLight = 1.0 - saturate(shimmerX / _LightSize);
                shimmerLight = pow(shimmerLight, 2.0) * isOnHorizontalEdge * _ShimmerIntensity;

                // Shimmer glow (wider, softer)
                float shimmerGlow = 1.0 - saturate(shimmerX / _LightGlow);
                shimmerGlow = pow(shimmerGlow, 3.0) * isOnHorizontalEdge * _ShimmerIntensity * 0.5;

                // Apply edge mask to shimmer (don't shimmer on hidden edges)
                float shimmerMask = 1.0;
                if (_EdgeMask.z < 0.5) shimmerMask *= smoothstep(0.0, 0.1, 1.0 - contentUV.y); // top hidden
                if (_EdgeMask.w < 0.5) shimmerMask *= smoothstep(0.0, 0.1, contentUV.y); // bottom hidden
                shimmerLight *= shimmerMask;
                shimmerGlow *= shimmerMask;

                // === COMPOSITE ===
                fixed4 finalColor = glassColor;

                fixed3 glowColor = borderColor.rgb;

                // Additive Glow Layers
                finalColor.rgb += glowColor * layer4 * _Layer4Alpha * 0.5;
                finalColor.a = max(finalColor.a, layer4 * _Layer4Alpha * 0.3);

                finalColor.rgb += glowColor * layer3 * _Layer3Alpha;
                finalColor.a = max(finalColor.a, layer3 * _Layer3Alpha * 0.5);

                finalColor.rgb = lerp(finalColor.rgb, glowColor * 1.2, layer2 * _Layer2Alpha);
                finalColor.a = max(finalColor.a, layer2 * _Layer2Alpha);

                // White core - hard clip handles junction, no extra suppression needed
                fixed3 whiteCore = fixed3(1,1,1);
                float coreMix = layer1 * _Layer1Alpha * 0.5;
                finalColor.rgb = lerp(finalColor.rgb, whiteCore, coreMix);
                finalColor.a = max(finalColor.a, layer1 * _Layer1Alpha);

                // Add shimmer
                finalColor.rgb += glowColor * shimmerGlow;
                finalColor.rgb += whiteCore * shimmerLight;
                finalColor.a = max(finalColor.a, shimmerLight * 0.8);

                // Apply margin-based edge fade (from panel edge to Board edge on masked edges)
                float edgeMask = getEdgeBorderMask(uv, _EdgeMask, _EdgePadding, aspect, _ContentBounds, _CornerRadius, _MarginH, _MarginV);
                finalColor.a *= edgeMask;
                finalColor.rgb *= edgeMask;

                finalColor *= i.color;

                return finalColor;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
