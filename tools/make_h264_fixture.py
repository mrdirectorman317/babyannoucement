#!/usr/bin/env python3
"""Generate tests/fixtures/motion720p60.h264: 2 seconds of moving test pattern, 1280x720 @ 60 fps, H.264 High,
no B-frames, one slice per picture, SPS/PPS repeated on the single IDR. Used by `mock_host.py --h264` so the
headset's hardware decoder can be exercised with NO Mac. Requires ffmpeg."""
import subprocess,sys
from pathlib import Path
OUT=Path(__file__).resolve().parents[1]/'tests/fixtures/motion720p60.h264'
def main()->int:
    cmd=['ffmpeg','-y','-loglevel','error','-f','lavfi','-i','testsrc2=size=1280x720:rate=60','-t','2','-c:v','libx264',
         '-profile:v','high','-preset','veryfast','-bf','0','-threads','1','-pix_fmt','yuv420p','-b:v','4M','-maxrate','6M','-bufsize','2M',
         '-x264-params','keyint=120:min-keyint=120:scenecut=0:repeat-headers=1:slices=1:aud=0','-f','h264',str(OUT)]
    r=subprocess.run(cmd)
    print('wrote',OUT,OUT.stat().st_size,'bytes') if r.returncode==0 else print('ffmpeg failed',file=sys.stderr)
    return r.returncode
if __name__=='__main__':sys.exit(main())
