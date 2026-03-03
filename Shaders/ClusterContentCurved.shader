Shader "Custom/ClusterContentCurved"
{
    // Multi-texture content shader for curved cluster mesh
    // Samples from multiple panel textures with soft blending at boundaries
    // Includes video sharpening for streaming quality

    Properties
    {
        [Header(Content Textures)]
        _Content0 ("Panel 0 Texture", 2D) = "black" {}
        _Content1 ("Panel 1 Texture", 2D) = "black" {}
        _Content2 ("Panel 2 Texture", 2D) = "black" {}
        _Content3 ("Panel 3 Texture", 2D) = "black" {}
        _Content4 ("Panel 4 Texture", 2D) = "black" {}
        _Content5 ("Panel 5 Texture", 2D) = "black" {}

        [Header(Cluster Settings)]
        _PanelCount ("Panel Count", Int) = 3
        _ClusterWidth ("Cluster Width (m)", Float) = 4.8
        _ClusterHeight ("Cluster Height (m)", Float) = 0.9

        [Header(Blend Settings)]
        _BlendZone ("Blend Zone Width", Range(0.01, 0.2)) = 0.05

        [Header(Rounded Corners)]
        _CornerRadius ("Corner Radius (m)", Float) = 0.04
        _EdgePadding ("Edge Padding (m)", Float) = 0.009

        [Header(Content Margins)]
        _MarginH ("Horizontal Margin", Float) = 0.04
        _MarginV ("Vertical Margin", Float) = 0.045

        [Header(Streaming Quality)]
        _Sharpness ("Sharpness", Range(0, 2)) = 0.5
        _SharpnessRadius ("Sharpness Radius", Range(0.5, 3)) = 1.0
        _ChromaSharpness ("Chroma Sharpness", Range(0, 1)) = 0.3
        _EnableSharpening ("Enable Sharpening", Float) = 1

        // VR quality - negative bias for sharper textures at distance
        [Header(VR Quality)]
        _MipMapBias ("Mipmap Bias", Range(-2, 0)) = 0
        _MaxMipLevel ("Max Mip Level", Range(0, 4)) = 1.5
        // Stable AA: 0=legacy tex2Dgrad (trilinear, may shimmer), 1=stable 4-sample (VR recommended)
        _StableAA ("Stable AA (VR anti-shimmer)", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }

        Cull Back
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "ClusterContentCurved"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;      // Global UV (0-1 across cluster)
                float2 uv2 : TEXCOORD1;     // Per-panel UV
                float2 uv3 : TEXCOORD2;     // (panelFrac, panelIndex)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 globalUV : TEXCOORD0;
                float2 panelUV : TEXCOORD1;
                float2 panelInfo : TEXCOORD2;  // (panelFrac, panelIndex)
                float2 localPos : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _Content0;
            sampler2D _Content1;
            sampler2D _Content2;
            sampler2D _Content3;
            sampler2D _Content4;
            sampler2D _Content5;

            float4 _Content0_TexelSize;
            float4 _Content1_TexelSize;
            float4 _Content2_TexelSize;
            float4 _Content3_TexelSize;
            float4 _Content4_TexelSize;
            float4 _Content5_TexelSize;

            int _PanelCount;
            float _ClusterWidth;
            float _ClusterHeight;
            float _BlendZone;

            float _CornerRadius;
            float _EdgePadding;
            float _MarginH;
            float _MarginV;

            float _Sharpness;
            float _SharpnessRadius;
            float _ChromaSharpness;
            float _EnableSharpening;

            // VR quality - mipmap bias for sharper textures at distance
            float _MipMapBias;
            float _MaxMipLevel;
            float _StableAA;

            // SDF for rounded box
            float sdRoundedBox(float2 pos, float2 halfSize, float radius)
            {
                float2 q = abs(pos) - (halfSize - radius);
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
            }

            // Sample texture with LOD clamping using tex2Dgrad.
            // Preserves anisotropic filtering and works reliably on Android.
            float4 SampleClampedLOD(sampler2D tex, float2 uv, float2 uvDx, float2 uvDy, float2 texSize, float maxLod, float bias)
            {
                float2 dx = uvDx * texSize;
                float2 dy = uvDy * texSize;
                float rho = max(length(dx), length(dy));
                // max(rho, 1.0): when rho < 1 (magnification/close-up), keeps scale=1
                float lod = log2(max(rho, 1.0));
                float targetLod = clamp(lod + bias, 0.0, maxLod);
                float scale = exp2(targetLod - lod);
                return tex2Dgrad(tex, uv, uvDx * scale, uvDy * scale);
            }

            // ==========================================
            // STABLE AA: VR anti-shimmer sampling
            // Floor LOD to integer + 4-sample rotated grid
            // ==========================================

            float ComputeStableMip(float2 uvDx, float2 uvDy, float2 texSize, float maxLod, float bias)
            {
                float2 dx = uvDx * texSize;
                float2 dy = uvDy * texSize;
                float minAxis = min(length(dx), length(dy));
                float lod = log2(max(minAxis, 1.0));
                float targetLod = clamp(lod + bias, 0.0, maxLod);
                return floor(targetLod);
            }

            float4 SampleStableLOD(sampler2D tex, float2 uv, float mipLevel)
            {
                return tex2Dlod(tex, float4(uv, 0, mipLevel));
            }

            float4 SampleStableAA(sampler2D tex, float2 uv, float2 uvDx, float2 uvDy, float mipLevel)
            {
                float2 sDx = uvDx * 0.125;
                float2 sDy = uvDy * 0.125;
                float4 s1 = tex2Dlod(tex, float4(uv + sDx + sDy, 0, mipLevel));
                float4 s2 = tex2Dlod(tex, float4(uv - sDx + sDy, 0, mipLevel));
                float4 s3 = tex2Dlod(tex, float4(uv + sDx - sDy, 0, mipLevel));
                float4 s4 = tex2Dlod(tex, float4(uv - sDx - sDy, 0, mipLevel));
                return (s1 + s2 + s3 + s4) * 0.25;
            }

            float4 SampleTexture(sampler2D tex, float2 uv, float2 uvDx, float2 uvDy,
                                 float2 texSize, float maxLod, float bias, float stableAA)
            {
                if (stableAA > 0.5)
                {
                    float mip = ComputeStableMip(uvDx, uvDy, texSize, maxLod, bias);
                    return SampleStableAA(tex, uv, uvDx, uvDy, mip);
                }
                else
                {
                    return SampleClampedLOD(tex, uv, uvDx, uvDy, texSize, maxLod, bias);
                }
            }

            // Unsharp Mask with LOD-clamped sampling and luminance-only sharpening
            // Supports both legacy (tex2Dgrad) and stable AA (tex2Dlod) modes
            float4 UnsharpMask(sampler2D tex, float2 uv, float2 uvDx, float2 uvDy,
                               float2 texelSize, float2 texSize,
                               float sharpness, float radius, float maxLod, float mipBias)
            {
                float4 center;
                float4 blur;

                if (_StableAA > 0.5)
                {
                    float mip = ComputeStableMip(uvDx, uvDy, texSize, maxLod, mipBias);
                    center = SampleStableAA(tex, uv, uvDx, uvDy, mip);
                    blur = (
                        SampleStableLOD(tex, uv + float2(-texelSize.x, 0) * radius, mip) +
                        SampleStableLOD(tex, uv + float2( texelSize.x, 0) * radius, mip) +
                        SampleStableLOD(tex, uv + float2(0, -texelSize.y) * radius, mip) +
                        SampleStableLOD(tex, uv + float2(0,  texelSize.y) * radius, mip)
                    ) * 0.25;
                }
                else
                {
                    center = SampleClampedLOD(tex, uv, uvDx, uvDy, texSize, maxLod, mipBias);
                    blur = (
                        SampleClampedLOD(tex, uv + float2(-texelSize.x, 0) * radius, uvDx, uvDy, texSize, maxLod, mipBias) +
                        SampleClampedLOD(tex, uv + float2( texelSize.x, 0) * radius, uvDx, uvDy, texSize, maxLod, mipBias) +
                        SampleClampedLOD(tex, uv + float2(0, -texelSize.y) * radius, uvDx, uvDy, texSize, maxLod, mipBias) +
                        SampleClampedLOD(tex, uv + float2(0,  texelSize.y) * radius, uvDx, uvDy, texSize, maxLod, mipBias)
                    ) * 0.25;
                }

                float centerLuma = dot(center.rgb, float3(0.299, 0.587, 0.114));
                float blurLuma = dot(blur.rgb, float3(0.299, 0.587, 0.114));
                float lumaDetail = (centerLuma - blurLuma) * sharpness;

                float4 sharpened = center;
                sharpened.rgb += lumaDetail;
                return saturate(sharpened);
            }

            // Chroma correction for YUV 4:2:0
            float4 ChromaCorrect(float4 color, float chromaSharpness)
            {
                float luma = dot(color.rgb, float3(0.299, 0.587, 0.114));
                float3 chromaDiff = color.rgb - float3(luma, luma, luma);
                color.rgb = luma + chromaDiff * (1.0 + chromaSharpness);
                return saturate(color);
            }

            // Sample from specific panel texture with stable or legacy sampling
            // uvDx/uvDy: pre-computed UV derivatives from interpolated panelUV (smooth, no discontinuity)
            float4 SamplePanel(int panelIndex, float2 panelUV, float2 uvDx, float2 uvDy)
            {
                float4 color = float4(0, 0, 0, 1);
                float2 texelSize = float2(0.001, 0.001);
                float2 texSize = float2(1920, 1080);

                // Sample based on panel index with stable or legacy sampling
                if (panelIndex == 0)
                {
                    texelSize = _Content0_TexelSize.xy;
                    texSize = _Content0_TexelSize.zw;
                    if (_EnableSharpening > 0.5)
                        color = UnsharpMask(_Content0, panelUV, uvDx, uvDy, texelSize, texSize, _Sharpness, _SharpnessRadius, _MaxMipLevel, _MipMapBias);
                    else
                        color = SampleTexture(_Content0, panelUV, uvDx, uvDy, texSize, _MaxMipLevel, _MipMapBias, _StableAA);
                }
                else if (panelIndex == 1)
                {
                    texelSize = _Content1_TexelSize.xy;
                    texSize = _Content1_TexelSize.zw;
                    if (_EnableSharpening > 0.5)
                        color = UnsharpMask(_Content1, panelUV, uvDx, uvDy, texelSize, texSize, _Sharpness, _SharpnessRadius, _MaxMipLevel, _MipMapBias);
                    else
                        color = SampleTexture(_Content1, panelUV, uvDx, uvDy, texSize, _MaxMipLevel, _MipMapBias, _StableAA);
                }
                else if (panelIndex == 2)
                {
                    texelSize = _Content2_TexelSize.xy;
                    texSize = _Content2_TexelSize.zw;
                    if (_EnableSharpening > 0.5)
                        color = UnsharpMask(_Content2, panelUV, uvDx, uvDy, texelSize, texSize, _Sharpness, _SharpnessRadius, _MaxMipLevel, _MipMapBias);
                    else
                        color = SampleTexture(_Content2, panelUV, uvDx, uvDy, texSize, _MaxMipLevel, _MipMapBias, _StableAA);
                }
                else if (panelIndex == 3)
                {
                    texelSize = _Content3_TexelSize.xy;
                    texSize = _Content3_TexelSize.zw;
                    if (_EnableSharpening > 0.5)
                        color = UnsharpMask(_Content3, panelUV, uvDx, uvDy, texelSize, texSize, _Sharpness, _SharpnessRadius, _MaxMipLevel, _MipMapBias);
                    else
                        color = SampleTexture(_Content3, panelUV, uvDx, uvDy, texSize, _MaxMipLevel, _MipMapBias, _StableAA);
                }
                else if (panelIndex == 4)
                {
                    texelSize = _Content4_TexelSize.xy;
                    texSize = _Content4_TexelSize.zw;
                    if (_EnableSharpening > 0.5)
                        color = UnsharpMask(_Content4, panelUV, uvDx, uvDy, texelSize, texSize, _Sharpness, _SharpnessRadius, _MaxMipLevel, _MipMapBias);
                    else
                        color = SampleTexture(_Content4, panelUV, uvDx, uvDy, texSize, _MaxMipLevel, _MipMapBias, _StableAA);
                }
                else if (panelIndex == 5)
                {
                    texelSize = _Content5_TexelSize.xy;
                    texSize = _Content5_TexelSize.zw;
                    if (_EnableSharpening > 0.5)
                        color = UnsharpMask(_Content5, panelUV, uvDx, uvDy, texelSize, texSize, _Sharpness, _SharpnessRadius, _MaxMipLevel, _MipMapBias);
                    else
                        color = SampleTexture(_Content5, panelUV, uvDx, uvDy, texSize, _MaxMipLevel, _MipMapBias, _StableAA);
                }

                // Apply chroma correction
                if (_EnableSharpening > 0.5)
                {
                    color = ChromaCorrect(color, _ChromaSharpness);
                }

                return color;
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.globalUV = v.uv;
                o.panelUV = v.uv2;
                o.panelInfo = v.uv3;

                // Calculate local position in cluster space (meters)
                o.localPos = (v.uv - 0.5) * float2(_ClusterWidth, _ClusterHeight);

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 localPos = i.localPos;

                // IMPORTANT: Calculate panelIndex and panelFrac from globalUV instead of using
                // interpolated values from vertex shader. GPU interpolation of panelIndex causes
                // incorrect panel assignment at boundaries (e.g., 0.5 between panel 0 and 1).
                float panelIndexFloat = i.globalUV.x * _PanelCount;
                int panelIndex = clamp((int)floor(panelIndexFloat), 0, _PanelCount - 1);
                float panelFrac = panelIndexFloat - float(panelIndex);

                // Handle edge case at UV = 1.0
                if (i.globalUV.x >= 0.9999)
                {
                    panelIndex = _PanelCount - 1;
                    panelFrac = 1.0;
                }

                // === ROUNDED CORNERS (outer only) ===
                float2 halfSize = float2(_ClusterWidth, _ClusterHeight) * 0.5;
                halfSize -= _EdgePadding;

                float dist = sdRoundedBox(localPos, halfSize, _CornerRadius);
                float edgeWidth = 0.002;
                float alphaMask = 1.0 - smoothstep(-edgeWidth, edgeWidth, dist);

                if (alphaMask <= 0.001)
                {
                    return fixed4(0, 0, 0, 0);
                }

                // NOTE: Content margin clipping is NOT applied here because
                // the mesh is already sized using boardWidth (panelWidth minus margins).
                // Margins are handled by the mesh geometry itself.

                // Build panelUV from calculated values (not interpolated from vertex shader)
                float2 panelUV = float2(panelFrac, i.globalUV.y);

                // Use interpolated panelUV (TEXCOORD1) for smooth derivatives
                // Avoids discontinuity from recomputed panelFrac at panel boundaries
                float2 uvDx = ddx(i.panelUV);
                float2 uvDy = ddy(i.panelUV);

                // === BLEND ZONE CALCULATION ===
                // panelFrac goes 0-1 within each panel
                // Blend at boundaries (near 0 and near 1)
                float4 finalColor;

                if (panelFrac < _BlendZone && panelIndex > 0)
                {
                    // Left edge - blend with previous panel
                    float blend = panelFrac / _BlendZone;
                    blend = smoothstep(0.0, 1.0, blend);

                    float4 leftColor = SamplePanel(panelIndex - 1, float2(1.0, panelUV.y), uvDx, uvDy);
                    float4 rightColor = SamplePanel(panelIndex, panelUV, uvDx, uvDy);

                    finalColor = lerp(leftColor, rightColor, blend);
                }
                else if (panelFrac > (1.0 - _BlendZone) && panelIndex < _PanelCount - 1)
                {
                    // Right edge - blend with next panel
                    float blend = (panelFrac - (1.0 - _BlendZone)) / _BlendZone;
                    blend = smoothstep(0.0, 1.0, blend);

                    float4 leftColor = SamplePanel(panelIndex, panelUV, uvDx, uvDy);
                    float4 rightColor = SamplePanel(panelIndex + 1, float2(0.0, panelUV.y), uvDx, uvDy);

                    finalColor = lerp(leftColor, rightColor, blend);
                }
                else
                {
                    // Center - just sample current panel
                    finalColor = SamplePanel(panelIndex, panelUV, uvDx, uvDy);
                }

                finalColor.a *= alphaMask;

                return finalColor;
            }
            ENDCG
        }
    }

    FallBack "Unlit/Transparent"
}
