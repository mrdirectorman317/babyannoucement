using System.Collections.Generic;
using UnityEngine;

namespace SpatialMac {
    // Per-stream live numbers for the on-panel readout (Home > Stats): frames/s shown, bitrate, and capture-to-display latency.
    public sealed class StreamStats {
        sealed class Entry { public int frames, shown; public long bytes; public float windowStart; public float fps, mbps; public float latencyMs = -1; }
        readonly Dictionary<uint, Entry> entries = new Dictionary<uint, Entry>();
        Entry Get(uint id) { if (!entries.TryGetValue(id, out var e)) { e = new Entry { windowStart = Time.unscaledTime }; entries[id] = e; } return e; }
        public void Frame(uint id, long totalBytes, double latencyMs) {
            var e = Get(id);
            e.frames++;
            if (latencyMs > -1000 && latencyMs < 5000) e.latencyMs = e.latencyMs < 0 ? (float)latencyMs : Mathf.Lerp(e.latencyMs, (float)latencyMs, 0.1f);
            float now = Time.unscaledTime, span = now - e.windowStart;
            if (span >= 1f) {
                e.fps = e.frames / span; e.mbps = (totalBytes - e.bytes) * 8f / 1e6f / span;
                e.bytes = totalBytes; e.frames = 0; e.windowStart = now;
            }
        }
        public void Remove(uint id) { entries.Remove(id); }
        public string Line(SpatialPanel p) {
            if (!entries.TryGetValue(p.StreamID, out var e)) return "no frames yet";
            string lat = e.latencyMs >= 0 ? e.latencyMs.ToString("0") + " ms" : "- ms";
            string codec = p.Codec == "hevc" ? "HEVC" : p.Codec == "h264" ? "H.264" : "JPEG";
            int dropped = p.Decoder != null ? p.Decoder.FramesDropped : 0;
            return codec + " " + p.PixelWidth + "×" + p.PixelHeight + " · " + e.fps.ToString("0") + " fps · " + lat + " · " + e.mbps.ToString("0.0") + " Mbps" + (dropped > 0 ? " · " + dropped + " dropped" : "");
        }
    }
}
