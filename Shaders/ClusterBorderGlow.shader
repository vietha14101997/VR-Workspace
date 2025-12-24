Shader "Custom/ClusterBorderGlow"
{
    Properties
    {
        [Header(Gradient Colors)]
        [HDR] _ColorA ("Color A (Start)", Color) = (0.3, 1, 1, 1)
        [HDR] _ColorB ("Color B (End)", Color) = (1, 0.4, 1, 1)

        [Header(Border Settings)]
        _BorderWidth ("Border Width", Range(0.01, 0.5)) = 0.15
        _GlowWidth ("Glow Width", Range(0.1, 1.0)) = 0.5

        [Header(Glow Layers)]
        _Layer1Alpha ("Core Alpha", Range(0, 3)) = 1.5
        _Layer2Alpha ("Inner Glow Alpha", Range(0, 2)) = 1.0
        _Layer3Alpha ("Mid Glow Alpha", Range(0, 1)) = 0.5
        _Layer4Alpha ("Outer Glow Alpha", Range(0, 0.5)) = 0.2

        _Layer1Power ("Core Sharpness", Range(0.2, 2)) = 0.5
        _Layer2Power ("Inner Glow Falloff", Range(0.5, 3)) = 1.2
        _Layer3Power ("Mid Glow Falloff", Range(1, 4)) = 2.0
        _Layer4Power ("Outer Glow Falloff", Range(1, 5)) = 3.0

        [Header(Visual Effects)]
        _Brightness ("Brightness", Range(0.5, 2)) = 1.0
        _Saturation ("Saturation", Range(0, 2)) = 1.0

        [Header(Corner Settings)]
        _CornerBoost ("Corner Brightness Boost", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent+100"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha One

        Pass
        {
            Name "ClusterBorderGlow"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _ColorA;
            fixed4 _ColorB;

            float _BorderWidth;
            float _GlowWidth;

            float _Layer1Alpha;
            float _Layer2Alpha;
            float _Layer3Alpha;
            float _Layer4Alpha;

            float _Layer1Power;
            float _Layer2Power;
            float _Layer3Power;
            float _Layer4Power;

            float _Brightness;
            float _Saturation;
            float _CornerBoost;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;

                // UV.x = position along perimeter (0-1, wraps)
                // UV.y = perpendicular distance (0 = inner edge, 0.5 = center, 1 = outer edge)

                // Gradient color along perimeter
                float gradientT = uv.x;
                fixed3 baseColor = lerp(_ColorA.rgb, _ColorB.rgb, gradientT);

                // Distance from center line (UV.y = 0.5 is center)
                float distFromCenter = abs(uv.y - 0.5) * 2.0; // Normalize to 0-1

                // Multi-layer glow calculation
                // Layer 1: Core - sharp, bright center
                float layer1 = 1.0 - saturate(distFromCenter / _BorderWidth);
                layer1 = pow(layer1, _Layer1Power) * _Layer1Alpha;

                // Layer 2: Inner glow
                float layer2 = 1.0 - saturate(distFromCenter / (_BorderWidth * 2.0));
                layer2 = pow(layer2, _Layer2Power) * _Layer2Alpha;

                // Layer 3: Mid glow
                float layer3 = 1.0 - saturate(distFromCenter / (_GlowWidth * 0.6));
                layer3 = pow(layer3, _Layer3Power) * _Layer3Alpha;

                // Layer 4: Outer ambient glow
                float layer4 = 1.0 - saturate(distFromCenter / _GlowWidth);
                layer4 = pow(layer4, _Layer4Power) * _Layer4Alpha;

                // Combine all layers
                float totalIntensity = layer1 + layer2 + layer3 + layer4;

                // Corner boost from vertex color (alpha channel can indicate corners)
                float cornerFactor = i.color.a;
                totalIntensity *= (1.0 + _CornerBoost * cornerFactor);

                // Apply saturation
                fixed3 finalColor = baseColor;
                float luminance = dot(finalColor, float3(0.299, 0.587, 0.114));
                finalColor = lerp(float3(luminance, luminance, luminance), finalColor, _Saturation);

                // Apply brightness and intensity
                finalColor *= totalIntensity * _Brightness;

                // Alpha based on total intensity
                float alpha = saturate(totalIntensity);

                // Edge fade at UV boundaries (soft ends if not looped)
                // For a closed loop, this won't be needed, but helps with open strips
                float edgeFade = 1.0;

                return fixed4(finalColor, alpha * edgeFade);
            }
            ENDCG
        }
    }

    FallBack "Sprites/Default"
}
