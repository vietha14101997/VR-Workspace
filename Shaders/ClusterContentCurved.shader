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

            // SDF for rounded box
            float sdRoundedBox(float2 pos, float2 halfSize, float radius)
            {
                float2 q = abs(pos) - (halfSize - radius);
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
            }

            // Unsharp Mask sharpening
            float4 UnsharpMask(sampler2D tex, float2 uv, float2 texelSize, float sharpness, float radius)
            {
                float4 center = tex2D(tex, uv);

                float4 blur = (
                    tex2D(tex, uv + float2(-texelSize.x, 0) * radius) +
                    tex2D(tex, uv + float2(texelSize.x, 0) * radius) +
                    tex2D(tex, uv + float2(0, -texelSize.y) * radius) +
                    tex2D(tex, uv + float2(0, texelSize.y) * radius)
                ) * 0.25;

                float4 sharpened = center + (center - blur) * sharpness;
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

            // Sample from specific panel texture with optional sharpening
            float4 SamplePanel(int panelIndex, float2 panelUV)
            {
                float4 color = float4(0, 0, 0, 1);
                float2 texelSize = float2(0.001, 0.001);

                // Sample based on panel index
                if (panelIndex == 0)
                {
                    texelSize = _Content0_TexelSize.xy;
                    if (_EnableSharpening > 0.5)
                        color = UnsharpMask(_Content0, panelUV, texelSize, _Sharpness, _SharpnessRadius);
                    else
                        color = tex2D(_Content0, panelUV);
                }
                else if (panelIndex == 1)
                {
                    texelSize = _Content1_TexelSize.xy;
                    if (_EnableSharpening > 0.5)
                        color = UnsharpMask(_Content1, panelUV, texelSize, _Sharpness, _SharpnessRadius);
                    else
                        color = tex2D(_Content1, panelUV);
                }
                else if (panelIndex == 2)
                {
                    texelSize = _Content2_TexelSize.xy;
                    if (_EnableSharpening > 0.5)
                        color = UnsharpMask(_Content2, panelUV, texelSize, _Sharpness, _SharpnessRadius);
                    else
                        color = tex2D(_Content2, panelUV);
                }
                else if (panelIndex == 3)
                {
                    texelSize = _Content3_TexelSize.xy;
                    if (_EnableSharpening > 0.5)
                        color = UnsharpMask(_Content3, panelUV, texelSize, _Sharpness, _SharpnessRadius);
                    else
                        color = tex2D(_Content3, panelUV);
                }
                else if (panelIndex == 4)
                {
                    texelSize = _Content4_TexelSize.xy;
                    if (_EnableSharpening > 0.5)
                        color = UnsharpMask(_Content4, panelUV, texelSize, _Sharpness, _SharpnessRadius);
                    else
                        color = tex2D(_Content4, panelUV);
                }
                else if (panelIndex == 5)
                {
                    texelSize = _Content5_TexelSize.xy;
                    if (_EnableSharpening > 0.5)
                        color = UnsharpMask(_Content5, panelUV, texelSize, _Sharpness, _SharpnessRadius);
                    else
                        color = tex2D(_Content5, panelUV);
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

                // === BLEND ZONE CALCULATION ===
                // panelFrac goes 0-1 within each panel
                // Blend at boundaries (near 0 and near 1)
                float blendAlpha = 1.0;
                float4 finalColor;

                if (panelFrac < _BlendZone && panelIndex > 0)
                {
                    // Left edge - blend with previous panel
                    float blend = panelFrac / _BlendZone;
                    blend = smoothstep(0.0, 1.0, blend);

                    float4 leftColor = SamplePanel(panelIndex - 1, float2(1.0, panelUV.y));
                    float4 rightColor = SamplePanel(panelIndex, panelUV);

                    finalColor = lerp(leftColor, rightColor, blend);
                }
                else if (panelFrac > (1.0 - _BlendZone) && panelIndex < _PanelCount - 1)
                {
                    // Right edge - blend with next panel
                    float blend = (panelFrac - (1.0 - _BlendZone)) / _BlendZone;
                    blend = smoothstep(0.0, 1.0, blend);

                    float4 leftColor = SamplePanel(panelIndex, panelUV);
                    float4 rightColor = SamplePanel(panelIndex + 1, float2(0.0, panelUV.y));

                    finalColor = lerp(leftColor, rightColor, blend);
                }
                else
                {
                    // Center - just sample current panel
                    finalColor = SamplePanel(panelIndex, panelUV);
                }

                finalColor.a *= alphaMask;

                return finalColor;
            }
            ENDCG
        }
    }

    FallBack "Unlit/Transparent"
}
