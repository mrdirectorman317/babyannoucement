import AppKit
import ScreenCaptureKit
import CoreImage
import CoreMedia
import ImageIO
import SpatialCore
import SpatialMedia

struct CaptureTarget {
    var dto:SourceDTO
    var window:SCWindow?
    var display:SCDisplay?
}
protocol CaptureOutput:AnyObject { func invalidate() }
struct CaptureHandle {
    let stream:SCStream
    let output:CaptureOutput
    let source:SourceDTO
    let generation:UInt32
    let codec:String            // "jpeg", "h264" or "hevc": what is actually being sent
    var encoded:EncodedOutput? { output as? EncodedOutput }
    func requestKeyframe(){encoded?.requestKeyframe()}
    func setBitrate(_ bps:Int){encoded?.setBitrate(bps)}
}
@MainActor final class CaptureService {
    private(set) var targets=[String:CaptureTarget]()
    private(set) var sessions=[UInt32:CaptureHandle]()
    private var generation:UInt32=0
    func catalog() async throws -> [SourceDTO] {
        let content=try await SCShareableContent.excludingDesktopWindows(false,onScreenWindowsOnly:true)
        var fresh=[String:CaptureTarget]()
        for d in content.displays {
            let dto=SourceDTO(id:"display:\(d.displayID)",kind:"display",title:"Display \(d.displayID)",app:"Desktop",pid:0,bounds:RectDTO(d.frame))
            fresh[dto.id]=CaptureTarget(dto:dto,display:d)
        }
        for w in content.windows where w.windowLayer==0 && w.frame.width>=80 && w.frame.height>=60 {
            guard let app=w.owningApplication,app.processID != ProcessInfo.processInfo.processIdentifier else{continue}
            let dto=SourceDTO(id:"window:\(w.windowID)",kind:"window",title:w.title ?? "Untitled",app:app.applicationName,pid:app.processID,bounds:RectDTO(w.frame))
            fresh[dto.id]=CaptureTarget(dto:dto,window:w)
        }
        targets=fresh
        return fresh.values.map(\.dto).sorted{($0.kind,$0.app,$0.title)<($1.kind,$1.app,$1.title)}
    }
    func start(sourceID:String,streamID:UInt32,fps:Int,maxWidth:Int,codec:VideoCodec?=nil,bitrate:Int?=nil,
               onFrame:@escaping (JPEGFrame)->Void,onVideo:@escaping (VideoFrame)->Void={_ in},
               onError:@escaping (String)->Void) async throws -> CaptureHandle {
        guard let target=targets[sourceID],streamID>0 else{throw CaptureFailure.noSource}
        guard sessions[streamID] != nil || sessions.count<3 else{throw CaptureFailure.tooMany}
        await stop(streamID)
        let filter:SCContentFilter
        if let w=target.window{filter=SCContentFilter(desktopIndependentWindow:w)}
        else if let d=target.display{filter=SCContentFilter(display:d,excludingWindows:[])}
        else{throw CaptureFailure.noSource}
        let b=target.dto.bounds
        let wantVideo=codec != nil
        // Encoded video can carry far more pixels per second than the JPEG debug path.
        let width=max(320,min(wantVideo ? 2560:1920,maxWidth)) & ~1      // even sizes: required by 4:2:0 codecs
        let height=max(2,min(4096,Int((Double(width)*b.height/b.width).rounded()))) & ~1
        let config=SCStreamConfiguration()
        config.width=width;config.height=height;config.scalesToFit=true
        let rate=max(5,min(wantVideo ? 60:30,fps))
        config.minimumFrameInterval=CMTime(value:1,timescale:CMTimeScale(rate))
        config.queueDepth=wantVideo ? 5:3     // video also holds a frame for settle-to-sharp / keyframe-on-request
        config.pixelFormat=wantVideo ? kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange:kCVPixelFormatType_32BGRA
        // Video streams hide the Mac cursor: the headset draws its own instantly, so the pointer never lags the video.
        config.showsCursor = !wantVideo;config.capturesAudio=false
        if wantVideo{config.colorMatrix=CGDisplayStream.yCbCrMatrix_ITU_R_709_2;config.colorSpaceName=CGColorSpace.sRGB}
        config.ignoreShadowsSingleWindow=true
        config.backgroundColor=CGColor(red:0,green:0,blue:0,alpha:1)
        config.destinationRect=CGRect(x:0,y:0,width:CGFloat(width),height:CGFloat(height))
        generation &+= 1;if generation==0{generation=1}
        let stream:SCStream,output:CaptureOutput&SCStreamOutput&SCStreamDelegate,usedCodec:String
        var made:(CaptureOutput&SCStreamOutput&SCStreamDelegate)?
        var chosen="jpeg"
        // Step down HEVC -> H.264 -> JPEG: the viewer always gets the best thing this Mac can actually encode.
        for candidate in (codec == .hevc ? [VideoCodec.hevc,.h264]:(codec != nil ? [VideoCodec.h264]:[])) {
            if let m=try? EncodedOutput(streamID:streamID,generation:generation,width:width,height:height,fps:rate,
                    bitrate:bitrate ?? AdaptiveBitrate.suggested(width:width,height:height,fps:rate,codec:candidate),codec:candidate,onVideo:onVideo,onError:onError) {
                made=m;chosen=candidate.name;break
            }
        }
        if let made{output=made;usedCodec=chosen}
        else{
            // No hardware/software encoder available (or JPEG requested): fall back to the JPEG debug path.
            config.pixelFormat=kCVPixelFormatType_32BGRA;config.showsCursor=true
            output=JPEGOutput(streamID:streamID,generation:generation,onFrame:onFrame,onError:onError);usedCodec="jpeg"
        }
        stream=SCStream(filter:filter,configuration:config,delegate:output)
        let outputQueue=(output as? EncodedOutput)?.queue ?? (output as? JPEGOutput)?.queue ?? DispatchQueue.global(qos:.userInitiated)
        try stream.addStreamOutput(output,type:.screen,sampleHandlerQueue:outputQueue)
        try await stream.startCapture()
        let handle=CaptureHandle(stream:stream,output:output,source:target.dto,generation:generation,codec:usedCodec)
        sessions[streamID]=handle;return handle
    }
    func stop(_ id:UInt32) async {
        guard let h=sessions.removeValue(forKey:id) else{return}
        h.output.invalidate();try? await h.stream.stopCapture()
    }
    func stopAll() async {for id in Array(sessions.keys){await stop(id)}}
}
enum CaptureFailure:Error {case noSource,tooMany}

