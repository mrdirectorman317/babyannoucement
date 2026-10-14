# Validation report (integration pass, 2026-10-14)

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
| `python3 tools/check_shaders.py` | Linux + CI | The 3 shaders' HLSL blocks compile (vert+frag) with glslang, Unity macros stubbed | Unity accepts the ShaderLab wrapper; works in your render pipeline; looks right |
| `bash -n scripts/*.sh` | CI | Syntax OK | Scripts work on a Mac |

CI history on the branch shows the failures that were found and fixed (compile errors listed in `ORIGINAL_HANDOFF_CORRECTIONS.md`).

## Not executed (honest list)
Unity editor import; shader compilation; Quest APK; real display capture; CGEvent/Accessibility; chat.db reading on a real Messages database; AppleScript sending; Siri; real OpenAI requests; microphone/whisper; hand tracking; passthrough; anchors; any performance measurement; any security review.

## Reproduce
```bash
python3 -m unittest discover -s tests -v
python3 tools/integration_smoke.py && python3 tools/demo_smoke.py
(cd macOS && swift test)          # SpatialCore only on Linux; full host on macOS
dotnet build quest/typecheck && dotnet build quest/typecheck -p:Android=true
```
