using System;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SpatialMac {
    // One TLS socket per lane prevents video TCP backlog from delaying mouse-up.
    // IL2CPP/SslStream behavior MUST be verified on the actual Android build.
    public sealed class SecureLane : IDisposable {
        readonly ClientConfig config; readonly string role;
        readonly Action<Packet> onPacket; readonly Action<string> onFailure;
        readonly ConcurrentQueue<byte[]> outgoing=new ConcurrentQueue<byte[]>();
        readonly SemaphoreSlim available=new SemaphoreSlim(0);
        readonly CancellationTokenSource stop=new CancellationTokenSource();
        TcpClient tcp; SslStream ssl; int queued,disposed;
        public volatile bool Ready;
        public SecureLane(ClientConfig config,string role,Action<Packet> onPacket,Action<string> onFailure){
            this.config=config;this.role=role;this.onPacket=onPacket;this.onFailure=onFailure;
        }
        public Task StartAsync()=>Task.Run(Run);
        async Task Run() {
            try {
                tcp=new TcpClient{NoDelay=true};
                Task connect=tcp.ConnectAsync(config.host,config.port);
                if(await Task.WhenAny(connect,Task.Delay(8000,stop.Token))!=connect)throw new TimeoutException("TCP connection timed out.");
                await connect.ConfigureAwait(false);
                ssl=new SslStream(tcp.GetStream(),false,VerifyPinnedCertificate);
                Task handshake=ssl.AuthenticateAsClientAsync("spatialmac.local",null,SslProtocols.Tls12,false);
                if(await Task.WhenAny(handshake,Task.Delay(8000,stop.Token))!=handshake)throw new TimeoutException("TLS handshake timed out.");
                await handshake.ConfigureAwait(false);
                byte[] hello=Wire.Json(new Message("hello"){token=config.token,clientID=config.clientID,role=role});
                await ssl.WriteAsync(hello,0,hello.Length,stop.Token).ConfigureAwait(false);
                Task<Packet> welcomeRead=Wire.ReadAsync(ssl,stop.Token);
                if(await Task.WhenAny(welcomeRead,Task.Delay(8000,stop.Token))!=welcomeRead)throw new TimeoutException("Pairing welcome timed out.");
                Packet answer=await welcomeRead.ConfigureAwait(false);
                if(answer.Kind!=PacketKind.Json)throw new AuthenticationException("Missing welcome.");
                string answerText=Encoding.UTF8.GetString(answer.Payload);
                var welcome=UnityEngine.JsonUtility.FromJson<Message>(answerText);
                if(welcome==null||welcome.type!="welcome"||welcome.role!=role)throw new AuthenticationException("Pairing rejected.");
                Ready=true;onPacket(answer);
                Task reader=ReadLoop(),writer=WriteLoop();
                Task first=await Task.WhenAny(reader,writer).ConfigureAwait(false);
                await first.ConfigureAwait(false);
            } catch(OperationCanceledException) { }
              catch(Exception e) {if(!stop.IsCancellationRequested)onFailure(role+": "+e.Message);}
            finally {Dispose();}
        }
        bool VerifyPinnedCertificate(object sender,X509Certificate certificate,X509Chain chain,SslPolicyErrors errors) {
            if(certificate==null)return false;
            using(var cert=new X509Certificate2(certificate))
            using(var sha=SHA256.Create()) {
                DateTime now=DateTime.UtcNow;
                if(now<cert.NotBefore.ToUniversalTime()||now>cert.NotAfter.ToUniversalTime())return false;
                byte[] actual=sha.ComputeHash(cert.RawData),expected=new byte[32];
                try{for(int i=0;i<32;i++)expected[i]=Convert.ToByte(config.certificateSHA256.Substring(i*2,2),16);}catch{return false;}
                int diff=0;for(int i=0;i<32;i++)diff|=actual[i]^expected[i];
                return diff==0; // Exact trusted pin, not a blanket trust-all fallback.
            }
        }
        async Task ReadLoop(){while(!stop.IsCancellationRequested)onPacket(await Wire.ReadAsync(ssl,stop.Token).ConfigureAwait(false));}
        async Task WriteLoop() {
            while(!stop.IsCancellationRequested) {
                await available.WaitAsync(stop.Token).ConfigureAwait(false);
                if(outgoing.TryDequeue(out byte[] next)){Interlocked.Decrement(ref queued);await ssl.WriteAsync(next,0,next.Length,stop.Token).ConfigureAwait(false);}
            }
        }
        public bool Send(Message message)=>SendBytes(Wire.Json(message));
        public bool SendPCM(byte[] data)=>SendBytes(Wire.Encode(PacketKind.Pcm,data));
        bool SendBytes(byte[] data) {
            if(!Ready||stop.IsCancellationRequested)return false;
            if(Interlocked.Increment(ref queued)>64){Interlocked.Decrement(ref queued);onFailure(role+": outgoing queue full; disconnecting safely.");Dispose();return false;}
            outgoing.Enqueue(data);available.Release();return true;
        }
        public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;Ready=false;stop.Cancel();try{ssl?.Dispose();tcp?.Close();}catch{}}
    }
}
