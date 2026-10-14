import Foundation
import Network
import Security
import SpatialCore

struct HostSettings: Codable {
    var token: String
    var p12Password: String
    var bindLAN: Bool
    var port: UInt16
    var whisperExecutable: String?
    var whisperModel: String?
    static var directory: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/SpatialMac", isDirectory: true)
    }
    static func load() throws -> HostSettings {
        let d=try Data(contentsOf:directory.appendingPathComponent("host-settings.json"))
        return try JSONDecoder().decode(HostSettings.self,from:d)
    }
}

final class SecurePeer {
    let id=UUID()
    var role="" // Assigned/read only on MainActor after hello validation.
    private let connection: NWConnection
    private let queue: DispatchQueue
    private let onPacket: (SecurePeer, Packet)->Void
    private let onClose: (SecurePeer)->Void
    private var closed=false, authenticated=false, sending=false
    private var controls=[Data]()
    private var video=[UInt32:[Data]]()   // per stream: JPEG keeps only the newest; encoded video keeps a short ordered queue
    private var videoOrder=[UInt32]()
    private var awaitingKeyframe=Set<UInt32>()
    /// Called (on the network queue) when a stream's queue overflowed and frames were dropped: ask for an IDR.
    var onCongestion:((UInt32)->Void)?
    private var rateWindow=Date(), rateCount=0
    init(_ c:NWConnection, queue:DispatchQueue,
         onPacket:@escaping (SecurePeer,Packet)->Void, onClose:@escaping (SecurePeer)->Void) {
        connection=c;self.queue=queue;self.onPacket=onPacket;self.onClose=onClose
    }
    func start() {
        connection.stateUpdateHandler={ [weak self] state in
            guard let self else{return}
            switch state {
            case .ready: self.readHeader()
            case .failed, .cancelled: self.closeOnQueue()
            default:break
            }
        }
        connection.start(queue:queue)
        queue.asyncAfter(deadline:.now()+8) { [weak self] in
            guard let self else{return};if !self.authenticated {self.closeOnQueue()}
        }
    }
    func markAuthenticated() { queue.async { self.authenticated=true } }
    func close() { queue.async { self.closeOnQueue() } }
    private func closeOnQueue() {
        guard !closed else{return};closed=true;controls.removeAll();video.removeAll()
        connection.cancel();onClose(self)
    }
    func send(_ m:Message) {
        do { let bytes=try Wire.json(m)
            queue.async {
                guard !self.closed else{return}
                guard self.controls.count<128 else{self.closeOnQueue();return}
                self.controls.append(bytes);self.drain()
            }
        } catch {close()}
    }
    // At most one frame in flight plus one replacement per stream; never queue old frames.
    func sendFrame(_ frame:JPEGFrame) {
        do {
            let bytes=try Wire.encode(Packet(kind:.jpeg,payload:frame.payload()))
            queue.async {
                guard !self.closed else{return}
                self.enqueueVideo(frame.streamID,bytes,replacing:true)
            }
        } catch { /* Oversized/invalid frames are dropped, not sent. */ }
    }
    /// Encoded video depends on earlier frames, so we cannot drop just any frame. Policy:
    ///  - a keyframe supersedes everything queued for that stream (older frames are useless once it plays);
    ///  - if the queue backs up (slow link), drop the whole queue, ignore delta frames until the next keyframe,
    ///    and tell the host to send one (and lower the bitrate). The viewer sees a brief freeze, never a smear.
    func sendVideo(_ frame:VideoFrame) {
        do {
            let bytes=try Wire.encode(Packet(kind:.video,payload:frame.payload()))
            let key=frame.isKeyframe,id=frame.streamID
            queue.async {
                guard !self.closed else{return}
                if key {
                    self.awaitingKeyframe.remove(id);self.video[id]=nil;self.videoOrder.removeAll{$0==id}
                    self.enqueueVideo(id,bytes,replacing:false);return
                }
                if self.awaitingKeyframe.contains(id){return}
                if (self.video[id]?.count ?? 0)>=4 {
                    self.video[id]=nil;self.videoOrder.removeAll{$0==id};self.awaitingKeyframe.insert(id)
                    self.onCongestion?(id);return
                }
                self.enqueueVideo(id,bytes,replacing:false)
            }
        } catch { /* invalid frames are dropped */ }
    }
    private func enqueueVideo(_ stream:UInt32,_ bytes:Data,replacing:Bool) {
        if video[stream] == nil {
            guard video.count<3 else{return}
            video[stream]=[]
        }
        if replacing{video[stream]=[bytes]}else{video[stream]?.append(bytes)}
        if !videoOrder.contains(stream){videoOrder.append(stream)}
        drain()
    }
    private func nextPacket() -> Data? {
        if !controls.isEmpty{return controls.removeFirst()}
        while let key=videoOrder.first {
            videoOrder.removeFirst()
            guard var q=video[key],!q.isEmpty else{video[key]=nil;continue}
            let packet=q.removeFirst()
            if q.isEmpty{video[key]=nil}else{video[key]=q;videoOrder.append(key)}
            return packet
        }
        return nil
    }
    private func drain() {
        guard !closed && !sending else{return}
        guard let next=nextPacket() else{return}
        sending=true
        connection.send(content:next,completion:.contentProcessed({ [weak self] error in
            guard let self else{return};self.sending=false
            if error != nil {self.closeOnQueue()} else{self.drain()}
        }))
    }
    private func readExact(_ count:Int,_ done:@escaping (Data)->Void) {
        if count==0 {done(Data());return}
        var accumulated=Data()
        func readPart() {
            connection.receive(minimumIncompleteLength:1,maximumLength:count-accumulated.count) {
                [weak self] data,_,complete,error in
                guard let self,!self.closed else{return}
                if let data {accumulated.append(data)}
                if error != nil || (complete && accumulated.count<count) {self.closeOnQueue();return}
                if accumulated.count==count {done(accumulated)}
                else if data?.isEmpty != false {self.closeOnQueue()}
                else{readPart()}
            }
        }
        readPart()
    }
    private func readHeader() {
        guard !closed else{return}
        readExact(Wire.headerSize) { [weak self] header in
            guard let self else{return}
            do {
                let (kind,count)=try Wire.decodeHeader(header)
                // Mac is a capture server, never a JPEG receiver.
                guard kind != .jpeg && kind != .video else{self.closeOnQueue();return}
                guard kind != .json || count<=16_384 else{self.closeOnQueue();return}
                self.readExact(count) { payload in
                    if Date().timeIntervalSince(self.rateWindow)>=1 {self.rateWindow=Date();self.rateCount=0}
                    self.rateCount += 1
                    guard self.rateCount<=200 else{self.closeOnQueue();return}
                    self.onPacket(self,Packet(kind:kind,payload:payload));self.readHeader()
                }
            } catch{self.closeOnQueue()}
        }
    }
}

