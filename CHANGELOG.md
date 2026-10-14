# Changelog

## 0.3.1 - HEVC first-class (2026-10-14)
- HEVC (H.265) end to end: hardware-decoder detection on the headset, HEVC->H.264->JPEG fallback on both sides, lower starting bitrate, closed GOP, `Home > Video` codec switch, codec-config handoff to MediaCodec, HEVC fixture + mock + tests (incl. a real HEVC encode->decode test on macOS CI).

## 0.3.0 - video pipeline (2026-10-14)
- Hardware video: VideoToolbox H.264/HEVC encoder (`SpatialMedia`), packet kind 4, safe per-stream queue with keyframe recovery, adaptive bitrate, keyframe-on-request, clock-synced latency readout.
- Headset: MediaCodec decoder plugin (Java) + 1.8 KB native helper, YUV->RGB shader, JPEG fallback, per-panel Stats.
- Feel: One Euro ray smoothing (unit-tested), instant local cursor for video streams, haptics.
- Tests/tools: mock host streams real H.264, `video_smoke.py`, Android plugin verification script, encode->decode test on macOS CI.

## 0.2.0 - integration pass (2026-10-14)
- Repo repurposed for SpatialMac; baby announcement site archived to `archive/baby-announcement/`.
- Fixed compile errors in the imported starter (Unity C# x2, Swift host x2+).
- Mac host: iMessage -> headset notifications, confirmed message replies, ChatGPT assistant with tiered tools and human confirmation, Siri/Shortcuts App Intents, optional OpenAI speech-to-text, Keychain-stored API key, Integrations UI, XcodeGen/Xcode app build.
- Quest client: Vision-Pro-style glass windows, toasts, assistant card, home window, dimmer, recenter crown, palm-up menu, spatial UI sounds, optional TTS, click-blocking glass surfaces.
- Protocol: additive notification/assistant/confirm/reply/ui_command messages (framing unchanged).
- Tooling: GitHub Actions CI (Linux + macOS), C# type-check without Unity, demo mock host + smoke test, calibration generator, speech setup script.

## 0.1.0 - original ChatGPT-authored starter (2026-10-14)
See `START_HERE_FOR_NEXT_CHAT.md` and `docs/ORIGINAL_HANDOFF_CORRECTIONS.md`.
