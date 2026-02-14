Shader "VRWorkspace/Media/VideoImmersive"
{
    Properties
    {
        _MainTex ("Video Texture", 2D) = "black" {}
        _Brightness ("Brightness", Range(0, 2)) = 1
        _Contrast ("Contrast", Range(0, 2)) = 1
        _Saturation ("Saturation", Range(0, 2)) = 1

        [Header(Projection)]
        _ProjectionMode ("Projection Mode", Float) = 0  // 0=Equirect360, 1=Equirect180
        _FOV ("Field of View", Range(180, 420)) = 300    // Visible FOV (lower = more zoom, higher = zoom out)
        _Rotation ("Rotation Offset", Float) = 0        // Y-axis rotation (degrees)
        _Tilt ("Tilt Offset", Float) = 0                // X-axis tilt (degrees)
        _FadeSharpness ("Fade Sharpness", Range(1, 20)) = 8  // Back hemisphere fade for 180 mode

        [Header(Stereo)]
        _StereoMode ("Stereo Mode", Float) = 0  // 0=Mono, 1=SBS, 2=OU
        _EyeIndex ("Eye Index", Float) = 0      // 0=Left, 1=Right (editor fallback)

        [Header(NV12 Support)]
        _UseNV12 ("Use NV12", Float) = 0
        _YTex ("Y Plane", 2D) = "black" {}
        _UVTex ("UV Plane", 2D) = "gray" {}
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Opaque" }

        // Inside-out rendering: cull front faces so back faces (visible from inside) render
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

            // ===== Uniforms =====
            sampler2D _MainTex;
            float4 _MainTex_ST;

            sampler2D _YTex;
            sampler2D _UVTex;
            float _UseNV12;

            float _Brightness;
            float _Contrast;
            float _Saturation;

            float _ProjectionMode;
            float _FOV;
            float _Rotation;
            float _Tilt;
            float _FadeSharpness;

            float _StereoMode;
            float _EyeIndex;

            float4 _CameraForward;  // Set from C# each frame (camera look direction)

            // ===== Structures =====
            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 objPos : TEXCOORD0;    // Object-space position (NOT normalized) - interpolates correctly
                float2 meshUV : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ===== Vertex Shader =====
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);

                // Pass object-space position directly (interpolates correctly)
                // For centered sphere: normalize(objPos) in fragment = view direction
                // This fixes pole distortion caused by interpolating normalized vectors
                o.objPos = v.vertex.xyz;

                // Pass mesh UVs for seam-free equirectangular mapping
                o.meshUV = v.uv;

                return o;
            }

            // ===== Helper: YUV (BT.709) to RGB =====
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

            // ===== Helper: Color Correction =====
            float3 ColorCorrect(float3 color, float brightness, float contrast, float saturation)
            {
                color *= brightness;
                color = (color - 0.5) * contrast + 0.5;
                float luma = dot(color, float3(0.299, 0.587, 0.114));
                color = lerp(float3(luma, luma, luma), color, saturation);
                return saturate(color);
            }

            // ===== Helper: Rotate Direction Vector =====
            // Applies Y-axis rotation (horizontal) then X-axis tilt (vertical)
            float3 RotateDirection(float3 dir, float rotationDeg, float tiltDeg)
            {
                float rotRad = rotationDeg * 0.01745329;  // deg to rad
                float tiltRad = tiltDeg * 0.01745329;

                // Y-axis rotation (horizontal pan)
                float cosRot = cos(rotRad);
                float sinRot = sin(rotRad);
                float3 rotated = float3(
                    dir.x * cosRot - dir.z * sinRot,
                    dir.y,
                    dir.x * sinRot + dir.z * cosRot
                );

                // X-axis tilt (vertical)
                float cosTilt = cos(tiltRad);
                float sinTilt = sin(tiltRad);
                return float3(
                    rotated.x,
                    rotated.y * cosTilt - rotated.z * sinTilt,
                    rotated.y * sinTilt + rotated.z * cosTilt
                );
            }

            // ===== Helper: Zoom Direction =====
            // Unified zoom: picks the best projection for the zoom direction.
            // Zoom IN  (zoomFactor > 1): gnomonic/rectilinear — preserves straight lines.
            // Zoom OUT (zoomFactor < 1): spherical — handles full sphere without edge stretching.
            float3 ZoomDirection(float3 viewDir, float3 zoomCenter, float zoomFactor)
            {
                if (zoomFactor > 1.001)
                {
                    // === Zoom IN: gnomonic (rectilinear) projection ===
                    // Projects onto tangent plane at zoomCenter, scales, projects back.
                    // This preserves straight lines (no barrel distortion).
                    float d = dot(viewDir, zoomCenter);
                    if (d > 0.001)
                    {
                        float3 offset = (viewDir / d) - zoomCenter;
                        offset /= zoomFactor;
                        return normalize(zoomCenter + offset);
                    }
                    return viewDir;
                }
                else if (zoomFactor < 0.999)
                {
                    // === Zoom OUT: spherical angle scaling ===
                    // Scales angular distance from zoomCenter outward.
                    // Handles the full sphere without the edge-stretching that gnomonic causes.
                    float cosAngle = clamp(dot(viewDir, zoomCenter), -1.0, 1.0);
                    float angle = acos(cosAngle);

                    float3 perp = viewDir - cosAngle * zoomCenter;
                    float perpLen = length(perp);
                    if (perpLen < 0.0001) return viewDir;
                    perp /= perpLen;

                    float newAngle = min(angle / zoomFactor, 3.14159265);
                    return zoomCenter * cos(newAngle) + perp * sin(newAngle);
                }

                return viewDir; // zoomFactor ≈ 1, no change
            }

            // ===== Helper: Stereo UV Offset =====
            float2 GetStereoUV(float2 uv, float stereoMode, float eyeIndex)
            {
                if (stereoMode < 0.5)
                {
                    // Mono: no change
                    return uv;
                }
                else if (stereoMode < 1.5)
                {
                    // Side-by-Side: left eye = left half, right eye = right half
                    float halfU = uv.x * 0.5;
                    if (eyeIndex > 0.5)
                        halfU += 0.5;
                    return float2(halfU, uv.y);
                }
                else
                {
                    // Over-Under: left eye = top half, right eye = bottom half
                    float halfV = uv.y * 0.5;
                    if (eyeIndex < 0.5)
                        halfV += 0.5;  // Left eye is top half
                    return float2(uv.x, halfV);
                }
            }

            // ===== Fragment Shader =====
            fixed4 frag(v2f i) : SV_Target
            {
                // Normalize AFTER interpolation - this is the key fix for pole distortion!
                // Object-space positions interpolate correctly, then we derive direction per-pixel
                float3 viewDir = normalize(i.objPos);

                float2 equirectUV;
                float alpha = 1.0;

                if (_ProjectionMode > 0.5)
                {
                    // === Equirect 180 mode ===
                    viewDir = RotateDirection(viewDir, _Rotation, _Tilt);

                    float zoomFactor = 180.0 / max(_FOV, 1.0);
                    // Zoom-in: center on camera look direction (zoom into what you're viewing)
                    // Zoom-out: center on video front — content stays fixed in space,
                    // preventing UI elements from appearing to slide across the video.
                    float3 zoomCenter = (zoomFactor > 1.0)
                        ? normalize(RotateDirection(_CameraForward.xyz, _Rotation, _Tilt))
                        : float3(0, 0, 1);
                    viewDir = ZoomDirection(viewDir, zoomCenter, zoomFactor);

                    // Fade based on ZOOMED direction — hides anything pushed outside 180° content
                    // Scale sharpness inversely with zoom: zoom-out → crisper edge (content is smaller)
                    float effectiveSharpness = _FadeSharpness / max(zoomFactor, 0.1);
                    alpha = saturate(viewDir.z * effectiveSharpness);

                    float phi = atan2(viewDir.x, viewDir.z);
                    float theta = asin(clamp(viewDir.y, -1.0, 1.0));

                    // Standard 180° equirectangular UV mapping (no angle scaling)
                    float baseFovRad = 3.14159265 * 0.5;
                    equirectUV.x = (phi / baseFovRad) * 0.5 + 0.5;
                    equirectUV.y = 0.5 + (theta / (3.14159265 * 0.5)) * 0.5;
                    equirectUV = saturate(equirectUV);
                }
                else
                {
                    // === Equirect 360 mode ===
                    viewDir = RotateDirection(viewDir, _Rotation, _Tilt);

                    float zoomFactor = 360.0 / max(_FOV, 1.0);
                    float3 zoomCenter = (zoomFactor > 1.0)
                        ? normalize(RotateDirection(_CameraForward.xyz, _Rotation, _Tilt))
                        : float3(0, 0, 1);
                    viewDir = ZoomDirection(viewDir, zoomCenter, zoomFactor);

                    float phi = atan2(viewDir.x, viewDir.z);
                    float theta = asin(clamp(viewDir.y, -1.0, 1.0));

                    // Standard 360° equirectangular UV mapping (no angle scaling)
                    equirectUV.x = phi / (2.0 * 3.14159265) + 0.5;
                    equirectUV.y = theta / 3.14159265 + 0.5;
                }

                // Apply stereo eye offset
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float eye = unity_StereoEyeIndex;
                float2 stereoUV = GetStereoUV(equirectUV, _StereoMode, eye);

                // Sample video texture
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

                // Apply 180 back-hemisphere fade
                color *= alpha;

                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }

    FallBack "Unlit/Texture"
}
