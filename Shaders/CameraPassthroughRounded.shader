Shader "Custom/CameraPassthroughRounded"
{
    Properties
    {
        _MainTex ("Camera Texture", 2D) = "black" {}

        [Header(Shape)]
        _CornerRadius ("Corner Radius", Range(0, 0.3)) = 0.04
        _EdgePadding ("Edge Padding", Range(0, 0.1)) = 0.0075
        _Aspect ("Aspect Ratio", Float) = 1.777

        [Header(Border)]
        _BorderEnabled ("Border Enabled", Float) = 1
        _BorderWidth ("Border Width", Range(0, 0.2)) = 0.025
        _ColorA ("Border Color A", Color) = (0.3, 1, 1, 1)
        _ColorB ("Border Color B", Color) = (1, 0.4, 1, 1)
        _GradientAngle ("Gradient Angle", Range(-90, 90)) = -10

        [Header(Glow Layers)]
        _Layer1Width ("Layer 1 Width", Range(0, 0.2)) = 0.01
        _Layer1Alpha ("Layer 1 Alpha", Range(0, 3)) = 1.5
        _Layer2Width ("Layer 2 Width", Range(0, 0.2)) = 0.02
        _Layer2Alpha ("Layer 2 Alpha", Range(0, 3)) = 1.0
        _Layer3Width ("Layer 3 Width", Range(0, 0.2)) = 0.045
        _Layer3Alpha ("Layer 3 Alpha", Range(0, 3)) = 0.6
        _Layer4Width ("Layer 4 Width", Range(0, 0.2)) = 0.09
        _Layer4Alpha ("Layer 4 Alpha", Range(0, 3)) = 0.3

        [Header(Animation)]
        _ShimmerSpeed ("Shimmer Speed", Range(0, 1)) = 0.1
        _ShimmerIntensity ("Shimmer Intensity", Range(0, 1)) = 0.2

        [Header(Scan Frame)]
        _ScanFrameEnabled ("Scan Frame Enabled", Float) = 1
        _ScanFrameSize ("Scan Frame Size", Range(0.3, 0.9)) = 0.65
        _ScanFrameColor ("Scan Frame Color", Color) = (1, 1, 1, 1)
        _BracketLength ("Bracket Length", Range(0.05, 0.3)) = 0.15
        _BracketWidth ("Bracket Width", Range(0.002, 0.02)) = 0.006
        _ScanLineY ("Scan Line Y Position", Range(0, 1)) = 0.5
        _ScanLineColor ("Scan Line Color", Color) = (0, 0.9, 1, 1)
        _OverlayAlpha ("Overlay Alpha", Range(0, 0.8)) = 0.4
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                UNITY_FOG_COORDS(1)
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            float _CornerRadius;
            float _EdgePadding;
            float _Aspect;

            float _BorderEnabled;
            float _BorderWidth;
            float4 _ColorA;
            float4 _ColorB;
            float _GradientAngle;

            float _Layer1Width;
            float _Layer1Alpha;
            float _Layer2Width;
            float _Layer2Alpha;
            float _Layer3Width;
            float _Layer3Alpha;
            float _Layer4Width;
            float _Layer4Alpha;

            float _ShimmerSpeed;
            float _ShimmerIntensity;

            float _ScanFrameEnabled;
            float _ScanFrameSize;
            float4 _ScanFrameColor;
            float _BracketLength;
            float _BracketWidth;
            float _ScanLineY;
            float4 _ScanLineColor;
            float _OverlayAlpha;

            // SDF for rounded rectangle - Wide formula for Android compatibility
            float sdRoundedBox(float2 uv, float aspect, float radius, float padding)
            {
                float2 center = float2(0.5, 0.5);
                float2 pos = (uv - center);
                pos.x *= aspect;

                // Wide formula: padding is uniform in scaled space
                float2 halfSize = float2(0.5 * aspect - padding, 0.5 - padding);

                float2 d = abs(pos) - halfSize + radius;
                return min(max(d.x, d.y), 0.0) + length(max(d, 0.0)) - radius;
            }

            // Gradient color based on angle
            float3 getGradientColor(float2 uv, float angle)
            {
                float rad = radians(angle);
                float2 dir = float2(cos(rad), sin(rad));
                float t = dot(uv - 0.5, dir) + 0.5;
                t = saturate(t);
                return lerp(_ColorA.rgb, _ColorB.rgb, t);
            }

            // Shimmer effect
            float getShimmer(float2 uv)
            {
                float time = _Time.y * _ShimmerSpeed;
                float shimmer = sin((uv.x + uv.y) * 10.0 + time * 6.28) * 0.5 + 0.5;
                return shimmer * _ShimmerIntensity;
            }

            // Draw corner brackets for scan frame
            float drawBrackets(float2 uv, float frameSize, float bracketLen, float bracketW)
            {
                float2 center = float2(0.5, 0.5);
                float halfSize = frameSize * 0.5;

                // Calculate frame bounds
                float left = center.x - halfSize;
                float right = center.x + halfSize;
                float top = center.y + halfSize;
                float bottom = center.y - halfSize;

                float bracket = 0.0;
                float aa = 0.002; // anti-alias width

                // Top-Left corner
                bracket = max(bracket, smoothstep(left - aa, left + aa, uv.x) * smoothstep(left + bracketLen + aa, left + bracketLen - aa, uv.x)
                    * smoothstep(top - bracketW - aa, top - bracketW + aa, uv.y) * smoothstep(top + aa, top - aa, uv.y));
                bracket = max(bracket, smoothstep(left - aa, left + aa, uv.x) * smoothstep(left + bracketW + aa, left + bracketW - aa, uv.x)
                    * smoothstep(top - bracketLen - aa, top - bracketLen + aa, uv.y) * smoothstep(top + aa, top - aa, uv.y));

                // Top-Right corner
                bracket = max(bracket, smoothstep(right - bracketLen - aa, right - bracketLen + aa, uv.x) * smoothstep(right + aa, right - aa, uv.x)
                    * smoothstep(top - bracketW - aa, top - bracketW + aa, uv.y) * smoothstep(top + aa, top - aa, uv.y));
                bracket = max(bracket, smoothstep(right - bracketW - aa, right - bracketW + aa, uv.x) * smoothstep(right + aa, right - aa, uv.x)
                    * smoothstep(top - bracketLen - aa, top - bracketLen + aa, uv.y) * smoothstep(top + aa, top - aa, uv.y));

                // Bottom-Left corner
                bracket = max(bracket, smoothstep(left - aa, left + aa, uv.x) * smoothstep(left + bracketLen + aa, left + bracketLen - aa, uv.x)
                    * smoothstep(bottom - aa, bottom + aa, uv.y) * smoothstep(bottom + bracketW + aa, bottom + bracketW - aa, uv.y));
                bracket = max(bracket, smoothstep(left - aa, left + aa, uv.x) * smoothstep(left + bracketW + aa, left + bracketW - aa, uv.x)
                    * smoothstep(bottom - aa, bottom + aa, uv.y) * smoothstep(bottom + bracketLen + aa, bottom + bracketLen - aa, uv.y));

                // Bottom-Right corner
                bracket = max(bracket, smoothstep(right - bracketLen - aa, right - bracketLen + aa, uv.x) * smoothstep(right + aa, right - aa, uv.x)
                    * smoothstep(bottom - aa, bottom + aa, uv.y) * smoothstep(bottom + bracketW + aa, bottom + bracketW - aa, uv.y));
                bracket = max(bracket, smoothstep(right - bracketW - aa, right - bracketW + aa, uv.x) * smoothstep(right + aa, right - aa, uv.x)
                    * smoothstep(bottom - aa, bottom + aa, uv.y) * smoothstep(bottom + bracketLen + aa, bottom + bracketLen - aa, uv.y));

                return saturate(bracket);
            }

            // Draw scan line
            float drawScanLine(float2 uv, float frameSize, float scanY, float lineWidth)
            {
                float2 center = float2(0.5, 0.5);
                float halfSize = frameSize * 0.5;
                float left = center.x - halfSize * 0.8;
                float right = center.x + halfSize * 0.8;

                // Map scanY (0-1) to frame vertical range
                float yPos = center.y - halfSize + scanY * frameSize;

                float aa = 0.003;
                float inX = smoothstep(left - aa, left + aa, uv.x) * smoothstep(right + aa, right - aa, uv.x);
                float inY = smoothstep(yPos - lineWidth - aa, yPos - lineWidth + aa, uv.y) * smoothstep(yPos + lineWidth + aa, yPos + lineWidth - aa, uv.y);

                return inX * inY;
            }

            // Check if inside scan frame area
            float insideScanFrame(float2 uv, float frameSize)
            {
                float2 center = float2(0.5, 0.5);
                float halfSize = frameSize * 0.5;
                float2 d = abs(uv - center);
                return step(d.x, halfSize) * step(d.y, halfSize);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float aspect = _Aspect;

                // Calculate SDF
                float sdf = sdRoundedBox(uv, aspect, _CornerRadius, _EdgePadding);

                // Anti-aliased edge for content area
                float pixelWidth = fwidth(sdf);
                float contentMask = smoothstep(pixelWidth, -pixelWidth, sdf);

                // Camera texture (only inside the rounded rect)
                fixed4 camColor = tex2D(_MainTex, uv);

                // Border with glow layers (matching RTTMenuFrame style)
                if (_BorderEnabled > 0.5)
                {
                    // Clip at max glow extent
                    float maxGlowWidth = _Layer4Width;
                    clip(maxGlowWidth - sdf - 0.001);

                    // Border color with gradient and shimmer
                    float3 borderColor = getGradientColor(uv, _GradientAngle);
                    borderColor += getShimmer(uv);

                    // Glow layers (matching GlowingGlassBorder style - additive blending)
                    float3 glowColor = float3(0, 0, 0);
                    float glowAlpha = 0.0;

                    // Layer 4 (outermost glow)
                    float l4 = 1.0 - saturate(sdf / _Layer4Width);
                    l4 = l4 * l4;
                    glowColor += borderColor * l4 * _Layer4Alpha * 0.5;
                    glowAlpha = max(glowAlpha, l4 * _Layer4Alpha * 0.3);

                    // Layer 3
                    float l3 = 1.0 - saturate(sdf / _Layer3Width);
                    l3 = l3 * l3;
                    glowColor += borderColor * l3 * _Layer3Alpha;
                    glowAlpha = max(glowAlpha, l3 * _Layer3Alpha * 0.5);

                    // Layer 2
                    float l2 = 1.0 - saturate(sdf / _Layer2Width);
                    l2 = l2 * l2;
                    glowColor = lerp(glowColor, borderColor * 1.2, l2 * _Layer2Alpha);
                    glowAlpha = max(glowAlpha, l2 * _Layer2Alpha);

                    // Layer 1 (innermost, brightest with white core)
                    float l1 = 1.0 - saturate(sdf / _Layer1Width);
                    l1 = l1 * l1;
                    float coreMix = l1 * _Layer1Alpha * 0.5;
                    float3 whiteCore = lerp(borderColor * 1.5, float3(1, 1, 1), 0.3);
                    glowColor = lerp(glowColor, whiteCore, coreMix);
                    glowAlpha = max(glowAlpha, l1 * _Layer1Alpha);

                    // Solid border edge
                    float borderEdge = smoothstep(_BorderWidth * 0.3, 0.0, abs(sdf));
                    glowColor = lerp(glowColor, whiteCore, borderEdge * 0.8);
                    glowAlpha = max(glowAlpha, borderEdge);

                    // Start with camera inside, glow outside
                    float3 finalColor = lerp(glowColor, camColor.rgb, contentMask);
                    float finalAlpha = lerp(glowAlpha, 1.0, contentMask);

                    // Add scan frame overlay if enabled (only inside content area)
                    if (_ScanFrameEnabled > 0.5 && contentMask > 0.5)
                    {
                        // Dark overlay outside scan frame area
                        float inFrame = insideScanFrame(uv, _ScanFrameSize);
                        float overlay = (1.0 - inFrame) * _OverlayAlpha;
                        finalColor = lerp(finalColor, float3(0, 0, 0), overlay);

                        // Draw corner brackets
                        float brackets = drawBrackets(uv, _ScanFrameSize, _BracketLength * _ScanFrameSize, _BracketWidth);
                        finalColor = lerp(finalColor, _ScanFrameColor.rgb, brackets);

                        // Draw scan line
                        float scanLine = drawScanLine(uv, _ScanFrameSize, _ScanLineY, 0.003);
                        finalColor = lerp(finalColor, _ScanLineColor.rgb, scanLine);
                    }

                    float4 result;
                    result.rgb = finalColor;
                    result.a = saturate(finalAlpha);

                    UNITY_APPLY_FOG(i.fogCoord, result);
                    return result;
                }
                else
                {
                    // No border, just camera with rounded corners
                    // clip() discards pixel if value < 0
                    clip(-sdf - 0.001);

                    fixed4 result = fixed4(camColor.rgb, contentMask);
                    UNITY_APPLY_FOG(i.fogCoord, result);
                    return result;
                }
            }
            ENDCG
        }
    }
}
