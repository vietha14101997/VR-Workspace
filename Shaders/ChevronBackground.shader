Shader "Custom/ChevronBackground"
{
    // Chevron-shaped background for breadcrumb buttons
    // Right side: normal convex rounded corner (pill shape)
    // Left side: concave (inward) curved corner

    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Shape)]
        _Aspect ("Aspect Ratio", Float) = 3.0
        _EdgePadding ("Edge Padding (UV)", Range(0, 0.1)) = 0.02

        [Header(Background)]
        _BackgroundColor ("Background Color", Color) = (0, 0.9, 1, 1)
        _BackgroundAlpha ("Background Alpha", Range(0, 1)) = 1.0

        [Header(Glow Effect)]
        _EdgeGlow ("Edge Glow Strength", Range(0, 0.5)) = 0.15
        _CenterGlow ("Center Glow Strength", Range(0, 0.5)) = 0.1

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
            Name "ChevronBackground"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

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

            float _Aspect;
            float _EdgePadding;
            fixed4 _BackgroundColor;
            float _BackgroundAlpha;
            float _EdgeGlow;
            float _CenterGlow;
            float _HoverAmount;

            // SDF for chevron shape
            // Right side: full semicircle (convex)
            // Left side: concave curve (inward arc)
            float sdChevron(float2 uv, float aspect, float padding)
            {
                float2 pos = uv - float2(0.5, 0.5);
                pos.x *= aspect;

                float halfW = 0.5 * aspect - padding;
                float halfH = 0.5 - padding;

                // Radius = halfH for full semicircle spanning full height
                float r = halfH;

                // Right side: full semicircle (convex, bulging right)
                // Arc center at (halfW - r, 0), radius r
                if (pos.x > halfW - r)
                {
                    float2 arcCenter = float2(halfW - r, 0.0);
                    float d = length(pos - arcCenter) - r;
                    return d;
                }

                // Left side: concave curve (inward arc)
                // Arc center OUTSIDE button (to the left) creates inward curve
                // Arc passes through corners (-halfW, ±halfH) and deepest point (-halfW + r, 0)
                if (pos.x < -halfW + r)
                {
                    // Arc center at (-halfW, 0) - outside button to the left
                    float2 arcCenter = float2(-halfW, 0.0);
                    float arcDist = length(pos - arcCenter) - r;

                    // For concave: INSIDE circle = CUT OUT (positive SDF)
                    // So we negate: -arcDist
                    float vertBound = abs(pos.y) - halfH;

                    return max(-arcDist, vertBound);
                }

                // Middle: simple top/bottom edges
                return abs(pos.y) - halfH;
            }

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

                float aspect = (_Aspect > 0.0) ? _Aspect : 1.0;

                // SDF
                float dist = sdChevron(uv, aspect, _EdgePadding);

                // Alpha mask with smooth edge
                float alphaMask = 1.0 - smoothstep(-0.02, 0.0, dist);

                // Early out for transparent pixels
                if (alphaMask <= 0.001)
                {
                    return fixed4(0, 0, 0, 0);
                }

                // Hover effect
                float hoverBrightness = 1.0 + _HoverAmount * 0.3;

                // Edge glow (fresnel-like) - brighter near edges
                float edgeFactor = 1.0 - saturate(abs(dist) / 0.15);
                float edgeGlow = edgeFactor * edgeFactor * _EdgeGlow;

                // Center glow - subtle brightness in center
                float2 centerDist = abs(uv - 0.5);
                float centerFactor = 1.0 - saturate(length(centerDist) / 0.5);
                float centerGlow = centerFactor * centerFactor * _CenterGlow;

                // Final color with glow
                fixed4 finalColor = _BackgroundColor;
                finalColor.a = _BackgroundAlpha * alphaMask;
                finalColor.rgb += edgeGlow + centerGlow;
                finalColor.rgb *= hoverBrightness;
                finalColor *= i.color;

                return finalColor;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