final class SecureHost {
    private var listener:NWListener?
    private var peers=[UUID:SecurePeer]() // Confined to queue.
    private let queue=DispatchQueue(label:"SpatialMac.network",qos:.userInteractive)
    var onPacket:((SecurePeer,Packet)->Void)?
    var onClose:((SecurePeer)->Void)?
    func start(settings:HostSettings) throws {
        let data=try Data(contentsOf:HostSettings.directory.appendingPathComponent("identity.p12"))
        var items:CFArray?
        let options=[kSecImportExportPassphrase as String:settings.p12Password] as CFDictionary
        let status=SecPKCS12Import(data as CFData,options,&items)
        guard status==errSecSuccess,
              let entries=items as? [[String:Any]],let first=entries.first,
              let rawIdentity=first[kSecImportItemIdentity as String] else {
            throw NSError(domain:"SpatialMac.TLS",code:Int(status),userInfo:[NSLocalizedDescriptionKey:"Cannot import identity.p12. Re-run certificate setup; do not disable TLS."])
        }
        let identity=rawIdentity as! SecIdentity
        guard let networkIdentity=sec_identity_create(identity) else {
            throw NSError(domain:"SpatialMac.TLS",code:1)
        }
        let tls=NWProtocolTLS.Options()
        sec_protocol_options_set_min_tls_protocol_version(tls.securityProtocolOptions,.TLSv12)
        sec_protocol_options_set_local_identity(tls.securityProtocolOptions,networkIdentity)
        let tcp=NWProtocolTCP.Options();tcp.noDelay=true;tcp.connectionTimeout=8
        let params=NWParameters(tls:tls,tcp:tcp)
        params.requiredLocalEndpoint = .hostPort(
            host:settings.bindLAN ? .ipv4(.any):.ipv4(.loopback),
            port:NWEndpoint.Port(rawValue:settings.port)!)
        let listener=try NWListener(using:params)
        listener.newConnectionHandler={ [weak self] connection in
            guard let self else{connection.cancel();return}
            guard self.peers.count<8 else{connection.cancel();return}
            let peer=SecurePeer(connection,queue:self.queue,onPacket:{[weak self] p,packet in
                self?.onPacket?(p,packet)
            },onClose:{[weak self] p in
                self?.peers.removeValue(forKey:p.id);self?.onClose?(p)
            })
            self.peers[peer.id]=peer;peer.start()
        }
        listener.start(queue:queue);self.listener=listener
    }
    func stop() {
        queue.async {
            self.listener?.cancel();self.listener=nil
            for peer in Array(self.peers.values){peer.close()}
        }
    }
}
