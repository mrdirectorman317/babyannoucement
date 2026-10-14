# Known limitations — intentionally explicit

> Updated after the integration pass. What changed: the Mac host **now compiles with a real Apple SDK in CI** and the Unity runtime **type-checks** (it did not before - see `docs/ORIGINAL_HANDOFF_CORRECTIONS.md`). Everything below about hardware, Unity runtime behavior, and permissions still stands.

## Video pipeline (added later; all unverified on hardware)
See `docs/VIDEO_PIPELINE.md` "Known gaps". In short: one CPU copy per decoded frame, 4:2:0 chroma, fixed per-stream resolution, YUV shader orientation/range unverified on a device, no measured latency, JPEG is still the fallback and the Editor path. The 1.8 KB `libspatialmacnative.so` is a committed binary (reproducible from `quest/native/`).

## Added with the integrations / glass UI (all unverified on hardware)

- Glass/veil shaders, text sizing (`GlassUI.NewText` estimate), hit targets and layout have never run in Unity or on a Quest.
- The Messages watcher depends on the private `chat.db` schema (can change with macOS updates) and Full Disk Access. Group chats, SMS/RCS forwarding, edits/unsends and message threads are only lightly handled. Sending uses `chat id` in AppleScript, which needs verification on your macOS.
- Siri discovers intents only from the Xcode-built app; phrase recognition, localization and Shortcuts parameter UI are unchecked. Siri cannot run on the headset itself.
- The assistant uses the OpenAI Responses API shapes as I know them; model names and fields may have changed. There is no streaming, no Realtime voice, no interrupt/cancel of an in-flight request, and conversation state is server-side via `previous_response_id`.
- Spoken replies rely on Android TTS being present on the Quest; off by default.
- The dimmer is a black sphere with alpha. Whether it dims the Meta passthrough underlay as intended depends on your camera/layer setup - verify.
- Palm-up gesture thresholds are guesses.

## Not executed here

No Apple SDK/native host build, no actual ScreenCaptureKit capture, no CGEvent injection, no Unity compilation, no IL2CPP build, no Quest deployment, no camera passthrough, no hand tracking, no saved-anchor test, and no real speech transcription occurred in this environment. Syntax checks and portable unit tests are narrower evidence.

## Source/integration limitations

The Unity generator builds a preview scene, not a configured XR app. SDK-dependent adapters are compile-symbol gated. API references were checked, but current package compatibility must be established on the user's machine. No fabricated PackageManager lockfile is included.

Three JPEG streams at debug resolution are the current policy. Texture loading occurs on the Unity main thread. There is no hardware video decoder plugin or production-grade adaptive bitrate. The panel shader, stereo orientation, render pipeline, and Android TLS/certificate behavior remain integration risks.

The mock server only presents a synthetic image. Its successful transport test does not validate the Swift host or Unity client. It is loopback-only and deliberately refuses remote control. Its development certificates are not reusable production credentials.

## Application-window limitations

The host initially catalogs on-screen standard windows and displays; background/minimized/hidden windows and special surfaces are not promised. Normal macOS focus is global. The conservative focus/bounds checks may reject legitimate interactions rather than risk clicking another window. Popups, menus, dock interactions, permission prompts, IME composition, secure inputs, accessibility-denied apps, and complex creative software need separate validation.

There is no virtual monitor driver, independent app focus session, headless Mac display solution, or automatic macOS window-layout manager. Isolated-window capture is not equivalent to an application remoting framework. Use a full desktop panel as fallback.

Saved sources use conservative app/title matching. Titles can change, collide, or be misleading. Add durable rebind UI and stronger identity information. Generic errors lack full request correlation. Rapid refresh/close/reconnect and simultaneous capture-stop callbacks need stress tests.

## Interaction limitations

Controller input uses a generic tracked device pose; aim calibration may be needed. Hand rays are an initial shoulder-to-pinch approximation. Pinch-to-grab and wrist rotation math is included, but there is no production hover/press feedback, near-touch toolkit, gesture tutorial, accessibility calibration, or hand-only menu summon affordance.

A browser pinch-to-zoom gesture inside content is not the same as resizing the panel; only panel resizing is implemented. Two-finger scrolling, text selection ergonomics, a virtual keyboard, keyboard composition, and high-precision editing are not complete. Use a physical keyboard/trackpad for initial work.

No joystick teleport/snap-turn system is installed. Controller sticks currently scroll content. A future navigation mode must explicitly separate these actions.

## Anchors and persistence

Real anchor save/load/localize adapter code is present but not device-tested. The loader skips session-only world transforms after a new session. Room naming, room detection, anchor recovery UI, map reset handling, explicit forget/delete UI, bulk cleanup, anchor cancellation and quotas require implementation/testing.

Closing a panel does not complete saved-anchor deletion. Repeated create/close experiments can leave saved anchors. Follow placements restore near the current head rather than pretending their old global coordinates are valid. Layout writing uses temporary-file replacement but needs a stronger atomic replace/backup strategy for crash safety.

## Voice / assistant limitations

A local whisper.cpp binary and model must be installed/configured separately. No model weights are bundled. Microphone permission, sample-rate conversion quality, latency, non-English command intent handling, cancellation, and file cleanup need testing. The deterministic command parser currently expects a small set of English phrases. No wake-word engine, always-listening assistant, arbitrary model tools, or background Mac agent exists.

## OS/product limitations

This app does not replace Quest home, auto-launch into the user's Mac on every headset boot, install macOS on Quest, enable Mac SteamVR support, or make a Quest 3S display sharper than its physical lenses/panels permit. Mac desktops can already be streamed by existing tools; this project is about a more customizable interface, not claiming an entirely unprecedented capability.
