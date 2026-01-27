Shader "VRWorkspace/Media/Video180Dome"
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

        [Header(180 Settings)]
        _FOV ("Field of View", Range(90, 220)) = 180
        _Rotation ("Rotation Offset", Range(-180, 180)) = 0
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

            // 180 Settings
            float _FOV;
            float _Rotation;

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

            // Convert 3D direction to equirectangular UV for 180 dome
            float2 DirectionToEquirect180(float3 dir, float fov, float rotation)
            {
                // Apply rotation around Y axis
                float rotRad = rotation * 0.01745329; // deg to rad
                float cosRot = cos(rotRad);
                float sinRot = sin(rotRad);
                float3 rotDir = float3(
                    dir.x * cosRot - dir.z * sinRot,
                    dir.y,
                    dir.x * sinRot + dir.z * cosRot
                );

                // Convert to spherical coordinates
                // For 180 video, we only look at the front hemisphere (z > 0)
                float phi = atan2(rotDir.x, rotDir.z);   // -PI to PI (horizontal angle)
                float theta = asin(clamp(rotDir.y, -1, 1)); // -PI/2 to PI/2 (vertical angle)

                // Map to UV (0-1)
                float fovRad = fov * 0.01745329 * 0.5;

                // U: horizontal, centered at 0.5
                float u = (phi / fovRad) * 0.5 + 0.5;

                // V: vertical, 0 at bottom, 1 at top
                float v = (theta / (3.14159265 * 0.5)) * 0.5 + 0.5;

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
                // Convert view direction to equirectangular UV
                float3 viewDir = normalize(i.viewDir);

                // Only render front hemisphere (z >= 0)
                // For behind the viewer, fade to black
                float frontFade = saturate(viewDir.z * 5 + 0.5);

                float2 equirectUV = DirectionToEquirect180(viewDir, _FOV, _Rotation);

                // Clamp UV to valid range
                equirectUV = saturate(equirectUV);

                // Apply stereo mode
                float2 stereoUV = GetStereoUV(equirectUV, _StereoMode, _EyeIndex);

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

                // Apply front hemisphere fade
                color *= frontFade;

                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }

    FallBack "Unlit/Texture"
}
