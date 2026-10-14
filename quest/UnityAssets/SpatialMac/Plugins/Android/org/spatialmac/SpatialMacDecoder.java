package org.spatialmac;

import android.graphics.Rect;
import android.media.Image;
import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.os.Build;
import java.nio.ByteBuffer;

/**
 * Hardware H.264/HEVC decoder for SpatialMac video streams, driven from Unity's main thread:
 * feed access units with {@link #queue}, then {@link #poll} once per rendered frame. Latency policy:
 * only the NEWEST decoded frame is ever shown (older ones are released unseen), input never blocks, and if the
 * decoder falls behind the caller flushes and waits for the next keyframe.
 *
 * Output is read as a flexible YUV420 {@link Image} (the shader converts to RGB), so the decoded picture crosses
 * the CPU once per frame. A zero-copy GPU path (surface output) is future work.
 */
public final class SpatialMacDecoder {
    private static boolean nativeOk;
    private static String nativeError = "";
    static {
        try { System.loadLibrary("spatialmacnative"); nativeOk = true; }
        catch (Throwable t) { nativeError = "libspatialmacnative.so failed to load: " + t.getMessage(); }
    }
    // Implemented in quest/native/spatialmacnative.c (returns the address of a direct ByteBuffer's memory).
    private static native long nativeAddress(ByteBuffer buffer);
    private static native long nativeCapacity(ByteBuffer buffer);

    private MediaCodec codec;
    private final MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
    private Image held;
    private int heldIndex = -1;
    private long heldPtsUs;
    private int width, height;
    private volatile String lastError = "";
    private long framesOut, framesReplaced;
    private boolean hevcStream;
    private boolean needConfig = true;   // hand the parameter sets over as a CODEC_CONFIG buffer before the first picture

    /**
     * True if this device has a HARDWARE HEVC decoder. Software HEVC decoding cannot keep up at 60 fps, so the app
     * only asks the Mac for HEVC when this is true (otherwise it asks for H.264).
     */
    public static boolean supportsHevc() {
        try {
            android.media.MediaCodecList list = new android.media.MediaCodecList(android.media.MediaCodecList.REGULAR_CODECS);
            for (android.media.MediaCodecInfo info : list.getCodecInfos()) {
                if (info.isEncoder()) continue;
                for (String type : info.getSupportedTypes()) {
                    if (!type.equalsIgnoreCase(MediaFormat.MIMETYPE_VIDEO_HEVC)) continue;
                    boolean hardware = Build.VERSION.SDK_INT >= 29
                            ? info.isHardwareAccelerated() && !info.isSoftwareOnly()
                            : !info.getName().startsWith("OMX.google.") && !info.getName().startsWith("c2.android.");
                    if (hardware) return true;
                }
            }
        } catch (Throwable ignored) { }
        return false;
    }

