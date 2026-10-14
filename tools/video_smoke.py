#!/usr/bin/env python3
"""End-to-end check of the video path against the mock host (Python <-> Python): negotiation, packet framing,
frame cadence, keyframe-on-request, clock-sync reply, and that the received bytes are a valid H.264 stream
(decoded by ffmpeg when available). Does NOT exercise the Swift host, VideoToolbox, or the Quest decoder."""
from pathlib import Path
import json,shutil,socket,subprocess,sys,tempfile,time
r=Path(__file__).resolve().parents[1];sys.path.insert(0,str(r/'tools'))
from protocol import VideoFrame,connect,json_packet,read_packet

def run_case(r,td,config,codec_wanted,mock_flags,expect_codec,expect_id,ffmpeg_fmt):
    control=connect(config,'control');video=connect(config,'video')
    control.sendall(json_packet(dict(type='subscribe',sourceID='display:1',streamID=5,fps=60,maxWidth=1280,codec=codec_wanted)))
    control.settimeout(5)
    while True:
        kind,data=read_packet(control);m=json.loads(data)
        if m.get('type')=='subscribed':break
    assert m['codec']==expect_codec,f"asked {codec_wanted}, host answered {m['codec']} (expected {expect_codec})"
    video.settimeout(5);frames=[];t0=time.time()
    while len(frames)<90:
        kind,data=read_packet(video)
        assert kind==4,'expected video packets'
        frames.append(VideoFrame.parse(data))
    elapsed=time.time()-t0
    assert all(f.codec==expect_id for f in frames),'codec byte must match the negotiated codec'
    assert frames[0].is_key and frames[0].flags&2,'stream must start with a keyframe carrying parameter sets'
    assert [f.sequence for f in frames]==list(range(len(frames))),'sequence must be gapless'
    assert all(b.pts_us>=a.pts_us for a,b in zip(frames,frames[1:])),'pts must not go backwards'
    fps=len(frames)/elapsed
    assert 40<fps<75,f'expected ~60 fps cadence, got {fps:.1f}'
    control.sendall(json_packet(dict(type='request_keyframe',streamID=5)))
    for _ in range(10):
        kind,data=read_packet(video);f=VideoFrame.parse(data)
        if f.is_key and f.sequence>frames[-1].sequence:break
    else:raise AssertionError('request_keyframe did not produce a new keyframe')
    decoded='ffmpeg not installed'
    if shutil.which('ffmpeg'):
        out=subprocess.run(['ffmpeg','-v','error','-f',ffmpeg_fmt,'-i','-','-f','null','-'],input=b''.join(f.bitstream for f in frames),capture_output=True)
        assert out.returncode==0 and not out.stderr.strip(),out.stderr.decode()[:300]
        decoded='ffmpeg decoded it cleanly'
    control.close();video.close()
    print(f'PASS [{codec_wanted or "jpeg"} -> {expect_codec}]: {len(frames)} packets at {fps:.0f} fps, gapless, keyframe on request, {decoded}.')

def start_mock(r,td,flags):
    with socket.socket() as s:s.bind(('127.0.0.1',0));port=s.getsockname()[1]
    d=Path(td)/f'mock{port}';d.mkdir()
    proc=subprocess.Popen([sys.executable,str(r/'tools/mock_host.py'),'--directory',str(d),'--create-config','--port',str(port),*flags],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
    for _ in range(100):
        time.sleep(.1)
        if proc.poll() is not None:raise RuntimeError(proc.communicate())
        try:
            with socket.create_connection(('127.0.0.1',port),timeout=.1):break
        except OSError:continue
    else:raise RuntimeError('Mock did not start')
    return proc,json.loads((d/'client-config.json').read_text())

with tempfile.TemporaryDirectory(prefix='spatialmac-video-') as td:
    procs=[]
    try:
        # 1) both codecs offered: each request is honored; clock sync works
        proc,config=start_mock(r,td,['--h264','--h265']);procs.append(proc)
        control=connect(config,'control')
        control.sendall(json_packet(dict(type='ping',requestID='x',t0=1234)));control.settimeout(5)
        while True:
            kind,data=read_packet(control);m=json.loads(data)
            if m.get('type')=='pong':break
        assert m['t0']==1234 and m['t1']>0,'pong must echo t0 and carry host time'
        control.close();time.sleep(.3)
        run_case(r,td,config,'h264',None,'h264',1,'h264');time.sleep(.3)
        run_case(r,td,config,'hevc',None,'hevc',2,'hevc')
        # 2) only H.264 available: an HEVC request falls back to H.264 (same order as the Mac host), never straight to nothing
        proc2,config2=start_mock(r,td,['--h264']);procs.append(proc2)
        run_case(r,td,config2,'hevc',None,'h264',1,'h264')
        print('NOT TESTED: Swift host, VideoToolbox, Quest MediaCodec decoder, real network behavior.')
    finally:
        for p in procs:
            p.terminate()
            try:p.wait(timeout=3)
            except subprocess.TimeoutExpired:p.kill();p.wait()
