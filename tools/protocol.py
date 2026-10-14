"""Standard-library reference for SpatialMac v1. Never executes remote input."""
from __future__ import annotations
import hashlib,hmac,json,socket,ssl,struct
from dataclasses import dataclass
from pathlib import Path
MAGIC=b'SMAC';VERSION=1
LIMITS={1:65536,2:8388608,3:384008}
class ProtocolError(ValueError):pass

def header(kind:int,n:int)->bytes:
    if kind not in LIMITS or not 0<=n<=LIMITS[kind]:raise ProtocolError('Invalid kind/length')
    return struct.pack('>4sBBBBI',MAGIC,VERSION,kind,0,0,n)

def parse_header(data:bytes)->tuple[int,int]:
    if len(data)!=12:raise ProtocolError('Truncated header')
    magic,ver,kind,a,b,n=struct.unpack('>4sBBBBI',data)
    if magic!=MAGIC or ver!=VERSION or a or b:raise ProtocolError('Invalid header')
    header(kind,n)
    return kind,n

def pack(kind:int,payload:bytes)->bytes:return header(kind,len(payload))+payload

def json_packet(value:dict)->bytes:
    return pack(1,json.dumps(value,ensure_ascii=False,separators=(',',':'),allow_nan=False).encode('utf-8'))

def read_exact(stream,n:int)->bytes:
    result=bytearray()
    while len(result)<n:
        part=stream.recv(n-len(result))
        if not part:raise EOFError('Connection ended inside packet')
        result.extend(part)
    return bytes(result)

def read_packet(stream)->tuple[int,bytes]:
    kind,n=parse_header(read_exact(stream,12));return kind,read_exact(stream,n)

@dataclass(frozen=True)
class Frame:
    stream_id:int;sequence:int;width:int;height:int;generation:int;captured_us:int;jpeg:bytes
    def payload(self)->bytes:
        if not(0<self.stream_id<=0xffffffff and 0<self.generation<=0xffffffff and
               1<=self.width<=4096 and 1<=self.height<=4096 and self.jpeg.startswith(b'\xff\xd8')):
            raise ProtocolError('Invalid frame')
        out=struct.pack('>IIHHIQ',self.stream_id,self.sequence,self.width,self.height,self.generation,self.captured_us)+self.jpeg
        if len(out)>LIMITS[2]:raise ProtocolError('Frame too large')
        return out
    @classmethod
    def parse(cls,payload:bytes)->Frame:
        if len(payload)<26 or len(payload)>LIMITS[2]:raise ProtocolError('Invalid frame length')
        f=cls(*struct.unpack('>IIHHIQ',payload[:24]),payload[24:]);f.payload();return f

def pcm_payload(samples:bytes)->bytes:
    if len(samples)%2 or len(samples)>384000:raise ProtocolError('PCM must be <=12 seconds of mono PCM16')
    return struct.pack('>II',16000,len(samples)//2)+samples

def check_pcm(payload:bytes)->tuple[int,bytes]:
    if len(payload)<8:raise ProtocolError('Truncated PCM')
    rate,count=struct.unpack('>II',payload[:8])
    if rate!=16000 or count>192000 or len(payload)!=8+count*2:raise ProtocolError('Invalid PCM')
    return rate,payload[8:]

def connect(config:dict,role:str='control')->ssl.SSLSocket:
    """Pin the exact leaf certificate BEFORE transmitting the secret token.

    The self-signed leaf intentionally has no public CA chain. This is NOT an
    accept-any-certificate fallback; SHA-256 pin mismatch aborts immediately.
    """
    if role not in ('control','video','audio'):raise ProtocolError('Unknown lane')
    context=ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    context.check_hostname=False;context.verify_mode=ssl.CERT_NONE
    context.minimum_version=ssl.TLSVersion.TLSv1_2
    raw=socket.create_connection((config['host'],config['port']),timeout=8)
    try:
        tls=context.wrap_socket(raw,server_hostname='spatialmac.local')
        actual=hashlib.sha256(tls.getpeercert(binary_form=True)).hexdigest()
        if not hmac.compare_digest(actual,config['certificateSHA256'].lower()):
            tls.close();raise ProtocolError('Pinned certificate mismatch; token was not sent')
        tls.sendall(json_packet(dict(type='hello',token=config['token'],clientID=config['clientID'],role=role)))
        kind,data=read_packet(tls);answer=json.loads(data)
        if kind!=1 or answer.get('type')!='welcome' or answer.get('role')!=role:
            tls.close();raise ProtocolError('Pairing rejected')
        return tls
    except BaseException:raw.close();raise
