Shader "Custom/GlowingHorizontalLine"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Line Settings)]
        _LineWidth ("Line Width", Range(0.01, 0.3)) = 0.08
        _GlowWidth ("Glow Width", Range(0.05, 0.5)) = 0.25

        [Header(Gradient Colors)]
        _ColorA ("Color A (Left)", Color) = (0.3, 1, 1, 1)
        _ColorB ("Color B (Right)", Color) = (1, 0.4, 1, 1)

        [Header(Glow Layers)]
        _Layer1Alpha ("Core Alpha", Range(0, 2)) = 1.2
        _Layer2Alpha ("Inner Glow Alpha", Range(0, 2)) = 0.8
        _Layer3Alpha ("Outer Glow Alpha", Range(0, 1)) = 0.4
        _Layer4Alpha ("Ambient Alpha", Range(0, 1)) = 0.2

        [Header(Edge Fade)]
        _EdgeFade ("Edge Fade", Range(0, 1)) = 1
        _EdgeFadePower ("Edge Fade Power", Range(0.5, 4)) = 2

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
            "Queue"="Transparent"
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

        Pass
        {
            Name "GlowingHorizontalLine"
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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            float _LineWidth;
            float _GlowWidth;

            fixed4 _ColorA;
            fixed4 _ColorB;

            float _Layer1Alpha;
            float _Layer2Alpha;
            float _Layer3Alpha;
            float _Layer4Alpha;

            float _EdgeFade;
            float _EdgeFadePower;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;

                // Horizontal gradient color (pure gradient, no white)
                fixed3 lineColor = lerp(_ColorA.rgb, _ColorB.rgb, uv.x);

                // Distance from center line (Y = 0.5)
                float distFromCenter = abs(uv.y - 0.5) * 2.0; // Normalize to 0-1

                // Edge alpha fade - only fade alpha at last 20% of both ends to 30%
                float edgeAlphaFade = 1.0;
                if (_EdgeFade > 0.0)
                {
                    float edgeDist = min(uv.x, 1.0 - uv.x); // 0 at edges, 0.5 at center
                    float fadeStart = 0.2; // Start fading at 20% from edge
                    if (edgeDist < fadeStart)
                    {
                        // Fade from 30% at edge to 100% at fadeStart
                        edgeAlphaFade = lerp(0.3, 1.0, edgeDist / fadeStart);
                    }
                }

                // Line intensity based on distance from center
                float lineIntensity = 1.0 - saturate(distFromCenter / _LineWidth);
                lineIntensity = pow(lineIntensity, 0.5); // Soft falloff

                // Glow intensity
                float glowIntensity = 1.0 - saturate(distFromCenter / _GlowWidth);
                glowIntensity = pow(glowIntensity, 1.5);

                // Combine: pure gradient color
                float totalIntensity = max(lineIntensity * _Layer1Alpha, glowIntensity * _Layer2Alpha);
                fixed3 finalRGB = lineColor * totalIntensity;
                float finalAlpha = totalIntensity * edgeAlphaFade;

                // Output
                fixed4 finalColor = fixed4(saturate(finalRGB), saturate(finalAlpha));
                finalColor *= i.color;

                return finalColor;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
