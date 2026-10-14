import Foundation

public struct RectDTO: Codable, Equatable {
    public var x: Double, y: Double, width: Double, height: Double
    public init(x: Double, y: Double, width: Double, height: Double) {
        self.x = x; self.y = y; self.width = width; self.height = height
    }
    public func point(u: Double, v: Double) -> (Double, Double)? {
        guard u.isFinite, v.isFinite, (0...1).contains(u), (0...1).contains(v),
              width.isFinite, height.isFinite, x.isFinite, y.isFinite,
              width > 0, height > 0 else { return nil }
        // The last pixel/point must stay inside the half-open rectangle.
        return (x + min(u * width, max(0, width - 0.5)),
                y + min(v * height, max(0, height - 0.5)))
    }
    public func approximatelyEquals(_ r: RectDTO, tolerance: Double = 1) -> Bool {
        abs(x-r.x) <= tolerance && abs(y-r.y) <= tolerance &&
        abs(width-r.width) <= tolerance && abs(height-r.height) <= tolerance
    }
}
public struct SourceDTO: Codable, Identifiable {
    public var id: String
    public var kind: String
    public var title: String
    public var app: String
    public var pid: Int32
    public var bounds: RectDTO
    public init(id: String, kind: String, title: String, app: String, pid: Int32, bounds: RectDTO) {
        self.id=id; self.kind=kind; self.title=title; self.app=app; self.pid=pid; self.bounds=bounds
    }
}
// Explicit optional fields keep one JSON envelope interoperable with Unity JsonUtility.
public struct Message: Codable {
    public var type: String
    public var token: String?, clientID: String?, role: String?, sourceID: String?
    public var streamID: UInt32?, generation: UInt32?
    public var fps: Int?, maxWidth: Int?
    public var action: String?, button: Int?, keyCode: Int?, modifiers: UInt64?
    public var u: Double?, v: Double?, dx: Double?, dy: Double?
    public var text: String?, appID: String?, error: String?, requestID: String?
    public var controlEnabled: Bool?, sharingEnabled: Bool?, speechEnabled: Bool?
    // Integrations (additive; protocol framing version is unchanged).
    public var assistantEnabled: Bool?, messagesEnabled: Bool?, replyEnabled: Bool?, replyable: Bool?
    public var title: String?, body: String?, appName: String?, category: String?
    public var notificationID: String?, confirmID: String?
    public var command: String?, arg: String?, value: Double?
    public var sources: [SourceDTO]?
    public init(_ type: String) { self.type = type }
}
public enum InputValidation {
    public static let shortcuts: Set<String> = ["copy", "paste", "undo", "redo", "find", "select_all", "escape", "enter", "backspace", "tab"]
    /// Explicit launch allowlist. The headset can never supply a path or command.
    public static let apps: Set<String> = [
        "com.apple.finder", "com.apple.Safari", "com.apple.TextEdit", "com.apple.dt.Xcode",
        "com.apple.MobileSMS", "com.apple.Notes", "com.apple.iCal", "com.apple.Music",
        "com.openai.chat"
    ]
    public static func isValid(_ m: Message) -> Bool {
        switch m.type {
        case "pointer":
            guard let a=m.action, ["move", "down", "up", "scroll"].contains(a),
                  let u=m.u, let v=m.v, u.isFinite, v.isFinite,
                  (0...1).contains(u), (0...1).contains(v),
                  let s=m.streamID, s > 0, let g=m.generation, g > 0 else { return false }
            if let b=m.button, ![0,1].contains(b) { return false }
            if a == "scroll" {
                return [m.dx ?? 0, m.dy ?? 0].allSatisfy { $0.isFinite && abs($0) <= 100 }
            }
            return true
        case "text":
            guard let t=m.text, !t.isEmpty, t.utf16.count <= 512 else { return false }
            return (m.streamID ?? 0) > 0 && (m.generation ?? 0) > 0
        case "shortcut":
            return shortcuts.contains(m.action ?? "") && (m.streamID ?? 0) > 0 && (m.generation ?? 0) > 0
        case "launch": return apps.contains(m.appID ?? "")
        default: return false
        }
    }
    public static func tokenMatches(_ expected: String, _ supplied: String) -> Bool {
        // Fixed-length comparison. Token is 32 random bytes represented as 64 hex characters.
        let a=Array(expected.utf8), b=Array(supplied.utf8)
        guard a.count == 64, b.count == 64 else { return false }
        var difference: UInt8=0
        for i in 0..<64 { difference |= a[i] ^ b[i] }
        return difference == 0
    }
}
