Shader "Unlit/WorldPanelBoard"
{
    Properties
    {
        _MainTex       ("Texture", 2D) = "white" {}
        _Color         ("Tint", Color) = (1,1,1,1)

        // Kích thước panel theo mét (được set từ WorldPanelPlus)
        _PanelSize     ("Panel Size (W,H)", Vector) = (1,1,0,0)

        // Fade mép (theo mét) — giữ lại hành vi cũ
        _EdgeFadeX     ("Edge Fade X (m)", Float) = 0 // 0.03
        _EdgeFadeY     ("Edge Fade Y (m)", Float) = 0 //0.05
        _EdgeMinAlpha  ("Edge Min Alpha", Range(0,1)) = 0 // 0.40

        // NEW: Bo góc (theo mét) + feather của mép
        _CornerRadius  ("Corner Radius (m)", Float) = 0.03
        _EdgeFeather   ("Edge Feather (m)", Float) = 0.001

        // Content bounds clipping (UV space: left, right, bottom, top)
        _ContentBounds ("Content Bounds (L,R,B,T)", Vector) = (0,1,0,1)
        _EnableClipping ("Enable Clipping", Float) = 0

        // Edge mask for cluster panels (Left, Right, Top, Bottom): 1=show corner, 0=no corner
        _EdgeMask ("Edge Mask (L,R,T,B)", Vector) = (1,1,1,1)

        // Video picture adjustments (for Media Player)
        [Header(Picture Adjustments)]
        _Brightness ("Brightness", Range(0, 2)) = 1
        _Contrast ("Contrast", Range(0, 2)) = 1
        _Saturation ("Saturation", Range(0, 2)) = 1
        _Tint ("Tint", Range(-1, 1)) = 0
        _Temperature ("Temperature", Range(-1, 1)) = 0
        _LRInverse ("LR Inverse", Float) = 0

        // Video streaming quality enhancement
        // NOTE: Sharpening disabled by default - can cause artifacts on some devices
        [Header(Streaming Quality)]
        _Sharpness ("Sharpness", Range(0, 2)) = 1.0
        _SharpnessRadius ("Sharpness Radius", Range(0.5, 3)) = 1.0
        _ChromaSharpness ("Chroma Sharpness", Range(0, 1)) = 0.3
        _EnableSharpening ("Enable Sharpening", Float) = 1

        // VR quality - negative bias for sharper textures at distance
        [Header(VR Quality)]
        _MipMapBias ("Mipmap Bias", Range(-2, 0)) = -0.7
        _MaxMipLevel ("Max Mip Level", Range(0, 4)) = 1.5
        // Stable AA: 0=legacy tex2Dgrad (trilinear, may shimmer), 1=stable 4-sample (VR recommended)
        _StableAA ("Stable AA (VR anti-shimmer)", Float) = 1
        // Shimmer Blend: controlled partial trilinear between adjacent mip levels.
        // 0 = pure integer mip (sharp but may shimmer), 0.5 = full trilinear (no shimmer but blurry)
        // 0.2 = recommended: eliminates shimmer while keeping ~80% of mip-0 sharpness
        _ShimmerBlend ("Shimmer Blend (anti-noise)", Range(0, 0.5)) = 0.2
        // Temporal Smooth: suppress video encoder micro-noise on static content.
        // 0 = off, 0.5 = recommended (kills banding without losing detail), 1 = max
        _TemporalSmooth ("Temporal Smooth (anti-banding)", Range(0, 1)) = 0.5

        [Header(Stereo)]
        _StereoMode ("Stereo Mode", Float) = 0  // 0=Mono, 1=SBS, 2=OU
        _EyeIndex ("Eye Index", Float) = 0      // 0=Left, 1=Right
        _StereoStrength ("Stereo Strength", Range(0, 1)) = 1  // 1=full 3D, 0=mono
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }

        Cull Back
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4    _MainTex_ST;
            float4    _MainTex_TexelSize; // (1/width, 1/height, width, height)
            float4    _Color;

            // Picture adjustments
            float _Brightness;
            float _Contrast;
            float _Saturation;
            float _Tint;
            float _Temperature;
            float _LRInverse;

            // Sharpening parameters
            float _Sharpness;
            float _SharpnessRadius;
            float _ChromaSharpness;
            float _EnableSharpening;

            // VR quality - mipmap bias for sharper textures at distance
            float _MipMapBias;
            float _MaxMipLevel;
            float _StableAA;
            float _ShimmerBlend;
            float _TemporalSmooth;

            float4 _PanelSize;   // (W,H,0,0)
            float   _EdgeFadeX;
            float   _EdgeFadeY;
            float   _EdgeMinAlpha;

            float   _CornerRadius;
            float   _EdgeFeather;

            float4  _ContentBounds; // (left, right, bottom, top) in UV space
            float   _EnableClipping;

            float4  _EdgeMask; // (Left, Right, Top, Bottom): 1=show corner, 0=no corner

            // Stereo properties
            float _StereoMode;
            float _EyeIndex;
            float _StereoStrength;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            
            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            // Get stereo UV based on mode and eye
            // eyeIndex supports continuous values [0,1] for smooth mono↔stereo transitions
            // 0.0 = left eye, 1.0 = right eye, intermediate = blend between halves
            float2 GetStereoUV(float2 uv, float stereoMode, float eyeIndex)
            {
                if (stereoMode < 0.5)
                {
                    // Mono - no modification
                    return uv;
                }
                else if (stereoMode < 1.5)
                {
                    // Side-by-Side: smooth blend between left half and right half
                    return float2(uv.x * 0.5 + eyeIndex * 0.5, uv.y);
                }
                else
                {
                    // Over-Under: smooth blend between top half and bottom half
                    return float2(uv.x, uv.y * 0.5 + (1.0 - eyeIndex) * 0.5);
                }
            }

            // Tính alpha fade viền theo mét quanh cạnh trái/phải & trên/dưới
            float EdgeFade(float2 pMeters, float2 halfSize, float fadeX, float fadeY, float minA)
            {
                // khoảng cách (m) từ tâm theo trục X/Y
                float ax = abs(pMeters.x);
                float ay = abs(pMeters.y);

                // khoảng cách còn lại tới biên (m)
                float rx = halfSize.x - ax;
                float ry = halfSize.y - ay;

                // 0 ở trong, →1 khi sát biên (trong dải fade)
                float ex = saturate(1.0 - saturate(rx / max(1e-6, fadeX)));
                float ey = saturate(1.0 - saturate(ry / max(1e-6, fadeY)));

                // lấy mức fade lớn hơn theo 2 chiều
                float e = max(ex, ey);

                // nội suy alpha về minA tại mép
                return lerp(1.0, minA, e);
            }

            // SDF cho rounded-rect theo mét với EdgeMask support
            // pMeters: toạ độ từ tâm (m), halfSize: nửa kích thước (m), r: bán kính (m)
            // edgeMask: (Left, Right, Top, Bottom) - 1=show corner, 0=no corner
            float RoundRectSDF(float2 pMeters, float2 halfSize, float r, float4 edgeMask)
            {
                // Determine which corner we're in based on position
                // Use <= and >= to avoid gaps at center lines (x=0 or y=0)
                // TopLeft: x<=0, y>=0 -> needs Left(x) AND Top(z) visible
                // TopRight: x>=0, y>=0 -> needs Right(y) AND Top(z) visible
                // BottomLeft: x<=0, y<=0 -> needs Left(x) AND Bottom(w) visible
                // BottomRight: x>=0, y<=0 -> needs Right(y) AND Bottom(w) visible

                // Default corner radius for interior pixels (far from corners)
                float cornerRadius = r;

                // Only modify corner radius when actually near a corner
                // Distance threshold: only apply corner logic when close to edges
                float edgeThresholdX = halfSize.x - r * 2.0;
                float edgeThresholdY = halfSize.y - r * 2.0;
                bool nearCorner = abs(pMeters.x) > edgeThresholdX && abs(pMeters.y) > edgeThresholdY;

                if (nearCorner)
                {
                    // Check which quadrant we're in and apply appropriate corner radius
                    if (pMeters.x <= 0 && pMeters.y >= 0) // Top-Left
                    {
                        cornerRadius = (edgeMask.x > 0.5 && edgeMask.z > 0.5) ? r : 0.0;
                    }
                    else if (pMeters.x >= 0 && pMeters.y >= 0) // Top-Right
                    {
                        cornerRadius = (edgeMask.y > 0.5 && edgeMask.z > 0.5) ? r : 0.0;
                    }
                    else if (pMeters.x <= 0 && pMeters.y <= 0) // Bottom-Left
                    {
                        cornerRadius = (edgeMask.x > 0.5 && edgeMask.w > 0.5) ? r : 0.0;
                    }
                    else if (pMeters.x >= 0 && pMeters.y <= 0) // Bottom-Right
                    {
                        cornerRadius = (edgeMask.y > 0.5 && edgeMask.w > 0.5) ? r : 0.0;
                    }
                }

                // co lại nửa kích thước để chừa chỗ cho bán kính
                float2 q = abs(pMeters) - (halfSize - cornerRadius);
                // length(max(q,0)) - r  : >0 ngoài bo, <0 trong bo
                return length(max(q, 0.0)) - cornerRadius;
            }

            // ==========================================
            // LEGACY: tex2Dgrad sampling (trilinear + aniso)
            // May shimmer in VR due to LOD blend ratio fluctuation.
            // Kept as fallback when _StableAA = 0.
            // ==========================================
            float4 SampleClampedLOD(sampler2D tex, float2 uv, float2 uvDx, float2 uvDy, float2 texSize, float maxLod, float bias)
            {
                float2 dx = uvDx * texSize;
                float2 dy = uvDy * texSize;
                float rho = max(length(dx), length(dy));
                float lod = log2(max(rho, 1.0));
                float targetLod = clamp(lod + bias, 0.0, maxLod);
                float scale = exp2(targetLod - lod);
                return tex2Dgrad(tex, uv, uvDx * scale, uvDy * scale);
            }

            // ==========================================
            // STABLE AA: VR anti-shimmer sampling
            //
            // Fixes two problems with the legacy approach:
            //
            // 1. SHIMMER: Trilinear blending between mip levels means the blend
            //    ratio (e.g., 70% mip0 + 30% mip1) changes every micro-frame as
            //    the head moves in VR. This causes visible temporal flickering.
            //    FIX: Floor LOD to integer → always sample from ONE mip level.
            //
            // 2. EDGE BLUR: Using max(|dx|, |dy|) for LOD selects based on the
            //    most stretched axis, over-blurring the minor axis.
            //    FIX: Use min(|dx|, |dy|) for LOD, let the 4-sample pattern
            //    cover the pixel footprint along both axes.
            //
            // Additionally, offset multiplier reduced from 0.25 to 0.125 to
            // dampen derivative jitter from VR head tracking (less shimmer).
            //
            // Cost: 4 tex2Dlod per sample (vs 1 tex2Dgrad), but tex2Dlod is
            // cheaper per-call on mobile (no derivative calculation needed).
            // ==========================================

            // Compute stable mip level: min-axis LOD, rounded to nearest integer.
            // Returns TWO values via out parameter:
            //   baseMip  = primary mip level (integer, for sharp 4-sample reads)
            //   blendMip = adjacent mip level for controlled anti-shimmer blending
            // The caller uses _ShimmerBlend to lerp between them.
            float ComputeStableMip(float2 uvDx, float2 uvDy, float2 texSize, float maxLod, float bias,
                                   out float blendMip)
            {
                float2 dx = uvDx * texSize;
                float2 dy = uvDy * texSize;

                // Use MINOR axis for LOD (aniso-friendly)
                // This prevents over-blurring when the Quad is viewed at an angle.
                // The 4-sample pattern handles the major axis coverage.
                float minAxis = min(length(dx), length(dy));

                float lod = log2(max(minAxis, 1.0));
                float targetLod = clamp(lod + bias, 0.0, maxLod);

                // Round to nearest integer mip (instead of floor).
                // This snaps to the CLOSEST mip level, reducing over-sharpness
                // at the transition boundary compared to always flooring down.
                float baseMip = floor(targetLod + 0.5); // round()
                baseMip = clamp(baseMip, 0.0, maxLod);

                // Blend target: the adjacent mip in the direction of the fractional LOD.
                // If targetLod > baseMip → blend with baseMip+1 (blur direction)
                // If targetLod < baseMip → blend with baseMip-1 (sharp direction)
                float frac_lod = targetLod - baseMip;
                blendMip = clamp(baseMip + sign(frac_lod + 0.001), 0.0, maxLod);

                return baseMip;
            }

            // Single sample at stable mip level (for blur taps in unsharp mask)
            float4 SampleStableLOD(sampler2D tex, float2 uv, float mipLevel)
            {
                return tex2Dlod(tex, float4(uv, 0, mipLevel));
            }

            // 4-sample anti-aliased read with controlled shimmer blend.
            //
            // The base mip is sampled with a tight 4-sample rotated grid for spatial AA.
            // Then a single sample from the adjacent mip is blended in at _ShimmerBlend
            // ratio to smooth temporal aliasing (shimmer) without destroying sharpness.
            //
            // shimmerBlend=0.0 → pure integer mip (sharp, may shimmer)
            // shimmerBlend=0.2 → 80% base + 20% adjacent (recommended: sharp + stable)
            // shimmerBlend=0.5 → 50/50 blend (very stable but softer)
            float4 SampleStableAA(sampler2D tex, float2 uv, float2 uvDx, float2 uvDy,
                                  float baseMip, float blendMip, float shimmerBlend)
            {
                // Sub-pixel offsets (0.125 = 1/8 pixel) sized to average out
                // DCT block-boundary artifacts from video compression while
                // still providing spatial anti-aliasing.
                float2 sDx = uvDx * 0.125;
                float2 sDy = uvDy * 0.125;

                // 4-sample rotated grid at base mip level (primary sharp read)
                float4 s1 = tex2Dlod(tex, float4(uv + sDx + sDy, 0, baseMip));
                float4 s2 = tex2Dlod(tex, float4(uv - sDx + sDy, 0, baseMip));
                float4 s3 = tex2Dlod(tex, float4(uv + sDx - sDy, 0, baseMip));
                float4 s4 = tex2Dlod(tex, float4(uv - sDx - sDy, 0, baseMip));
                float4 baseColor = (s1 + s2 + s3 + s4) * 0.25;

                // Single sample from adjacent mip for temporal smoothing
                // (cheaper than 4-sample: the blend mip is inherently smoother)
                float4 blendColor = tex2Dlod(tex, float4(uv, 0, blendMip));

                // Controlled blend: just enough to kill shimmer, preserve sharpness
                return lerp(baseColor, blendColor, shimmerBlend);
            }

            // Unified sampling: choose legacy or stable AA based on toggle
            float4 SampleTexture(sampler2D tex, float2 uv, float2 uvDx, float2 uvDy,
                                 float2 texSize, float maxLod, float bias, float stableAA)
            {
                if (stableAA > 0.5)
                {
                    float blendMip;
                    float baseMip = ComputeStableMip(uvDx, uvDy, texSize, maxLod, bias, blendMip);
                    return SampleStableAA(tex, uv, uvDx, uvDy, baseMip, blendMip, _ShimmerBlend);
                }
                else
                {
                    return SampleClampedLOD(tex, uv, uvDx, uvDy, texSize, maxLod, bias);
                }
            }

            // ==========================================
            // TEMPORAL SMOOTH: suppress encoder micro-noise
            //
            // Video encoders (H264/H265) produce slightly different quantization
            // noise each frame even when the source desktop is static. This
            // causes visible flicker/banding on dark regions in VR.
            //
            // Two techniques:
            // 1. Color quantization: round colors to coarser steps (e.g., 64 levels
            //    instead of 256) to collapse micro-variations into stable values.
            // 2. Ordered dithering: add a screen-space Bayer pattern offset before
            //    quantization so that banding breaks up into imperceptible noise.
            // ==========================================

            // 4x4 Bayer dither matrix (normalized to 0..1)
            static const float BayerMatrix[16] = {
                 0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0,
                12.0/16.0,  4.0/16.0, 14.0/16.0,  6.0/16.0,
                 3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0,
                15.0/16.0,  7.0/16.0, 13.0/16.0,  5.0/16.0
            };

            // Apply temporal smoothing + ordered dithering to suppress encoder noise.
            // strength: 0 = off, 0.5 = recommended, 1 = maximum smoothing
            float4 TemporalSmooth(float4 color, float2 screenPos, float strength)
            {
                if (strength < 0.001) return color;

                // Number of quantization levels: fewer = more stable but coarser.
                // At strength=0.5: ~128 levels (noise < 0.4% invisible in VR)
                // At strength=1.0: ~64 levels  (noise < 0.8% still imperceptible)
                float levels = lerp(256.0, 64.0, strength);
                float invLevels = 1.0 / levels;

                // Screen-space Bayer dither offset
                int2 sp = int2(fmod(abs(screenPos), 4.0));
                float dither = BayerMatrix[sp.y * 4 + sp.x] - 0.5; // center around 0
                float ditherScale = invLevels * strength; // scale dither by step size

                // Quantize with dither: add dither before rounding, then round
                float3 c = color.rgb + dither * ditherScale;
                c = floor(c * levels + 0.5) * invLevels;

                return float4(saturate(c), color.a);
            }

            // Unsharp Mask sharpening.
            // Uses luminance-only sharpening to prevent color fringing.
            // Supports both legacy (tex2Dgrad) and stable AA (tex2Dlod) modes.
            float4 UnsharpMask(sampler2D tex, float2 uv, float2 uvDx, float2 uvDy,
                               float2 texelSize, float2 texSize,
                               float sharpness, float radius, float maxLod, float mipBias,
                               float stableAA)
            {
                float4 center;
                float4 blur;

                if (stableAA > 0.5)
                {
                    // Stable AA path: 4-sample center + shimmer blend + 4 single-sample blur taps
                    float blendMip;
                    float baseMip = ComputeStableMip(uvDx, uvDy, texSize, maxLod, mipBias, blendMip);
                    center = SampleStableAA(tex, uv, uvDx, uvDy, baseMip, blendMip, _ShimmerBlend);
                    blur = (
                        SampleStableLOD(tex, uv + float2(-texelSize.x, 0) * radius, baseMip) +
                        SampleStableLOD(tex, uv + float2( texelSize.x, 0) * radius, baseMip) +
                        SampleStableLOD(tex, uv + float2(0, -texelSize.y) * radius, baseMip) +
                        SampleStableLOD(tex, uv + float2(0,  texelSize.y) * radius, baseMip)
                    ) * 0.25;
                }
                else
                {
                    // Legacy path: 5 tex2Dgrad reads
                    center = SampleClampedLOD(tex, uv, uvDx, uvDy, texSize, maxLod, mipBias);
                    blur = (
                        SampleClampedLOD(tex, uv + float2(-texelSize.x, 0) * radius, uvDx, uvDy, texSize, maxLod, mipBias) +
                        SampleClampedLOD(tex, uv + float2( texelSize.x, 0) * radius, uvDx, uvDy, texSize, maxLod, mipBias) +
                        SampleClampedLOD(tex, uv + float2(0, -texelSize.y) * radius, uvDx, uvDy, texSize, maxLod, mipBias) +
                        SampleClampedLOD(tex, uv + float2(0,  texelSize.y) * radius, uvDx, uvDy, texSize, maxLod, mipBias)
                    ) * 0.25;
                }

                // Luminance-only sharpening: prevents color fringing artifacts
                float centerLuma = dot(center.rgb, float3(0.299, 0.587, 0.114));
                float blurLuma = dot(blur.rgb, float3(0.299, 0.587, 0.114));
                float lumaDetail = (centerLuma - blurLuma) * sharpness;

                float4 sharpened = center;
                sharpened.rgb += lumaDetail;

                return saturate(sharpened);
            }

            // Color correction for video playback
            float3 VideoColorCorrect(float3 color, float brightness, float contrast, float saturation)
            {
                color *= brightness;
                color = (color - 0.5) * contrast + 0.5;
                float luma = dot(color, float3(0.299, 0.587, 0.114));
                color = lerp(float3(luma, luma, luma), color, saturation);
                return saturate(color);
            }

            // Tint and temperature adjustment
            float3 ApplyTintTemperature(float3 color, float tint, float temperature)
            {
                color.r += temperature * 0.1;
                color.b -= temperature * 0.1;
                color.g += tint * 0.1;
                return saturate(color);
            }

            // Chroma correction to reduce YUV 4:2:0 color bleeding on text edges
            float4 ChromaCorrect(float4 color, float chromaSharpness)
            {
                // Calculate luma (perceived brightness)
                float luma = dot(color.rgb, float3(0.299, 0.587, 0.114));

                // Extract chroma (color difference from gray)
                float3 chromaDiff = color.rgb - float3(luma, luma, luma);

                // Boost chroma to counteract 4:2:0 subsampling blur
                color.rgb = luma + chromaDiff * (1.0 + chromaSharpness);

                return saturate(color);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // ---- Content bounds clipping (UV space) ----
                if (_EnableClipping > 0.5)
                {
                    // _ContentBounds = (left, right, bottom, top)
                    float left = _ContentBounds.x;
                    float right = _ContentBounds.y;
                    float bottom = _ContentBounds.z;
                    float top = _ContentBounds.w;

                    // Clip pixels outside content bounds
                    if (i.uv.x < left || i.uv.x > right || i.uv.y < bottom || i.uv.y > top)
                    {
                        clip(-1);
                        return fixed4(0,0,0,0);
                    }
                }

                // ---- Tính toạ độ theo mét (gốc giữa panel) ----
                float2 size     = _PanelSize.xy;
                float2 halfSize = 0.5 * size;

                // UV 0..1  ->  (-W/2..+W/2, -H/2..+H/2) mét
                float2 pMeters = (i.uv - 0.5) * size;

                // ---- Rounded-rect clip + feather with EdgeMask and Edge-only Anti-aliasing ----
                float r   = max(0.0, _CornerRadius);
                float sdf = RoundRectSDF(pMeters, halfSize, r, _EdgeMask);

                // Edge-only anti-aliasing: only smooth at the boundary, keep interior fully opaque
                float aaWidth = fwidth(sdf);

                // aRound = 1 inside, smooth transition to 0 at edge, 0 outside
                // Only the narrow edge band gets AA, interior stays at full alpha
                float aRound = 1.0 - smoothstep(-aaWidth, aaWidth, sdf);

                // Hard clip pixels that are fully outside
                clip(aRound - 0.001);

                // ---- Lấy màu texture với sharpening (nếu enabled) ----
                // Apply Stereo UV before sampling
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float eye = unity_StereoEyeIndex * _StereoStrength;
                // LR Inverse: swap left/right eye
                if (_LRInverse > 0.5) eye = 1.0 - eye;
                float2 stereoUV = GetStereoUV(i.uv, _StereoMode, eye);

                // Compute UV derivatives once for LOD-clamped sampling
                float2 uvDx = ddx(stereoUV);
                float2 uvDy = ddy(stereoUV);

                fixed4 col;
                if (_EnableSharpening > 0.5)
                {
                    // Apply luminance-only Unsharp Mask with stable or legacy sampling
                    col = UnsharpMask(_MainTex, stereoUV, uvDx, uvDy,
                                     _MainTex_TexelSize.xy, _MainTex_TexelSize.zw,
                                     _Sharpness, _SharpnessRadius, _MaxMipLevel, _MipMapBias,
                                     _StableAA);

                    // Apply chroma correction to reduce YUV 4:2:0 color bleeding
                    col = ChromaCorrect(col, _ChromaSharpness);
                }
                else
                {
                    col = SampleTexture(_MainTex, stereoUV, uvDx, uvDy,
                                        _MainTex_TexelSize.zw,
                                        _MaxMipLevel, _MipMapBias, _StableAA);
                }
                // Apply video picture adjustments (only effective when values differ from defaults)
                col.rgb = VideoColorCorrect(col.rgb, _Brightness, _Contrast, _Saturation);
                col.rgb = ApplyTintTemperature(col.rgb, _Tint, _Temperature);

                // Anti-banding: suppress encoder micro-noise on static content
                col = TemporalSmooth(col, i.pos.xy, _TemporalSmooth);

                col *= _Color;

                // ---- Edge fade cũ (mờ dần về mép) ----
                float aEdge = EdgeFade(pMeters, halfSize, _EdgeFadeX, _EdgeFadeY, _EdgeMinAlpha);

                // ---- Hợp alpha: bo góc trước, rồi fade mép ----
                col.a *= aRound;
                col.a *= aEdge;

                return col;
            }
            ENDCG
        }
    }

    FallBack Off
}
