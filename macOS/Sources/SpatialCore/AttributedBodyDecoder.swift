import Foundation

/// Recent macOS versions often leave `message.text` NULL in chat.db and store the
/// body inside `attributedBody`, an NSArchiver "typedstream" blob. This is a
/// deliberately small, bounds-checked extractor for the plain string only; it does
/// not implement the typedstream format. Returns nil when the layout is unfamiliar.
public enum AttributedBodyDecoder {
    public static func plainText(from blob: Data) -> String? {
        let bytes = [UInt8](blob)
        guard bytes.count >= 16, bytes.count <= 1_000_000 else { return nil }
        let marker = Array("NSString".utf8)
        guard let at = find(marker, in: bytes) else { return nil }
        // After the class name: a few typedstream bytes (version/type tags), then '+' (0x2B)
        // introducing the string, then a length: 1 byte, or 0x81 + 2 bytes LE, or 0x82 + 4 bytes LE.
        var i = at + marker.count
        let limit = min(bytes.count, i + 16)
        while i < limit && bytes[i] != 0x2B { i += 1 }
        guard i < limit, bytes[i] == 0x2B else { return nil }
        i += 1
        guard i < bytes.count else { return nil }
        var length = 0
        switch bytes[i] {
        case 0x81:
            guard i + 2 < bytes.count else { return nil }
            length = Int(bytes[i + 1]) | Int(bytes[i + 2]) << 8; i += 3
        case 0x82:
            guard i + 4 < bytes.count else { return nil }
            length = Int(bytes[i + 1]) | Int(bytes[i + 2]) << 8 | Int(bytes[i + 3]) << 16 | Int(bytes[i + 4]) << 24
            i += 5
        default:
            length = Int(bytes[i]); i += 1
        }
        guard length > 0, length <= 100_000, i + length <= bytes.count else { return nil }
        return String(data: Data(bytes[i..<(i + length)]), encoding: .utf8)
    }
    private static func find(_ needle: [UInt8], in hay: [UInt8]) -> Int? {
        guard hay.count >= needle.count else { return nil }
        for start in 0...(hay.count - needle.count) where hay[start] == needle[0] {
            if Array(hay[start..<(start + needle.count)]) == needle { return start }
        }
        return nil
    }
}
