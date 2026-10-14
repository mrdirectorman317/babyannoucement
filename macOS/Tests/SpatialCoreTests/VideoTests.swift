import XCTest
@testable import SpatialCore

final class VideoTests: XCTestCase {
    private func nal(_ header: UInt8, _ body: [UInt8]) -> [UInt8] { [header] + body }
    func testFrameRoundTrip() throws {
        let bits = Data([0, 0, 0, 1, 0x67, 1, 2, 0, 0, 0, 1, 0x65, 9, 9])
        let f = VideoFrame(streamID: 3, sequence: 77, width: 1920, height: 1080, generation: 2, ptsMicros: 123_456_789,
                           flags: VideoFrame.flagKeyframe | VideoFrame.flagParameterSets, codec: .h264, bitstream: bits)
        let back = try VideoFrame.decode(f.payload())
        XCTAssertEqual(back, f); XCTAssertTrue(back.isKeyframe)
        XCTAssertEqual(try f.payload().count, 28 + bits.count)
    }
    func testGoldenPrefixBytes() throws {
        let f = VideoFrame(streamID: 1, sequence: 2, width: 640, height: 360, generation: 3, ptsMicros: 4, flags: 1, codec: .hevc,
                           bitstream: Data([0, 0, 1, 0x26]))
        let p = [UInt8](try f.payload())
        XCTAssertEqual(Array(p[0..<28]), [0,0,0,1, 0,0,0,2, 2,128, 1,104, 0,0,0,3, 0,0,0,0,0,0,0,4, 1, 2, 0, 0])
    }
    func testRejectsBadFrames() {
        XCTAssertThrowsError(try VideoFrame.decode(Data(repeating: 0, count: 28)))            // empty bitstream
        var good = (try? VideoFrame(streamID: 1, sequence: 0, width: 2, height: 2, generation: 1, ptsMicros: 0, flags: 0, codec: .h264,
                                    bitstream: Data([0, 0, 0, 1, 0x41])).payload()) ?? Data()
        XCTAssertNoThrow(try VideoFrame.decode(good))
        good[25] = 9; XCTAssertThrowsError(try VideoFrame.decode(good))                       // unknown codec
        good[25] = 1; good[26] = 1; XCTAssertThrowsError(try VideoFrame.decode(good))         // reserved bits
        good[26] = 0; good[24] = 0x80; XCTAssertThrowsError(try VideoFrame.decode(good))      // unknown flag
        XCTAssertThrowsError(try VideoFrame(streamID: 1, sequence: 0, width: 2, height: 2, generation: 1, ptsMicros: 0, flags: 0, codec: .h264,
                                            bitstream: Data([1, 2, 3, 4])).payload())          // no start code
    }
    func testVideoPacketKindHeader() throws {
        let h = try Wire.header(kind: .video, count: 100)
        XCTAssertEqual(Array(h), [83,77,65,67,1,4,0,0,0,0,0,100])
        let r = try Wire.decodeHeader(h); XCTAssertEqual(r.0, .video); XCTAssertEqual(r.1, 100)
        XCTAssertThrowsError(try Wire.header(kind: .video, count: Wire.maxVideo + 1))
    }
    func testAnnexBConversion() throws {
        let a: [UInt8] = [0x67, 1, 2, 3], b: [UInt8] = [0x65, 9, 8]
        let avcc = Data([0,0,0,4] + a + [0,0,0,3] + b)
        let out = try XCTUnwrap(AnnexB.fromLengthPrefixed(avcc))
        XCTAssertEqual([UInt8](out), [0,0,0,1] + a + [0,0,0,1] + b)
        XCTAssertEqual(AnnexB.nalUnits(out).map { [UInt8]($0) }, [a, b])
        XCTAssertTrue(AnnexB.containsKeyframe(out, codec: .h264)); XCTAssertTrue(AnnexB.containsParameterSets(out, codec: .h264))
    }
    func testAnnexBRejectsMalformed() {
        XCTAssertNil(AnnexB.fromLengthPrefixed(Data([0,0,0,9, 1, 2])))      // length beyond data
        XCTAssertNil(AnnexB.fromLengthPrefixed(Data([0,0,0])))              // truncated length
        XCTAssertNil(AnnexB.fromLengthPrefixed(Data([0,0,0,0])))            // zero-length NAL
        XCTAssertNil(AnnexB.fromLengthPrefixed(Data()))
        XCTAssertNil(AnnexB.fromLengthPrefixed(Data([0,0,0,1,1]), lengthSize: 5))
    }
    func testNalClassificationHEVC() {
        let idr = Data([0,0,0,1, 0x26, 1, 0,0,1, 0x42, 1])                  // IDR_W_RADL (19) + SPS (33)
        XCTAssertTrue(AnnexB.containsKeyframe(idr, codec: .hevc)); XCTAssertTrue(AnnexB.containsParameterSets(idr, codec: .hevc))
        XCTAssertFalse(AnnexB.containsKeyframe(Data([0,0,0,1, 0x02, 1]), codec: .hevc))   // TRAIL_R (1)
        XCTAssertFalse(AnnexB.containsKeyframe(Data([0,0,0,1, 0x41, 1]), codec: .h264))   // non-IDR slice
    }
    func testBitrateBacksOffFastAndRecoversSlowly() {
        var r = AdaptiveBitrate(initial: 12_000_000, minimum: 3_000_000, maximum: 20_000_000)
        let t0 = Date(timeIntervalSince1970: 1000)
        XCTAssertEqual(r.congestion(now: t0), 8_400_000)
        XCTAssertNil(r.congestion(now: t0.addingTimeInterval(0.2)), "rate limited")
        XCTAssertNotNil(r.congestion(now: t0.addingTimeInterval(0.7)))
        XCTAssertNil(r.tick(now: t0.addingTimeInterval(2)), "no recovery right after congestion")
        let up = r.tick(now: t0.addingTimeInterval(6)); XCTAssertNotNil(up); XCTAssertGreaterThan(up!, r.minimum)
        for i in 0..<60 { _ = r.congestion(now: t0.addingTimeInterval(10 + Double(i))) }
        XCTAssertEqual(r.current, 3_000_000, "never below minimum")
        for i in 0..<400 { _ = r.tick(now: t0.addingTimeInterval(100 + Double(i) * 3)) }
        XCTAssertEqual(r.current, 20_000_000, "never above maximum")
    }
    func testSuggestedBitrate() {
        XCTAssertEqual(AdaptiveBitrate.suggested(width: 1920, height: 1080, fps: 60), 12_441_600)
        XCTAssertEqual(AdaptiveBitrate.suggested(width: 320, height: 200, fps: 5), 3_000_000)
    }
}
