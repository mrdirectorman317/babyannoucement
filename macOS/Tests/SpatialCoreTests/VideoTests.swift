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

/// Unity's JsonUtility writes EVERY field of Message with its default (0, "", false). The host must read those
/// defaults as "no preference", not as real values.
final class UnityEnvelopeTests: XCTestCase {
    private func decode(_ json: String) throws -> Message { try JSONDecoder().decode(Message.self, from: Data(json.utf8)) }
    private let defaults = #""token":"","clientID":"","role":"","sourceID":"","action":"","text":"","appID":"","error":"","requestID":"","title":"","body":"","appName":"","category":"","notificationID":"","confirmID":"","command":"","arg":"","codec":"","kbps":0,"t0":0,"t1":0,"streamID":0,"generation":0,"fps":0,"maxWidth":0,"button":0,"keyCode":0,"modifiers":0,"u":0,"v":0,"dx":0,"dy":0,"value":0,"controlEnabled":false,"sharingEnabled":false,"speechEnabled":false,"assistantEnabled":false,"messagesEnabled":false,"replyEnabled":false,"replyable":false"#
    func testSubscribeWithVideoPreference() throws {
        let m = try decode(#"{"type":"subscribe",\#(defaults.replacingOccurrences(of: #""codec":"""#, with: #""codec":"h264""#).replacingOccurrences(of: #""streamID":0"#, with: #""streamID":5"#).replacingOccurrences(of: #""fps":0"#, with: #""fps":60"#).replacingOccurrences(of: #""maxWidth":0"#, with: #""maxWidth":1920"#)),"sources":[]}"#)
        XCTAssertEqual(m.type, "subscribe"); XCTAssertEqual(m.streamID, 5); XCTAssertEqual(m.fps, 60)
        XCTAssertEqual(m.codec.flatMap(VideoCodec.init(name:)), .h264)
        XCTAssertEqual(m.kbps, 0, "0 means 'host default' and must not be clamped to a 1 Mbps stream")
    }
    func testEmptyCodecMeansJPEG() throws {
        let m = try decode(#"{"type":"subscribe",\#(defaults)}"#)
        XCTAssertNil(m.codec.flatMap(VideoCodec.init(name:)))
    }
    func testPingCarriesClientClock() throws {
        let m = try decode(#"{"type":"ping","requestID":"abc","t0":1234567890123.5,"t1":0}"#)
        XCTAssertEqual(m.t0, 1234567890123.5)
    }
    func testKeyframeRequest() throws {
        let m = try decode(#"{"type":"request_keyframe",\#(defaults.replacingOccurrences(of: #""streamID":0"#, with: #""streamID":3"#))}"#)
        XCTAssertEqual(m.type, "request_keyframe"); XCTAssertEqual(m.streamID, 3)
    }
}
