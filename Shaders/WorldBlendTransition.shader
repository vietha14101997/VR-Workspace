Shader "Custom/WorldBlendTransition"
{
    Properties
    {
        _RealWorldTex ("Real World Texture", 2D) = "white" {}
        _VirtualWorldTex ("Virtual World Texture", 2D) = "white" {}
        _BlendFactor ("Blend Factor", Range(0, 1)) = 0
        _FadeEdgeSmooth ("Fade Edge Smoothness", Range(0, 1)) = 0.1
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Transparent+1000" }
        
        ZWrite Off
        ZTest Always
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _RealWorldTex;
            sampler2D _VirtualWorldTex;
            float _BlendFactor;
            float _FadeEdgeSmooth;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample both textures
                fixed4 realColor = tex2D(_RealWorldTex, i.uv);
                fixed4 virtualColor = tex2D(_VirtualWorldTex, i.uv);

                // Calculate fade effect based on blend factor
                // When _BlendFactor is 0, show virtual world fully
                // When _BlendFactor is 1, show real world fully
                
                // Ensure real world is visible when blend factor is high
                // This is critical for the black screen issue
                if (_BlendFactor > 0.99f)
                {
                    // Force real world to be fully visible
                    return realColor;
                }
                else if (_BlendFactor < 0.01f)
                {
                    // Force virtual world to be fully visible
                    return virtualColor;
                }
                else
                {
                    // Create a smooth transition effect with a radial wipe
                    float2 center = float2(0.5, 0.5);
                    float dist = distance(i.uv, center);
                    
                    // Normalize distance to [0,1] range (assuming max distance is ~0.7)
                    dist = saturate(dist / 0.7);
                    
                    // Create a smooth transition edge
                    float transitionEdge = _BlendFactor - _FadeEdgeSmooth * 0.5;
                    float transitionWidth = _FadeEdgeSmooth;
                    
                    // Calculate local blend factor with smooth edge
                    float localBlend = smoothstep(transitionEdge, transitionEdge + transitionWidth, dist);
                    
                    // Combine global and local blend factors
                    float finalBlend = lerp(localBlend, _BlendFactor, 0.7);
                    
                    // Use proper linear interpolation for transition
                    return lerp(virtualColor, realColor, finalBlend);
                }
            }
            ENDCG
        }
    }
}
