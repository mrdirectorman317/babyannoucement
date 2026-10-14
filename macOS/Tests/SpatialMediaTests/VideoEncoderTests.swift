import CoreMedia
import CoreVideo
import VideoToolbox
import XCTest
import SpatialCore
@testable import SpatialMedia

/// Encodes synthetic frames with VideoEncoder, then decodes the resulting Annex-B with a standard VideoToolbox
/// decoder. That proves the bitstream we put on the wire is a valid, self-contained H.264 stream.
final class VideoEncoderTests: XCTestCase {
    static let width = 640, height = 360

    private func makeBuffer(phase: Int) -> CVPixelBuffer {
        var buffer: CVPixelBuffer?
        let attrs = [kCVPixelBufferIOSurfacePropertiesKey: [:]] as CFDictionary
        CVPixelBufferCreate(nil, Self.width, Self.height, kCVPixelFormatType_32BGRA, attrs, &buffer)
        let pb = buffer!
        CVPixelBufferLockBaseAddress(pb, [])
        let base = CVPixelBufferGetBaseAddress(pb)!.assumingMemoryBound(to: UInt8.self)
        let stride = CVPixelBufferGetBytesPerRow(pb)
        for y in 0..<Self.height {
            for x in 0..<Self.width {
                let o = y * stride + x * 4
                let bar = abs(x - (phase * 12) % Self.width) < 20
                base[o] = bar ? 255 : UInt8(x * 255 / Self.width)       // B
                base[o + 1] = bar ? 255 : UInt8(y * 255 / Self.height)  // G
                base[o + 2] = bar ? 255 : 64                           // R
                base[o + 3] = 255
            }
        }
        CVPixelBufferUnlockBaseAddress(pb, [])
        return pb
    }

    func testH264StreamIsValidAndDecodable() throws {
        let lock = NSLock(); var units = [EncodedUnit]()
        let encoder: VideoEncoder
        do {
            encoder = try VideoEncoder(config: .init(width: Self.width, height: Self.height, fps: 60, bitrate: 4_000_000)) {
                lock.lock(); units.append($0); lock.unlock()
            }
        } catch VideoEncoder.Failure.create(let status) {
            throw XCTSkip("No VideoToolbox H.264 encoder available on this machine (status \(status)).")
        }
        print("ENCODER hardware=\(encoder.usesHardware)")
        let frames = 45
        for i in 0..<frames { encoder.encode(makeBuffer(phase: i), ptsMicros: Int64(i) * 16_667) }
        encoder.finish()
        lock.lock(); let got = units; lock.unlock()
        XCTAssertGreaterThanOrEqual(got.count, frames - 3, "encoder should not drop frames at this rate")
        let first = try XCTUnwrap(got.first)
        XCTAssertTrue(first.isKeyframe); XCTAssertTrue(first.hasParameterSets)
        XCTAssertTrue(AnnexB.startsWithStartCode(first.bitstream))
        XCTAssertTrue(AnnexB.containsKeyframe(first.bitstream, codec: .h264))
        XCTAssertTrue(AnnexB.containsParameterSets(first.bitstream, codec: .h264))
        XCTAssertTrue(got.dropFirst().allSatisfy { AnnexB.startsWithStartCode($0.bitstream) })
        // Wrap in the real wire packet and back, as the host/headset would.
        for (i, u) in got.enumerated() {
            let f = VideoFrame(streamID: 1, sequence: UInt32(i), width: UInt16(Self.width), height: UInt16(Self.height), generation: 1,
                               ptsMicros: UInt64(u.ptsMicros), flags: u.isKeyframe ? VideoFrame.flagKeyframe | VideoFrame.flagParameterSets : 0,
                               codec: .h264, bitstream: u.bitstream)
            XCTAssertEqual(try VideoFrame.decode(f.payload()), f)
        }
        // Decode with a standard decoder.
        let decoder = TestDecoder()
        for u in got { try decoder.feed(u.bitstream) }
        decoder.finish()
        XCTAssertGreaterThanOrEqual(decoder.decodedCount, got.count - 1, "decoder should reconstruct (nearly) every frame")
        XCTAssertEqual(decoder.size.width, CGFloat(Self.width)); XCTAssertEqual(decoder.size.height, CGFloat(Self.height))
    }

    func testRefinementEmitsAnExtraKeyframe() throws {
        let lock = NSLock(); var units = [EncodedUnit]()
        let encoder: VideoEncoder
        do { encoder = try VideoEncoder(config: .init(width: Self.width, height: Self.height, fps: 60, bitrate: 3_000_000)) { lock.lock(); units.append($0); lock.unlock() } }
        catch { throw XCTSkip("No encoder: \(error)") }
        let still = makeBuffer(phase: 3)
        for i in 0..<6 { encoder.encode(still, ptsMicros: Int64(i) * 16_667) }
        encoder.encodeRefinement(still, ptsMicros: 400_000)
        encoder.finish()
        lock.lock(); let got = units; lock.unlock()
        XCTAssertTrue(got.last?.isKeyframe ?? false, "refinement frame must be a keyframe")
        XCTAssertTrue(got.last?.hasParameterSets ?? false)
        XCTAssertEqual(got.filter { $0.isKeyframe }.count, 2, "first frame + the refinement")
    }

