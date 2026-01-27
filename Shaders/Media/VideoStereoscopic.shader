Shader "VRWorkspace/Media/VideoStereoscopic"
{
    Properties
    {
        _MainTex ("Video Texture", 2D) = "black" {}
        _Brightness ("Brightness", Range(0, 2)) = 1
        _Contrast ("Contrast", Range(0, 2)) = 1
        _Saturation ("Saturation", Range(0, 2)) = 1

        [Header(Stereo Layout)]
        _StereoLayout ("Stereo Layout", Float) = 1  // 0=Mono, 1=SBS, 2=OU, 3=SBS_Half, 4=OU_Half
        _SwapEyes ("Swap Eyes", Float) = 0          // 0=Normal, 1=Swap Left/Right

        [Header(Eye Selection)]
        _EyeIndex ("Eye Index", Float) = 0          // 0=Left, 1=Right (set per eye)

        [Header(NV12 Support)]
        _UseNV12 ("Use NV12", Float) = 0
        _YTex ("Y Plane", 2D) = "black" {}
        _UVTex ("UV Plane", 2D) = "gray" {}

        [Header(Screen Settings)]
        _Curvature ("Curvature", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }

        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            // NV12 textures
            sampler2D _YTex;
            sampler2D _UVTex;
            float _UseNV12;

            // Color correction
            float _Brightness;
            float _Contrast;
            float _Saturation;

            // Stereo
            float _StereoLayout;
            float _SwapEyes;
            float _EyeIndex;

            // Screen
            float _Curvature;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                return o;
            }

            // Convert YUV (BT.709) to RGB
            float3 YUVtoRGB(float y, float2 uv)
            {
                float3 yuv = float3(y, uv.x - 0.5, uv.y - 0.5);
                float3x3 bt709 = float3x3(
                    1.0,  0.0,       1.5748,
                    1.0, -0.1873,   -0.4681,
                    1.0,  1.8556,    0.0
                );
                return mul(bt709, yuv);
            }

            // Apply color correction
            float3 ColorCorrect(float3 color, float brightness, float contrast, float saturation)
            {
                color *= brightness;
                color = (color - 0.5) * contrast + 0.5;
                float luma = dot(color, float3(0.299, 0.587, 0.114));
                color = lerp(float3(luma, luma, luma), color, saturation);
                return saturate(color);
            }

            // Get stereo UV based on layout and eye
            // Stereo layouts:
            // 0 = Mono (no stereo)
            // 1 = Side-by-Side Full (left half = left eye, right half = right eye)
            // 2 = Over-Under Full (top half = left eye, bottom half = right eye)
            // 3 = Side-by-Side Half (left eye stretched to full width)
            // 4 = Over-Under Half (left eye stretched to full height)
            float2 GetStereoUV(float2 uv, float layout, float eyeIndex, float swapEyes)
            {
                // Apply eye swap if enabled
                float eye = eyeIndex;
                if (swapEyes > 0.5)
                    eye = 1.0 - eye;

                if (layout < 0.5)
                {
                    // Mono - no modification
                    return uv;
                }
                else if (layout < 1.5)
                {
                    // Side-by-Side Full
                    // Left eye: u = 0 to 0.5, Right eye: u = 0.5 to 1
                    float halfU = uv.x * 0.5;
                    if (eye > 0.5)
                        halfU += 0.5;
                    return float2(halfU, uv.y);
                }
                else if (layout < 2.5)
                {
                    // Over-Under Full
                    // Left eye: v = 0.5 to 1 (top), Right eye: v = 0 to 0.5 (bottom)
                    float halfV = uv.y * 0.5;
                    if (eye < 0.5)
                        halfV += 0.5;  // Left eye is top half
                    return float2(uv.x, halfV);
                }
                else if (layout < 3.5)
                {
                    // Side-by-Side Half (anamorphic)
                    // Each eye's content is horizontally compressed in source
                    float halfU = uv.x * 0.5;
                    if (eye > 0.5)
                        halfU += 0.5;
                    return float2(halfU, uv.y);
                }
                else
                {
                    // Over-Under Half (anamorphic)
                    // Each eye's content is vertically compressed in source
                    float halfV = uv.y * 0.5;
                    if (eye < 0.5)
                        halfV += 0.5;
                    return float2(uv.x, halfV);
                }
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Get stereo-adjusted UV
                float2 stereoUV = GetStereoUV(i.uv, _StereoLayout, _EyeIndex, _SwapEyes);

                float3 color;

                if (_UseNV12 > 0.5)
                {
                    // NV12 format (HEVC hardware decode output)
                    float y = tex2D(_YTex, stereoUV).r;
                    float2 uvSample = tex2D(_UVTex, stereoUV).rg;
                    color = YUVtoRGB(y, uvSample);
                }
                else
                {
                    // Standard RGB texture
                    color = tex2D(_MainTex, stereoUV).rgb;
                }

                // Apply color correction
                color = ColorCorrect(color, _Brightness, _Contrast, _Saturation);

                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }

    FallBack "Unlit/Texture"
}
