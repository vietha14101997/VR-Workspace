Shader "Custom/GlassGradientBackgroundOverlay"
{
    // Same as GlassGradientBackground but with higher render queue
    // so GrabPass captures UI elements behind it (like VRMenuFrame)
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Glassmorphism)]
        _BlurEnabled ("Enable Glassmorphism", Range(0, 1)) = 0
        _BlurRadius ("Blur Intensity", Range(0, 40)) = 15
        _BlurIterations ("Blur Quality", Range(1, 8)) = 4
        _GlassOpacity ("Glass Opacity", Range(0, 1)) = 0.25
        _TintStrength ("Tint Strength", Range(0, 1)) = 0.1
        _InnerGlow ("Inner Glow", Range(0, 0.5)) = 0.15
        _Brightness ("Brightness", Range(0.9, 1.3)) = 1.05
        _Saturation ("Saturation", Range(0.5, 1)) = 0.85

        [Header(Rounded Corners)]
        _CornerRadius ("Corner Radius (UV)", Range(0.01, 0.2)) = 0.07
        _EdgePadding ("Edge Padding (UV)", Range(0, 0.2)) = 0.05

        [Header(Gradient)]
        _ColorA ("Color A (Cyan)", Color) = (0.3, 0.85, 1, 0.15)
        _ColorB ("Color B (Purple)", Color) = (0.7, 0.4, 1, 0.2)
        _GradientOffset ("Gradient Offset", Range(-0.5, 0.5)) = 0.2
        _GradientAngle ("Gradient Angle", Range(-45, 45)) = -15
        _CyanRatio ("Cyan Ratio", Range(0.3, 0.9)) = 0.7

        [Header(Glass Effect)]
        _GlassAlpha ("Base Alpha", Range(0, 0.5)) = 0.1
        _FresnelPower ("Fresnel Power", Range(1, 5)) = 2.5
        _FresnelStrength ("Fresnel Strength", Range(0, 0.3)) = 0.1

        [Header(Hover State)]
        _HoverAmount ("Hover Amount", Range(0, 1)) = 0

        // UI Masking
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent+100"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        // GrabPass with unique name for overlay elements
        GrabPass
        {
            "_GlassOverlayGrabTexture"
        }

        Pass
        {
            Name "GlassGradientBackgroundOverlay"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 grabPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            // Glassmorphism variables
            sampler2D _GlassOverlayGrabTexture;
            float4 _GlassOverlayGrabTexture_TexelSize;
            float _BlurEnabled;
            float _BlurRadius;
            float _BlurIterations;
            float _GlassOpacity;
            float _TintStrength;
            float _InnerGlow;
            float _Brightness;
            float _Saturation;

            float _CornerRadius;
            float _EdgePadding;
            fixed4 _ColorA;
            fixed4 _ColorB;
            float _GradientOffset;
            float _GradientAngle;
            float _CyanRatio;
            float _GlassAlpha;
            float _FresnelPower;
            float _FresnelStrength;
            float _HoverAmount;

            float _Aspect;

            // SDF with Wide formula: padding NOT scaled by aspect to match GlowingElementBorder shader
            float sdRoundedBoxAspect(float2 uv, float aspect, float radius, float padding)
            {
                float2 center = float2(0.5, 0.5);
                float2 pos = (uv - center);
                pos.x *= aspect;

                // Wide formula: padding is uniform in scaled space (matches border shader)
                float2 halfSize = float2(0.5 * aspect - padding, 0.5 - padding);

                float2 d = abs(pos) - halfSize + radius;
                return min(max(d.x, d.y), 0.0) + length(max(d, 0.0)) - radius;
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                o.grabPos = ComputeGrabScreenPos(o.vertex);

                return o;
            }

            half4 GlassmorphismBlur(float4 grabPos)
            {
                float2 texelSize = _GlassOverlayGrabTexture_TexelSize.xy * _BlurRadius;
                half4 blurColor = half4(0, 0, 0, 0);
                float totalWeight = 0;

                int iterations = (int)_BlurIterations;
                float weights[8] = {1.0, 0.9, 0.75, 0.6, 0.45, 0.3, 0.18, 0.08};

                for (int ring = 0; ring < iterations; ring++)
                {
                    float offset = (ring + 1) * 1.2;
                    float weight = weights[ring];

                    blurColor += tex2Dproj(_GlassOverlayGrabTexture, grabPos + float4(texelSize.x * offset, 0, 0, 0)) * weight;
                    blurColor += tex2Dproj(_GlassOverlayGrabTexture, grabPos + float4(-texelSize.x * offset, 0, 0, 0)) * weight;
                    blurColor += tex2Dproj(_GlassOverlayGrabTexture, grabPos + float4(0, texelSize.y * offset, 0, 0)) * weight;
                    blurColor += tex2Dproj(_GlassOverlayGrabTexture, grabPos + float4(0, -texelSize.y * offset, 0, 0)) * weight;

                    float diag = offset * 0.707;
                    blurColor += tex2Dproj(_GlassOverlayGrabTexture, grabPos + float4(texelSize.x * diag, texelSize.y * diag, 0, 0)) * weight;
                    blurColor += tex2Dproj(_GlassOverlayGrabTexture, grabPos + float4(-texelSize.x * diag, texelSize.y * diag, 0, 0)) * weight;
                    blurColor += tex2Dproj(_GlassOverlayGrabTexture, grabPos + float4(texelSize.x * diag, -texelSize.y * diag, 0, 0)) * weight;
                    blurColor += tex2Dproj(_GlassOverlayGrabTexture, grabPos + float4(-texelSize.x * diag, -texelSize.y * diag, 0, 0)) * weight;

                    totalWeight += weight * 8;
                }

                blurColor += tex2Dproj(_GlassOverlayGrabTexture, grabPos) * 1.5;
                totalWeight += 1.5;

                half4 result = blurColor / totalWeight;
                result.rgb *= _Brightness;

                float lum = dot(result.rgb, float3(0.299, 0.587, 0.114));
                result.rgb = lerp(float3(lum, lum, lum), result.rgb, _Saturation);

                return result;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float aspect = (_Aspect > 0.0) ? _Aspect : 1.0;

                float dist = sdRoundedBoxAspect(uv, aspect, _CornerRadius, _EdgePadding);

                float alphaMask = 1.0 - smoothstep(-0.01, 0.0, dist);
                if (alphaMask <= 0.001) clip(-1);

                float t = uv.x;
                t = pow(t, 1.2);
                float angleOffset = (1.0 - uv.y) * 0.15;
                t += angleOffset;
                t = saturate(t);
                fixed4 gradColor = lerp(_ColorA, _ColorB, t);

                float hoverBrightness = 1.0 + _HoverAmount * 0.3;
                float hoverAlphaBoost = _HoverAmount * 0.1;

                fixed4 finalColor;

                if (_BlurEnabled > 0.5)
                {
                    half4 blurredBg = GlassmorphismBlur(i.grabPos);

                    float3 glassColor = blurredBg.rgb;

                    float3 tint = lerp(float3(1,1,1), gradColor.rgb * 1.5, _TintStrength);
                    glassColor *= tint;

                    float edgeDist = saturate(-dist / 0.15);
                    float innerGlow = pow(edgeDist, 2.0) * _InnerGlow;
                    glassColor += float3(1, 1, 1) * innerGlow;

                    float fresnelEdge = pow(edgeDist, _FresnelPower) * _FresnelStrength * 0.5;
                    glassColor += gradColor.rgb * fresnelEdge;

                    glassColor *= hoverBrightness;

                    float3 glassOverlay = gradColor.rgb * 0.3;
                    glassColor = lerp(glassColor, glassColor + glassOverlay, _GlassOpacity);

                    finalColor.rgb = glassColor;
                    finalColor.a = alphaMask;
                }
                else
                {
                    float2 centerDist = abs(uv - 0.5);
                    float centerGlow = 1.0 - saturate(length(centerDist) / 0.5);
                    centerGlow = pow(centerGlow, 1.5) * 0.15;

                    float edgeFactor = 1.0 - saturate(abs(dist) / 0.2);
                    float fresnel = pow(edgeFactor, _FresnelPower) * _FresnelStrength;

                    finalColor = gradColor;
                    finalColor.a = _GlassAlpha + gradColor.a * 0.5 + hoverAlphaBoost;
                    finalColor.a *= alphaMask;
                    finalColor.rgb += fresnel + centerGlow;
                    finalColor.rgb *= hoverBrightness;
                }

                finalColor *= i.color;

                return finalColor;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}