using System;
using System.Collections.Concurrent;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using System.Runtime.InteropServices;
using Unity.Collections;
#endif

namespace SpatialMac {
    // Hardware video for one stream. Network thread -> Enqueue(); main thread -> Pump() once per rendered frame.
    // The Android build wraps org.spatialmac.SpatialMacDecoder (MediaCodec, Plugins/Android). Elsewhere (the Editor)
    // this is an inert stub and the session simply asks the Mac for JPEG instead.
    public sealed class VideoStreamDecoder : IDisposable {
        public const int MaxPending = 10;           // ~170 ms at 60 fps; beyond this we resync on the next keyframe
        public readonly uint StreamID, Generation;
        public Texture2D YTex, UVTex, VTex;         // luma / chroma (interleaved UV or separate U,V)
        public bool Planar;                         // true: U and V are separate R8 textures
        public Vector4 Scale = new Vector4(1, 1, 1, 1);   // xy luma uv scale, zw chroma uv scale (strides are wider than the picture)
        public int Width, Height;
        public ulong LastPtsMicros;                 // host-clock capture time of the frame now on screen
        public int FramesDecoded, FramesDropped;
        public string Error = "";
        public bool Failed { get; private set; }
        readonly ConcurrentQueue<VideoFrame> pending = new ConcurrentQueue<VideoFrame>();
        volatile bool needKeyframe = true, wantsKeyframe = true;
        VideoFrame retry; int retryPumps;      // main thread only: a frame the codec had no input buffer for yet
        public long BytesReceived;

        // Called on the network thread.
        public void Enqueue(VideoFrame f) {
            if (f.StreamID != StreamID || f.Generation != Generation) return;
            System.Threading.Interlocked.Add(ref BytesReceived, f.Bitstream.Length);
            if (pending.Count >= MaxPending) {      // we are behind: flush and wait for a clean keyframe
                while (pending.TryDequeue(out _)) FramesDropped++;
                needKeyframe = true; wantsKeyframe = true;
            }
            pending.Enqueue(f);
        }
        // Main thread: true once if the decoder wants an IDR (new stream, dropped frames, decode error).
        public bool ConsumeKeyframeRequest() { bool w = wantsKeyframe; wantsKeyframe = false; return w; }
        void LostSync() { needKeyframe = true; wantsKeyframe = true; }

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject java;
        bool started;
        byte[] scratch = new byte[0];
        public VideoStreamDecoder(uint streamID, uint generation) { StreamID = streamID; Generation = generation; }
        public bool Pump() {
            if (Failed) return false;
            try {
                int budget = 8;
                while (budget-- > 0) {
                    VideoFrame f = retry; retry = null;
                    if (f == null && !pending.TryDequeue(out f)) break;
                    if (needKeyframe && !f.IsKeyframe) { FramesDropped++; continue; }
                    if (f.IsKeyframe) needKeyframe = false;
                    if (!started) {
                        java = new AndroidJavaObject("org.spatialmac.SpatialMacDecoder");
                        if (!java.Call<bool>("start", f.Codec == VideoCodecId.Hevc, (int)f.Width, (int)f.Height)) {
                            Failed = true; Error = "Hardware decoder unavailable: " + java.Call<string>("error"); return false;
                        }
                        started = true;
                    }
                    int r = java.Call<int>("queue", f.Bitstream, f.Bitstream.Length, (long)f.PtsMicros, f.IsKeyframe);
                    if (r == -1) {                      // no free input buffer yet (just started, or briefly saturated): try again next frame
                        if (++retryPumps > 6) { retryPumps = 0; FramesDropped++; java.Call("flush"); LostSync(); }   // ~100 ms stuck: resync
                        else retry = f;
                        break;
                    }
                    retryPumps = 0;
                    if (r < 0) { FramesDropped++; java.Call("flush"); LostSync(); break; }   // rejected (too large / codec error)
                }
                if (!started || !java.Call<bool>("poll")) return false;
                bool ok = Upload();
                java.Call("releaseFrame");
                if (ok) FramesDecoded++;
                return ok;
            } catch (Exception e) { Failed = true; Error = e.Message; return false; }
        }
        bool Upload() {
            int w = java.Call<int>("width"), h = java.Call<int>("height");
            int planes = java.Call<int>("planeCount");
            if (w <= 0 || h <= 0 || planes < 3) return false;
            int yStride = java.Call<int>("rowStride", 0), cStride = java.Call<int>("rowStride", 1), cPixel = java.Call<int>("pixelStride", 1);
            bool planar = cPixel == 1;
            Ensure(w, h, yStride, cStride, planar);
            if (!Copy(java.Call<long>("planeAddress", 0), java.Call<long>("planeCapacity", 0), YTex)) return false;
            if (!Copy(java.Call<long>("planeAddress", 1), java.Call<long>("planeCapacity", 1), UVTex)) return false;
            if (planar && !Copy(java.Call<long>("planeAddress", 2), java.Call<long>("planeCapacity", 2), VTex)) return false;
            LastPtsMicros = (ulong)java.Call<long>("ptsUs");
            return true;
        }
        int texKey;
        void Ensure(int w, int h, int yStride, int cStride, bool planar) {
            int key = ((w * 31 + h) * 31 + yStride) * 31 + cStride + (planar ? 1 : 0);
            if (key == texKey && YTex != null) return;
            texKey = key; Width = w; Height = h; Planar = planar;
            Destroy(YTex); Destroy(UVTex); Destroy(VTex);
            YTex = Make(yStride, h, TextureFormat.R8);
            if (planar) { UVTex = Make(cStride, h / 2, TextureFormat.R8); VTex = Make(cStride, h / 2, TextureFormat.R8); }
            else { UVTex = Make(cStride / 2, h / 2, TextureFormat.RG16); VTex = UVTex; }
            float lx = w / (float)yStride;
            float cx = planar ? (w / 2f) / cStride : (w / 2f) / (cStride / 2f);
            Scale = new Vector4(lx, 1f, cx, 1f);
        }
        static Texture2D Make(int w, int h, TextureFormat f) {
            var t = new Texture2D(Mathf.Max(1, w), Mathf.Max(1, h), f, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            return t;
        }
        // Plane buffers are direct ByteBuffers owned by MediaCodec: copy native -> native with no managed allocation.
        // (Two memcpy through a reusable scratch array; a single copy needs `unsafe` code, which needs an asmdef.)
        bool Copy(long address, long capacity, Texture2D tex) {
            if (address == 0 || capacity <= 0) { Error = "Decoder returned no readable buffer."; return false; }
            NativeArray<byte> dst = tex.GetRawTextureData<byte>();
            int n = (int)Math.Min(capacity, dst.Length);
            if (scratch.Length < n) scratch = new byte[n];
            Marshal.Copy(new IntPtr(address), scratch, 0, n);
            NativeArray<byte>.Copy(scratch, dst, n);
            tex.Apply(false, false);
            return true;
        }
        static void Destroy(UnityEngine.Object o) { if (o != null) UnityEngine.Object.Destroy(o); }
        public void Dispose() {
            try { if (java != null) { java.Call("stop"); java.Dispose(); } } catch (Exception) { }
            java = null; started = false;
            Destroy(YTex); if (Planar) Destroy(VTex); Destroy(UVTex); YTex = UVTex = VTex = null;
        }
#else
        public VideoStreamDecoder(uint streamID, uint generation) {
            StreamID = streamID; Generation = generation; Failed = true;
            Error = "Hardware video decoding only runs in the Android (headset) build.";
        }
        public bool Pump() { while (pending.TryDequeue(out _)) { } return false; }
        public void Dispose() { }
#endif
    }
}