    /** Creates and starts the decoder. Returns false (see {@link #error()}) if the device cannot decode this stream. */
    public boolean start(boolean hevc, int initialWidth, int initialHeight) {
        stop();
        if (!nativeOk) { lastError = nativeError; return false; }
        try {
            String mime = hevc ? MediaFormat.MIMETYPE_VIDEO_HEVC : MediaFormat.MIMETYPE_VIDEO_AVC;
            MediaFormat format = MediaFormat.createVideoFormat(mime, initialWidth, initialHeight);
            format.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatYUV420Flexible);
            format.setInteger(MediaFormat.KEY_MAX_WIDTH, 3840);     // allow in-stream resolution changes
            format.setInteger(MediaFormat.KEY_MAX_HEIGHT, 2160);
            if (Build.VERSION.SDK_INT >= 30) format.setInteger(MediaFormat.KEY_LOW_LATENCY, 1);
            if (Build.VERSION.SDK_INT >= 23) {
                format.setInteger(MediaFormat.KEY_PRIORITY, 0);                       // real-time
                format.setInteger(MediaFormat.KEY_OPERATING_RATE, Short.MAX_VALUE);   // "as fast as possible"
            }
            format.setInteger("vendor.qti-ext-dec-low-latency.enable", 1);            // Qualcomm (Quest): ignored elsewhere
            codec = MediaCodec.createDecoderByType(mime);
            codec.configure(format, null, null, 0);
            codec.start();
            width = initialWidth;
            height = initialHeight;
            hevcStream = hevc;
            needConfig = true;
            lastError = "";
            return true;
        } catch (Exception e) {
            lastError = e.getClass().getSimpleName() + ": " + e.getMessage();
            releaseQuietly();
            return false;
        }
    }

    /**
     * Length of the leading parameter-set NALs (VPS/SPS/PPS, plus any SEI before the first picture) of an Annex-B access
     * unit, i.e. the offset of the first picture NAL's start code. 0 if there are none or no picture NAL is found.
     * Package-private for tests.
     */
    static int parameterSetPrefixLength(byte[] d, int length, boolean hevc) {
        int i = 0;
        while (i + 3 < length) {
            int start = -1, header = -1;
            if (d[i] == 0 && d[i + 1] == 0 && d[i + 2] == 1) { start = i; header = i + 3; }
            else if (i + 4 < length && d[i] == 0 && d[i + 1] == 0 && d[i + 2] == 0 && d[i + 3] == 1) { start = i; header = i + 4; }
            if (start < 0) { i++; continue; }
            int b = d[header] & 0xFF;
            boolean picture = hevc ? ((b >> 1) & 0x3F) < 32 : ((b & 0x1F) == 1 || (b & 0x1F) == 5);
            if (picture) return start;
            i = header + 1;
        }
        return 0;
    }

    private int submit(byte[] data, int length, long ptsUs, int flags, long timeoutUs) throws Exception {
        int index = codec.dequeueInputBuffer(timeoutUs);
        if (index < 0) return -1;
        ByteBuffer buffer = codec.getInputBuffer(index);
        if (buffer == null || length > buffer.capacity() || length > data.length) {
            codec.queueInputBuffer(index, 0, 0, ptsUs, 0);
            return -2;
        }
        buffer.clear();
        buffer.put(data, 0, length);
        codec.queueInputBuffer(index, 0, length, ptsUs, flags);
        return 0;
    }

    /** Queues one Annex-B access unit. Returns 0 on success, -1 if the decoder has no free input buffer, -2 if it is too large or the codec failed. */
    public int queue(byte[] data, int length, long ptsUs, boolean keyFrame) {
        if (codec == null) return -2;
        try {
            // Right after start() the codec has no free input buffer for a few ms. Wait a little for keyframes (the frame
            // we cannot afford to lose); never block for delta frames, so a saturated decoder cannot stall rendering.
            long wait = keyFrame ? 8000 : 0;
            if (keyFrame && needConfig) {
                int prefix = parameterSetPrefixLength(data, length, hevcStream);
                if (prefix > 0) {
                    int r = submit(data, prefix, ptsUs, MediaCodec.BUFFER_FLAG_CODEC_CONFIG, wait);
                    if (r != 0) return r;
                }
                needConfig = false;      // (no prefix: rely on in-band parameter sets)
            }
            return submit(data, length, ptsUs, keyFrame ? MediaCodec.BUFFER_FLAG_KEY_FRAME : 0, wait);
        } catch (Exception e) {
            lastError = e.getClass().getSimpleName() + ": " + e.getMessage();
            return -2;
        }
    }

    /** Pulls decoded output. Returns true if a NEW frame is now held (read it, then call {@link #releaseFrame}). */
    public boolean poll() {
        if (codec == null) return false;
        boolean got = false;
        try {
            for (int i = 0; i < 8; i++) {
                int index = codec.dequeueOutputBuffer(info, 0);
                if (index >= 0) {
                    Image image = Build.VERSION.SDK_INT >= 21 ? codec.getOutputImage(index) : null;
                    if (image == null) { codec.releaseOutputBuffer(index, false); continue; }
                    if (heldIndex >= 0) { releaseHeld(); framesReplaced++; }     // a newer frame arrived: the older one is never shown
                    held = image;
                    heldIndex = index;
                    heldPtsUs = info.presentationTimeUs;
                    Rect crop = image.getCropRect();
                    width = crop.width() > 0 ? crop.width() : image.getWidth();
                    height = crop.height() > 0 ? crop.height() : image.getHeight();
                    framesOut++;
                    got = true;
                } else if (index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                    continue;
                } else {
                    break;      // INFO_TRY_AGAIN_LATER
                }
            }
        } catch (Exception e) {
            lastError = e.getClass().getSimpleName() + ": " + e.getMessage();
            return false;
        }
        return got;
    }

    public ByteBuffer planeBuffer(int plane) { return held == null ? null : held.getPlanes()[plane].getBuffer(); }
    /** Native address/size of a plane's memory (direct buffer), for a zero-garbage copy into a Unity texture. 0 if unavailable. */
    public long planeAddress(int plane) { ByteBuffer b = planeBuffer(plane); return (b == null || !nativeOk) ? 0 : nativeAddress(b); }
    public long planeCapacity(int plane) { ByteBuffer b = planeBuffer(plane); return (b == null || !nativeOk) ? 0 : nativeCapacity(b); }
    public int rowStride(int plane) { return held == null ? 0 : held.getPlanes()[plane].getRowStride(); }
    public int pixelStride(int plane) { return held == null ? 0 : held.getPlanes()[plane].getPixelStride(); }
    public int planeCount() { return held == null ? 0 : held.getPlanes().length; }
    public int width() { return width; }
    public int height() { return height; }
    public long ptsUs() { return heldPtsUs; }
    public long framesOut() { return framesOut; }
    public long framesReplaced() { return framesReplaced; }
    public String error() { return lastError; }

    /** Call after the held frame's planes have been copied. */
    public void releaseFrame() { releaseHeld(); }

    /** Drops everything queued inside the codec (use before resyncing on a keyframe). */
    public void flush() {
        releaseHeld();
        needConfig = true;
        try { if (codec != null) codec.flush(); } catch (Exception e) { lastError = e.getMessage(); }
    }

    public void stop() { releaseHeld(); releaseQuietly(); }

    private void releaseHeld() {
        try { if (held != null) held.close(); } catch (Exception ignored) { }
        try { if (codec != null && heldIndex >= 0) codec.releaseOutputBuffer(heldIndex, false); } catch (Exception ignored) { }
        held = null;
        heldIndex = -1;
    }

    private void releaseQuietly() {
        try { if (codec != null) { codec.stop(); } } catch (Exception ignored) { }
        try { if (codec != null) { codec.release(); } } catch (Exception ignored) { }
        codec = null;
    }
}
