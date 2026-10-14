#!/usr/bin/env python3
"""Loopback-only TLS protocol simulator. NO Mac capture and NO remote control.
Use with a throwaway certificate/config created by --create-config. This lets
Unity Editor/probe testing proceed before the native Mac app is integrated.
"""
import argparse,hashlib,hmac,json,secrets,socket,ssl,struct,subprocess,threading,time,uuid
from pathlib import Path
from protocol import Frame,VideoFrame,json_packet,pack,read_packet,split_access_units

DEMO_NOTES=[
    (4,dict(type='notification',notificationID='demo-1',title='Sam',body='Running 5 min late - save me a seat?',appName='Messages',category='message',replyable=True)),
    (14,dict(type='notification',notificationID='demo-2',title='Calendar',body='Design review starts in 10 minutes',appName='SpatialMac',category='custom',replyable=False)),
]

def create_config(directory:Path,port:int):
    directory.mkdir(parents=True,exist_ok=True)
    if (directory/'client-config.json').exists():raise ValueError('Refusing to overwrite existing test pairing')
    subprocess.run(['openssl','req','-x509','-newkey','rsa:2048','-sha256','-nodes','-days','2',
                    '-subj','/CN=spatialmac.local','-keyout',str(directory/'key.pem'),'-out',str(directory/'cert.pem')],check=True,capture_output=True)
    der=ssl.PEM_cert_to_DER_cert((directory/'cert.pem').read_text())
    config=dict(host='127.0.0.1',port=port,token=secrets.token_hex(32),clientID=str(uuid.uuid4()),certificateSHA256=hashlib.sha256(der).hexdigest(),autoConnect=True)
    (directory/'client-config.json').write_text(json.dumps(config,indent=2))
    for path in directory.iterdir():path.chmod(0o600)
    directory.chmod(0o700)

