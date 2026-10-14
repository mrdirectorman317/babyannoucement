#!/usr/bin/env python3
"""Best-effort syntax/type check of the Unity shaders' HLSL blocks WITHOUT Unity.

Extracts each HLSLPROGRAM block, replaces `UnityCG.cginc` with minimal stand-ins for the Unity macros/functions
used, and compiles vertex + fragment entry points with glslangValidator's HLSL front end.
This catches typos and type errors. It does NOT prove Unity accepts the ShaderLab wrapper, the render-pipeline
compatibility, or the visual result.   Requires: glslang-tools (`apt install glslang-tools`).
"""
import re,shutil,subprocess,sys,tempfile
from pathlib import Path
STUB='''
#define UNITY_VERTEX_INPUT_INSTANCE_ID
#define UNITY_VERTEX_OUTPUT_STEREO
#define UNITY_SETUP_INSTANCE_ID(x)
#define UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(x)
#define UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(x)
float4x4 unity_MatrixVP;
float4 UnityObjectToClipPos(float4 v){return mul(unity_MatrixVP,v);}
'''
def main()->int:
    tool=shutil.which('glslangValidator')
    if not tool:print('glslangValidator not installed; skipping');return 0
    root=Path(__file__).resolve().parents[1]/'quest/UnityAssets/SpatialMac/Runtime/Resources'
    failed=0
    for shader in sorted(root.glob('*.shader')):
        text=shader.read_text()
        m=re.search(r'HLSLPROGRAM(.*?)ENDHLSL',text,re.S)
        if not m:print('SKIP',shader.name);continue
        body='\n'.join(l for l in m.group(1).splitlines() if not l.strip().startswith('#pragma') and 'UnityCG.cginc' not in l)
        src=STUB+body
        for stage,entry in (('vert','vert'),('frag','frag')):
            with tempfile.TemporaryDirectory() as td:
                f=Path(td)/f'{shader.stem}.hlsl';f.write_text(src)
                r=subprocess.run([tool,'-D','-V','-e',entry,'-S',stage,str(f),'-o',str(Path(td)/'o.spv')],capture_output=True,text=True)
                ok=r.returncode==0
                print(('OK  ' if ok else 'FAIL'),shader.name,stage)
                if not ok:failed+=1;print(r.stdout[-1500:],r.stderr[-500:])
    return 1 if failed else 0
if __name__=='__main__':sys.exit(main())