final class JPEGOutput:NSObject,SCStreamOutput,SCStreamDelegate,CaptureOutput {
    let queue=DispatchQueue(label:"SpatialMac.jpeg",qos:.userInitiated)
    private let context=CIContext(options:[.cacheIntermediates:false])
    private let colorSpace=CGColorSpace(name:CGColorSpace.sRGB)!
    private let streamID:UInt32,generation:UInt32
    private var sequence:UInt32=0
    private var valid=true
    private let onFrame:(JPEGFrame)->Void
    private let onError:(String)->Void
    init(streamID:UInt32,generation:UInt32,onFrame:@escaping (JPEGFrame)->Void,onError:@escaping (String)->Void) {
        self.streamID=streamID;self.generation=generation;self.onFrame=onFrame;self.onError=onError
    }
    func invalidate(){queue.async{self.valid=false}}
    func stream(_ stream:SCStream,didStopWithError error:Error){onError(error.localizedDescription)}
    func stream(_ stream:SCStream,didOutputSampleBuffer buffer:CMSampleBuffer,of type:SCStreamOutputType) {
        guard valid,type == .screen,buffer.isValid,
              let attachments=CMSampleBufferGetSampleAttachmentsArray(buffer,createIfNecessary:false) as? [[SCStreamFrameInfo:Any]],
              let raw=attachments.first?[.status] as? Int,SCFrameStatus(rawValue:raw) == .complete,
              let pixel=buffer.imageBuffer else{return}
        autoreleasepool {
            let image=CIImage(cvPixelBuffer:pixel)
            let quality=CIImageRepresentationOption(rawValue:kCGImageDestinationLossyCompressionQuality as String)
            guard let jpeg=context.jpegRepresentation(of:image,colorSpace:colorSpace,options:[quality:0.72]),
                  image.extent.width>0,image.extent.height>0,
                  image.extent.width<=4096,image.extent.height<=4096 else{return}
            sequence &+= 1
            let frame=JPEGFrame(streamID:streamID,sequence:sequence,
                width:UInt16(image.extent.width),height:UInt16(image.extent.height),generation:generation,
                capturedMicros:DispatchTime.now().uptimeNanoseconds/1000,jpeg:jpeg)
            onFrame(frame)
        }
    }
}
extension RectDTO {
    init(_ r:CGRect){self.init(x:r.minX,y:r.minY,width:r.width,height:r.height)}
    var cgRect:CGRect{CGRect(x:x,y:y,width:width,height:height)}
}

