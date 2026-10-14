import Foundation

/// Notification/message text is attacker-controlled data. Before it reaches the
/// headset (or a language model) strip invisible/bidi-control characters that can
/// spoof what the user reads, flatten whitespace and bound the length.
public enum TextSanitizer {
    static let stripped: Set<UInt32> = [
        0x200B, 0x200E, 0x200F, 0x202A, 0x202B, 0x202C, 0x202D, 0x202E,
        0x2060, 0x2066, 0x2067, 0x2068, 0x2069, 0xFEFF, 0xFFFC
    ]
    public static func clean(_ text: String, maxLength: Int) -> String {
        var out = String.UnicodeScalarView()
        var lastWasSpace = true
        for scalar in text.unicodeScalars {
            if stripped.contains(scalar.value) { continue }
            let isWhitespace = scalar == "\n" || scalar == "\r" || scalar == "\t" || scalar == " "
            if isWhitespace {
                if !lastWasSpace { out.append(" ") }
                lastWasSpace = true
                continue
            }
            // Drop remaining control characters (C0, DEL, C1).
            if scalar.value < 0x20 || (0x7F...0x9F).contains(scalar.value) { continue }
            out.append(scalar); lastWasSpace = false
        }
        var result = String(out).trimmingCharacters(in: .whitespaces)
        if result.count > maxLength {
            result = String(result.prefix(max(0, maxLength - 1))) + "…"
        }
        return result
    }
}
