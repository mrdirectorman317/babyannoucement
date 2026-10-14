import AppKit
import Combine
import SpatialCore

@MainActor final class HostModel:ObservableObject {
    @Published var sources=[SourceDTO]()
    @Published var approved=Set<String>()
    @Published private(set) var running=false,sharingEnabled=false,controlEnabled=false,speechEnabled=false
    @Published private(set) var status="Stopped. Run scripts/setup-mac.sh before starting."
    @Published private(set) var connected=false
    private let host=SecureHost(),capture=CaptureService(),input=InputRouter()
    private var settings:HostSettings?
    private var peers=[String:SecurePeer]()
    private var activeClientID:String?
    private var captureBusy=false,speechBusy=false
    private var lastHeartbeat=Date()
    private var timer:Timer?
    init() {
        host.onPacket={ [weak self] peer,packet in Task{@MainActor in self?.receive(peer,packet)} }
        host.onClose={ [weak self] peer in Task{@MainActor in self?.closed(peer)} }
        timer=Timer.scheduledTimer(withTimeInterval:0.5,repeats:true){ [weak self] _ in
            Task{@MainActor in
                guard let self else{return}
                if self.connected && Date().timeIntervalSince(self.lastHeartbeat)>3 {
                    self.setControl(false);self.status="Control paused: heartbeat lost."
                }
            }
        }
    }
    func start() {
        guard !running else{return}
        do {
            let s=try HostSettings.load();settings=s;try host.start(settings:s);running=true
            status=s.bindLAN ? "TLS listening on LAN. Explicit sharing/input approval still required.":"TLS listening on 127.0.0.1. Use adb reverse."
            Task{await refresh()}
        } catch{status=error.localizedDescription}
    }
    func stop() {
        setSharing(false);setControl(false);host.stop();running=false;connected=false
        peers.removeAll();activeClientID=nil;status="Stopped."
    }
    func refresh() async {
        do{sources=try await capture.catalog();sendCatalog()}catch{status="Screen capture permission required: \(error.localizedDescription)"}
    }
    func approve(_ id:String,_ enabled:Bool) {
        if enabled{approved.insert(id)}else{
            approved.remove(id);input.releaseAll()
            Task{
                for (streamID,h) in Array(capture.sessions) where h.source.id==id {
                    await capture.stop(streamID)
                    var m=Message("unsubscribed");m.streamID=streamID;peers["control"]?.send(m)
                }
            }
        }
        sendCatalog()
    }
    func setSharing(_ enabled:Bool) {
        sharingEnabled=enabled
        if !enabled{setControl(false);Task{await capture.stopAll()}}
        sendPermissions();sendCatalog()
    }
    func setControl(_ enabled:Bool) {
        controlEnabled=enabled && connected && sharingEnabled && AXIsProcessTrusted()
        if !controlEnabled{input.releaseAll()}
        sendPermissions()
    }
    func setSpeech(_ enabled:Bool){speechEnabled=enabled;sendPermissions()}
    func emergencyStop() {setControl(false);setSharing(false);setSpeech(false);status="Emergency stop: sharing and remote control are off."}
    func requestScreenPermission(){_ = CGRequestScreenCaptureAccess();Task{await refresh()}}
    func requestAccessibility(){_ = AXIsProcessTrustedWithOptions([kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String:true] as CFDictionary)}
    private func sendPermissions() {
        var m=Message("permissions");m.controlEnabled=controlEnabled;m.sharingEnabled=sharingEnabled;m.speechEnabled=speechEnabled
        peers["control"]?.send(m)
    }
    private func sendCatalog() {
        var m=Message("catalog");m.sources=sharingEnabled ? sources.filter{approved.contains($0.id)}:[]
        peers["control"]?.send(m)
    }
    private func error(_ text:String,to peer:SecurePeer) {var m=Message("error");m.error=text;peer.send(m)}
    private func receive(_ peer:SecurePeer,_ packet:Packet) {
        if packet.kind == .pcm {
            guard peers["audio"]?.id==peer.id,speechEnabled,!speechBusy,let settings else {
                error("Speech is disabled, unavailable, or busy.",to:peers["control"] ?? peer);return
            }
            speechBusy=true
            let controlID=peers["control"]?.id
            Task {
                defer{speechBusy=false}
                do {
                    let text=try await LocalSpeech(settings:settings).transcribe(packet.payload)
                    guard controlID==peers["control"]?.id else{return}
                    var m=Message("transcript");m.text=text;peers["control"]?.send(m)
                } catch{if let p=peers["control"]{self.error(error.localizedDescription,to:p)}}
            }
            return
        }
        guard packet.kind == .json,let m=try? JSONDecoder().decode(Message.self,from:packet.payload) else{peer.close();return}
        if peer.role.isEmpty {
            guard m.type=="hello",let s=settings,InputValidation.tokenMatches(s.token,m.token ?? ""),
                  let role=m.role,["control","video","audio"].contains(role),
                  let id=m.clientID,UUID(uuidString:id) != nil,
                  activeClientID==nil || activeClientID==id,peers[role]==nil,
                  role=="control" || peers["control"] != nil else{peer.close();return}
            activeClientID=id;peer.role=role;peers[role]=peer;peer.markAuthenticated()
            if role=="control"{connected=true;lastHeartbeat=Date()}
            var welcome=Message("welcome");welcome.role=role;peer.send(welcome)
            if role=="control"{sendPermissions();sendCatalog()}
            return
        }
        guard peers[peer.role]?.id==peer.id else{peer.close();return}
        guard peer.role=="control" else{if m.type != "ping"{peer.close()};return}
        switch m.type {
        case "ping":lastHeartbeat=Date();var pong=Message("pong");pong.requestID=m.requestID;peer.send(pong)
        case "list_sources":sendCatalog()
        case "release_all":input.releaseAll()
        case "subscribe":
            guard sharingEnabled,!captureBusy,let sourceID=m.sourceID,approved.contains(sourceID),
                  let streamID=m.streamID,streamID>0,let video=peers["video"] else{error("Not approved, video disconnected, or capture busy. Retry.",to:peer);return}
            captureBusy=true;input.releaseAll()
            Task {
                defer{captureBusy=false}
                do {
                    _ = try await capture.catalog()
                    let h=try await capture.start(sourceID:sourceID,streamID:streamID,fps:m.fps ?? 15,maxWidth:m.maxWidth ?? 1280,
                        onFrame:{[weak video] frame in video?.sendFrame(frame)},
                        onError:{[weak self] text in Task{@MainActor in
                            guard let self else{return};self.input.releaseAll()
                            await self.capture.stop(streamID)
                            var stopped=Message("unsubscribed");stopped.streamID=streamID;self.peers["control"]?.send(stopped)
                            self.status="Capture stopped: \(text)"
                        }})
                    guard sharingEnabled,approved.contains(sourceID),peers["control"]?.id==peer.id else{await capture.stop(streamID);return}
                    var response=Message("subscribed");response.sourceID=sourceID;response.streamID=streamID;response.generation=h.generation
                    peer.send(response)
                }catch{self.error(error.localizedDescription,to:peer)}
            }
        case "unsubscribe":
            if let id=m.streamID{input.releaseAll();Task{await capture.stop(id);var r=Message("unsubscribed");r.streamID=id;peer.send(r)}}
        case "pointer","text","shortcut","launch":
            guard controlEnabled,sharingEnabled,Date().timeIntervalSince(lastHeartbeat)<3 else{error("Mac remote control is not armed.",to:peer);return}
            do{try input.apply(m,handle:m.streamID.flatMap{capture.sessions[$0]})}
            catch{self.error(error.localizedDescription,to:peer)}
        default:error("Unsupported command. No generic shell or arbitrary automation is exposed.",to:peer)
        }
    }
    private func closed(_ peer:SecurePeer) {
        guard peers[peer.role]?.id==peer.id else{return}
        peers.removeValue(forKey:peer.role)
        if peer.role=="control" || peer.role=="video" {
            input.releaseAll();controlEnabled=false;connected=false
            let old=Array(peers.values);peers.removeAll();activeClientID=nil
            for p in old{p.close()}
            Task{await capture.stopAll()};status="Headset disconnected. Input disarmed."
        }
    }
}
