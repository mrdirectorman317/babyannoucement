#!/usr/bin/env python3
"""End-to-end check of the video path against the mock host (Python <-> Python): negotiation, packet framing,
frame cadence, keyframe-on-request, clock-sync reply, and that the received bytes are a valid H.264 stream
(decoded by ffmpeg when available). Does NOT exercise the Swift host, VideoToolbox, or the Quest decoder."""
from pathlib import Path
import json,shutil,socket,subprocess,sys,tempfile,time
r=Path(__file__).resolve().parents[1];sys.path.insert(0,str(r/'tools'))
from protocol import VideoFrame,connect,json_packet,read_packet

with tempfile.TemporaryDirectory(prefix='spatialmac-video-') as td:
    d=Path(td)
    with socket.socket() as s:s.bind(('127.0.0.1',0));port=s.getsockname()[1]
    proc=subprocess.Popen([sys.executable,str(r/'tools/mock_host.py'),'--directory',td,'--create-config','--port',str(port),'--h264'],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
    try:
        for _ in range(100):
            time.sleep(.1)
            if proc.poll() is not None:raise RuntimeError(proc.communicate())
            try:
                with socket.create_connection(('127.0.0.1',port),timeout=.1):break
            except OSError:continue
        else:raise RuntimeError('Mock did not start')
        config=json.loads((d/'client-config.json').read_text())
        control=connect(config,'control');video=connect(config,'video')
        control.sendall(json_packet(dict(type='ping',requestID='x',t0=1234)))
        control.settimeout(5)
        while True:
            kind,data=read_packet(control);m=json.loads(data)
            if m.get('type')=='pong':break
        assert m['t0']==1234 and m['t1']>0,'pong must echo t0 and carry host time'
        control.sendall(json_packet(dict(type='subscribe',sourceID='display:1',streamID=5,fps=60,maxWidth=1280,codec='h264')))
        while True:
            kind,data=read_packet(control);m=json.loads(data)
            if m.get('type')=='subscribed':break
        assert m['codec']=='h264' and m['streamID']==5
        video.settimeout(5);frames=[];t0=time.time()
        while len(frames)<90:
            kind,data=read_packet(video)
            assert kind==4,'expected video packets'
            frames.append(VideoFrame.parse(data))
        elapsed=time.time()-t0
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
        if shutil.which('ffmpeg'):
            stream=b''.join(f.bitstream for f in frames)
            out=subprocess.run(['ffmpeg','-v','error','-f','h264','-i','-','-f','null','-'],input=stream,capture_output=True)
            assert out.returncode==0 and not out.stderr.strip(),out.stderr.decode()[:300]
            print('PASS: ffmpeg decoded the received stream with no errors.')
        print(f'PASS: h264 negotiation, {len(frames)} video packets at {fps:.0f} fps, gapless sequence, keyframe on request, clock-sync reply.')
        print('NOT TESTED: Swift host, VideoToolbox, Quest MediaCodec decoder, real network behavior.')
    finally:
        proc.terminate()
        try:proc.wait(timeout=3)
        except subprocess.TimeoutExpired:proc.kill();proc.wait()
