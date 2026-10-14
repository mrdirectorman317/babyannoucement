import Foundation

/// Simple AIMD-style controller for the video bitrate. The host calls `congestion` when a client's send queue
/// overflows (it had to drop frames) and `tick` periodically; the bitrate backs off fast and recovers slowly.
public struct AdaptiveBitrate: Equatable {
    public let minimum: Int, maximum: Int
    public private(set) var current: Int
    private var lastCongestion = Date.distantPast
    private var lastIncrease = Date.distantPast
    public init(initial: Int, minimum: Int, maximum: Int) {
        precondition(minimum > 0 && minimum <= maximum)
        self.minimum = minimum; self.maximum = maximum
        self.current = Swift.min(Swift.max(initial, minimum), maximum)
    }
    /// Returns the new bitrate if it changed.
    @discardableResult public mutating func congestion(now: Date = Date()) -> Int? {
        guard now.timeIntervalSince(lastCongestion) > 0.5 else { return nil }   // react once per half second
        lastCongestion = now
        let reduced = Swift.max(minimum, Int(Double(current) * 0.7))
        guard reduced != current else { return nil }
        current = reduced; return current
    }
    @discardableResult public mutating func tick(now: Date = Date()) -> Int? {
        // Recover cautiously: every overshoot costs the viewer a freeze + keyframe, so wait for a long calm stretch.
        guard now.timeIntervalSince(lastCongestion) > 10, now.timeIntervalSince(lastIncrease) > 5, current < maximum else { return nil }
        lastIncrease = now
        current = Swift.min(maximum, Int(Double(current) * 1.08) + 1)
        return current
    }
    /// A sensible starting point: ~0.1 bit per pixel per frame for H.264 (about 12 Mbps for 1080p60). HEVC reaches the
    /// same quality at roughly two thirds of that, which is the point of using it: crisper text for the same bandwidth.
    public static func suggested(width: Int, height: Int, fps: Int, codec: VideoCodec = .h264) -> Int {
        let bitsPerPixel = codec == .hevc ? 0.065 : 0.1
        return Swift.min(40_000_000, Swift.max(2_000_000, Int(Double(width * height * fps) * bitsPerPixel)))
    }
}
