"""Standard-library reference for SpatialMac v1. Never executes remote input."""
from __future__ import annotations
import hashlib,hmac,json,socket,ssl,struct
from dataclasses import dataclass
from pathlib import Path
MAGIC=b'SMAC';VERSION=1
LIMITS={1:65536,2:8388608,3:384008,4:4194304}
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

@dataclass(frozen=True)
class VideoFrame:
    """Encoded video access unit (packet kind 4). 28-byte big-endian prefix + Annex-B bitstream."""
    stream_id:int;sequence:int;width:int;height:int;generation:int;pts_us:int;flags:int;codec:int;bitstream:bytes
    KEY=1;PARAMS=2
    @property
    def is_key(self)->bool:return bool(self.flags&1)
    def payload(self)->bytes:
        has_start=self.bitstream[:3]==b'\x00\x00\x01' or self.bitstream[:4]==b'\x00\x00\x00\x01'
        if not(0<self.stream_id<=0xffffffff and 0<self.generation<=0xffffffff and 1<=self.width<=8192 and 1<=self.height<=8192
               and self.codec in (1,2) and not self.flags&~3 and has_start):
            raise ProtocolError('Invalid video frame')
        out=struct.pack('>IIHHIQBBH',self.stream_id,self.sequence,self.width,self.height,self.generation,self.pts_us,self.flags,self.codec,0)+self.bitstream
        if len(out)>LIMITS[4]:raise ProtocolError('Video frame too large')
        return out
    @classmethod
    def parse(cls,payload:bytes)->'VideoFrame':
        if len(payload)<=28 or len(payload)>LIMITS[4]:raise ProtocolError('Invalid video length')
        sid,seq,w,h,gen,pts,flags,codec,reserved=struct.unpack('>IIHHIQBBH',payload[:28])
        if reserved:raise ProtocolError('Reserved bits set')
        f=cls(sid,seq,w,h,gen,pts,flags,codec,payload[28:]);f.payload();return f

def _nal_positions(data:bytes)->list[tuple[int,int]]:
    """(start_code_offset, payload_offset) for every NAL in an Annex-B stream."""
    out=[];i=0
    while i+3<=len(data):
        if data[i:i+3]==b'\x00\x00\x01':out.append((i,i+3));i+=3
        elif data[i:i+4]==b'\x00\x00\x00\x01':out.append((i,i+4));i+=4
        else:i+=1
    return out

def split_access_units(annexb:bytes,codec:int=1)->list[bytes]:
    """Split an Annex-B stream (one slice per picture) into access units. Parameter sets/SEI before a
    picture are kept with that picture, so a keyframe unit starts with its SPS/PPS."""
    nals=_nal_positions(annexb);units=[];begin=0
    for k,(_,payload) in enumerate(nals):
        header=annexb[payload]
        nal_type=header&0x1f if codec==1 else (header>>1)&0x3f
        is_picture=nal_type in (1,5) if codec==1 else nal_type<32
        if is_picture:
            end=nals[k+1][0] if k+1<len(nals) else len(annexb)
            units.append(annexb[begin:end]);begin=end
    return units

def is_key_unit(unit:bytes,codec:int=1)->bool:
    i=0
    while i+3<=len(unit):
        if unit[i:i+3]==b'\x00\x00\x01':n=unit[i+3];i+=3
        elif unit[i:i+4]==b'\x00\x00\x00\x01':n=unit[i+4];i+=4
        else:i+=1;continue
        t=n&0x1f if codec==1 else (n>>1)&0x3f
        if (codec==1 and t==5) or (codec==2 and t in (19,20,21)):return True
    return False

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
