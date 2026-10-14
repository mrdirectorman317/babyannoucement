// Inside-out sphere around the user that dims the surroundings (passthrough or virtual).
Shader "SpatialMac/Veil" {
    Properties { _Level("Dim level",Range(0,1))=0 }
    SubShader {
        Tags { "Queue"="Transparent-100" "RenderType"="Transparent" }
        Cull Front ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha
        Pass {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION;UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos:SV_POSITION;UNITY_VERTEX_OUTPUT_STEREO };
            float _Level;
            v2f vert(appdata v){v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.pos=UnityObjectToClipPos(v.vertex);return o;}
            half4 frag(v2f i):SV_Target{UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);return half4(0.01,0.012,0.02,_Level);}
            ENDHLSL
        }
    }
}
