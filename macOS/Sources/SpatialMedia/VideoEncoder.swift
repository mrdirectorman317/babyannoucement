import CoreMedia
import CoreVideo
import Foundation
import SpatialCore
import VideoToolbox

public struct EncodedUnit {
    public let bitstream: Data          // Annex-B; parameter sets are prepended on keyframes
    public let isKeyframe: Bool
    public let hasParameterSets: Bool
    public let ptsMicros: Int64
}

/// Low-latency hardware H.264/HEVC encoder (VideoToolbox). One instance per stream.
/// Tuned for screen content on a link to a headset: real-time, no B-frames, tiny frame delay, rate-limited.
public final class VideoEncoder {
    public struct Config {
        public var width: Int, height: Int, fps: Int, bitrate: Int
        public var codec: VideoCodec
        public var keyframeSeconds: Double
        public init(width: Int, height: Int, fps: Int, bitrate: Int, codec: VideoCodec = .h264, keyframeSeconds: Double = 4) {
            self.width = width; self.height = height; self.fps = fps; self.bitrate = bitrate
            self.codec = codec; self.keyframeSeconds = keyframeSeconds
        }
    }
    public enum Failure: LocalizedError {
        case create(OSStatus), encode(OSStatus), badOutput
        public var errorDescription: String? {
            switch self {
            case .create(let s): return "Video encoder could not start (VideoToolbox status \(s))."
            case .encode(let s): return "Video encode failed (status \(s))."
            case .badOutput: return "Video encoder produced unreadable output."
            }
        }
    }
    public let config: Config
    public private(set) var usesHardware = false
    private var session: VTCompressionSession?
    private let lock = NSLock()
    private var needKeyframe = true      // the very first frame is always an IDR
    private let onUnit: (EncodedUnit) -> Void

    public init(config: Config, onUnit: @escaping (EncodedUnit) -> Void) throws {
        self.config = config; self.onUnit = onUnit
        let codecType: CMVideoCodecType = config.codec == .h264 ? kCMVideoCodecType_H264 : kCMVideoCodecType_HEVC
        // Prefer hardware + low-latency rate control; step down if this Mac's encoder refuses that combination.
        let specs: [[CFString: Any]?] = [
            [kVTVideoEncoderSpecification_EnableHardwareAcceleratedVideoEncoder: true,
             kVTVideoEncoderSpecification_EnableLowLatencyRateControl: true],
            [kVTVideoEncoderSpecification_EnableHardwareAcceleratedVideoEncoder: true],
            nil
        ]
        var created: VTCompressionSession?, lastStatus: OSStatus = -1
        for spec in specs {
            var s: VTCompressionSession?
            lastStatus = VTCompressionSessionCreate(allocator: nil, width: Int32(config.width), height: Int32(config.height),
                                                    codecType: codecType, encoderSpecification: spec as CFDictionary?,
                                                    imageBufferAttributes: nil, compressedDataAllocator: nil,
                                                    outputCallback: nil, refcon: nil, compressionSessionOut: &s)
            if lastStatus == noErr, let s { created = s; break }
        }
        guard let session = created else { throw Failure.create(lastStatus) }
        self.session = session
        set(kVTCompressionPropertyKey_RealTime, kCFBooleanTrue)
        set(kVTCompressionPropertyKey_AllowFrameReordering, kCFBooleanFalse)
        set(kVTCompressionPropertyKey_ProfileLevel, config.codec == .h264 ? kVTProfileLevel_H264_High_AutoLevel : kVTProfileLevel_HEVC_Main_AutoLevel)
        set(kVTCompressionPropertyKey_ExpectedFrameRate, config.fps as CFNumber)
        set(kVTCompressionPropertyKey_MaxKeyFrameInterval, Int(Double(config.fps) * config.keyframeSeconds) as CFNumber)
        set(kVTCompressionPropertyKey_MaxKeyFrameIntervalDuration, config.keyframeSeconds as CFNumber)
        set(kVTCompressionPropertyKey_PrioritizeEncodingSpeedOverQuality, kCFBooleanTrue)
        set(kVTCompressionPropertyKey_MaxFrameDelayCount, 1 as CFNumber)
        setBitrate(config.bitrate)
        VTCompressionSessionPrepareToEncodeFrames(session)
        var hw: CFTypeRef?
        if VTSessionCopyProperty(session, key: kVTCompressionPropertyKey_UsingHardwareAcceleratedVideoEncoder, allocator: nil, valueOut: &hw) == noErr {
            usesHardware = (hw as? Bool) ?? false
        }
    }
    deinit { invalidate() }

