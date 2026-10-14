# Validation report (2026-10-14, after the integration + video passes)

> **Correction to the earlier version of this report:** it said the Xcode app build "passed". That CI step's exit status was being masked by a shell pipe, and the app bundle was in fact not being checked. A later CI run exposed that the Xcode target's scheme collided with the Swift package's own `SpatialMacHost` scheme; the target is now `SpatialMacApp`, and CI **fails** unless the built app contains `Metadata.appintents` (what Siri/Shortcuts read). That assertion now passes. `swift test` output is likewise no longer masked.

## Current totals
Python 20 tests · Swift 39 portable + 4 macOS-only encoder tests (43 on the Mac runner, 0 failures; the H.264, HEVC, forced-keyframe and settle-to-sharp tests each reported `passed`, none `skipped`) · C# math 6 tests · 6 shader stage checks · 4 end-to-end smoke scripts · Android plugin check · C# type-check x2.

Evidence only; each row says what it does **not** prove. Reproduce with the commands shown.

## Executed and observed

| Check | Where | Result | Does NOT prove |
|---|---|---|---|
| `python3 -m unittest discover -s tests -v` | Linux + CI | 17 passed | Anything about Swift/Unity |
| `python3 tools/integration_smoke.py` | Linux + CI | Passed: pinned TLS control+video, catalog, subscribe, exact JPEG, wrong pin rejected | Native implementations |
| `python3 tools/demo_smoke.py` | Linux + CI | Passed: notification, assistant ask/confirm/reply, message reply, ui_command (mock) | That the Swift host/Unity client implement these identically |
| `swift test` (macOS/ `cd macOS`) | Linux (Swift 6.0.3) + CI macOS-15 | 26 passed (9 original + 17 new: sanitizer, attributedBody, tool policy, confirmations, OpenAI shapes, validation) | Apple-framework behavior |
| `swift build -c release --product SpatialMacHost` | CI macOS-15 (Xcode) | Compiles (ScreenCaptureKit, Network, SQLite3, Contacts, AppIntents, ...) | It runs, has the right permissions, or behaves correctly |
| `xcodegen` + `xcodebuild` full app | CI macOS-15 | Builds `SpatialMacHost.app` | Siri discovers/recognizes the intents; app launches |
| `dotnet build quest/typecheck` (+ `-p:Android=true`) | Linux + CI | 0 errors against Unity 2021.3 reference assemblies | Runs in Unity; shaders compile; Unity-6-only API; Meta/XR-Hands code (symbol-gated, not compiled) |
| `swift test` on `macos-15` incl. `SpatialMediaTests` | CI | **Encode -> wire packet -> standard VideoToolbox decode round-trip passes** (valid H.264, SPS/PPS on keyframe, forced keyframe works, settle-to-sharp emits a keyframe). The runner reported `ENCODER hardware=false`, i.e. Apple's *software* encoder: the **hardware** path on a real Mac is untested | Hardware-encoder behavior, latency, or anything on a headset |
| `python3 tools/video_smoke.py` | Linux + CI | Mock host streams a real 1280x720@60 H.264 clip: negotiation, ~60 fps cadence, gapless sequence, keyframe on request, clock-sync reply; **ffmpeg decodes the received bytes without errors** | The Swift host or Quest decoder |
| `bash scripts/check-android-plugin.sh` | Linux + CI | Java decoder compiles against Android 15's real API; the real class loads the helper and its JNI functions return correct addresses (direct buffer ok, heap/null -> 0); arm64 helper builds reproducibly and exports the symbols | MediaCodec decoding on a Quest, or the arm64 library loading on-device |
| `dotnet test quest/mathtests` | Linux + CI | One Euro filter: constant input, tremor reduced >4x, lower lag when fast, reset, zero-dt safe | Feel on real hands |
| Unity-envelope decoding tests (Swift) | Linux + CI | Unity's all-defaults JSON (`kbps:0`, `codec:""`) decodes as "no preference" (this caught a real 1 Mbps-clamp bug) | - |
| `python3 tools/check_shaders.py` | Linux + CI | The 3 shaders' HLSL blocks compile (vert+frag) with glslang, Unity macros stubbed | Unity accepts the ShaderLab wrapper; works in your render pipeline; looks right |
| `bash -n scripts/*.sh` | CI | Syntax OK | Scripts work on a Mac |

CI history on the branch shows the failures that were found and fixed (compile errors listed in `ORIGINAL_HANDOFF_CORRECTIONS.md`).

## Not executed (honest list)
Unity editor import; real shader compilation in Unity (YUV orientation/range/plane layout unverified on a device); MediaCodec decode, texture upload speed, frame pacing, thermals and **any latency number** on a Quest; the hardware VideoToolbox encoder; Quest APK; real display capture; CGEvent/Accessibility; chat.db reading on a real Messages database; AppleScript sending; Siri; real OpenAI requests; microphone/whisper; hand tracking; passthrough; anchors; any performance measurement; any security review.

## Reproduce
```bash
python3 -m unittest discover -s tests -v
python3 tools/integration_smoke.py && python3 tools/demo_smoke.py
(cd macOS && swift test)          # SpatialCore only on Linux; full host on macOS
dotnet build quest/typecheck && dotnet build quest/typecheck -p:Android=true
```

## Review pass (after the video work): defects found by reading the code, then fixed
These were not caught by any test; they came from adversarially reviewing code that has never run on a device.

| Defect | Effect if left | Fix |
|---|---|---|
| `request_keyframe` on a **static** screen produced nothing (ScreenCaptureKit sends no frames when nothing changes) | Headset joins or recovers and shows no picture until something moves | Host re-encodes the last captured frame as a keyframe on request |
| Decoder startup: `MediaCodec` has no free input buffer for a few ms after `start()`; code treated that as overload and flushed | Could loop forever without showing a first frame | Wait briefly for keyframes; retry a frame next pump; only resync after ~100 ms stuck |
| Bitrate controller kept probing up to 40 Mbps | A freeze + keyframe every ~20 s on a Wi-Fi link | Recover after 10 s calm, +8% steps, capped at 1.5x the starting rate |
| Retained frame vs a 3-deep capture pool | Possible dropped captures | Pool depth 5 for video streams |
| Xcode scheme collided with the package's `SpatialMacHost` scheme; the step's failure was masked | Siri metadata silently missing | Target renamed `SpatialMacApp`; CI asserts `Metadata.appintents` |

## HEVC (H.265) pass
| Check | Result |
|---|---|
| Real HEVC encode -> VideoToolbox HEVC decode round trip, VPS/SPS/PPS on keyframe, delta second frame | **Passed on the macOS runner** (software encoder; hardware path untested) |
| Mock host serves real HEVC and H.264 clips at ~60 fps; HEVC request falls back to H.264 when only H.264 is offered; ffmpeg decodes every received stream | Passed (`tools/video_smoke.py`, CI) |
| Codec-config (VPS/SPS/PPS) splitting on the real H.264 and HEVC fixtures; degenerate inputs | Passed (`ParamSetCheck`, CI) |
| Headset detects a hardware HEVC decoder via `MediaCodecList`; picks HEVC only if present | Compiles; **not run on a Quest** - whether the Quest 3S reports one is exactly what the first run will show |
| HEVC decoding on a Quest, HEVC vs H.264 latency, real bandwidth savings | **Not tested** |
