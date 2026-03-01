// Sharp Downsample shader for custom mipmap generation.
// Uses Lanczos-2 inspired 4x4 kernel with adjustable sharpness.
// The negative lobes of the kernel preserve edge detail during downscale,
// producing mipmaps that are anti-aliased but NOT blurry like box-filter mipmaps.
//
// Weight formula (1D): w = { -s, 0.5+s, 0.5+s, -s }
//   s=0       → box filter (Unity default GenerateMips)
//   s=0.0625  → Lanczos-2 approximation
//   s=0.10    → good for desktop/text streaming
//   s=0.15    → aggressive sharpening for text-heavy content
//
// 2D weights are separable: w2d[i][j] = w1d[i] * w1d[j]
// Total samples: 16 (4x4 grid), weights always sum to 1.0.

Shader "Hidden/SharpDownsample"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Sharpness ("Sharpness", Float) = 0.1
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize; // (1/w, 1/h, w, h)
            float _Sharpness;

            float4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float2 ts = _MainTex_TexelSize.xy;
                float s = _Sharpness;

                // 1D weights: { -s, 0.5+s, 0.5+s, -s } → always sums to 1.0
                // 2D separable weights (outer product):
                //   Corner (4x): s * s
                //   Edge   (8x): -s * (0.5 + s)
                //   Center (4x): (0.5 + s) * (0.5 + s)
                float wCorner = s * s;
                float wEdge   = -s * (0.5 + s);
                float wCenter = (0.5 + s) * (0.5 + s);

                // 4x4 sample grid centered on the 2x2 source block
                // Offsets: -1.5, -0.5, +0.5, +1.5 texels from center

                // Center 2x2 (weight = wCenter each)
                float4 c00 = tex2D(_MainTex, uv + float2(-0.5, -0.5) * ts);
                float4 c10 = tex2D(_MainTex, uv + float2( 0.5, -0.5) * ts);
                float4 c01 = tex2D(_MainTex, uv + float2(-0.5,  0.5) * ts);
                float4 c11 = tex2D(_MainTex, uv + float2( 0.5,  0.5) * ts);

                // Edge samples - top/bottom (weight = wEdge each)
                float4 eT0 = tex2D(_MainTex, uv + float2(-0.5, -1.5) * ts);
                float4 eT1 = tex2D(_MainTex, uv + float2( 0.5, -1.5) * ts);
                float4 eB0 = tex2D(_MainTex, uv + float2(-0.5,  1.5) * ts);
                float4 eB1 = tex2D(_MainTex, uv + float2( 0.5,  1.5) * ts);

                // Edge samples - left/right (weight = wEdge each)
                float4 eL0 = tex2D(_MainTex, uv + float2(-1.5, -0.5) * ts);
                float4 eL1 = tex2D(_MainTex, uv + float2(-1.5,  0.5) * ts);
                float4 eR0 = tex2D(_MainTex, uv + float2( 1.5, -0.5) * ts);
                float4 eR1 = tex2D(_MainTex, uv + float2( 1.5,  0.5) * ts);

                // Corner samples (weight = wCorner each)
                float4 cTL = tex2D(_MainTex, uv + float2(-1.5, -1.5) * ts);
                float4 cTR = tex2D(_MainTex, uv + float2( 1.5, -1.5) * ts);
                float4 cBL = tex2D(_MainTex, uv + float2(-1.5,  1.5) * ts);
                float4 cBR = tex2D(_MainTex, uv + float2( 1.5,  1.5) * ts);

                // Weighted sum (always sums to 1.0 for any s value)
                float4 result = (c00 + c10 + c01 + c11) * wCenter
                              + (eT0 + eT1 + eB0 + eB1 + eL0 + eL1 + eR0 + eR1) * wEdge
                              + (cTL + cTR + cBL + cBR) * wCorner;

                return saturate(result);
            }
            ENDCG
        }
    }

    FallBack Off
}
