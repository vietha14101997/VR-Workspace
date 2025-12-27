Shader "Custom/GlassGradientBackgroundPanel"
{
    // Panel variant with per-edge control for seamless cluster arrangement
    // EdgeMask allows disabling corners/padding on specific edges
    // Use for WorldPanelCluster where panels need to appear connected

    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Rounded Corners)]
        _CornerRadius ("Corner Radius (UV)", Range(0.01, 0.2)) = 0.07
        _EdgePadding ("Edge Padding (UV)", Range(0, 0.2)) = 0.05
        _Aspect ("Aspect Ratio", Float) = 1.0

        [Header(Edge Masking for Cluster)]
        _EdgeMask ("Edge Mask (L,R,T,B)", Vector) = (1,1,1,1)
        // x = Left edge:   1 = show corner/padding, 0 = hide (extend to edge)
        // y = Right edge:  1 = show, 0 = hide
        // z = Top edge:    1 = show, 0 = hide
        // w = Bottom edge: 1 = show, 0 = hide

        [Header(Content Bounds for Expanded Quads)]
        _ContentBounds ("Content Bounds (L,R,B,T)", Vector) = (0,1,0,1)
        // UV bounds of actual content area when quad is expanded for glow overflow
        // x = left edge UV, y = right edge UV, z = bottom edge UV, w = top edge UV

        [Header(Gradient)]
        _ColorA ("Color A (Cyan)", Color) = (0.3, 0.85, 1, 0.15)
        _ColorB ("Color B (Purple)", Color) = (0.7, 0.4, 1, 0.2)
        _GradientOffset ("Gradient Offset", Range(-0.5, 0.5)) = 0.2
        _GradientAngle ("Gradient Angle", Range(-45, 45)) = -15
        _CyanRatio ("Cyan Ratio", Range(0.3, 0.9)) = 0.7

        [Header(Glass Effect)]
        _GlassAlpha ("Base Alpha", Range(0, 1)) = 0.1
        _FresnelPower ("Fresnel Power", Range(1, 5)) = 2.5
        _FresnelStrength ("Fresnel Strength", Range(0, 0.3)) = 0.1

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
            Name "GlassGradientBackgroundPanel"
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

            float _CornerRadius;
            float _EdgePadding;
            float _Aspect;
            float4 _EdgeMask; // L, R, T, B
            float4 _ContentBounds; // L, R, B, T in UV space
            fixed4 _ColorA;
            fixed4 _ColorB;
            float _GradientOffset;
            float _GradientAngle;
            float _CyanRatio;
            float _GlassAlpha;
            float _FresnelPower;
            float _FresnelStrength;
            float _HoverAmount;

            // SDF for rounded box with per-corner radius based on EdgeMask
            // Corners are disabled when their adjacent edges are masked out
            // contentBounds: (left, right, bottom, top) in UV space - defines actual content area
            float sdRoundedBoxPanel(float2 uv, float aspect, float radius, float padding, float4 edgeMask, float4 contentBounds)
            {
                // Remap UV from quad space to content-normalized space (0-1)
                float contentWidth = contentBounds.y - contentBounds.x;
                float contentHeight = contentBounds.w - contentBounds.z;

                // Handle non-expanded case (contentBounds = 0,1,0,1)
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

                // Scale to square space for correct corner calculation
                pos.x *= aspect;

                // Per-edge padding: 0 = no padding (extend to edge), 1 = full padding
                float padL = padding * edgeMask.x;
                float padR = padding * edgeMask.y;
                float padT = padding * edgeMask.z;
                float padB = padding * edgeMask.w;

                // Half size with per-edge padding
                float halfW = 0.5 * aspect;
                float halfH = 0.5;

                // Adjust bounds based on per-edge padding
                float left = -halfW + padL;
                float right = halfW - padR;
                float bottom = -halfH + padB;
                float top = halfH - padT;

                // Per-corner radius: corner is rounded only if both adjacent edges have mask = 1
                float rBL = radius * edgeMask.x * edgeMask.w; // bottom-left
                float rBR = radius * edgeMask.y * edgeMask.w; // bottom-right
                float rTR = radius * edgeMask.y * edgeMask.z; // top-right
                float rTL = radius * edgeMask.x * edgeMask.z; // top-left

                // Determine which quadrant we're in and select appropriate radius
                float r;
                if (pos.x < 0.0)
                {
                    r = (pos.y < 0.0) ? rBL : rTL;
                }
                else
                {
                    r = (pos.y < 0.0) ? rBR : rTR;
                }

                // Box half-size (average of left-right and top-bottom)
                float2 halfSize = float2((right - left) * 0.5, (top - bottom) * 0.5);
                float2 boxCenter = float2((left + right) * 0.5, (bottom + top) * 0.5);

                float2 d = abs(pos - boxCenter) - halfSize + r;
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

            // Check if we're at a masked edge (should not fade there)
            float getEdgeAlphaOverride(float2 uv, float4 edgeMask, float aspect, float padding, float4 contentBounds)
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

                // Position in aspect-corrected space
                float2 pos = (contentUV - 0.5) * float2(aspect, 1.0);

                // Calculate per-edge padding (0 if masked)
                float padL = padding * edgeMask.x;
                float padR = padding * edgeMask.y;
                float padT = padding * edgeMask.z;
                float padB = padding * edgeMask.w;

                // Box boundaries
                float left = -0.5 * aspect + padL;
                float right = 0.5 * aspect - padR;
                float bottom = -0.5 + padB;
                float top = 0.5 - padT;

                // Distance from each edge (positive = inside)
                float dLeft = pos.x - left;
                float dRight = right - pos.x;
                float dBottom = pos.y - bottom;
                float dTop = top - pos.y;

                // Find which edge we're closest to
                float minHDist = min(dLeft, dRight);
                float minVDist = min(dBottom, dTop);

                bool onVerticalEdge = minHDist < minVDist;
                bool closerToLeft = dLeft < dRight;
                bool closerToRight = dRight < dLeft;

                // If we're at a masked edge, don't fade (return 1 to override alpha)
                if (onVerticalEdge)
                {
                    if (closerToLeft && edgeMask.x < 0.5) return 1.0;
                    if (closerToRight && edgeMask.y < 0.5) return 1.0;
                }

                return 0.0; // Normal fade behavior
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;

                // Default aspect to 1.0 if not set
                float aspect = (_Aspect > 0.0) ? _Aspect : 1.0;

                // Calculate content-normalized UV for gradient and effects
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
                float dist = sdRoundedBoxPanel(uv, aspect, _CornerRadius, _EdgePadding, _EdgeMask, _ContentBounds);

                // Alpha mask for rounded corners
                float alphaMask = 1.0 - smoothstep(-0.01, 0.0, dist);

                // Override alpha at masked edges (no fade at junctions)
                float edgeOverride = getEdgeAlphaOverride(uv, _EdgeMask, aspect, _EdgePadding, _ContentBounds);
                if (edgeOverride > 0.5 && dist < 0.01) alphaMask = 1.0;

                // Early out for transparent pixels
                if (alphaMask <= 0.001)
                {
                    return fixed4(0, 0, 0, 0);
                }

                // === GRADIENT COLOR (use content-normalized UV) ===
                float t = contentUV.x;
                t = saturate(t * t * 1.2); // Slight curve for gradient
                float angleOffset = (1.0 - contentUV.y) * 0.15;
                t += angleOffset;
                t = saturate(t);
                fixed4 gradColor = lerp(_ColorA, _ColorB, t);

                // === HOVER ===
                float hoverBrightness = 1.0 + _HoverAmount * 0.3;
                float hoverAlphaBoost = _HoverAmount * 0.1;

                // ========== GLASS EFFECT (use content-normalized UV) ==========
                float2 centerDist = abs(contentUV - 0.5);
                float centerGlow = 1.0 - saturate(length(centerDist) / 0.5);
                centerGlow = centerGlow * centerGlow * 0.15;

                float edgeFactor = 1.0 - saturate(abs(dist) / 0.2);
                float fresnel = edgeFactor * edgeFactor * _FresnelStrength;

                fixed4 finalColor = gradColor;
                finalColor.a = _GlassAlpha + gradColor.a * 0.5 + hoverAlphaBoost;
                finalColor.a *= alphaMask;
                finalColor.rgb += fresnel + centerGlow;
                finalColor.rgb *= hoverBrightness;

                finalColor *= i.color;

                return finalColor;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
