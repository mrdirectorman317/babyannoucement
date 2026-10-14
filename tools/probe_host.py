#!/usr/bin/env python3
"""Read-only pairing/catalog probe; --frame receives one approved source image.
Does not request Accessibility and never sends mouse, key, or shell commands.
"""
import argparse,json,time
from pathlib import Path
from protocol import connect,json_packet,read_packet,Frame

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--config',type=Path,required=True)
    p.add_argument('--frame',metavar='SOURCE_ID')
    p.add_argument('--output',type=Path,default=Path('frame.jpg'))
    a=p.parse_args();config=json.loads(a.config.read_text())
    with connect(config) as control:
        control.sendall(json_packet(dict(type='list_sources')))
        # Native host sends an initial catalog after welcome; the first is enough.
        while True:
            kind,data=read_packet(control)
            if kind!=1:raise ValueError('Expected JSON')
            message=json.loads(data)
            if message.get('type')=='catalog':
                print(json.dumps(message,indent=2));break
        if not a.frame:return
        with connect(config,'video') as video:
            control.sendall(json_packet(dict(type='subscribe',sourceID=a.frame,streamID=1,fps=5,maxWidth=640)))
            control.sendall(json_packet(dict(type='ping',requestID='probe')))
            kind,data=read_packet(video)
            if kind!=2:raise ValueError('Expected JPEG frame')
            frame=Frame.parse(data);a.output.write_bytes(frame.jpeg)
            print(f'Received {frame.width} x {frame.height}; generation {frame.generation}; wrote {a.output}')
            control.sendall(json_packet(dict(type='unsubscribe',streamID=1)))
if __name__=='__main__':main()
