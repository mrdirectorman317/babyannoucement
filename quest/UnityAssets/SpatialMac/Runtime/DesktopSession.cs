using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;

namespace SpatialMac {
    public sealed class DesktopSession:MonoBehaviour {
        public Transform head;
        public Material panelMaterial;
        public WorkspaceEnvironment environment;
        public WorkspaceMenu menu;
        public AnchorProvider anchorProvider;
        public PushToTalkMicrophone microphone;
        public string Status="Not connected.";
        public string Transcript="";
        public bool ControlEnabled {get;private set;}
        public bool SharingEnabled {get;private set;}
        public bool SpeechEnabled {get;private set;}
        public bool Connected=>control!=null&&control.Ready&&video!=null&&video.Ready;
        public SourceDTO[] Sources {get;private set;}=Array.Empty<SourceDTO>();
        public IReadOnlyCollection<SpatialPanel> Panels=>panels.Values;
        public int PointerOwner {get;private set;}=-1;
        public SpatialPanel Selected {get;private set;}
        readonly Dictionary<uint,SpatialPanel> panels=new Dictionary<uint,SpatialPanel>();
        readonly ConcurrentQueue<Message> messages=new ConcurrentQueue<Message>();
        readonly ConcurrentQueue<string> failures=new ConcurrentQueue<string>();
        readonly object frameLock=new object();
        readonly Dictionary<uint,JpegFrame> latest=new Dictionary<uint,JpegFrame>();
        readonly HashSet<uint> allowedFrameIDs=new HashSet<uint>();
        readonly Queue<uint> subscriptions=new Queue<uint>();
        ClientConfig config;SecureLane control,video,audio;uint nextID=1,pending;
        int networkEpoch;
        readonly HashSet<uint> anchorInFlight=new HashSet<uint>();
        int queuedMessages;float nextPing,lastPong,subscribeSentAt;bool connecting,shuttingDown;
        readonly string sessionID=Guid.NewGuid().ToString();
        public string SessionID=>sessionID;
        void Start() {
            try {
                string path=Path.Combine(Application.persistentDataPath,"client-config.json");
                config=JsonUtility.FromJson<ClientConfig>(File.ReadAllText(path));config.Validate();
                if(config.autoConnect)Connect();
            }catch(Exception e){Status="Pairing config missing/invalid: "+e.Message;}
            if(menu!=null)SummonMenu();
        }
        public void Connect() {
            if(connecting||Connected||config==null)return;
            Disconnect();connecting=true;Status="Connecting with pinned TLS…";
            control=Lane("control");_ = control.StartAsync();lastPong=Time.unscaledTime;
        }
        SecureLane Lane(string role) {
            int epoch=networkEpoch;
            return new SecureLane(config,role,p=>{lock(frameLock){if(epoch==networkEpoch)Receive(p);}},e=>{lock(frameLock){if(epoch==networkEpoch)Failure(e);}});
        }
        void Receive(Packet p) {
            if(p.Kind==PacketKind.Jpeg) {
                try {
                    var f=JpegFrame.Parse(p.Payload);
                    lock(frameLock){if(allowedFrameIDs.Contains(f.StreamID))latest[f.StreamID]=f;}
                }catch(Exception e){Failure(e.Message);}
            } else if(p.Kind==PacketKind.Json) {
                try {
                    var m=JsonUtility.FromJson<Message>(Encoding.UTF8.GetString(p.Payload));
                    if(m==null)throw new FormatException("Invalid JSON envelope.");
                    if(Interlocked.Increment(ref queuedMessages)>256){Interlocked.Decrement(ref queuedMessages);Failure("Inbound control queue overflow.");return;}
                    messages.Enqueue(m);
                }catch(Exception e){Failure(e.Message);}
            } else Failure("Unexpected packet on receiving lane.");
        }
        void Failure(string message){if(failures.Count<8)failures.Enqueue(message);}
        void Update() {
            if(failures.TryDequeue(out string failure)){Disconnect();Status=failure;return;}
            int budget=64;
            while(budget-->0&&messages.TryDequeue(out Message m)){Interlocked.Decrement(ref queuedMessages);Handle(m);}
            if(control!=null&&control.Ready&&Time.unscaledTime>=nextPing){nextPing=Time.unscaledTime+1;control.Send(new Message("ping"){requestID=Guid.NewGuid().ToString()});}
            if(Connected&&Time.unscaledTime-lastPong>5){Disconnect();Status="Connection heartbeat expired.";return;}
            if(pending!=0&&Time.unscaledTime-subscribeSentAt>8){pending=0;Status="Capture request timed out; refresh the panel.";PumpSubscriptions();}
            // One JPEG upload per render frame. This is a prototype, NOT the
            // final hardware-decoded video path. Iterate fairly between streams.
            JpegFrame frame=null;
            lock(frameLock){
                if(latest.Count>0){uint id=latest.Keys.OrderBy(x=>x<=lastDecoded?1:0).ThenBy(x=>x).First();frame=latest[id];latest.Remove(id);lastDecoded=id;}
            }
            if(SharingEnabled&&frame!=null&&panels.TryGetValue(frame.StreamID,out SpatialPanel panel))panel.Apply(frame);
        }
        uint lastDecoded;
        void Handle(Message m) {
            switch(m.type) {
                case "welcome":
                    if(m.role=="control"){
                        if(video==null){video=Lane("video");_ = video.StartAsync();}
                        if(audio==null){audio=Lane("audio");_ = audio.StartAsync();}
                    }
                    if(m.role=="video"){connecting=false;Status="Connected. Approve sources and arm input on the Mac.";control.Send(new Message("list_sources"));}
                    break;
                case "permissions":
                    ControlEnabled=m.controlEnabled;SharingEnabled=m.sharingEnabled;SpeechEnabled=m.speechEnabled;
                    if(!ControlEnabled)ReleaseAll();
                    if(!SharingEnabled)foreach(var shown in panels.Values)shown.ClearPixels();
                    break;
                case "catalog":Sources=m.sources??Array.Empty<SourceDTO>();menu?.Rebuild();break;
                case "subscribed":
                    if(panels.TryGetValue(m.streamID,out SpatialPanel p)){p.Generation=m.generation;Status="Streaming "+p.Source.title;}
                    if(pending==m.streamID){pending=0;PumpSubscriptions();}break;
                case "unsubscribed":RemovePanel(m.streamID);break;
                case "pong":lastPong=Time.unscaledTime;break;
                case "transcript":Transcript=m.text??"";Status="Review the transcript before applying it.";menu?.Rebuild();break;
                case "error":Status=m.error??"Host rejected request.";if(pending!=0){pending=0;PumpSubscriptions();}break;
            }
        }
        public SpatialPanel OpenPanel(SourceDTO source) {
            if(!Connected||!SharingEnabled){Status="Connect and enable sharing on the Mac first.";return null;}
            var existing=panels.Values.FirstOrDefault(p=>p.Source.id==source.id);
            if(existing!=null){Selected=existing;if(existing.Generation==0)RefreshPanel(existing);return existing;}
            if(panels.Count>=3){Status="Prototype limit: three active sources. Close one first.";return null;}
            uint id=nextID++;var go=new GameObject("Spatial panel "+id);var panel=go.AddComponent<SpatialPanel>();
            panel.Initialize(this,source,id);panels[id]=panel;Selected=panel;
            float angle=(panels.Count-1)*42f-42f;Vector3 flat=Vector3.ProjectOnPlane(head.forward,Vector3.up).normalized;
            if(flat.sqrMagnitude<0.01f)flat=Vector3.forward;
            Vector3 direction=Quaternion.AngleAxis(angle,Vector3.up)*flat;
            go.transform.SetPositionAndRotation(head.position+direction*1.65f-Vector3.up*0.12f,Quaternion.LookRotation(direction,Vector3.up));
            lock(frameLock)allowedFrameIDs.Add(id);
            subscriptions.Enqueue(id);PumpSubscriptions();return panel;
        }
        void PumpSubscriptions() {
            if(pending!=0||!Connected)return;
            while(subscriptions.Count>0){uint id=subscriptions.Dequeue();if(!panels.TryGetValue(id,out SpatialPanel p))continue;
                pending=id;subscribeSentAt=Time.unscaledTime;
                control.Send(new Message("subscribe"){streamID=id,sourceID=p.Source.id,fps=15,maxWidth=1280});break;}
        }
        public void RefreshPanel(SpatialPanel panel){if(panel==null)return;ReleaseAll();panel.Generation=0;panel.ClearPixels();subscriptions.Enqueue(panel.StreamID);PumpSubscriptions();}
        public void ClosePanel(SpatialPanel panel){if(panel==null)return;control?.Send(new Message("unsubscribe"){streamID=panel.StreamID});RemovePanel(panel.StreamID);SaveLayout();}
        void RemovePanel(uint id){if(!panels.TryGetValue(id,out SpatialPanel p))return;ReleaseAll();panels.Remove(id);lock(frameLock){latest.Remove(id);allowedFrameIDs.Remove(id);}if(Selected==p)Selected=null;Destroy(p.gameObject);}
        public bool TryAcquirePointer(int id){if(!Connected||!ControlEnabled||(PointerOwner>=0&&PointerOwner!=id))return false;PointerOwner=id;return true;}
        public void ReleasePointer(int id){if(PointerOwner==id)PointerOwner=-1;}
        public bool SendInput(Message m,SpatialPanel panel) {
            if(m.type=="pointer"&&m.action=="up"){control?.Send(m);return true;}
            if(!ControlEnabled||!Connected||panel==null||!panel.HasUsablePixels||panel.Manipulating)return false;
            Selected=panel;return control.Send(m);
        }
        public void ReleaseAll(){PointerOwner=-1;control?.Send(new Message("release_all"));}
        public void PanelAction(SpatialPanel panel,string action) {
            Selected=panel;
            switch(action) {
                case "follow":panel.SetPlacement(PlacementMode.Follow);break;
                case "world":panel.SetPlacement(PlacementMode.World);break;
                case "lock":panel.SetPlacement(panel.Locked?PlacementMode.World:PlacementMode.SessionLocked);break;
                case "remember":RememberPanel(panel);return;
                case "refresh":RefreshPanel(panel);return;
                case "close":ClosePanel(panel);return;
            }
            SaveLayout();
        }
        public async void RememberPanel(SpatialPanel panel) {
            if(anchorProvider==null||!anchorProvider.IsPersistent){Status="Persistent anchor provider is not configured. Session lock is available; no permanent pin was saved.";return;}
            if(panel==null||!anchorInFlight.Add(panel.StreamID)){Status="Anchor operation already running.";return;}
            uint operationID=panel.StreamID;
            string old=panel.AnchorID;
            try {
                var result=await anchorProvider.CreateAndSave(new Pose(panel.transform.position,panel.transform.rotation));
                if(panel==null){await anchorProvider.Erase(result.id);return;}
                panel.transform.SetParent(result.root,true);panel.AnchorID=result.id;panel.SetPlacement(PlacementMode.PersistentLocked);SaveLayout();
                if(!string.IsNullOrEmpty(old)&&old!=result.id)await anchorProvider.Erase(old);
                Status="Anchor saved. Verify it relocalizes after restart before relying on it.";
            }catch(Exception e){Status="Anchor not saved: "+e.Message;}finally{anchorInFlight.Remove(operationID);}
        }
        public void SaveLayout(){try{LayoutStore.Save(Panels,sessionID);}catch(Exception e){Status="Layout save failed: "+e.Message;}}
        public async void RestoreLayout(){await LayoutStore.Restore(this);}
        public void SummonMenu(){if(menu==null||head==null)return;Vector3 direction=Vector3.ProjectOnPlane(head.forward,Vector3.up).normalized;if(direction.sqrMagnitude<0.1f)direction=Vector3.forward;
            menu.transform.SetPositionAndRotation(head.position+direction*1.1f,Quaternion.LookRotation(direction,Vector3.up));menu.gameObject.SetActive(true);menu.Rebuild();}
        public void RefreshSources(){control?.Send(new Message("list_sources"));}
        public void ToggleSpeech(){if(microphone==null)return;if(microphone.IsRecording)microphone.StopAndSend();else microphone.Begin();}
        public void SendSpeech(byte[] pcm){if(!SpeechEnabled||audio==null||!audio.Ready){Status="Enable and configure local speech on the Mac first.";return;}audio.SendPCM(pcm);Status="Transcribing locally…";}
        public void ConfirmVoiceCommand(){VoiceIntentRouter.Apply(Transcript,this);}
        public void TypeTranscript(){if(Selected==null)return;SendInput(new Message("text"){streamID=Selected.StreamID,generation=Selected.Generation,text=Transcript},Selected);}
        public void LaunchAllowedApp(string bundleID){if(ControlEnabled)control?.Send(new Message("launch"){appID=bundleID});}
        public void Disconnect(){
            ReleaseAll();control?.Dispose();video?.Dispose();audio?.Dispose();control=video=audio=null;
            connecting=false;ControlEnabled=SharingEnabled=SpeechEnabled=false;pending=0;subscriptions.Clear();
            lock(frameLock){networkEpoch++;latest.Clear();
                while(messages.TryDequeue(out _)){}Interlocked.Exchange(ref queuedMessages,0);
                while(failures.TryDequeue(out _)){}
            }
            Sources=Array.Empty<SourceDTO>();
            foreach(var p in panels.Values){p.ClearPixels();p.Generation=0;p.Manipulator.CancelAll();}
            Status="Disconnected; remote input released.";
        }
        void OnApplicationPause(bool pause){if(pause)Disconnect();else if(!shuttingDown&&config!=null&&config.autoConnect)Connect();}
        void OnApplicationQuit(){shuttingDown=true;SaveLayout();Disconnect();}
        void OnDestroy(){shuttingDown=true;Disconnect();}
    }
}
