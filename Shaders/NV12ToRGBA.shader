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
        [Toggle] _FlipY ("Flip Y", Float) = 0
        [Toggle] _FullRange ("Full Range", Float) = 0
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
            float _FlipY;
            float _FullRange;

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

                if (_FlipY > 0.5) {
                    o.uv.y = 1.0 - v.uv.y;
                }

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
                float yp, up, vp;

                if (_FullRange > 0.5) {
                    // Full Range [0, 1]
                    yp = y;
                    up = u - 0.5;
                    vp = v - 0.5;
                } else {
                    // BT.709 Limited Range (default)
                    // Y  range: [16/255, 235/255]
                    // UV range: [16/255, 240/255]
                    yp = (y - 16.0 / 255.0) * (255.0 / 219.0);
                    up = (u - 128.0 / 255.0) * (255.0 / 224.0);
                    vp = (v - 128.0 / 255.0) * (255.0 / 224.0);
                }

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
