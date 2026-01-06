Shader "Custom/ClusterBackgroundCurved"
{
    // Glass gradient background for curved cluster mesh
    // Uses world-space SDF for consistent corner radius on curved surface
    // Only rounds the 4 outer corners of the entire cluster

    Properties
    {
        [Header(Cluster Dimensions)]
        _ClusterWidth ("Cluster Width (m)", Float) = 4.8
        _ClusterHeight ("Cluster Height (m)", Float) = 0.9
        _ArcRadius ("Arc Radius (m)", Float) = 2.0

        [Header(Rounded Corners)]
        _CornerRadius ("Corner Radius (m)", Float) = 0.04
        _EdgePadding ("Edge Padding (m)", Float) = 0.009

        [Header(Gradient)]
        [HDR] _ColorA ("Color A (Cyan)", Color) = (0, 0.55, 0.65, 0.35)
        [HDR] _ColorB ("Color B (Purple)", Color) = (0.30, 0.12, 0.50, 0.32)
        _GradientAngle ("Gradient Angle", Range(-45, 45)) = -10
        _CyanRatio ("Cyan Ratio", Range(0.3, 0.9)) = 0.7

        [Header(Glass Effect)]
        _GlassAlpha ("Base Alpha", Range(0, 1)) = 0.65
        _FresnelPower ("Fresnel Power", Range(1, 5)) = 2.2
        _FresnelStrength ("Fresnel Strength", Range(0, 0.3)) = 0.12

        [Header(Content Margins)]
        _MarginH ("Horizontal Margin", Float) = 0.04
        _MarginV ("Vertical Margin", Float) = 0.045
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent-1"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }

        Cull Back
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "ClusterBackgroundCurved"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;      // Global UV (0-1 across cluster)
                float2 uv2 : TEXCOORD1;     // Per-panel UV
                float2 uv3 : TEXCOORD2;     // Panel index info
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 globalUV : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 viewDir : TEXCOORD2;
                float2 localPos : TEXCOORD3;  // Position in cluster space (meters)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float _ClusterWidth;
            float _ClusterHeight;
            float _ArcRadius;

            float _CornerRadius;
            float _EdgePadding;

            fixed4 _ColorA;
            fixed4 _ColorB;
            float _GradientAngle;
            float _CyanRatio;

            float _GlassAlpha;
            float _FresnelPower;
            float _FresnelStrength;

            float _MarginH;
            float _MarginV;

            // SDF for rounded box at outer corners only
            // pos: position from center (in meters)
            // halfSize: half width/height (in meters)
            // radius: corner radius (in meters)
            float sdRoundedBoxOuter(float2 pos, float2 halfSize, float radius)
            {
                float2 q = abs(pos) - (halfSize - radius);
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.globalUV = v.uv;

                // World normal for fresnel
                o.worldNormal = UnityObjectToWorldNormal(v.normal);

                // View direction for fresnel
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = normalize(_WorldSpaceCameraPos - worldPos);

                // Calculate local position in cluster space (meters)
                // globalUV goes 0-1, map to actual cluster dimensions
                o.localPos = (v.uv - 0.5) * float2(_ClusterWidth, _ClusterHeight);

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.globalUV;
                float2 localPos = i.localPos;

                // Half size of cluster in meters
                float2 halfSize = float2(_ClusterWidth, _ClusterHeight) * 0.5;

                // Calculate SDF for outer corners
                float paddedHalfSize_x = halfSize.x - _EdgePadding;
                float paddedHalfSize_y = halfSize.y - _EdgePadding;
                float2 paddedHalfSize = float2(paddedHalfSize_x, paddedHalfSize_y);

                float dist = sdRoundedBoxOuter(localPos, paddedHalfSize, _CornerRadius);

                // Alpha mask for rounded corners with smooth edge
                float edgeWidth = 0.002; // 2mm edge smoothing
                float alphaMask = 1.0 - smoothstep(-edgeWidth, edgeWidth, dist);

                // Early out for transparent pixels
                if (alphaMask <= 0.001)
                {
                    return fixed4(0, 0, 0, 0);
                }

                // === GRADIENT ===
                // Rotate UV for angled gradient
                float angleRad = _GradientAngle * 3.14159 / 180.0;
                float2 centeredUV = uv - 0.5;
                float2 rotatedUV;
                rotatedUV.x = centeredUV.x * cos(angleRad) - centeredUV.y * sin(angleRad);
                rotatedUV.y = centeredUV.x * sin(angleRad) + centeredUV.y * cos(angleRad);

                float t = saturate(rotatedUV.x + 0.5);
                t = pow(t, 1.0 / _CyanRatio);
                fixed4 gradColor = lerp(_ColorA, _ColorB, t);

                // === GLASS EFFECT ===
                // Center glow
                float2 centerDist = abs(uv - 0.5);
                float centerGlow = 1.0 - saturate(length(centerDist) / 0.5);
                centerGlow = centerGlow * centerGlow * 0.15;

                // Fresnel edge effect
                float NdotV = saturate(dot(normalize(i.worldNormal), normalize(i.viewDir)));
                float fresnel = pow(1.0 - NdotV, _FresnelPower) * _FresnelStrength;

                // Edge proximity fresnel (stronger near edges)
                float edgeFactor = 1.0 - saturate(abs(dist) / 0.1);
                fresnel += edgeFactor * edgeFactor * _FresnelStrength * 0.5;

                // === COMPOSITE ===
                fixed4 finalColor = gradColor;
                finalColor.a = _GlassAlpha + gradColor.a * 0.5;
                finalColor.a *= alphaMask;
                finalColor.rgb += fresnel + centerGlow;

                return finalColor;
            }
            ENDCG
        }
    }

    FallBack "Transparent/Diffuse"
}
