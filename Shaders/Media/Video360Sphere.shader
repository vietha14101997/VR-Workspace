Shader "VRWorkspace/Media/Video360Sphere"
{
    Properties
    {
        _MainTex ("Video Texture", 2D) = "black" {}
        _Brightness ("Brightness", Range(0, 2)) = 1
        _Contrast ("Contrast", Range(0, 2)) = 1
        _Saturation ("Saturation", Range(0, 2)) = 1

        [Header(Stereo)]
        _StereoMode ("Stereo Mode", Float) = 0  // 0=Mono, 1=SBS, 2=OU
        _EyeIndex ("Eye Index", Float) = 0      // 0=Left, 1=Right

        [Header(NV12 Support)]
        _UseNV12 ("Use NV12", Float) = 0
        _YTex ("Y Plane", 2D) = "black" {}
        _UVTex ("UV Plane", 2D) = "gray" {}

        [Header(360 Settings)]
        _Rotation ("Rotation Offset", Range(-180, 180)) = 0
        _Tilt ("Tilt Offset", Range(-90, 90)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Opaque" }

        // Render inside-out (backface)
        Cull Front
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
            float _StereoMode;
            float _EyeIndex;

            // 360 Settings
            float _Rotation;
            float _Tilt;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 viewDir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);

                // Get world space direction from center to vertex
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 worldCenter = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                o.viewDir = normalize(worldPos - worldCenter);

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

            // Rotate direction by Y (rotation) and X (tilt) angles
            float3 RotateDirection(float3 dir, float rotationDeg, float tiltDeg)
            {
                float rotRad = rotationDeg * 0.01745329;
                float tiltRad = tiltDeg * 0.01745329;

                // Rotation around Y axis (horizontal rotation)
                float cosRot = cos(rotRad);
                float sinRot = sin(rotRad);
                float3 rotated = float3(
                    dir.x * cosRot - dir.z * sinRot,
                    dir.y,
                    dir.x * sinRot + dir.z * cosRot
                );

                // Tilt around X axis (vertical tilt)
                float cosTilt = cos(tiltRad);
                float sinTilt = sin(tiltRad);
                return float3(
                    rotated.x,
                    rotated.y * cosTilt - rotated.z * sinTilt,
                    rotated.y * sinTilt + rotated.z * cosTilt
                );
            }

            // Convert 3D direction to equirectangular UV for 360 sphere
            float2 DirectionToEquirect360(float3 dir)
            {
                // Convert to spherical coordinates
                // phi: horizontal angle (-PI to PI)
                // theta: vertical angle (-PI/2 to PI/2)
                float phi = atan2(dir.x, dir.z);
                float theta = asin(clamp(dir.y, -1, 1));

                // Map to UV (0-1)
                // U: 0 at center-back, 0.25 at right, 0.5 at center-front, 0.75 at left
                float u = phi / (2.0 * 3.14159265) + 0.5;

                // V: 0 at bottom, 1 at top
                float v = theta / 3.14159265 + 0.5;

                return float2(u, v);
            }

            // Get stereo UV based on mode and eye
            float2 GetStereoUV(float2 uv, float stereoMode, float eyeIndex)
            {
                if (stereoMode < 0.5)
                {
                    // Mono
                    return uv;
                }
                else if (stereoMode < 1.5)
                {
                    // Side-by-Side (common for 360 3D)
                    float halfU = uv.x * 0.5;
                    if (eyeIndex > 0.5)
                        halfU += 0.5;
                    return float2(halfU, uv.y);
                }
                else
                {
                    // Over-Under (top-bottom 360 3D)
                    float halfV = uv.y * 0.5;
                    if (eyeIndex < 0.5)
                        halfV += 0.5;  // Left eye is top half
                    return float2(uv.x, halfV);
                }
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Normalize and rotate view direction
                float3 viewDir = normalize(i.viewDir);
                viewDir = RotateDirection(viewDir, _Rotation, _Tilt);

                // Convert to equirectangular UV
                float2 equirectUV = DirectionToEquirect360(viewDir);

                // Apply stereo mode
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float eye = unity_StereoEyeIndex;
                float2 stereoUV = GetStereoUV(equirectUV, _StereoMode, eye);

                float3 color;

                if (_UseNV12 > 0.5)
                {
                    float y = tex2D(_YTex, stereoUV).r;
                    float2 uvSample = tex2D(_UVTex, stereoUV).rg;
                    color = YUVtoRGB(y, uvSample);
                }
                else
                {
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
