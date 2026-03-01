Shader "Custom/ClusterContentFlatPlanar"
{
    // Multi-texture content shader for flat planar cluster mesh
    // Samples from multiple panel textures, only renders on panel faces (not folds)
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

        [Header(Rounded Corners)]
        _CornerRadius ("Corner Radius (m)", Float) = 0.04
        _EdgePadding ("Edge Padding (m)", Float) = 0.009

        [Header(Streaming Quality)]
        _Sharpness ("Sharpness", Range(0, 2)) = 0.5
        _SharpnessRadius ("Sharpness Radius", Range(0.5, 3)) = 1.0
        _ChromaSharpness ("Chroma Sharpness", Range(0, 1)) = 0.3
        _EnableSharpening ("Enable Sharpening", Float) = 1

        // VR quality - negative bias for sharper textures at distance
        [Header(VR Quality)]
        _MipMapBias ("Mipmap Bias", Range(-2, 0)) = 0
        _MaxMipLevel ("Max Mip Level", Range(0, 4)) = 1.5
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
            Name "ClusterContentFlatPlanar"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;      // Global UV (0-1 across cluster)
                float2 uv2 : TEXCOORD1;     // Per-panel UV (0-1 within panel)
                float2 uv3 : TEXCOORD2;     // (localT, panelIndex) - panelIndex can be fractional for folds
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 globalUV : TEXCOORD0;
                float2 panelUV : TEXCOORD1;
                float2 panelInfo : TEXCOORD2;  // (localT, panelIndex)
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

            float _CornerRadius;
            float _EdgePadding;

            float _Sharpness;
            float _SharpnessRadius;
            float _ChromaSharpness;
            float _EnableSharpening;

            // VR quality - mipmap bias for sharper textures at distance
            float _MipMapBias;
            float _MaxMipLevel;

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

            // Unsharp Mask with LOD-clamped sampling and luminance-only sharpening
            float4 UnsharpMask(sampler2D tex, float2 uv, float2 uvDx, float2 uvDy,
                               float2 texelSize, float2 texSize,
                               float sharpness, float radius, float maxLod, float mipBias)
            {
                float4 center = SampleClampedLOD(tex, uv, uvDx, uvDy, texSize, maxLod, mipBias);

                float4 blur = (
                    SampleClampedLOD(tex, uv + float2(-texelSize.x, 0) * radius, uvDx, uvDy, texSize, maxLod, mipBias) +
                    SampleClampedLOD(tex, uv + float2( texelSize.x, 0) * radius, uvDx, uvDy, texSize, maxLod, mipBias) +
                    SampleClampedLOD(tex, uv + float2(0, -texelSize.y) * radius, uvDx, uvDy, texSize, maxLod, mipBias) +
                    SampleClampedLOD(tex, uv + float2(0,  texelSize.y) * radius, uvDx, uvDy, texSize, maxLod, mipBias)
                ) * 0.25;

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

            // Sample from specific panel texture with LOD-clamped sampling
            // uvDx/uvDy: pre-computed UV derivatives for smooth LOD calculation
            float4 SamplePanel(int panelIndex, float2 panelUV, float2 uvDx, float2 uvDy)
            {
                float4 color = float4(0, 0, 0, 1);
                float2 texelSize = float2(0.001, 0.001);
                float2 texSize = float2(1920, 1080);

                // Sample based on panel index with LOD-clamped sampling
                if (panelIndex == 0)
                {
                    texelSize = _Content0_TexelSize.xy;
                    texSize = _Content0_TexelSize.zw;
                    if (_EnableSharpening > 0.5)
                        color = UnsharpMask(_Content0, panelUV, uvDx, uvDy, texelSize, texSize, _Sharpness, _SharpnessRadius, _MaxMipLevel, _MipMapBias);
                    else
                        color = SampleClampedLOD(_Content0, panelUV, uvDx, uvDy, texSize, _MaxMipLevel, _MipMapBias);
                }
                else if (panelIndex == 1)
                {
                    texelSize = _Content1_TexelSize.xy;
                    texSize = _Content1_TexelSize.zw;
                    if (_EnableSharpening > 0.5)
                        color = UnsharpMask(_Content1, panelUV, uvDx, uvDy, texelSize, texSize, _Sharpness, _SharpnessRadius, _MaxMipLevel, _MipMapBias);
                    else
                        color = SampleClampedLOD(_Content1, panelUV, uvDx, uvDy, texSize, _MaxMipLevel, _MipMapBias);
                }
                else if (panelIndex == 2)
                {
                    texelSize = _Content2_TexelSize.xy;
                    texSize = _Content2_TexelSize.zw;
                    if (_EnableSharpening > 0.5)
                        color = UnsharpMask(_Content2, panelUV, uvDx, uvDy, texelSize, texSize, _Sharpness, _SharpnessRadius, _MaxMipLevel, _MipMapBias);
                    else
                        color = SampleClampedLOD(_Content2, panelUV, uvDx, uvDy, texSize, _MaxMipLevel, _MipMapBias);
                }
                else if (panelIndex == 3)
                {
                    texelSize = _Content3_TexelSize.xy;
                    texSize = _Content3_TexelSize.zw;
                    if (_EnableSharpening > 0.5)
                        color = UnsharpMask(_Content3, panelUV, uvDx, uvDy, texelSize, texSize, _Sharpness, _SharpnessRadius, _MaxMipLevel, _MipMapBias);
                    else
                        color = SampleClampedLOD(_Content3, panelUV, uvDx, uvDy, texSize, _MaxMipLevel, _MipMapBias);
                }
                else if (panelIndex == 4)
                {
                    texelSize = _Content4_TexelSize.xy;
                    texSize = _Content4_TexelSize.zw;
                    if (_EnableSharpening > 0.5)
                        color = UnsharpMask(_Content4, panelUV, uvDx, uvDy, texelSize, texSize, _Sharpness, _SharpnessRadius, _MaxMipLevel, _MipMapBias);
                    else
                        color = SampleClampedLOD(_Content4, panelUV, uvDx, uvDy, texSize, _MaxMipLevel, _MipMapBias);
                }
                else if (panelIndex == 5)
                {
                    texelSize = _Content5_TexelSize.xy;
                    texSize = _Content5_TexelSize.zw;
                    if (_EnableSharpening > 0.5)
                        color = UnsharpMask(_Content5, panelUV, uvDx, uvDy, texelSize, texSize, _Sharpness, _SharpnessRadius, _MaxMipLevel, _MipMapBias);
                    else
                        color = SampleClampedLOD(_Content5, panelUV, uvDx, uvDy, texSize, _MaxMipLevel, _MipMapBias);
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

                // Get panel index from vertex data
                // panelInfo.y contains the panel index (integer for panel faces, fractional for folds)
                float panelIndexFloat = i.panelInfo.y;

                // Check if this is a fold region (panel index is fractional)
                float fracPart = frac(panelIndexFloat);
                bool isFold = (fracPart > 0.01 && fracPart < 0.99);

                // For fold regions, make transparent (don't render content on folds)
                if (isFold)
                {
                    return fixed4(0, 0, 0, 0);
                }

                // Get integer panel index
                int panelIndex = clamp((int)round(panelIndexFloat), 0, _PanelCount - 1);

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

                // Use per-panel UV directly from mesh (panelUV = uv2)
                float2 panelUV = i.panelUV;

                // Compute UV derivatives from interpolated panelUV for smooth LOD
                float2 uvDx = ddx(panelUV);
                float2 uvDy = ddy(panelUV);

                // Sample content from the correct panel with LOD-clamped sampling
                float4 finalColor = SamplePanel(panelIndex, panelUV, uvDx, uvDy);
                finalColor.a *= alphaMask;

                return finalColor;
            }
            ENDCG
        }
    }

    FallBack "Unlit/Transparent"
}
