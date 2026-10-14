import Foundation

public enum VideoCodec: UInt8 {
    case h264 = 1, hevc = 2
    public init?(name: String) {
        switch name.lowercased() { case "h264": self = .h264; case "hevc": self = .hevc; default: return nil }
    }
    public var name: String { self == .h264 ? "h264" : "hevc" }
}

/// One encoded access unit (Annex-B, parameter sets in-band on keyframes) for the `video` packet kind.
/// Layout (28-byte big-endian prefix, then the bitstream):
///   u32 streamID, u32 sequence, u16 width, u16 height, u32 generation, u64 ptsMicros (host-monotonic),
///   u8 flags (bit0 keyframe, bit1 has parameter sets), u8 codec, u16 reserved(0)
public struct VideoFrame: Equatable {
    public static let metadataSize = 28
    public static let flagKeyframe: UInt8 = 1, flagParameterSets: UInt8 = 2
    public let streamID: UInt32, sequence: UInt32
    public let width: UInt16, height: UInt16
    public let generation: UInt32
    public let ptsMicros: UInt64
    public let flags: UInt8
    public let codec: VideoCodec
    public let bitstream: Data
    public var isKeyframe: Bool { flags & Self.flagKeyframe != 0 }

    public init(streamID: UInt32, sequence: UInt32, width: UInt16, height: UInt16, generation: UInt32,
                ptsMicros: UInt64, flags: UInt8, codec: VideoCodec, bitstream: Data) {
        self.streamID = streamID; self.sequence = sequence; self.width = width; self.height = height
        self.generation = generation; self.ptsMicros = ptsMicros; self.flags = flags
        self.codec = codec; self.bitstream = bitstream
    }
    private func validate() throws {
        guard streamID > 0, generation > 0, width > 0, height > 0, width <= 8192, height <= 8192,
              !bitstream.isEmpty, bitstream.count + Self.metadataSize <= Wire.maxVideo,
              flags & ~(Self.flagKeyframe | Self.flagParameterSets) == 0,
              AnnexB.startsWithStartCode(bitstream) else { throw WireError.badFrame }
    }
    public func payload() throws -> Data {
        try validate()
        var d = Data(); d.appendBE(streamID); d.appendBE(sequence); d.appendBE(width); d.appendBE(height)
        d.appendBE(generation); d.appendBE(ptsMicros); d.append(flags); d.append(codec.rawValue)
        d.appendBE(UInt16(0)); d.append(bitstream); return d
    }
    public static func decode(_ data: Data) throws -> VideoFrame {
        guard data.count > metadataSize, data.count <= Wire.maxVideo else { throw WireError.badFrame }
        let b = [UInt8](data)
        guard b[26] == 0, b[27] == 0, let codec = VideoCodec(rawValue: b[25]) else { throw WireError.badFrame }
        let f = VideoFrame(streamID: read32(b, 0), sequence: read32(b, 4), width: read16(b, 8), height: read16(b, 10),
                           generation: read32(b, 12), ptsMicros: read64(b, 16), flags: b[24], codec: codec,
                           bitstream: Data(b[28...]))
        try f.validate(); return f
    }
}

public enum AnnexB {
    public static func startsWithStartCode(_ d: Data) -> Bool {
        let b = [UInt8](d.prefix(4))
        return (b.count >= 4 && b[0] == 0 && b[1] == 0 && b[2] == 0 && b[3] == 1) || (b.count >= 3 && b[0] == 0 && b[1] == 0 && b[2] == 1)
    }
    /// Converts length-prefixed NAL units (AVCC/HVCC as produced by VideoToolbox) into Annex-B start-code form.
    /// Returns nil for any malformed length (never reads out of bounds).
    public static func fromLengthPrefixed(_ data: Data, lengthSize: Int = 4) -> Data? {
        guard (1...4).contains(lengthSize) else { return nil }
        let b = [UInt8](data)
        var out = Data(), i = 0
        while i < b.count {
            guard i + lengthSize <= b.count else { return nil }
            var n = 0
            for k in 0..<lengthSize { n = n << 8 | Int(b[i + k]) }
            i += lengthSize
            guard n > 0, i + n <= b.count else { return nil }
            out.append(contentsOf: [0, 0, 0, 1]); out.append(contentsOf: b[i..<(i + n)]); i += n
        }
        return out.isEmpty ? nil : out
    }
    /// Splits an Annex-B stream into NAL units (without start codes).
    public static func nalUnits(_ data: Data) -> [Data] {
        let b = [UInt8](data)
        var starts = [(code: Int, payload: Int)](), i = 0
        while i + 3 <= b.count {
            if b[i] == 0, b[i + 1] == 0 {
                if b[i + 2] == 1 { starts.append((i, i + 3)); i += 3; continue }
                if i + 4 <= b.count, b[i + 2] == 0, b[i + 3] == 1 { starts.append((i, i + 4)); i += 4; continue }
            }
            i += 1
        }
        return starts.enumerated().map { idx, s in
            let end = idx + 1 < starts.count ? starts[idx + 1].code : b.count
            return Data(b[s.payload..<end])
        }
    }
    public static func nalType(_ nal: Data, codec: VideoCodec) -> Int? {
        guard let first = nal.first else { return nil }
        return codec == .h264 ? Int(first & 0x1F) : Int((first >> 1) & 0x3F)
    }
    public static func containsKeyframe(_ data: Data, codec: VideoCodec) -> Bool {
        nalUnits(data).contains { nal in
            guard let t = nalType(nal, codec: codec) else { return false }
            return codec == .h264 ? t == 5 : (t == 19 || t == 20 || t == 21)
        }
    }
    public static func containsParameterSets(_ data: Data, codec: VideoCodec) -> Bool {
        nalUnits(data).contains { nal in
            guard let t = nalType(nal, codec: codec) else { return false }
            return codec == .h264 ? (t == 7 || t == 8) : (t >= 32 && t <= 34)
        }
    }
}