/// ScreenCaptureKit -> VideoToolbox. Each captured frame is encoded immediately on a high-priority queue;
/// the encoder owns the only buffering (frame delay <= 1), so there is no pile-up between capture and the wire.
final class EncodedOutput:NSObject,SCStreamOutput,SCStreamDelegate,CaptureOutput {
    let queue=DispatchQueue(label:"SpatialMac.encode",qos:.userInteractive)
    private let streamID:UInt32,generation:UInt32,width:UInt16,height:UInt16,codec:VideoCodec
    private var encoder:VideoEncoder?
    private var sequence:UInt32=0       // touched only from the encoder's output callback
    private var valid=true
    private var lastPixel:CVPixelBuffer?,refineWork:DispatchWorkItem?
    private let onError:(String)->Void
    init(streamID:UInt32,generation:UInt32,width:Int,height:Int,fps:Int,bitrate:Int,codec:VideoCodec,
         onVideo:@escaping (VideoFrame)->Void,onError:@escaping (String)->Void) throws {
        self.streamID=streamID;self.generation=generation;self.codec=codec;self.onError=onError
        self.width=UInt16(clamping:width);self.height=UInt16(clamping:height)
        super.init()
        encoder=try VideoEncoder(config:.init(width:width,height:height,fps:fps,bitrate:bitrate,codec:codec)) { [weak self] unit in
            guard let self else{return}
            self.sequence &+= 1
            var flags:UInt8=0
            if unit.isKeyframe{flags |= VideoFrame.flagKeyframe}
            if unit.hasParameterSets{flags |= VideoFrame.flagParameterSets}
            onVideo(VideoFrame(streamID:self.streamID,sequence:self.sequence,width:self.width,height:self.height,generation:self.generation,
                               ptsMicros:UInt64(max(0,unit.ptsMicros)),flags:flags,codec:self.codec,bitstream:unit.bitstream))
        }
    }
    /// A viewer needs a clean picture NOW. If the screen is static ScreenCaptureKit will not send another frame,
    /// so re-encode the last one as a keyframe instead of waiting for something to change.
    func requestKeyframe() {
        queue.async { [weak self] in
            guard let self, self.valid, let encoder = self.encoder else { return }
            if let px = self.lastPixel { encoder.encodeRefinement(px, ptsMicros: Int64(DispatchTime.now().uptimeNanoseconds/1000)) }
            else { encoder.requestKeyframe() }
        }
    }
    func setBitrate(_ bps:Int){encoder?.setBitrate(bps)}
    func invalidate(){queue.async{self.valid=false;self.encoder?.invalidate();self.encoder=nil}}
    func stream(_ stream:SCStream,didStopWithError error:Error){onError(error.localizedDescription)}
    func stream(_ stream:SCStream,didOutputSampleBuffer buffer:CMSampleBuffer,of type:SCStreamOutputType) {
        guard valid,type == .screen,buffer.isValid,
              let attachments=CMSampleBufferGetSampleAttachmentsArray(buffer,createIfNecessary:false) as? [[SCStreamFrameInfo:Any]],
              let raw=attachments.first?[.status] as? Int,SCFrameStatus(rawValue:raw) == .complete,
              let pixel=buffer.imageBuffer else{return}
        // Same clock as the clock-sync replies, so the headset can compute capture-to-display latency.
        encoder?.encode(pixel,ptsMicros:Int64(DispatchTime.now().uptimeNanoseconds/1000))
        // Settle to sharp: if nothing changes for 250 ms, re-send the final picture as a keyframe (once per idle period).
        lastPixel=pixel;refineWork?.cancel()
        let work=DispatchWorkItem{[weak self] in
            guard let self,self.valid,let px=self.lastPixel else{return}
            self.encoder?.encodeRefinement(px,ptsMicros:Int64(DispatchTime.now().uptimeNanoseconds/1000))
        }
        refineWork=work;queue.asyncAfter(deadline:.now()+0.25,execute:work)
    }
}
