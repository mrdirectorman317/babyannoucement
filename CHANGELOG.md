# Changelog

## 0.2.0 - integration pass (2026-10-14)
- Repo repurposed for SpatialMac; baby announcement site archived to `archive/baby-announcement/`.
- Fixed compile errors in the imported starter (Unity C# x2, Swift host x2+).
- Mac host: iMessage -> headset notifications, confirmed message replies, ChatGPT assistant with tiered tools and human confirmation, Siri/Shortcuts App Intents, optional OpenAI speech-to-text, Keychain-stored API key, Integrations UI, XcodeGen/Xcode app build.
- Quest client: Vision-Pro-style glass windows, toasts, assistant card, home window, dimmer, recenter crown, palm-up menu, spatial UI sounds, optional TTS, click-blocking glass surfaces.
- Protocol: additive notification/assistant/confirm/reply/ui_command messages (framing unchanged).
- Tooling: GitHub Actions CI (Linux + macOS), C# type-check without Unity, demo mock host + smoke test, calibration generator, speech setup script.

## 0.1.0 - original ChatGPT-authored starter (2026-10-14)
See `START_HERE_FOR_NEXT_CHAT.md` and `docs/ORIGINAL_HANDOFF_CORRECTIONS.md`.