    @discardableResult private func set(_ key: CFString, _ value: CFTypeRef) -> Bool {
        guard let session else { return false }
        return VTSessionSetProperty(session, key: key, value: value) == noErr   // optional tuning: unsupported keys are ignored
    }
    /// Change the target bitrate while running (used by congestion control).
    public func setBitrate(_ bitsPerSecond: Int) {
        set(kVTCompressionPropertyKey_AverageBitRate, bitsPerSecond as CFNumber)
        // Hard cap: at most 1.5x the average in any one second keeps bursts from flooding a Wi-Fi/USB link.
        set(kVTCompressionPropertyKey_DataRateLimits, [Int(Double(bitsPerSecond) / 8 * 1.5), 1] as CFArray)
    }
    /// The next frame will be an IDR (new viewer, recovery after dropped frames, or a decode error).
    public func requestKeyframe() { lock.lock(); needKeyframe = true; lock.unlock() }

    public func encode(_ pixelBuffer: CVPixelBuffer, ptsMicros: Int64) {
        guard let session else { return }
        lock.lock(); let key = needKeyframe; needKeyframe = false; lock.unlock()
        let props = key ? [kVTEncodeFrameOptionKey_ForceKeyFrame: true] as CFDictionary : nil
        var flags = VTEncodeInfoFlags()
        let status = VTCompressionSessionEncodeFrame(session, imageBuffer: pixelBuffer,
            presentationTimeStamp: CMTime(value: ptsMicros, timescale: 1_000_000), duration: .invalid,
            frameProperties: props, infoFlagsOut: &flags) { [weak self] status, _, sample in
                self?.handle(status: status, sample: sample, ptsMicros: ptsMicros)
        }
        if status != noErr { requestKeyframe() }
    }
    /// Flush pending frames (call before reading the last output in tests or when stopping).
    public func finish() { if let session { VTCompressionSessionCompleteFrames(session, untilPresentationTimeStamp: .invalid) } }
    public func invalidate() {
        guard let session else { return }
        VTCompressionSessionInvalidate(session); self.session = nil
    }

    private func handle(status: OSStatus, sample: CMSampleBuffer?, ptsMicros: Int64) {
        guard status == noErr, let sample, CMSampleBufferDataIsReady(sample), let block = CMSampleBufferGetDataBuffer(sample) else {
            requestKeyframe(); return
        }
        let attachments = CMSampleBufferGetSampleAttachmentsArray(sample, createIfNecessary: false) as? [[CFString: Any]]
        let key = !((attachments?.first?[kCMSampleAttachmentKey_NotSync] as? Bool) ?? false)
        let length = CMBlockBufferGetDataLength(block)
        var data = Data(count: length)
        let copied = data.withUnsafeMutableBytes { raw -> OSStatus in
            guard let base = raw.baseAddress else { return -1 }
            return CMBlockBufferCopyDataBytes(block, atOffset: 0, dataLength: length, destination: base)
        }
        guard copied == kCMBlockBufferNoErr, let format = CMSampleBufferGetFormatDescription(sample) else { requestKeyframe(); return }
        let (sets, nalLength) = parameterSets(format)
        guard let body = AnnexB.fromLengthPrefixed(data, lengthSize: nalLength) else { requestKeyframe(); return }
        var out = Data()
        if key { for set in sets { out.append(contentsOf: [0, 0, 0, 1]); out.append(set) } }
        out.append(body)
        onUnit(EncodedUnit(bitstream: out, isKeyframe: key, hasParameterSets: key && !sets.isEmpty, ptsMicros: ptsMicros))
    }

    private func parameterSets(_ format: CMFormatDescription) -> ([Data], Int) {
        var count = 0, nalLength: Int32 = 4
        func fetch(_ index: Int, _ ptr: inout UnsafePointer<UInt8>?, _ size: inout Int) -> OSStatus {
            config.codec == .h264
                ? CMVideoFormatDescriptionGetH264ParameterSetAtIndex(format, parameterSetIndex: index, parameterSetPointerOut: &ptr, parameterSetSizeOut: &size, parameterSetCountOut: &count, nalUnitHeaderLengthOut: &nalLength)
                : CMVideoFormatDescriptionGetHEVCParameterSetAtIndex(format, parameterSetIndex: index, parameterSetPointerOut: &ptr, parameterSetSizeOut: &size, parameterSetCountOut: &count, nalUnitHeaderLengthOut: &nalLength)
        }
        var p: UnsafePointer<UInt8>?, s = 0
        guard fetch(0, &p, &s) == noErr else { return ([], 4) }
        var sets = [Data]()
        for i in 0..<count {
            var ptr: UnsafePointer<UInt8>?, size = 0
            if fetch(i, &ptr, &size) == noErr, let ptr { sets.append(Data(bytes: ptr, count: size)) }
        }
        return (sets, Int(nalLength))
    }
}