class Mock:
    def __init__(self,directory:Path,fixture:Path,demo:bool=False,h264:Path|None=None,h265:Path|None=None):
        self.demo=demo
        # codec name -> (wire codec id, access units). A codec that was not loaded is not offered; subscribers fall back.
        self.video={}
        if h264 and h264.exists():self.video['h264']=(1,split_access_units(h264.read_bytes(),1))
        if h265 and h265.exists():self.video['hevc']=(2,split_access_units(h265.read_bytes(),2))
        self.config=json.loads((directory/'client-config.json').read_text());self.jpeg=fixture.read_bytes()
        self.context=ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER);self.context.minimum_version=ssl.TLSVersion.TLSv1_2
        self.context.load_cert_chain(directory/'cert.pem',directory/'key.pem')
        self.source=dict(id='display:1',kind='display',title='Calibration fixture (NOT your Mac)',app='Mock',pid=0,bounds=dict(x=0,y=0,width=640,height=360))
        self.lock=threading.RLock();self.roles={};self.subscriptions={};self.running=True
    def send(self,role,message):
        with self.lock:
            peer=self.roles.get(role)
            if peer:peer.sendall(json_packet(message))
    def handle(self,raw):
        role=None;peer=None
        try:
            peer=self.context.wrap_socket(raw,server_side=True);peer.settimeout(8)
            kind,data=read_packet(peer);m=json.loads(data)
            if kind!=1 or m.get('type')!='hello' or not hmac.compare_digest(str(m.get('token','')),self.config['token']):return
            if m.get('clientID')!=self.config['clientID'] or m.get('role') not in ('control','video','audio'):return
            role=m['role']
            with self.lock:
                if role in self.roles:return
                if role!='control' and 'control' not in self.roles:return
                self.roles[role]=peer
                peer.sendall(json_packet(dict(type='welcome',role=role)))
            if role=='control':
                self.send(role,dict(type='permissions',sharingEnabled=True,controlEnabled=False,speechEnabled=False,
                    assistantEnabled=self.demo,messagesEnabled=self.demo,replyEnabled=self.demo))
                if self.demo:threading.Thread(target=self.demo_script,daemon=True).start()
                self.send(role,dict(type='catalog',sources=[self.source]))
            peer.settimeout(None)
            while self.running:
                kind,data=read_packet(peer)
                if kind!=1:continue
                m=json.loads(data);t=m.get('type')
                if role!='control':continue
                if t=='ping':self.send(role,dict(type='pong',requestID=m.get('requestID'),t0=m.get('t0'),t1=time.monotonic_ns()//1000))
                elif t=='request_keyframe':
                    with self.lock:
                        sub=self.subscriptions.get(int(m.get('streamID',0)))
                        if sub:sub['idx']=0
                elif t=='list_sources':self.send(role,dict(type='catalog',sources=[self.source]))
                elif t=='subscribe' and m.get('sourceID')=='display:1' and int(m.get('streamID',0))>0:
                    with self.lock:
                        stream=int(m['streamID']);g=self.subscriptions.get(stream,{'g':0})['g']+1
                        want=m.get('codec')
                        # Same fallback order as the real host: HEVC -> H.264 -> JPEG
                        codec=next((c for c in ((want,'h264') if want=='hevc' else (want,)) if c in self.video),'jpeg')
                        self.subscriptions[stream]=dict(g=g,seq=0,codec=codec,idx=0)
                        self.send(role,dict(type='subscribed',streamID=stream,generation=g,codec=codec))
                elif t=='unsubscribe':
                    with self.lock:self.subscriptions.pop(int(m.get('streamID',0)),None)
                    self.send(role,dict(type='unsubscribed',streamID=m.get('streamID')))
                elif self.demo and t=='assistant_ask':
                    threading.Thread(target=self.demo_assistant,args=(m,),daemon=True).start()
                elif self.demo and t=='confirm':
                    ok=m.get('action')=='approve'
                    self.send(role,dict(type='notification',notificationID='demo-c',title='Demo',body='Approved - a real host would act now.' if ok else 'Denied - nothing happened.',appName='SpatialMac',category='custom',replyable=False))
                elif self.demo and t=='message_reply':
                    self.send(role,dict(type='notification',notificationID='demo-r',title='Reply sent (demo)',body=str(m.get('text',''))[:80],appName='Messages',category='custom',replyable=False))
                elif self.demo and t=='dismiss':pass
                elif t not in ('release_all',):self.send(role,dict(type='error',error='Read-only fixture server: no input is implemented.'))
        except (OSError,EOFError,ValueError):pass
        finally:
            with self.lock:
                if role and self.roles.get(role) is peer:
                    self.roles.pop(role,None)
                    if role=='control':
                        self.subscriptions.clear()
                        for other in list(self.roles.values()):
                            try:other.shutdown(socket.SHUT_RDWR);other.close()
                            except OSError:pass
                        self.roles.clear()
            try:(peer or raw).close()
            except OSError:pass
    def demo_script(self):
        start=time.monotonic()
        for delay,note in DEMO_NOTES:
            time.sleep(max(0,delay-(time.monotonic()-start)))
            self.send('control',note)
    def demo_assistant(self,m):
        rid=m.get('requestID');text=str(m.get('text','')).lower()
        self.send('control',dict(type='assistant_state',action='thinking',requestID=rid));time.sleep(1.2)
        if 'safari' in text or 'open' in text:
            self.send('control',dict(type='confirm_request',confirmID=str(uuid.uuid4()),title='Confirm action',body='Open Safari on the Mac'))
            self.send('control',dict(type='assistant_reply',requestID=rid,text='I can open Safari - please approve it in the headset.'))
        elif 'room' in text or 'passthrough' in text:
            self.send('control',dict(type='ui_command',command='set_environment',arg='passthrough'))
            self.send('control',dict(type='assistant_reply',requestID=rid,text='Showing your room.'))
        else:
            self.send('control',dict(type='assistant_reply',requestID=rid,text='This is the demo assistant. With a real OpenAI key on the Mac, ChatGPT answers here and can change your workspace.'))
    def frames(self):
        tick=0;period=1/60;next_at=time.monotonic()
        while self.running:
            next_at+=period;time.sleep(max(0,next_at-time.monotonic()));tick+=1
            with self.lock:
                peer=self.roles.get('video')
                if not peer:continue
                for stream,sub in list(self.subscriptions.items()):
                    try:
                        if sub['codec'] in self.video:
                            codec_id,units=self.video[sub['codec']]
                            unit=units[sub['idx']%len(units)]
                            key=sub['idx']%len(units)==0
                            flags=(VideoFrame.KEY|VideoFrame.PARAMS) if key else 0
                            f=VideoFrame(stream,sub['seq'],1280,720,sub['g'],time.monotonic_ns()//1000,flags,codec_id,unit)
                            peer.sendall(pack(4,f.payload()));sub['idx']+=1
                        elif tick%12==0:
                            f=Frame(stream,sub['seq'],640,360,sub['g'],time.monotonic_ns()//1000,self.jpeg)
                            peer.sendall(pack(2,f.payload()))
                        else:continue
                        sub['seq']+=1
                    except OSError:break
    def run(self):
        threading.Thread(target=self.frames,daemon=True).start()
        with socket.socket() as server:
            server.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1)
            server.bind(('127.0.0.1',self.config['port']));server.listen(8)
            print('READ-ONLY MOCK ready on loopback',self.config['port'],flush=True)
            while True:
                peer,_=server.accept();threading.Thread(target=self.handle,args=(peer,),daemon=True).start()

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--directory',type=Path,required=True)
    p.add_argument('--create-config',action='store_true');p.add_argument('--demo',action='store_true',help='simulate notifications and the assistant (still no Mac input)');p.add_argument('--port',type=int,default=9444)
    fx=Path(__file__).resolve().parents[1]/'tests/fixtures'
    p.add_argument('--h264',type=Path,default=None,nargs='?',const=fx/'motion720p60.h264',help='offer a real 1280x720@60 H.264 test clip to clients that ask for h264')
    p.add_argument('--h265',type=Path,default=None,nargs='?',const=fx/'motion720p60.h265',help='offer a real 1280x720@60 HEVC test clip to clients that ask for hevc')
    p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'tests/fixtures/calibration.jpg')
    a=p.parse_args()
    if a.create_config:create_config(a.directory,a.port)
    Mock(a.directory,a.fixture,a.demo,a.h264,a.h265).run()
if __name__=='__main__':main()
