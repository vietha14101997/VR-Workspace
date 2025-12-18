Shader "Custom/GlassGradientBackground"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        
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
            Name "GlassGradientBackground"
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
            
            float _Aspect; // Aspect Ratio (Width/Height)

            // SDF for rounded box with Aspect Ratio correction
            float sdRoundedBoxAspect(float2 uv, float aspect, float radius, float padding)
            {
                float2 center = float2(0.5, 0.5);
                float2 pos = (uv - center);
                pos.x *= aspect; 
                
                float2 halfSize = float2(0.5 * aspect - padding * aspect, 0.5 - padding);
                
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
                
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                
                // Default aspect if not set
                float aspect = (_Aspect > 0.0) ? _Aspect : 1.0;
                
                // SDF with aspect correction
                float dist = sdRoundedBoxAspect(uv, aspect, _CornerRadius, _EdgePadding);
                
                // Hard clip for background to avoid drawing outside the "glass" shape?
                // Actually, for background we just want the shape itself.
                // But if we want soft edges, we can smoothstep.
                // However, background doesn't usually have a glow, it stays INSIDE the border.
                
                // The border shader handles the glow. This shader handles the fill.
                // So if dist > 0, we can basically clip or fade out very quickly.
                
                float alphaMask = 1.0 - smoothstep(-0.005, 0.005, dist);
                if (alphaMask <= 0.01) clip(-1);
                
                // === GRADIENT ===
                float t = uv.x; 
                t = pow(t, 1.2); 
                float angleOffset = (1.0 - uv.y) * 0.15;
                t += angleOffset;
                t = saturate(t);
                
                fixed4 gradColor = lerp(_ColorA, _ColorB, t);
                
                // === CENTER GLOW ===
                float2 centerDist = abs(uv - 0.5);
                float centerGlow = 1.0 - saturate(length(centerDist) / 0.5);
                centerGlow = pow(centerGlow, 1.5) * 0.15;
                
                // Edge fresnel - brighter at edges (inside)
                // dist is negative inside, approaching 0 at edge.
                float edgeFactor = 1.0 - saturate(abs(dist) / 0.2); // 0.2 thickness from edge inwards
                float fresnel = pow(edgeFactor, _FresnelPower);
                fresnel *= _FresnelStrength;
                
                // Combine
                fixed4 finalColor = gradColor;
                finalColor.a = _GlassAlpha + gradColor.a * 0.5;
                finalColor.a *= alphaMask; 
                finalColor.rgb += fresnel + centerGlow;
                
                finalColor *= i.color;
                
                return finalColor;
            }
            ENDCG
        }
    }
    
    FallBack "UI/Default"
}
