import Foundation

public enum WireError: Error, Equatable {
    case badMagic, badVersion, badKind, badReserved, tooLarge, truncated, badFrame
}
public enum PacketKind: UInt8 { case json = 1, jpeg = 2, pcm = 3 }
public struct Packet {
    public let kind: PacketKind
    public let payload: Data
    public init(kind: PacketKind, payload: Data) { self.kind = kind; self.payload = payload }
}
public enum Wire {
    public static let headerSize = 12
    public static let maxJSON = 65_536
    public static let maxJPEG = 8_388_608
    public static let maxPCM = 384_008 // 12 seconds, 16 kHz, mono PCM16 + 8-byte header
    public static func limit(_ kind: PacketKind) -> Int {
        switch kind { case .json: return maxJSON; case .jpeg: return maxJPEG; case .pcm: return maxPCM }
    }
    public static func header(kind: PacketKind, count: Int) throws -> Data {
        guard count >= 0 && count <= limit(kind) else { throw WireError.tooLarge }
        var data = Data([0x53, 0x4d, 0x41, 0x43, 1, kind.rawValue, 0, 0])
        data.appendBE(UInt32(count)); return data
    }
    public static func decodeHeader(_ data: Data) throws -> (PacketKind, Int) {
        let b = [UInt8](data)
        guard b.count == headerSize else { throw WireError.truncated }
        guard Array(b[0..<4]) == [0x53, 0x4d, 0x41, 0x43] else { throw WireError.badMagic }
        guard b[4] == 1 else { throw WireError.badVersion }
        guard let kind = PacketKind(rawValue: b[5]) else { throw WireError.badKind }
        guard b[6] == 0 && b[7] == 0 else { throw WireError.badReserved }
        let n = Int(b[8]) << 24 | Int(b[9]) << 16 | Int(b[10]) << 8 | Int(b[11])
        guard n <= limit(kind) else { throw WireError.tooLarge }
        return (kind, n)
    }
    public static func encode(_ packet: Packet) throws -> Data {
        var data = try header(kind: packet.kind, count: packet.payload.count)
        data.append(packet.payload); return data
    }
    public static func json<T: Encodable>(_ value: T) throws -> Data {
        try encode(Packet(kind: .json, payload: JSONEncoder().encode(value)))
    }
}
public struct JPEGFrame {
    public static let metadataSize = 24
    public let streamID: UInt32
    public let sequence: UInt32
    public let width: UInt16
    public let height: UInt16
    public let generation: UInt32
    public let capturedMicros: UInt64
    public let jpeg: Data
    public init(streamID: UInt32, sequence: UInt32, width: UInt16, height: UInt16,
                generation: UInt32, capturedMicros: UInt64, jpeg: Data) {
        self.streamID = streamID; self.sequence = sequence; self.width = width
        self.height = height; self.generation = generation
        self.capturedMicros = capturedMicros; self.jpeg = jpeg
    }
    public func payload() throws -> Data {
        guard streamID > 0, generation > 0, width > 0, height > 0,
              width <= 4096, height <= 4096, jpeg.count >= 2,
              jpeg.starts(with: [0xff, 0xd8]),
              jpeg.count + Self.metadataSize <= Wire.maxJPEG else { throw WireError.badFrame }
        var d = Data(); d.appendBE(streamID); d.appendBE(sequence)
        d.appendBE(width); d.appendBE(height); d.appendBE(generation)
        d.appendBE(capturedMicros); d.append(jpeg); return d
    }
    public static func decode(_ data: Data) throws -> JPEGFrame {
        guard data.count >= metadataSize + 2, data.count <= Wire.maxJPEG else { throw WireError.badFrame }
        let b = [UInt8](data)
        let f = JPEGFrame(streamID: read32(b, 0), sequence: read32(b, 4),
                          width: read16(b, 8), height: read16(b, 10),
                          generation: read32(b, 12), capturedMicros: read64(b, 16),
                          jpeg: Data(b[24...]))
        _ = try f.payload(); return f
    }
}
public extension Data {
    mutating func appendBE<T: FixedWidthInteger>(_ value: T) {
        var v = value.bigEndian
        Swift.withUnsafeBytes(of: &v) { append(contentsOf: $0) }
    }
}
public func read16(_ b: [UInt8], _ i: Int) -> UInt16 { UInt16(b[i]) << 8 | UInt16(b[i+1]) }
public func read32(_ b: [UInt8], _ i: Int) -> UInt32 {
    UInt32(b[i]) << 24 | UInt32(b[i+1]) << 16 | UInt32(b[i+2]) << 8 | UInt32(b[i+3])
}
public func read64(_ b: [UInt8], _ i: Int) -> UInt64 {
    UInt64(read32(b, i)) << 32 | UInt64(read32(b, i+4))
}
