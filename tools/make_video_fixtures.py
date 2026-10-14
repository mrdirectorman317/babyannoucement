#!/usr/bin/env python3
"""Generate the mock host's video fixtures (needs ffmpeg with libx264 and libx265):
  tests/fixtures/motion720p60.h264  - 2 s, 1280x720 @ 60 fps, H.264 High, no B-frames, one slice/picture
  tests/fixtures/motion720p60.h265  - the same pattern as HEVC Main, closed GOP, no B-frames
Both start with a single keyframe that carries the parameter sets in-band, like the Mac host's encoder output.
Used by `mock_host.py --h264 --h265` to exercise the headset's hardware decoders with NO Mac."""
import subprocess,sys
from pathlib import Path
DIR=Path(__file__).resolve().parents[1]/'tests/fixtures'
SRC=['-f','lavfi','-i','testsrc2=size=1280x720:rate=60','-t','2','-pix_fmt','yuv420p']
JOBS={
 'motion720p60.h264':['-c:v','libx264','-profile:v','high','-preset','veryfast','-bf','0','-threads','1','-b:v','4M','-maxrate','6M','-bufsize','2M',
    '-x264-params','keyint=120:min-keyint=120:scenecut=0:repeat-headers=1:slices=1:aud=0','-f','h264'],
 'motion720p60.h265':['-c:v','libx265','-preset','veryfast','-b:v','2800k','-maxrate','4200k','-bufsize','1400k',
    '-x265-params','keyint=120:min-keyint=120:scenecut=0:bframes=0:repeat-headers=1:open-gop=0:no-info=1:log-level=error:frame-threads=1','-f','hevc'],
}
def main()->int:
    DIR.mkdir(parents=True,exist_ok=True);bad=0
    for name,args in JOBS.items():
        out=DIR/name
        r=subprocess.run(['ffmpeg','-y','-loglevel','error',*SRC,*args,str(out)])
        if r.returncode:print('ffmpeg failed for',name,file=sys.stderr);bad+=1
        else:print('wrote',out,out.stat().st_size,'bytes')
    return bad
if __name__=='__main__':sys.exit(main())
