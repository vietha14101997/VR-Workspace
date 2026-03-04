// NV12ToRGBA.shader
// Converts NV12 (YUV 4:2:0 biplanar) decoded by HevcDecoderPlugin to RGBA.
// Used by H265StreamReceiver to blit decoded H265 frames onto a RenderTexture.
//
// Input:
//   _YTex  : Texture2D (R8)  - Y plane, full resolution
//   _UVTex : Texture2D (RG16 or RG16) - interleaved UV plane, half resolution
//
// Color space: BT.709 (HDTV - used by NVENC H265 output)

Shader "VRWorkspace/NV12ToRGBA"
{
    Properties
    {
        _YTex  ("Y Plane",  2D) = "white" {}
        _UVTex ("UV Plane", 2D) = "gray"  {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _YTex;
            sampler2D _UVTex;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Sample Y (luminance) - full resolution
                float y = tex2D(_YTex, i.uv).r;

                // Sample UV (chrominance) - half resolution, interleaved
                // R channel = U (Cb), G channel = V (Cr)
                float2 uv_sample = tex2D(_UVTex, i.uv).rg;
                float u = uv_sample.r;
                float v = uv_sample.g;

                // BT.709 YUV → RGB conversion
                // Y  range: [16/255, 235/255] (limited range from hardware decoder)
                // UV range: [16/255, 240/255]
                // Shift to full range first
                float yp = (y  - 16.0 / 255.0) * (255.0 / 219.0);
                float up = (u  - 128.0 / 255.0) * (255.0 / 224.0);
                float vp = (v  - 128.0 / 255.0) * (255.0 / 224.0);

                // BT.709 matrix
                float r = yp + 1.5748 * vp;
                float g = yp - 0.1873 * up - 0.4681 * vp;
                float b = yp + 1.8556 * up;

                return fixed4(saturate(r), saturate(g), saturate(b), 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
