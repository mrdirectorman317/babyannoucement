# Video pipeline: from 15 fps JPEG to hardware-encoded 60 fps

**Goal:** panels that feel like a real monitor in the room - smooth, sharp, immediate. **Status:** built end to end and verified piece by piece; **never run on a headset.** Every number below that isn't marked *measured* is a design estimate.

## Why this was the bottleneck

Two different frame rates exist, and only one was ever slow:

| Thing | Rate | Depends on |
|---|---|---|
| Your head/hands/room (panels staying put as you move) | Quest refresh (72/90/120 Hz) | Unity rendering locally. Never waits on the Mac |
| The **content inside** a panel | The stream: was **15 fps JPEG at 1280 px** | Capture, encode, network, decode |

So the real fix is to make the stream fast and keep the two decoupled: the newest decoded frame is shown, and a late frame never delays the scene.

## The new path

```
Mac                                                           Quest 3S
ScreenCaptureKit (NV12, up to 60 fps, no Mac cursor)          network thread: parse -> per-stream queue (max 10)
  -> VideoToolbox H.264/HEVC (hardware, low-latency,          main thread, once per frame:
     real-time, no B-frames, frame delay 1, rate-limited)       feed MediaCodec (low-latency, real-time priority)
  -> Annex-B access units, SPS/PPS on every IDR                 keep ONLY the newest decoded picture
  -> packet kind 4 on the video TLS lane                        copy planes -> 3 small textures
  -> per-stream queue: keyframe supersedes, overflow =          shader: YUV (BT.709) -> RGB, rounded corners, glass
     drop all + wait for IDR + lower bitrate                    local cursor drawn instantly under your ray
```

Key decisions and why:

- **Hardware both ends.** VideoToolbox on the Mac, MediaCodec on the headset. JPEG could never reach 60 fps at useful resolution.
- **Never a smeared picture.** H.264 P-frames depend on earlier frames, so we can't drop arbitrary ones. Policy (`SecurePeer.sendVideo`): a keyframe discards everything queued before it; if the queue backs up we drop *all* of it, ignore deltas until the next keyframe, force one, and lower the bitrate (`AdaptiveBitrate`: -30% fast, +12% slowly after 4 s calm). The viewer sees at worst a brief hold, never corruption. The headset does the same (`VideoStreamDecoder`: >10 queued -> flush -> request a keyframe).
- **Late joiners and recovery.** Every keyframe carries its own SPS/PPS. The headset asks for a keyframe the moment a stream starts (frames can arrive before the control-lane `subscribed` message is handled) and whenever it loses sync (`request_keyframe`).
- **Newest frame wins.** MediaCodec output older than the newest is released unseen (`framesReplaced`).
- **Local cursor.** For video streams the Mac cursor is hidden and the headset draws its own right under your ray, so pointing feels instant even though the pixels behind it are tens of ms old. (JPEG streams keep the Mac cursor.)
- **Hand jitter removed, not lag added.** Aiming uses a One Euro filter on the ray (still when you hold still, responsive when you move). Grabbing/resizing stays 1:1 with your hand.
- **Haptics and sound** on hover/press so interaction feels solid even before the video catches up.
- **Hard fallback.** If the headset's decoder or native helper fails, the session says so and drops to the JPEG path rather than showing nothing.

## H.264 vs H.265 (HEVC)

Both are supported end to end; the app picks the best one it can and steps down automatically:

```
HEVC (only if the headset reports a HARDWARE HEVC decoder)  ->  H.264  ->  JPEG
```
The headset asks `MediaCodecList` for a hardware HEVC decoder at startup; the Mac host falls back HEVC -> H.264 -> JPEG if it can't create an encoder; and if a decoder fails at runtime the headset marks that codec broken for the session and re-subscribes one step down. **Home > Video** cycles Auto / H.264 / JPEG so you can A/B them against `Stats`.

| | H.264 (High) | HEVC (Main) |
|---|---|---|
| Starting bitrate at 1080p60 | ~12.4 Mbps | ~8.1 Mbps (about 35% less for similar quality - the main win: crisper text on the same link) |
| Clean fixture test: bytes per delta frame (same pattern) | ~8.3 KB | ~5.8 KB |
| Encode/decode latency | lowest | usually slightly higher; measure with Stats |
| Compatibility | everywhere | needs a hardware decoder (detected) |
| Chroma | 4:2:0 8-bit | 4:2:0 8-bit (Main) |

Why HEVC may or may not win on your setup: if the link is USB or strong Wi-Fi, bandwidth isn't the bottleneck and H.264's slightly lower latency may feel better; on a weaker link HEVC's smaller frames avoid the drop-and-resync hitches. That is a measurement for `Stats`, not a guess to trust.

