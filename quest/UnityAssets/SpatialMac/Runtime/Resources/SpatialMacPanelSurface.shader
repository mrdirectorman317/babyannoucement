// Desktop pixels with rounded corners and a faint rim that brightens on hover.
Shader "SpatialMac/PanelSurface" {
    Properties {
        _MainTex("Desktop",2D)="black"{}
        _Opacity("Opacity",Range(0,1))=1
        _Size("Size (m)",Vector)=(0.9,0.5,0,0)
        _Radius("Corner radius (m)",Float)=0.022
        _Hover("Hover",Range(0,1))=0
        _UVTex("Chroma",2D)="gray"{}
        _VTex("Chroma V",2D)="gray"{}
        _Mode("0 RGB, 1 NV12, 2 planar",Float)=0
        _YUVScale("YUV uv scale",Vector)=(1,1,1,1)
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
            sampler2D _MainTex;sampler2D _UVTex;sampler2D _VTex;float _Opacity;float4 _Size;float _Radius;float _Hover;float _Mode;float4 _YUVScale;
            v2f vert(appdata v){v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;}
            float sdRoundBox(float2 p,float2 b,float r){float2 q=abs(p)-b+r;return length(max(q,0.0))+min(max(q.x,q.y),0.0)-r;}
            half4 frag(v2f i):SV_Target{
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 c=half4(0,0,0,1);
                if(_Mode<0.5){c=tex2D(_MainTex,i.uv);}
                else{
                    // Decoded video: planes arrive top row first (Unity textures start at the bottom), so flip v.
                    float2 base=float2(i.uv.x,1.0-i.uv.y);
                    float y=tex2D(_MainTex,base*_YUVScale.xy).r;
                    float2 cuv=base*_YUVScale.zw;
                    float2 uvv=_Mode<1.5?tex2D(_UVTex,cuv).rg:float2(tex2D(_UVTex,cuv).r,tex2D(_VTex,cuv).r);
                    // BT.709, limited ("video") range
                    float Y=(y-16.0/255.0)*(255.0/219.0),U=(uvv.x-128.0/255.0)*(255.0/224.0),V=(uvv.y-128.0/255.0)*(255.0/224.0);
                    float3 rgb=saturate(float3(Y+1.5748*V,Y-0.1873*U-0.4681*V,Y+1.8556*U));
                    #ifndef UNITY_COLORSPACE_GAMMA
                    rgb=GammaToLinearSpace(rgb);
                    #endif
                    c=half4(rgb,1);
                }
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
