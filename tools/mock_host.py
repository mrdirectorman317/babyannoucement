#!/usr/bin/env python3
"""Loopback-only TLS protocol simulator. NO Mac capture and NO remote control.
Use with a throwaway certificate/config created by --create-config. This lets
Unity Editor/probe testing proceed before the native Mac app is integrated.
"""
import argparse,hashlib,hmac,json,secrets,socket,ssl,struct,subprocess,threading,time,uuid
from pathlib import Path
from protocol import Frame,json_packet,pack,read_packet

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
    def __init__(self,directory:Path,fixture:Path):
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
                self.send(role,dict(type='permissions',sharingEnabled=True,controlEnabled=False,speechEnabled=False))
                self.send(role,dict(type='catalog',sources=[self.source]))
            peer.settimeout(None)
            while self.running:
                kind,data=read_packet(peer)
                if kind!=1:continue
                m=json.loads(data);t=m.get('type')
                if role!='control':continue
                if t=='ping':self.send(role,dict(type='pong',requestID=m.get('requestID')))
                elif t=='list_sources':self.send(role,dict(type='catalog',sources=[self.source]))
                elif t=='subscribe' and m.get('sourceID')=='display:1' and int(m.get('streamID',0))>0:
                    with self.lock:
                        stream=int(m['streamID']);g=self.subscriptions.get(stream,(0,0))[0]+1
                        self.subscriptions[stream]=(g,0)
                        self.send(role,dict(type='subscribed',streamID=stream,generation=g))
                elif t=='unsubscribe':
                    with self.lock:self.subscriptions.pop(int(m.get('streamID',0)),None)
                    self.send(role,dict(type='unsubscribed',streamID=m.get('streamID')))
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
    def frames(self):
        while self.running:
            time.sleep(.2)
            with self.lock:
                peer=self.roles.get('video')
                if not peer:continue
                for stream,(generation,sequence) in list(self.subscriptions.items()):
                    f=Frame(stream,sequence,640,360,generation,time.monotonic_ns()//1000,self.jpeg)
                    try:peer.sendall(pack(2,f.payload()))
                    except OSError:break
                    self.subscriptions[stream]=(generation,sequence+1)
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
    p.add_argument('--create-config',action='store_true');p.add_argument('--port',type=int,default=9444)
    p.add_argument('--fixture',type=Path,default=Path(__file__).resolve().parents[1]/'tests/fixtures/calibration.jpg')
    a=p.parse_args()
    if a.create_config:create_config(a.directory,a.port)
    Mock(a.directory,a.fixture).run()
if __name__=='__main__':main()