    func testForcedKeyframeAndBitrateChange() throws {
        let lock = NSLock(); var units = [EncodedUnit]()
        let encoder: VideoEncoder
        do { encoder = try VideoEncoder(config: .init(width: Self.width, height: Self.height, fps: 60, bitrate: 3_000_000)) { lock.lock(); units.append($0); lock.unlock() } }
        catch { throw XCTSkip("No encoder: \(error)") }
        for i in 0..<10 { encoder.encode(makeBuffer(phase: i), ptsMicros: Int64(i) * 16_667) }
        encoder.setBitrate(1_500_000)
        encoder.requestKeyframe()
        for i in 10..<14 { encoder.encode(makeBuffer(phase: i), ptsMicros: Int64(i) * 16_667) }
        encoder.finish()
        lock.lock(); let got = units; lock.unlock()
        XCTAssertTrue(got[1].isKeyframe == false, "second frame is a delta frame")
        XCTAssertTrue(got.dropFirst(10).first?.isKeyframe ?? false, "requestKeyframe() must force an IDR on the next frame")
        XCTAssertTrue(got.dropFirst(10).first?.hasParameterSets ?? false, "forced IDR must carry parameter sets so a late joiner can decode it")
    }
}

private final class TestDecoder {
    private var session: VTDecompressionSession?
    private var format: CMVideoFormatDescription?
    private(set) var decodedCount = 0
    private(set) var size = CGSize.zero
    private let lock = NSLock()

    func feed(_ annexB: Data) throws {
        var sps: Data?, pps: Data?, slices = [Data]()
        for nal in AnnexB.nalUnits(annexB) {
            switch (nal.first ?? 0) & 0x1F { case 7: sps = nal; case 8: pps = nal; case 1, 5: slices.append(nal); default: break }
        }
        if session == nil, let sps, let pps { try makeSession(sps: sps, pps: pps) }
        guard let session, let format, !slices.isEmpty else { return }
        var avcc = Data()
        for n in slices { var len = UInt32(n.count).bigEndian; avcc.append(Data(bytes: &len, count: 4)); avcc.append(n) }
        var block: CMBlockBuffer?
        XCTAssertEqual(CMBlockBufferCreateWithMemoryBlock(allocator: nil, memoryBlock: nil, blockLength: avcc.count, blockAllocator: nil,
                                                          customBlockSource: nil, offsetToData: 0, dataLength: avcc.count, flags: 0, blockBufferOut: &block), noErr)
        _ = avcc.withUnsafeBytes { CMBlockBufferReplaceDataBytes(with: $0.baseAddress!, blockBuffer: block!, offsetIntoDestination: 0, dataLength: avcc.count) }
        var sample: CMSampleBuffer?
        XCTAssertEqual(CMSampleBufferCreateReady(allocator: nil, dataBuffer: block, formatDescription: format, sampleCount: 1,
                                                 sampleTimingEntryCount: 0, sampleTimingArray: nil, sampleSizeEntryCount: 0, sampleSizeArray: nil,
                                                 sampleBufferOut: &sample), noErr)
        var flags = VTDecodeInfoFlags()
        let status = VTDecompressionSessionDecodeFrame(session, sampleBuffer: sample!, flags: [], infoFlagsOut: &flags) { [weak self] status, _, image, _, _ in
            guard status == noErr, let image, let self else { return }
            self.lock.lock(); self.decodedCount += 1
            self.size = CGSize(width: CVPixelBufferGetWidth(image), height: CVPixelBufferGetHeight(image)); self.lock.unlock()
        }
        XCTAssertEqual(status, noErr)
    }
    func finish() { if let session { VTDecompressionSessionWaitForAsynchronousFrames(session) } }

    private func makeSession(sps: Data, pps: Data) throws {
        var fd: CMVideoFormatDescription?
        let status = sps.withUnsafeBytes { s in pps.withUnsafeBytes { p -> OSStatus in
            let pointers = [s.bindMemory(to: UInt8.self).baseAddress!, p.bindMemory(to: UInt8.self).baseAddress!]
            let sizes = [sps.count, pps.count]
            return CMVideoFormatDescriptionCreateFromH264ParameterSets(allocator: nil, parameterSetCount: 2, parameterSetPointers: pointers,
                                                                       parameterSetSizes: sizes, nalUnitHeaderLength: 4, formatDescriptionOut: &fd)
        } }
        XCTAssertEqual(status, noErr)
        format = fd
        var s: VTDecompressionSession?
        XCTAssertEqual(VTDecompressionSessionCreate(allocator: nil, formatDescription: fd!, decoderSpecification: nil, imageBufferAttributes: nil,
                                                    outputCallback: nil, decompressionSessionOut: &s), noErr)
        session = s
    }
}
