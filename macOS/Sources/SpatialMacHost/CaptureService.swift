import AppKit
import ScreenCaptureKit
import CoreImage
import CoreMedia
import ImageIO
import SpatialCore

struct CaptureTarget {
    var dto:SourceDTO
    var window:SCWindow?
    var display:SCDisplay?
}
struct CaptureHandle {
    let stream:SCStream
    let output:JPEGOutput
    let source:SourceDTO
    let generation:UInt32
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
    func start(sourceID:String,streamID:UInt32,fps:Int,maxWidth:Int,
               onFrame:@escaping (JPEGFrame)->Void,onError:@escaping (String)->Void) async throws -> CaptureHandle {
        guard let target=targets[sourceID],streamID>0 else{throw CaptureFailure.noSource}
        guard sessions[streamID] != nil || sessions.count<3 else{throw CaptureFailure.tooMany}
        await stop(streamID)
        let filter:SCContentFilter
        if let w=target.window{filter=SCContentFilter(desktopIndependentWindow:w)}
        else if let d=target.display{filter=SCContentFilter(display:d,excludingWindows:[])}
        else{throw CaptureFailure.noSource}
        let b=target.dto.bounds
        let width=max(320,min(1920,maxWidth))
        let height=max(1,min(4096,Int((Double(width)*b.height/b.width).rounded())))
        let config=SCStreamConfiguration()
        config.width=width;config.height=height;config.scalesToFit=true
        config.minimumFrameInterval=CMTime(value:1,timescale:CMTimeScale(max(5,min(30,fps))))
        config.queueDepth=3;config.pixelFormat=kCVPixelFormatType_32BGRA
        config.showsCursor=true;config.capturesAudio=false
        config.ignoreShadowsSingleWindow=true
        config.backgroundColor=CGColor(red:0,green:0,blue:0,alpha:1)
        config.destinationRect=CGRect(x:0,y:0,width:CGFloat(width),height:CGFloat(height))
        generation &+= 1;if generation==0{generation=1}
        let output=JPEGOutput(streamID:streamID,generation:generation,onFrame:onFrame,onError:onError)
        let stream=SCStream(filter:filter,configuration:config,delegate:output)
        try stream.addStreamOutput(output,type:.screen,sampleHandlerQueue:output.queue)
        try await stream.startCapture()
        let handle=CaptureHandle(stream:stream,output:output,source:target.dto,generation:generation)
        sessions[streamID]=handle;return handle
    }
    func stop(_ id:UInt32) async {
        guard let h=sessions.removeValue(forKey:id) else{return}
        h.output.invalidate();try? await h.stream.stopCapture()
    }
    func stopAll() async {for id in Array(sessions.keys){await stop(id)}}
}
enum CaptureFailure:Error {case noSource,tooMany}

final class JPEGOutput:NSObject,SCStreamOutput,SCStreamDelegate {
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
