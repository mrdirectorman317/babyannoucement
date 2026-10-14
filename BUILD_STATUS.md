# Build status — October 14, 2026 (integration pass)

## Changed in this pass (see git history for exact files)
- Imported the ChatGPT-authored starter into this repo; archived the old baby-announcement site to `archive/baby-announcement/`.
- Fixed real compile errors (2 in Unity C#, 2+ in the Swift host) - `docs/ORIGINAL_HANDOFF_CORRECTIONS.md`.
- Added: assistant tool policy + confirmations, OpenAI Responses wire layer, text sanitizer, iMessage decoder (SpatialCore, 17 new tests).
- Added to the Mac host: Messages watcher/sender, ChatGPT assistant, optional OpenAI speech-to-text, Keychain store, Siri/Shortcuts App Intents, Integrations UI, XcodeGen project + script.
- Added to the Quest client: glass shaders and UI toolkit, rebuilt `SpatialPanel`, notification toasts, assistant card, home window, dimmer, recenter crown, palm-up menu, spatial sounds, TTS, UI-command router.
- Added tooling: `quest/typecheck` (C# compile without Unity), `tools/mock_host.py --demo`, `tools/demo_smoke.py`, `tools/make_calibration.py`, `scripts/make-xcodeproj.sh`, `scripts/setup-speech.sh`, GitHub Actions CI.

## Video pass (latest)
Hardware H.264/HEVC pipeline built end to end (VideoToolbox -> kind-4 packets -> MediaCodec -> YUV shader), plus adaptive bitrate, keyframe recovery, settle-to-sharp, local cursor, ray smoothing, haptics, Stats readout, JPEG fallback. See `docs/VIDEO_PIPELINE.md`. **Not yet run on a headset: the first thing to do is `docs/WHEN_YOU_GET_BACK.md` step 0b (mock H.264 on the Quest), then read `Home > Stats`.**

## Executed and observed
See `docs/VALIDATION_REPORT.md` (commands, results, and what they do not prove).

## Not executed
Unity import/compile in a real editor, any shader compile, Quest build/run, real ScreenCaptureKit capture, Accessibility input, chat.db reading, AppleScript send, Siri recognition, real OpenAI calls, microphone, hand tracking, passthrough, anchors, performance.

## Dependency versions actually used
Swift 6.0.3 (Linux, local) and the Xcode/Swift on GitHub `macos-15` runners (see CI logs); .NET SDK 8.0 with `UnityEngine.Modules` 2021.3.33 reference assemblies for the C# type-check; Python 3.13. Unity/Meta/XR Hands versions: **not pinned yet** (`docs/VERSIONS_TO_PIN.md`).

## Next smallest hardware-verifiable task
`docs/WHEN_YOU_GET_BACK.md` step 0 (demo preview in the Unity Editor), then step 2 (one real Mac display on the Quest, view-only, no input). Do not enable remote control, replies or the assistant's consequential tools until that works.
