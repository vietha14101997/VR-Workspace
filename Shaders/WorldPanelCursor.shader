Shader "WP/Cursor"
{
    Properties{ _MainTex("Texture",2D)="white"{} _Color("Tint",Color)=(1,1,1,1) }
    SubShader{
        Tags{ "Queue"="Overlay" "RenderType"="Transparent" }
        ZWrite Off
        ZTest Always        // <— vẽ đè, khỏi phụ thuộc depth
        Cull Off
        Lighting Off
        Fog{ Mode Off }
        Blend SrcAlpha OneMinusSrcAlpha

        Pass{
            CGPROGRAM
            #pragma target 2.0     // ES2/ES3 ok
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color;
            struct v2f{ float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            v2f vert(appdata_base v){ v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=TRANSFORM_TEX(v.texcoord,_MainTex); return o; }
            fixed4 frag(v2f i):SV_Target{ return tex2D(_MainTex,i.uv)*_Color; }
            ENDCG
        }
    }
    FallBack Off
}
