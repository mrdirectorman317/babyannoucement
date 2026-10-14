// "Glass" used for panel backing, buttons, toasts and the home window: a translucent
// rounded rectangle with a bright hairline edge and hover/press response.
// Unlit and cheap on purpose (no grab-pass blur) so it holds frame rate on Quest.
Shader "SpatialMac/Glass" {
    Properties {
        _Color("Fill",Color)=(0.10,0.11,0.14,0.58)
        _EdgeColor("Edge",Color)=(1,1,1,0.32)
        _Size("Size (m)",Vector)=(1,1,0,0)
        _Radius("Corner radius (m)",Float)=0.03
        _EdgeWidth("Edge width (m)",Float)=0.003
        _Hover("Hover",Range(0,1))=0
        _Press("Press",Range(0,1))=0
        _Opacity("Opacity",Range(0,1))=1
    }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
        Pass {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos:SV_POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_OUTPUT_STEREO };
            float4 _Color;float4 _EdgeColor;float4 _Size;float _Radius;float _EdgeWidth;float _Hover;float _Press;float _Opacity;
            v2f vert(appdata v){v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;}
            float sdRoundBox(float2 p,float2 b,float r){float2 q=abs(p)-b+r;return length(max(q,0.0))+min(max(q.x,q.y),0.0)-r;}
            half4 frag(v2f i):SV_Target{
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float r=min(_Radius,min(_Size.x,_Size.y)*0.5);
                float2 p=(i.uv-0.5)*_Size.xy;
                float d=sdRoundBox(p,_Size.xy*0.5,r);
                float aa=fwidth(d)+1e-5;
                float mask=1.0-smoothstep(-aa,aa,d);
                float edge=smoothstep(-_EdgeWidth-aa,-_EdgeWidth,d)*mask;
                float sheen=i.uv.y*0.07;                       // faint top-light gradient
                float3 rgb=_Color.rgb+sheen+_Hover*0.10+_Press*0.18;
                float a=_Color.a+_Hover*0.12+_Press*0.22;
                rgb=lerp(rgb,_EdgeColor.rgb,edge*_EdgeColor.a*(0.65+_Hover*0.35));
                a=saturate(a+edge*_EdgeColor.a*0.6);
                return half4(rgb,a*mask*_Opacity);
            }
            ENDHLSL
        }
    }
}
