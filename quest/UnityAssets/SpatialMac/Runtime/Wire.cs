using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SpatialMac {
    public enum PacketKind : byte { Json=1, Jpeg=2, Pcm=3 }
    [Serializable] public class RectDTO { public double x,y,width,height; }
    [Serializable] public class SourceDTO {
        public string id,kind,title,app; public int pid; public RectDTO bounds;
    }
    [Serializable] public class Message {
        public string type,token,clientID,role,sourceID,action,text,appID,error,requestID;
        public string title,body,appName,category,notificationID,confirmID,command,arg;
        public uint streamID,generation; public int fps,maxWidth,button,keyCode;
        public ulong modifiers; public double u,v,dx,dy,value;
        public bool controlEnabled,sharingEnabled,speechEnabled,assistantEnabled,messagesEnabled,replyEnabled,replyable;
        public SourceDTO[] sources;
        public Message(string type){this.type=type;}
    }
    [Serializable] public class ClientConfig {
        public string host="127.0.0.1", certificateSHA256="", token="", clientID="";
        public int port=9443; public bool autoConnect=true;
        public void Validate() {
            if(string.IsNullOrWhiteSpace(host)||port<1||port>65535)throw new FormatException("Invalid host/port.");
            if(!IsHex64(certificateSHA256)||!IsHex64(token))throw new FormatException("Pair using a 64-hex certificate pin and token. Never disable pin verification.");
            if(!Guid.TryParse(clientID,out _))throw new FormatException("Invalid paired client ID.");
        }
        static bool IsHex64(string s) {
            if(s==null||s.Length!=64)return false;
            foreach(char c in s)if(!Uri.IsHexDigit(c))return false;return true;
        }
    }
    public sealed class Packet { public PacketKind Kind; public byte[] Payload; }
    public sealed class JpegFrame {
        public uint StreamID,Sequence,Generation; public ushort Width,Height;
        public ulong CapturedMicros; public byte[] Jpeg;
        public static JpegFrame Parse(byte[] b) {
            if(b==null||b.Length<26||b.Length>Wire.MaxJpeg)throw new FormatException("Invalid JPEG packet length.");
            var f=new JpegFrame{StreamID=Wire.U32(b,0),Sequence=Wire.U32(b,4),Width=Wire.U16(b,8),
                Height=Wire.U16(b,10),Generation=Wire.U32(b,12),CapturedMicros=Wire.U64(b,16)};
            if(f.StreamID==0||f.Generation==0||f.Width==0||f.Height==0||f.Width>4096||f.Height>4096||b[24]!=255||b[25]!=216)
                throw new FormatException("Invalid JPEG metadata.");
            f.Jpeg=new byte[b.Length-24];Buffer.BlockCopy(b,24,f.Jpeg,0,f.Jpeg.Length);return f;
        }
    }
    public static class Wire {
        public const int HeaderSize=12,MaxJson=65536,MaxJpeg=8388608,MaxPcm=384008;
        public static int Limit(PacketKind kind) {
            switch(kind){case PacketKind.Json:return MaxJson;case PacketKind.Jpeg:return MaxJpeg;case PacketKind.Pcm:return MaxPcm;default:throw new FormatException("Unknown packet kind.");}
        }
        public static ushort U16(byte[] b,int i)=>(ushort)((b[i]<<8)|b[i+1]);
        public static uint U32(byte[] b,int i)=>((uint)b[i]<<24)|((uint)b[i+1]<<16)|((uint)b[i+2]<<8)|b[i+3];
        public static ulong U64(byte[] b,int i)=>((ulong)U32(b,i)<<32)|U32(b,i+4);
        public static void Put32(byte[] b,int i,uint n){b[i]=(byte)(n>>24);b[i+1]=(byte)(n>>16);b[i+2]=(byte)(n>>8);b[i+3]=(byte)n;}
        public static byte[] Encode(PacketKind kind,byte[] payload) {
            if(payload==null||payload.Length>Limit(kind))throw new FormatException("Packet too large.");
            var b=new byte[HeaderSize+payload.Length];b[0]=83;b[1]=77;b[2]=65;b[3]=67;b[4]=1;b[5]=(byte)kind;
            Put32(b,8,(uint)payload.Length);Buffer.BlockCopy(payload,0,b,12,payload.Length);return b;
        }
        public static byte[] Json(Message message)=>Encode(PacketKind.Json,Encoding.UTF8.GetBytes(JsonUtility.ToJson(message)));
        public static async Task<Packet> ReadAsync(Stream stream,CancellationToken ct) {
            byte[] header=await Exact(stream,HeaderSize,ct).ConfigureAwait(false);
            if(header[0]!=83||header[1]!=77||header[2]!=65||header[3]!=67||header[4]!=1||header[6]!=0||header[7]!=0)
                throw new FormatException("Bad SMAC header.");
            var kind=(PacketKind)header[5];uint count=U32(header,8);
            if(count>Limit(kind))throw new FormatException("Packet exceeds hard limit.");
            return new Packet{Kind=kind,Payload=await Exact(stream,(int)count,ct).ConfigureAwait(false)};
        }
        static async Task<byte[]> Exact(Stream stream,int count,CancellationToken ct) {
            var bytes=new byte[count];int offset=0;
            while(offset<count){int n=await stream.ReadAsync(bytes,offset,count-offset,ct).ConfigureAwait(false);if(n<=0)throw new EndOfStreamException();offset+=n;}
            return bytes;
        }
    }
}
