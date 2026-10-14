// Desktop pixels with rounded corners and a faint rim that brightens on hover.
Shader "SpatialMac/PanelSurface" {
    Properties {
        _MainTex("Desktop",2D)="black"{}
        _Opacity("Opacity",Range(0,1))=1
        _Size("Size (m)",Vector)=(0.9,0.5,0,0)
        _Radius("Corner radius (m)",Float)=0.022
        _Hover("Hover",Range(0,1))=0
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
            sampler2D _MainTex;float _Opacity;float4 _Size;float _Radius;float _Hover;
            v2f vert(appdata v){v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;}
            float sdRoundBox(float2 p,float2 b,float r){float2 q=abs(p)-b+r;return length(max(q,0.0))+min(max(q.x,q.y),0.0)-r;}
            half4 frag(v2f i):SV_Target{
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 c=tex2D(_MainTex,i.uv);
                float2 p=(i.uv-0.5)*_Size.xy;
                float d=sdRoundBox(p,_Size.xy*0.5,_Radius);
                float aa=fwidth(d)+1e-5;
                float mask=1.0-smoothstep(-aa,aa,d);
                float rim=smoothstep(-0.003-aa,-0.003,d)*mask;
                c.rgb=lerp(c.rgb,half3(1,1,1),rim*(0.08+_Hover*0.30));
                return half4(c.rgb,_Opacity*mask);
            }
            ENDHLSL
        }
    }
}
