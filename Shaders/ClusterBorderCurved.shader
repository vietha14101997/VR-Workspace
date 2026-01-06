Shader "Custom/ClusterBorderCurved"
{
    // Multi-layer glowing border for curved cluster mesh
    // Renders glow only on outer edges of entire cluster
    // No internal borders between panels

    Properties
    {
        [Header(Cluster Dimensions)]
        _ClusterWidth ("Cluster Width (m)", Float) = 4.8
        _ClusterHeight ("Cluster Height (m)", Float) = 0.9

        [Header(Border Settings)]
        _CornerRadius ("Corner Radius (m)", Float) = 0.04
        _EdgePadding ("Edge Padding (m)", Float) = 0.009

        [Header(Multi Layer Glow)]
        _Layer1Width ("Layer 1 (Core) Width (m)", Float) = 0.01
        _Layer1Alpha ("Layer 1 Alpha", Range(0, 2)) = 1.5
        _Layer2Width ("Layer 2 (Mid) Width (m)", Float) = 0.02
        _Layer2Alpha ("Layer 2 Alpha", Range(0, 2)) = 1.0
        _Layer3Width ("Layer 3 (Outer) Width (m)", Float) = 0.045
        _Layer3Alpha ("Layer 3 Alpha", Range(0, 1)) = 0.6
        _Layer4Width ("Layer 4 (Ambient) Width (m)", Float) = 0.09
        _Layer4Alpha ("Layer 4 Alpha", Range(0, 1)) = 0.3

        [Header(Gradient Colors)]
        [HDR] _ColorA ("Color A (Cyan)", Color) = (0.3, 1, 1, 1)
        [HDR] _ColorB ("Color B (Purple)", Color) = (1, 0.4, 1, 1)
        _GradientAngle ("Gradient Angle", Range(-180, 180)) = -10

        [Header(Shimmer Effect)]
        _ShimmerSpeed ("Shimmer Speed", Float) = 0.4
        _ShimmerIntensity ("Shimmer Intensity", Range(0, 1)) = 0.2
        _LightSize ("Light Size", Float) = 0.15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent+1"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }

        Cull Back
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "ClusterBorderCurved"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;      // Global UV (0-1 across cluster)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 globalUV : TEXCOORD0;
                float2 localPos : TEXCOORD1;  // Position in cluster space (meters)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float _ClusterWidth;
            float _ClusterHeight;

            float _CornerRadius;
            float _EdgePadding;

            float _Layer1Width;
            float _Layer1Alpha;
            float _Layer2Width;
            float _Layer2Alpha;
            float _Layer3Width;
            float _Layer3Alpha;
            float _Layer4Width;
            float _Layer4Alpha;

            fixed4 _ColorA;
            fixed4 _ColorB;
            float _GradientAngle;

            float _ShimmerSpeed;
            float _ShimmerIntensity;
            float _LightSize;

            // SDF for rounded box
            float sdRoundedBox(float2 pos, float2 halfSize, float radius)
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

                // Calculate local position in cluster space (meters)
                o.localPos = (v.uv - 0.5) * float2(_ClusterWidth, _ClusterHeight);

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.globalUV;
                float2 localPos = i.localPos;

                // Half size of cluster in meters (with padding)
                float2 halfSize = float2(_ClusterWidth, _ClusterHeight) * 0.5;
                halfSize -= _EdgePadding;

                // SDF distance to border
                float dist = sdRoundedBox(localPos, halfSize, _CornerRadius);
                float absDist = abs(dist);

                // === GRADIENT ===
                float angleRad = _GradientAngle * 3.14159 / 180.0;
                float2 centeredUV = uv - 0.5;
                float2 rotatedUV;
                rotatedUV.x = centeredUV.x * cos(angleRad) - centeredUV.y * sin(angleRad);
                rotatedUV.y = centeredUV.x * sin(angleRad) + centeredUV.y * cos(angleRad);

                float t = saturate(rotatedUV.x + 0.5);
                fixed4 borderColor = lerp(_ColorA, _ColorB, t);

                // === MULTI-LAYER GLOW ===
                // Layer 4 (Ambient Outer)
                float layer4 = 1.0 - saturate(absDist / _Layer4Width);
                layer4 = pow(layer4, 2.0);

                // Layer 3 (Outer Glow)
                float layer3 = 1.0 - saturate(absDist / _Layer3Width);
                layer3 = pow(layer3, 2.0);

                // Layer 2 (Mid Glow)
                float layer2 = 1.0 - saturate(absDist / _Layer2Width);
                layer2 = pow(layer2, 1.5);

                // Layer 1 (Core Line)
                float layer1 = 1.0 - saturate(absDist / _Layer1Width);
                layer1 = pow(layer1, 0.5);

                // === SHIMMER EFFECT ===
                float time = _Time.y * _ShimmerSpeed;

                // Calculate position along perimeter for shimmer
                // Use atan2 to get angle around the shape
                float angle = atan2(localPos.y, localPos.x);
                float perimeter = (angle + 3.14159) / (2.0 * 3.14159); // 0-1 around perimeter

                float shimmerPos = frac(time);
                float shimmerDist = abs(perimeter - shimmerPos);
                shimmerDist = min(shimmerDist, 1.0 - shimmerDist); // Wrap around

                float shimmerLight = 1.0 - saturate(shimmerDist / _LightSize);
                shimmerLight = pow(shimmerLight, 2.0) * _ShimmerIntensity;

                // Only apply shimmer near the border
                float borderProximity = 1.0 - saturate(absDist / 0.02);
                shimmerLight *= borderProximity;

                // === COMPOSITE ===
                fixed4 finalColor = fixed4(0, 0, 0, 0);
                fixed3 glowColor = borderColor.rgb;

                // Additive Glow Layers
                finalColor.rgb += glowColor * layer4 * _Layer4Alpha * 0.5;
                finalColor.a = max(finalColor.a, layer4 * _Layer4Alpha * 0.3);

                finalColor.rgb += glowColor * layer3 * _Layer3Alpha;
                finalColor.a = max(finalColor.a, layer3 * _Layer3Alpha * 0.5);

                finalColor.rgb = lerp(finalColor.rgb, glowColor * 1.2, layer2 * _Layer2Alpha);
                finalColor.a = max(finalColor.a, layer2 * _Layer2Alpha);

                // White core
                fixed3 whiteCore = fixed3(1, 1, 1);
                float coreMix = layer1 * _Layer1Alpha * 0.5;
                finalColor.rgb = lerp(finalColor.rgb, whiteCore, coreMix);
                finalColor.a = max(finalColor.a, layer1 * _Layer1Alpha);

                // Add shimmer
                finalColor.rgb += glowColor * shimmerLight * 0.5;
                finalColor.rgb += whiteCore * shimmerLight;
                finalColor.a = max(finalColor.a, shimmerLight * 0.8);

                // Clip fully inside area (only render border region)
                float insideMask = step(0.0, dist) + (1.0 - step(0.0, dist)) * saturate(-dist / _Layer4Width);
                finalColor.a *= insideMask;

                return finalColor;
            }
            ENDCG
        }
    }

    FallBack "Transparent/Diffuse"
}
