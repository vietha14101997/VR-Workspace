Shader "Custom/UIBlurBackground"
{
    Properties
    {
        _Color ("Tint Color", Color) = (1,1,1,1)
        _Radius ("Blur Radius", Range(0, 20)) = 5
        [HideInInspector] _MainTex ("Sprite Texture", 2D) = "white" {}
        
        // UI Masking support
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

        GrabPass
        {
            "_BackgroundTexture"
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord  : TEXCOORD0;
                float4 grabPos  : TEXCOORD1;
            };

            fixed4 _Color;
            sampler2D _MainTex;
            sampler2D _BackgroundTexture;
            float4 _BackgroundTexture_TexelSize;
            float _Radius;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                // Compute Grab Screen Position correctly
                OUT.grabPos = ComputeGrabScreenPos(OUT.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // 1. Sample the sprite mask (Rounded Corners)
                half4 spriteCol = tex2D(_MainTex, IN.texcoord);
                
                // If sprite alpha is 0 (outside rounded corner), clip or return clear
                // Ideally blending handles it, but for GrabPass we might want to manually clip
                // to avoid expensive blur calculations in empty space (though fragment shader runs anyway).
                if(spriteCol.a < 0.01) discard;

                // 2. Sample Background with Blur
                float2 texelSize = _BackgroundTexture_TexelSize.xy * _Radius;
                half4 blurCol = 0;
                
                // Simple 5x5 Sparse Kernel or similar. Let's do a 9-sample box for reasonable cost.
                // Center
                blurCol += tex2Dproj(_BackgroundTexture, IN.grabPos);
                
                // Diagonals
                blurCol += tex2Dproj(_BackgroundTexture, IN.grabPos + float4(texelSize.x, texelSize.y, 0, 0));
                blurCol += tex2Dproj(_BackgroundTexture, IN.grabPos + float4(-texelSize.x, -texelSize.y, 0, 0));
                blurCol += tex2Dproj(_BackgroundTexture, IN.grabPos + float4(texelSize.x, -texelSize.y, 0, 0));
                blurCol += tex2Dproj(_BackgroundTexture, IN.grabPos + float4(-texelSize.x, texelSize.y, 0, 0));
                
                // Cardinals
                blurCol += tex2Dproj(_BackgroundTexture, IN.grabPos + float4(texelSize.x, 0, 0, 0));
                blurCol += tex2Dproj(_BackgroundTexture, IN.grabPos + float4(-texelSize.x, 0, 0, 0));
                blurCol += tex2Dproj(_BackgroundTexture, IN.grabPos + float4(0, texelSize.y, 0, 0));
                blurCol += tex2Dproj(_BackgroundTexture, IN.grabPos + float4(0, -texelSize.y, 0, 0));

                blurCol /= 9.0;
                
                // 3. Mix Blur with Tint
                // IN.color is the Tint color assigned in Image component
                // Use IN.color.a to determine how much "Glass Tint" overlays the "Blur"
                // Usually Frosted Glass = Blur * Tint
                
                fixed4 finalCol = blurCol * IN.color; 
                
                // Or if we want an overlay effect:
                // finalCol = lerp(blurCol, IN.color, IN.color.a);
                // But generally multiplying tint is better for glass loops.
                // Let's rely on standard tint behavior: Blur * Tint.rgb
                // And we force alpha to spriteCol.a to keep the rounded shape.
                
                // To make it look "milky", the tint should contain white and alpha should be high?
                // Actually, if we return full alpha, we block the real background (which is what we want, we replaced it with blur).
                
                finalCol.rgb = lerp(blurCol.rgb, IN.color.rgb, IN.color.a); 
                finalCol.a = spriteCol.a; 

                return finalCol;
            }
            ENDCG
        }
    }
}