Both codecs share the same safeguards: closed GOP (every keyframe decodes on its own), parameter sets in-band on each keyframe, and on the headset the parameter sets are also handed to MediaCodec as a separate codec-config buffer before the first picture (the approach Moonlight's Android client uses) because some decoders are picky about in-band-only config.

## Latency budget (design estimate - measure it)

| Stage | Estimate |
|---|---|
| Capture (<= 1 frame @60) | ~8-16 ms |
| Encode (hardware, low-latency) | ~3-8 ms |
| Network (USB or good Wi-Fi 6) | ~2-8 ms |
| Decode (hardware, low-latency) | ~4-10 ms |
| Upload + present (next vsync @90 Hz) | ~6-11 ms |
| **Capture -> display** | **~25-55 ms** |

`Home > Stats` shows *measured* fps, bitrate, dropped frames and capture-to-display latency per panel (clock-synchronized via ping/pong `t0/t1`; frame timestamps use the same host clock). Read it before believing the table.

## What is verified vs not

| Piece | Evidence |
|---|---|
| Packet format, Annex-B conversion, keyframe/param-set detection (H.264 and HEVC), bitrate controller | Unit tests (Swift, Python golden bytes match) |
| Mock host streams **both** H.264 and HEVC real clips at 60 fps, honors the HEVC -> H.264 fallback; ffmpeg decodes each received stream | `tools/video_smoke.py` |
| Codec-config splitting (VPS/SPS/PPS before the first picture) against the real H.264 and HEVC fixtures | `scripts/check-android-plugin.sh` (`ParamSetCheck`) |
| Mock host streams a real 1280x720@60 H.264 clip; cadence, gapless sequence, keyframe-on-request, clock reply; **ffmpeg decodes the received bytes with no errors** | `tools/video_smoke.py` (CI) |
| VideoToolbox encoder compiles; encode -> Annex-B -> standard VideoToolbox decode round-trip; forced keyframe carries SPS/PPS | `SpatialMediaTests` on a macOS CI runner (see VALIDATION_REPORT for the actual result) |
| Android decoder compiles against the real Android API; JNI binding resolves and returns correct addresses; arm64 helper builds and exports the right symbols | `scripts/check-android-plugin.sh` (CI) |
| C# decoder wrapper, session wiring, stats, smoothing | Type-checks (incl. Android branch); filter has unit tests |
| YUV shader | HLSL syntax/type check only. Orientation (flip), range (limited), color space and the plane layout handling are **unverified on a device** |
| MediaCodec actually decoding on a Quest, texture upload speed, frame pacing, thermals, real latency, USB/Wi-Fi behavior | **Not tested** |

## Test it on a headset without a Mac

```bash
python3 tools/mock_host.py --directory /tmp/spatialmac-mock --create-config --h264 --h265
# put its client-config.json on the headset, `adb reverse tcp:9444 tcp:9444`, open the app, open the calibration source
```
You should see a moving test pattern at 60 fps and `Home > Stats` should read `HEVC 1280×720 · ~60 fps` (or `H.264` if your headset reports no hardware HEVC decoder - or press **Home > Video** to force a codec). Test each codec separately; if HEVC fails and H.264 works, you've isolated it. If it doesn't, that isolates the headset decoder from everything on the Mac.

## Known gaps (the honest list)

1. **One CPU copy per frame** (decoder buffer -> texture, via a small scratch array). Fine at 1080p60 on paper, unproven; the zero-copy path is a MediaCodec output `Surface` -> external texture, which needs a native render-thread plugin. That is the next performance step if Stats shows decode/upload time is the limit.
2. **4:2:0 chroma** softens colored text edges slightly; HEVC/H.264 4:4:4 isn't widely hardware-decodable on Quest.
3. **No "settle to sharp"** yet (re-encode the idle frame at high quality after ~250 ms) - planned; it is what makes text look crisp after motion stops.
4. **Resolution is fixed per subscription** (1920 px wide). Choosing it from the panel's on-screen pixel size (Quest 3S is ~20 pixels per degree) and re-tuning live isn't built.
5. **Wi-Fi**: use USB (`adb reverse`) or a 5/6 GHz link to the Mac's own network; the adaptive bitrate protects you from stalls but can't create bandwidth.
6. The committed `libspatialmacnative.so` is a 1.8 KB arm64 binary built from `quest/native/spatialmacnative.c` by `scripts/build-native.sh` (reproducible by anyone with clang). It does nothing but return a buffer's address.
7. Windows/popup capture, HDR, and audio are unchanged from the earlier limitations.
