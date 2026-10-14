# Build-agent handoff — read before modifying anything

> **Update (integration pass):** this repository is no longer a pristine ChatGPT handoff. Read `BUILD_STATUS.md`, `docs/VALIDATION_REPORT.md` and `docs/ORIGINAL_HANDOFF_CORRECTIONS.md` first. The Mac host now compiles in CI on real macOS/Xcode and the Unity scripts type-check (`dotnet build quest/typecheck`). The "first concrete acceptance gate" below is **still unmet** - nothing has run on a headset. New features (glass UI, Messages, ChatGPT, Siri) are documented in `docs/INTEGRATIONS.md` and `docs/EXPERIENCE.md`. PippinVR was evaluated: `docs/FOUNDATION_DECISION.md`.

## The user's actual goal

Build a customizable Quest 3S interface to a real Mac: macOS content all around the user; fully virtual custom worlds **and** real-room passthrough; panels that can be grabbed at their edges, resized at their corners, freely tilted/rotated, placed low/high/around a room, locked in place, or set to follow. Later, add dependable room persistence, hand-only interaction, voice dictation/commands, and a Jarvis-like assistant. Preserve the user's ability to build their own Unity worlds. Do not pivot this into a list of paid Quest games.

“360 degrees” means a spatial workspace with content placed around the user, not a mandatory single giant equirectangular video. A curved desktop canvas may be added, but independent panels are the primary architecture. “Full macOS” means interacting with applications running on the Mac—not a macOS simulator running on Android.

## Truth boundary

The previous chat created a substantial source starter, **not a tested native app**. Portable Swift and Python tests were run; a Python TLS simulator transferred a synthetic JPEG. Native ScreenCaptureKit, CGEvent/Accessibility, Unity compilation, Android IL2CPP TLS, Meta hand tracking, real passthrough, and persistent anchors were NOT tested. Do not present them as already proven.

No user's existing Unity game/repository or computer was inspected for this build. Ask the user to choose an actual destination project before modifying a game. Start in a separate project/branch. Do not overwrite “Just Another Sword Game” or assume whether its assets are sprites or 3D meshes.

## Required initial actions

Read README, BUILD_SPEC, KNOWN_LIMITATIONS, PROTOCOL, SECURITY, VALIDATION_REPORT, and quest/README. Inspect the code instead of relying on this summary. Inventory the actual Mac, Xcode/SDK, installed Unity versions, Meta SDK packages, Quest firmware, and Android toolchain. Do not fabricate package-version compatibility. Run the provided tests locally and record exact commands and failures.

Evaluate **PippinVR** in `docs/RESEARCH.md` before committing to the transport rewrite. It may be the fastest path to a usable baseline. Compare source architecture, real Quest 3S behavior, input, licensing, private virtual-display API use, latency, stability, and maintainability. Inspect any external code/build scripts before running them; do not automatically install unsigned release files. Do not copy GPL source into this MIT directory and claim it became MIT.

If extending this original starter, first compile the Mac package. Fix Apple SDK errors with actual current documentation. Then compile Unity scripts with no optional symbols; verify mock source selection and image orientation. Configure ONE real XR rig. Only enable SPATIALMAC_META and SPATIALMAC_XR_HANDS after their dependencies work in a minimal sample. The bootstrap creates an Editor preview rig only.

## First concrete acceptance gate

1. Mac host remains off until started; sharing is off until locally enabled; no Accessibility permission is needed just to view a display.
2. One authorized USB-connected Quest reaches a loopback-bound TLS host using the explicit certificate pin and token.
3. One selected physical Mac display appears in the Quest with the calibration corners correctly oriented, readable content, and tracked panel pose.
4. Unplugging USB or revoking sharing clears the pixels and disarms control; reconnect does not silently re-arm it.
5. Save the exact dependency versions and collect logs plus a headset recording. Do not call this gate complete from an Editor-only screenshot.

## Then implement in this order

Controllers before hands. Physical desktop before isolated app-window focus. Local grab/resize before streaming-quality work. Reliable disconnect before AI commands. Session placement before persistent room anchors. Deterministic reviewed voice commands before an LLM tool layer. One quiet environment before heavy custom-world graphics.

Keep the prototype small: three active sources maximum, one global Mac pointer owner, JPEG debug path for initial integration. Do not add a generic “run shell” RPC. Keyboard shortcuts and app launching are explicit allowlists. Captured text is untrusted data, not assistant instructions.

When window capture becomes reliable, implement a **source identity and geometry transaction** rather than trusting raw window IDs after restart. Add a rebind picker and menu/popup fallback. Preserve the stale-generation guard. Use a physically attached keyboard/trackpad for serious initial productivity; a polished spatial keyboard is another feature, not already supplied.

## Priority technical review items

- ScreenCaptureKit stream resizing/cropping/Retina behavior and content bounds: verify before any remote click.
- Static scenes may produce no new image frames; do not equate “no fresh pixels” with a disconnected stream. Use transport liveness and explicit stream status.
- Capture-stop callbacks, reconnect epochs, permission revocation, closing a source during subscription, and async save/restore cancellation need native stress tests.
- Unity per-eye shader orientation, ray transform origins, controller aim versus grip pose, Android texture upload cost, and IL2CPP SslStream/certificate handling need headset tests.
- Add per-panel close/forget distinction, persistent-anchor cleanup/rebind UI, and robust atomic layout writes before distributing.
- The client’s pairing file is development storage. Replace it with appropriate platform credential storage and revocation UI for production.
- Make connection/capture status explicit; never show stale content as a live workstation after failure.

## What not to claim

Do not claim 120-fps desktop video, unlimited windows, eye-tracked selection on Quest 3S, permanent whole-house positions from JSON, seamless commercial-grade Mac app focus, a replacement Quest home shell, or a Mac PCVR runtime. None follows from this starter.

Do not promise full environmental recognition, automatic collision avoidance, safe use while cooking, or arbitrary agents that can “do anything.” Separate proposals from measured results.

## Deliverables for every iteration

Write a short BUILD_STATUS.md containing the exact changed files, dependency versions, commands executed, observed results, blockers, and the next smallest hardware-verifiable task. Keep a changelog. Run portable tests after protocol changes. Keep Mac and Quest protocol versions coordinated; update the golden frame fixture when changing binary layout. Never commit private keys, pairing tokens, captured desktops, or microphone recordings.

## First instruction to execute

**Inspect this repository, evaluate PippinVR as a candidate foundation, and choose a justified route. Then get one real Mac desktop panel displaying on the user's Quest 3S over USB, with explicit source approval and no remote input enabled. Stop at a documented hardware result before expanding scope.**
