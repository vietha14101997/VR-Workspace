Shader "VRWorkspace/Media/VideoFlatProjection"
{
    Properties
    {
        _MainTex ("Video Texture", 2D) = "black" {}
        _Brightness ("Brightness", Range(0, 2)) = 1
        _Contrast ("Contrast", Range(0, 2)) = 1
        _Saturation ("Saturation", Range(0, 2)) = 1

        [Header(Adjustments)]
        _Tint ("Tint", Range(-1, 1)) = 0
        _Temperature ("Temperature", Range(-1, 1)) = 0

        [Header(Stereo)]
        _StereoMode ("Stereo Mode", Float) = 0  // 0=Mono, 1=SBS, 2=OU
        _EyeIndex ("Eye Index", Float) = 0      // 0=Left, 1=Right
        _LRInverse ("LR Inverse", Float) = 0    // 1=swap left/right eye

        [Header(NV12 Support)]
        _UseNV12 ("Use NV12", Float) = 0
        _YTex ("Y Plane", 2D) = "black" {}
        _UVTex ("UV Plane", 2D) = "gray" {}

        [Header(Curvature)]
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

            // Adjustments
            float _Tint;
            float _Temperature;

            // Stereo
            float _StereoMode;
            float _EyeIndex;
            float _LRInverse;

            // Curvature (not used in vertex shader, kept for future)
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
                // BT.709 conversion matrix
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
                // Brightness
                color *= brightness;

                // Contrast (around mid-gray)
                color = (color - 0.5) * contrast + 0.5;

                // Saturation
                float luma = dot(color, float3(0.299, 0.587, 0.114));
                color = lerp(float3(luma, luma, luma), color, saturation);

                return saturate(color);
            }

            // Apply tint and temperature adjustments
            float3 ApplyTintTemperature(float3 color, float tint, float temperature)
            {
                color.r += temperature * 0.1;
                color.b -= temperature * 0.1;
                color.g += tint * 0.1;
                return saturate(color);
            }

            // Get stereo UV based on mode and eye
            float2 GetStereoUV(float2 uv, float stereoMode, float eyeIndex)
            {
                if (stereoMode < 0.5)
                {
                    // Mono - no modification
                    return uv;
                }
                else if (stereoMode < 1.5)
                {
                    // Side-by-Side
                    float halfU = uv.x * 0.5;
                    if (eyeIndex > 0.5)
                        halfU += 0.5;
                    return float2(halfU, uv.y);
                }
                else
                {
                    // Over-Under
                    float halfV = uv.y * 0.5;
                    if (eyeIndex < 0.5)
                        halfV += 0.5;  // Left eye is top half
                    return float2(uv.x, halfV);
                }
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                
                // Use unity_StereoEyeIndex for automatic left/right eye detection in VR
                float eye = unity_StereoEyeIndex;
                // LR Inverse: swap left/right eye
                if (_LRInverse > 0.5) eye = 1.0 - eye;
                
                // Get stereo-adjusted UV
                float2 stereoUV = GetStereoUV(i.uv, _StereoMode, eye);

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

                // Apply tint and temperature
                color = ApplyTintTemperature(color, _Tint, _Temperature);

                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }

    FallBack "Unlit/Texture"
}
