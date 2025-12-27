Shader "Unlit/WorldPanelBoard"
{
    Properties
    {
        _MainTex       ("Texture", 2D) = "white" {}
        _Color         ("Tint", Color) = (1,1,1,1)

        // Kích thước panel theo mét (được set từ WorldPanelPlus)
        _PanelSize     ("Panel Size (W,H)", Vector) = (1,1,0,0)

        // Fade mép (theo mét) — giữ lại hành vi cũ
        _EdgeFadeX     ("Edge Fade X (m)", Float) = 0 // 0.03
        _EdgeFadeY     ("Edge Fade Y (m)", Float) = 0 //0.05
        _EdgeMinAlpha  ("Edge Min Alpha", Range(0,1)) = 0 // 0.40

        // NEW: Bo góc (theo mét) + feather của mép
        _CornerRadius  ("Corner Radius (m)", Float) = 0.03
        _EdgeFeather   ("Edge Feather (m)", Float) = 0.001

        // Content bounds clipping (UV space: left, right, bottom, top)
        _ContentBounds ("Content Bounds (L,R,B,T)", Vector) = (0,1,0,1)
        _EnableClipping ("Enable Clipping", Float) = 0

        // Edge mask for cluster panels (Left, Right, Top, Bottom): 1=show corner, 0=no corner
        _EdgeMask ("Edge Mask (L,R,T,B)", Vector) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }

        Cull Back
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4    _MainTex_ST;
            float4    _Color;

            float4 _PanelSize;   // (W,H,0,0)
            float   _EdgeFadeX;
            float   _EdgeFadeY;
            float   _EdgeMinAlpha;

            float   _CornerRadius;
            float   _EdgeFeather;

            float4  _ContentBounds; // (left, right, bottom, top) in UV space
            float   _EnableClipping;

            float4  _EdgeMask; // (Left, Right, Top, Bottom): 1=show corner, 0=no corner

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

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            // Tính alpha fade viền theo mét quanh cạnh trái/phải & trên/dưới
            float EdgeFade(float2 pMeters, float2 halfSize, float fadeX, float fadeY, float minA)
            {
                // khoảng cách (m) từ tâm theo trục X/Y
                float ax = abs(pMeters.x);
                float ay = abs(pMeters.y);

                // khoảng cách còn lại tới biên (m)
                float rx = halfSize.x - ax;
                float ry = halfSize.y - ay;

                // 0 ở trong, →1 khi sát biên (trong dải fade)
                float ex = saturate(1.0 - saturate(rx / max(1e-6, fadeX)));
                float ey = saturate(1.0 - saturate(ry / max(1e-6, fadeY)));

                // lấy mức fade lớn hơn theo 2 chiều
                float e = max(ex, ey);

                // nội suy alpha về minA tại mép
                return lerp(1.0, minA, e);
            }

            // SDF cho rounded-rect theo mét với EdgeMask support
            // pMeters: toạ độ từ tâm (m), halfSize: nửa kích thước (m), r: bán kính (m)
            // edgeMask: (Left, Right, Top, Bottom) - 1=show corner, 0=no corner
            float RoundRectSDF(float2 pMeters, float2 halfSize, float r, float4 edgeMask)
            {
                // Determine which corner we're in based on position
                // TopLeft: x<0, y>0 -> needs Left(x) AND Top(z) visible
                // TopRight: x>0, y>0 -> needs Right(y) AND Top(z) visible
                // BottomLeft: x<0, y<0 -> needs Left(x) AND Bottom(w) visible
                // BottomRight: x>0, y<0 -> needs Right(y) AND Bottom(w) visible

                float cornerRadius = r;

                // Check which quadrant we're in and apply appropriate corner radius
                if (pMeters.x < 0 && pMeters.y > 0) // Top-Left
                {
                    cornerRadius = (edgeMask.x > 0.5 && edgeMask.z > 0.5) ? r : 0.0;
                }
                else if (pMeters.x > 0 && pMeters.y > 0) // Top-Right
                {
                    cornerRadius = (edgeMask.y > 0.5 && edgeMask.z > 0.5) ? r : 0.0;
                }
                else if (pMeters.x < 0 && pMeters.y < 0) // Bottom-Left
                {
                    cornerRadius = (edgeMask.x > 0.5 && edgeMask.w > 0.5) ? r : 0.0;
                }
                else if (pMeters.x > 0 && pMeters.y < 0) // Bottom-Right
                {
                    cornerRadius = (edgeMask.y > 0.5 && edgeMask.w > 0.5) ? r : 0.0;
                }

                // co lại nửa kích thước để chừa chỗ cho bán kính
                float2 q = abs(pMeters) - (halfSize - cornerRadius);
                // length(max(q,0)) - r  : >0 ngoài bo, <0 trong bo
                return length(max(q, 0.0)) - cornerRadius;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // ---- Content bounds clipping (UV space) ----
                if (_EnableClipping > 0.5)
                {
                    // _ContentBounds = (left, right, bottom, top)
                    float left = _ContentBounds.x;
                    float right = _ContentBounds.y;
                    float bottom = _ContentBounds.z;
                    float top = _ContentBounds.w;

                    // Clip pixels outside content bounds
                    if (i.uv.x < left || i.uv.x > right || i.uv.y < bottom || i.uv.y > top)
                    {
                        clip(-1);
                        return fixed4(0,0,0,0);
                    }
                }

                // ---- Tính toạ độ theo mét (gốc giữa panel) ----
                float2 size     = _PanelSize.xy;
                float2 halfSize = 0.5 * size;

                // UV 0..1  ->  (-W/2..+W/2, -H/2..+H/2) mét
                float2 pMeters = (i.uv - 0.5) * size;

                // ---- Rounded-rect clip + feather with EdgeMask ----
                float r   = max(0.0, _CornerRadius);
                float sdf = RoundRectSDF(pMeters, halfSize, r, _EdgeMask);

                // clip ngoài viền (giữ cạnh mượt bằng feather)
                // dist < 0 => bên trong; dùng smooth edge quanh 0..+_EdgeFeather
                float aRound = 1.0 - smoothstep(0.0, max(1e-6, _EdgeFeather), sdf);
                clip(aRound - 0.001); // bỏ hoàn toàn pixel ngoài bo

                // ---- Lấy màu texture + tint ----
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;

                // ---- Edge fade cũ (mờ dần về mép) ----
                float aEdge = EdgeFade(pMeters, halfSize, _EdgeFadeX, _EdgeFadeY, _EdgeMinAlpha);

                // ---- Hợp alpha: bo góc trước, rồi fade mép ----
                col.a *= aRound;
                col.a *= aEdge;

                return col;
            }
            ENDCG
        }
    }

    FallBack Off
}
