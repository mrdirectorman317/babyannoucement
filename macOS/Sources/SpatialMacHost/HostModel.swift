import AppKit
import Combine
import SpatialCore
import SpatialMedia

struct RecentNotification { let id:String,sender:String,body:String,chatGUID:String? }

@MainActor final class HostModel:ObservableObject,AssistantToolHost {
    static let shared=HostModel()
    @Published var sources=[SourceDTO]()
    @Published var approved=Set<String>()
    @Published private(set) var running=false
    @Published private(set) var sharingEnabled=false
    @Published private(set) var controlEnabled=false
    @Published private(set) var speechEnabled=false
    @Published private(set) var status="Stopped. Run scripts/setup-mac.sh before starting."
    @Published private(set) var connected=false
    // Integrations. Everything here is opt-in; reply/consequential powers reset when the headset disconnects.
    @Published private(set) var messagesEnabled=UserDefaults.standard.bool(forKey:"messagesEnabled")
    @Published private(set) var messagePreview=UserDefaults.standard.object(forKey:"messagePreview") as? Bool ?? true
    @Published private(set) var contactLookup=UserDefaults.standard.bool(forKey:"contactLookup")
    @Published private(set) var assistantReadsMessages=UserDefaults.standard.bool(forKey:"assistantReadsMessages")
    @Published private(set) var replyEnabled=false
    @Published private(set) var cloudSpeech=UserDefaults.standard.bool(forKey:"cloudSpeech")
    @Published private(set) var messagesStatus="Off."
    @Published private(set) var assistantHasKey=false
    @Published private(set) var assistantBusy=false
    @Published private(set) var lastAssistantReply=""
    @Published var assistantModel=UserDefaults.standard.string(forKey:"assistantModel") ?? "gpt-5" {
        didSet{UserDefaults.standard.set(assistantModel.trimmingCharacters(in:.whitespaces),forKey:"assistantModel")}
    }
    private let watcher=MessageWatcher(),assistant=AssistantService()
    private var recent=[RecentNotification]()
    private var confirmations=ConfirmationBook()
    private var lastReplyAt=Date.distantPast
    private var bitrates=[UInt32:AdaptiveBitrate]()
    private let host=SecureHost(),capture=CaptureService(),input=InputRouter()
    private var settings:HostSettings?
    private var peers=[String:SecurePeer]()
    private var activeClientID:String?
    private var captureBusy=false,speechBusy=false
    private var lastHeartbeat=Date()
    private var timer:Timer?
    init() {
        assistantHasKey=assistant.hasKey
        host.onPacket={ [weak self] peer,packet in Task{@MainActor in self?.receive(peer,packet)} }
        host.onClose={ [weak self] peer in Task{@MainActor in self?.closed(peer)} }
        timer=Timer.scheduledTimer(withTimeInterval:0.5,repeats:true){ [weak self] _ in
            Task{@MainActor in
                guard let self else{return}
                if self.connected && Date().timeIntervalSince(self.lastHeartbeat)>3 {
                    self.setControl(false);self.status="Control paused: heartbeat lost."
                }
                for (id,var rate) in self.bitrates {
                    if let faster=rate.tick(){self.bitrates[id]=rate;self.capture.sessions[id]?.setBitrate(faster)}
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
            if messagesEnabled{startWatcher()}
        } catch{status=error.localizedDescription}
    }
    func stop() {
        setSharing(false);setControl(false);setReply(false);watcher.stop();confirmations.clear();host.stop();running=false;connected=false
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
    func emergencyStop() {setControl(false);setSharing(false);setSpeech(false);setReply(false);confirmations.clear();status="Emergency stop: sharing and remote control are off."}
    func requestScreenPermission(){_ = CGRequestScreenCaptureAccess();Task{await refresh()}}
    func requestAccessibility(){_ = AXIsProcessTrustedWithOptions([kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String:true] as CFDictionary)}
    private func sendPermissions() {
        var m=Message("permissions");m.controlEnabled=controlEnabled;m.sharingEnabled=sharingEnabled;m.speechEnabled=speechEnabled
        m.assistantEnabled=assistantHasKey;m.messagesEnabled=messagesEnabled;m.replyEnabled=replyEnabled
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
                    let text:String
                    if cloudSpeech,let key=KeychainStore.read(AssistantService.keychainAccount){text=try await CloudSpeech(apiKey:key).transcribe(packet.payload)}
                    else{text=try await LocalSpeech(settings:settings).transcribe(packet.payload)}
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
            if role=="video"{peer.onCongestion={[weak self] stream in Task{@MainActor in self?.congested(stream)}}}
            if role=="control"{connected=true;lastHeartbeat=Date()}
            var welcome=Message("welcome");welcome.role=role;peer.send(welcome)
            if role=="control"{sendPermissions();sendCatalog()}
            return
        }
        guard peers[peer.role]?.id==peer.id else{peer.close();return}
        guard peer.role=="control" else{if m.type != "ping"{peer.close()};return}
        switch m.type {
        case "ping":
            lastHeartbeat=Date();var pong=Message("pong");pong.requestID=m.requestID
            pong.t0=m.t0;pong.t1=Double(DispatchTime.now().uptimeNanoseconds/1000)   // same clock as video timestamps
            peer.send(pong)
        case "request_keyframe":
            if let id=m.streamID{capture.sessions[id]?.requestKeyframe()}
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
                    let wanted=m.codec.flatMap(VideoCodec.init(name:))
                    let h=try await capture.start(sourceID:sourceID,streamID:streamID,fps:m.fps ?? 15,maxWidth:m.maxWidth ?? 1280,
                        codec:wanted,bitrate:(m.kbps ?? 0)>0 ? max(1_000,min(60_000,m.kbps ?? 0))*1000:nil,   // Unity sends 0 for "no preference"
                        
                        onFrame:{[weak video] frame in video?.sendFrame(frame)},
                        onVideo:{[weak video] frame in video?.sendVideo(frame)},
                        onError:{[weak self] text in Task{@MainActor in
                            guard let self else{return};self.input.releaseAll()
                            await self.capture.stop(streamID)
                            var stopped=Message("unsubscribed");stopped.streamID=streamID;self.peers["control"]?.send(stopped)
                            self.status="Capture stopped: \(text)"
                        }})
                    guard sharingEnabled,approved.contains(sourceID),peers["control"]?.id==peer.id else{await capture.stop(streamID);return}
                    var response=Message("subscribed");response.sourceID=sourceID;response.streamID=streamID;response.generation=h.generation
                    response.codec=h.codec
                    if let used=VideoCodec(name:h.codec),h.encoded != nil {
                        let w=min(2560,m.maxWidth ?? 1280),start=(m.kbps ?? 0)>0 ? max(1_000,min(60_000,m.kbps ?? 0))*1000:AdaptiveBitrate.suggested(width:w,height:w*9/16,fps:min(60,m.fps ?? 15),codec:used)
                        bitrates[streamID]=AdaptiveBitrate(initial:start,minimum:2_000_000,maximum:Swift.min(40_000_000,start*3/2))
                    }
                    peer.send(response)
                }catch{self.error(error.localizedDescription,to:peer)}
            }
        case "unsubscribe":
            if let id=m.streamID{input.releaseAll();Task{await capture.stop(id);var r=Message("unsubscribed");r.streamID=id;peer.send(r)}}
        case "assistant_ask":
            guard let text=ControlValidation.assistantAsk(m) else{error("Invalid question.",to:peer);return}
            handleAssistantAsk(text,requestID:m.requestID,peer:peer)
        case "confirm":
            guard let c=ControlValidation.confirm(m) else{error("Invalid confirmation.",to:peer);return}
            if let call=confirmations.resolve(id:c.id,approve:c.approve){execute(call)}
            else if c.approve{error("That confirmation expired or was already used.",to:peer)}
        case "message_reply":
            guard let r=ControlValidation.messageReply(m) else{error("Invalid reply.",to:peer);return}
            sendReply(notificationID:r.id,text:r.text,to:peer)
        case "dismiss":break
        case "pointer","text","shortcut","launch":
            guard controlEnabled,sharingEnabled,Date().timeIntervalSince(lastHeartbeat)<3 else{error("Mac remote control is not armed.",to:peer);return}
            do{try input.apply(m,handle:m.streamID.flatMap{capture.sessions[$0]})}
            catch{self.error(error.localizedDescription,to:peer)}
        default:error("Unsupported command. No generic shell or arbitrary automation is exposed.",to:peer)
        }
    }
    /// The headset's link can't keep up: back off the bitrate and send a clean keyframe once the queue is flushed.
    private func congested(_ stream:UInt32) {
        if var rate=bitrates[stream],let lower=rate.congestion(){bitrates[stream]=rate;capture.sessions[stream]?.setBitrate(lower)}
        capture.sessions[stream]?.requestKeyframe()
    }
    private func closed(_ peer:SecurePeer) {
        guard peers[peer.role]?.id==peer.id else{return}
        peers.removeValue(forKey:peer.role)
        if peer.role=="control" || peer.role=="video" {
            input.releaseAll();controlEnabled=false;connected=false;replyEnabled=false;confirmations.clear()
            let old=Array(peers.values);peers.removeAll();activeClientID=nil
            for p in old{p.close()}
            bitrates.removeAll();Task{await capture.stopAll()};status="Headset disconnected. Input disarmed."
        }
    }
    // MARK: Messages -> headset
    func setMessages(_ on:Bool) {
        messagesEnabled=on;UserDefaults.standard.set(on,forKey:"messagesEnabled")
        if on{if running{startWatcher()}else{messagesStatus="Will start with the server."}}
        else{watcher.stop();recent.removeAll();setReply(false);messagesStatus="Off."}
        sendPermissions()
    }
    func setCloudSpeech(_ on:Bool){cloudSpeech=on;UserDefaults.standard.set(on,forKey:"cloudSpeech")}
    func setMessagePreview(_ on:Bool){messagePreview=on;UserDefaults.standard.set(on,forKey:"messagePreview")}
    func setContactLookup(_ on:Bool) {
        contactLookup=on;UserDefaults.standard.set(on,forKey:"contactLookup");watcher.lookUpContacts=on
        if on{MessageWatcher.requestContactsAccess{_ in}}
    }
    func setAssistantReadsMessages(_ on:Bool){assistantReadsMessages=on;UserDefaults.standard.set(on,forKey:"assistantReadsMessages")}
    func setReply(_ on:Bool){replyEnabled=on && connected && messagesEnabled;sendPermissions()}
    private func startWatcher() {
        watcher.lookUpContacts=contactLookup
        watcher.onStatus={[weak self] text in Task{@MainActor in self?.messagesStatus=text}}
        watcher.onMessage={[weak self] message in Task{@MainActor in self?.ingest(message)}}
        watcher.start();messagesStatus="Starting…"
    }
    private func ingest(_ m:IncomingMessage) {
        guard messagesEnabled else{return}
        let id=UUID().uuidString
        recent.append(RecentNotification(id:id,sender:m.sender,body:m.body,chatGUID:m.chatGUID))
        if recent.count>20{recent.removeFirst()}
        guard connected,let control=peers["control"] else{return}
        var n=Message("notification");n.notificationID=id;n.title=m.sender
        n.body=messagePreview ? m.body:"New message";n.appName="Messages";n.category="message"
        n.replyable=replyEnabled && m.chatGUID != nil
        control.send(n)
    }
    private func sendReply(notificationID:String,text:String,to peer:SecurePeer?) {
        guard replyEnabled,Date().timeIntervalSince(lastReplyAt)>2,
              let n=recent.first(where:{$0.id==notificationID}),let guid=n.chatGUID else {
            if let peer{error("Replying is off, too fast, or the message is unknown. Nothing was sent.",to:peer)}
            return
        }
        lastReplyAt=Date()
        Task {
            do{try await MessageSender.send(text:text,chatGUID:guid);_ = toast(title:"Reply sent",body:n.sender)}
            catch{if let p=peers["control"]{self.error(error.localizedDescription,to:p)}}
        }
    }
    // MARK: Commands usable from the headset protocol, Siri / Shortcuts and the assistant
    @discardableResult func sendUICommand(_ command:String,arg:String?=nil,value:Double?=nil) -> Bool {
        guard connected,let p=peers["control"] else{return false}
        var m=Message("ui_command");m.command=command;m.arg=arg;m.value=value;p.send(m);return true
    }
    @discardableResult func toast(title:String,body:String) -> Bool {
        guard connected,let p=peers["control"] else{return false}
        var m=Message("notification");m.notificationID=UUID().uuidString;m.title=TextSanitizer.clean(title,maxLength:60)
        m.body=TextSanitizer.clean(body,maxLength:240);m.appName="SpatialMac";m.category="custom";m.replyable=false
        p.send(m);return true
    }
    func ensureStarted(){if !running{start()}}
    // MARK: Assistant (ChatGPT)
    func saveAPIKey(_ key:String) {
        let trimmed=key.trimmingCharacters(in:.whitespacesAndNewlines)
        guard trimmed.count>=20 else{status="That does not look like an API key.";return}
        assistantHasKey=KeychainStore.write(AssistantService.keychainAccount,trimmed);sendPermissions()
    }
    func removeAPIKey(){KeychainStore.delete(AssistantService.keychainAccount);assistantHasKey=false;assistant.newConversation();sendPermissions()}
    func newAssistantConversation(){assistant.newConversation()}
    func askAssistant(_ text:String) async -> String {
        do{
            let reply=try await assistant.ask(text,model:assistantModel.isEmpty ? "gpt-5":assistantModel,host:self)
            lastAssistantReply=reply;return reply.isEmpty ? "Done.":reply
        }catch{return error.localizedDescription}
    }
    private func handleAssistantAsk(_ text:String,requestID:String?,peer:SecurePeer) {
        guard assistantHasKey else{error(AssistantFailure.noKey.errorDescription ?? "No API key.",to:peer);return}
        guard !assistantBusy else{error("The assistant is still working on the last request.",to:peer);return}
        assistantBusy=true
        var state=Message("assistant_state");state.action="thinking";state.requestID=requestID;peer.send(state)
        Task {
            defer{assistantBusy=false}
            let reply=await askAssistant(text)
            var m=Message("assistant_reply");m.requestID=requestID;m.text=TextSanitizer.clean(reply,maxLength:4000,preserveLines:true)
            peers["control"]?.send(m)
        }
    }
    func performTool(_ call:ValidatedCall) -> String {
        switch call.risk {
        case .readOnly:
            if call.command=="get_workspace_state" {
                let list=sources.filter{approved.contains($0.id)}.prefix(20).map{"\($0.app) — \($0.title)"}
                return list.isEmpty ? "No sources are approved.":OpenAIWire.untrusted(list.joined(separator:"\n"))
            }
            guard messagesEnabled,assistantReadsMessages else{return "The user has not allowed the assistant to read messages."}
            let items=recent.suffix(Int(call.value ?? 5)).map{"[\($0.id)] \($0.sender): \($0.body)"}
            return items.isEmpty ? "No recent messages.":OpenAIWire.untrusted(items.joined(separator:"\n"))
        case .reversibleUI:
            guard connected else{return "The headset is not connected. Nothing changed."}
            if call.command=="show_notification"{_ = toast(title:call.arg ?? "",body:call.body ?? "");return "Shown."}
            sendUICommand(call.command,arg:call.arg,value:call.value);return "Sent to the headset."
        case .consequential:
            guard connected,let control=peers["control"] else{return "The headset is not connected. Nothing was done."}
            let summary:String
            switch call.command {
            case "launch_app":summary="Open \(call.arg ?? "an app") on the Mac"
            case "reply_to_message":
                guard replyEnabled,let n=recent.first(where:{$0.id==call.arg}) else{return "Replying is turned off on the Mac, or the message is unknown. Nothing was done."}
                summary="Reply to \(n.sender): “\(call.body ?? "")”"
            default:return "Unsupported action."
            }
            guard let pending=confirmations.add(call,summary:summary) else{return "Too many pending confirmations. Nothing was done."}
            var m=Message("confirm_request");m.confirmID=pending.id;m.title="Confirm action";m.body=summary;control.send(m)
            return "Awaiting the user's confirmation in the headset. Do not retry this action."
        }
    }
    private func execute(_ call:ValidatedCall) {
        switch call.command {
        case "launch_app":
            guard let id=call.arg,InputValidation.apps.contains(id),let url=NSWorkspace.shared.urlForApplication(withBundleIdentifier:id) else{return}
            NSWorkspace.shared.openApplication(at:url,configuration:NSWorkspace.OpenConfiguration(),completionHandler:{_,_ in})
            _ = toast(title:"Opening app",body:url.deletingPathExtension().lastPathComponent)
        case "reply_to_message":
            if let id=call.arg,let text=call.body{sendReply(notificationID:id,text:text,to:peers["control"])}
        default:break
        }
    }

}
