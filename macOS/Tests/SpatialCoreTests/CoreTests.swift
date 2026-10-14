import XCTest
@testable import SpatialCore

final class CoreTests: XCTestCase {
    func testWireHeaderGoldenBytes() throws {
        XCTAssertEqual(Array(try Wire.header(kind: .json, count: 258)),
                       [83,77,65,67,1,1,0,0,0,0,1,2])
    }
    func testHeaderRoundTrip() throws {
        for (k,n) in [(PacketKind.json, 12), (.jpeg, 100_000), (.pcm, 32_008)] {
            let r=try Wire.decodeHeader(Wire.header(kind:k,count:n))
            XCTAssertEqual(r.0.rawValue,k.rawValue); XCTAssertEqual(r.1,n)
        }
    }
    func testMalformedHeaders() throws {
        for index in [0,4,5,6,7] {
            var h=try Wire.header(kind:.json,count:12); h[index]=255
            XCTAssertThrowsError(try Wire.decodeHeader(h))
        }
        XCTAssertThrowsError(try Wire.decodeHeader(Data([0,1])))
        XCTAssertThrowsError(try Wire.header(kind:.json,count:65_537))
        XCTAssertThrowsError(try Wire.header(kind:.pcm,count:-1))
    }
    func testFrameRoundTrip() throws {
        let f=JPEGFrame(streamID:7,sequence:42,width:1280,height:720,generation:3,
                        capturedMicros:1_234_567,jpeg:Data([255,216,255,217]))
        let r=try JPEGFrame.decode(f.payload())
        XCTAssertEqual(r.streamID,7); XCTAssertEqual(r.sequence,42)
        XCTAssertEqual(r.width,1280); XCTAssertEqual(r.height,720)
        XCTAssertEqual(r.generation,3); XCTAssertEqual(r.capturedMicros,1_234_567)
        XCTAssertEqual(r.jpeg,f.jpeg)
    }
    func testBadFrames() throws {
        XCTAssertThrowsError(try JPEGFrame.decode(Data(repeating:0,count:30)))
        XCTAssertThrowsError(try JPEGFrame(streamID:0,sequence:1,width:1,height:1,
            generation:1,capturedMicros:0,jpeg:Data([255,216])).payload())
    }
    func testCoordinatesWithNegativeDisplayOrigin() {
        let r=RectDTO(x:-1920,y:200,width:1920,height:1080)
        let p=r.point(u:0.5,v:0.5)!
        XCTAssertEqual(p.0,-960); XCTAssertEqual(p.1,740)
        XCTAssertNil(r.point(u:.nan,v:0)); XCTAssertNil(r.point(u:1.01,v:0))
        XCTAssertLessThan(r.point(u:1,v:1)!.0,0)
    }
    func testControlValidation() {
        var m=Message("pointer");m.streamID=1;m.generation=1;m.u=0.2;m.v=0.3;m.action="down"
        XCTAssertTrue(InputValidation.isValid(m))
        m.u = .infinity;XCTAssertFalse(InputValidation.isValid(m))
        m.u=0;m.action="run_shell";XCTAssertFalse(InputValidation.isValid(m))
        m.action="scroll";m.dy=101;XCTAssertFalse(InputValidation.isValid(m))
        m.dy=1;XCTAssertTrue(InputValidation.isValid(m))
    }
    func testTokenAndAllowlist() {
        XCTAssertTrue(InputValidation.tokenMatches(String(repeating:"a",count:64),String(repeating:"a",count:64)))
        XCTAssertFalse(InputValidation.tokenMatches(String(repeating:"a",count:64),String(repeating:"b",count:64)))
        XCTAssertFalse(InputValidation.tokenMatches("abc","abc"))
        var m=Message("launch");m.appID="com.apple.Safari";XCTAssertTrue(InputValidation.isValid(m))
        m.appID="/bin/sh";XCTAssertFalse(InputValidation.isValid(m))
    }
    func testJSONInteroperability() throws {
        var m=Message("subscribe");m.streamID=9;m.sourceID="display:1";m.maxWidth=1280
        let data=try JSONEncoder().encode(m)
        let decoded=try JSONDecoder().decode(Message.self,from:data)
        XCTAssertEqual(decoded.type,"subscribe");XCTAssertEqual(decoded.streamID,9)
        XCTAssertEqual(decoded.maxWidth,1280)
    }
}
