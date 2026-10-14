# SpatialMac — Quest 3S spatial desktop starter

**Prepared October 14, 2026. Working project name, not a cleared product name.**

A native Quest workspace around the real Mac you already own: independent screen/window panels, fully virtual environments or passthrough, controller/hand interactions, follow mode, a persistent-anchor adapter, and reviewed local voice commands.

**This is source code and an engineering handoff—not a finished app, APK, verified Unity project, or macOS installer.** The portable Swift protocol tests and Python reference/mock transport were run in Linux. The macOS-specific host and Unity/Meta integrations have NOT been compiled or tested on their target platforms. Read `docs/VALIDATION_REPORT.md` before interpreting any implementation claim.

## Start here

Give the next coding chat **this entire repository** and `START_HERE_FOR_NEXT_CHAT.md`. For the human-readable design, open `docs/BUILD_SPEC.md`. The separate `SpatialMac_Full_Handoff.md` attachment consolidates the documentation and source code into one file.

Recommended order: inspect PippinVR as a possible existing foundation; run the portable checks; get the Swift host compiling on your Mac; stream the synthetic calibration fixture into Unity Editor; configure a real Quest rig; prove a single physical-desktop panel on the headset; only then expand input, individual windows, hands, anchoring, voice, and cinematic worlds.

## What is actually supplied

| Area | Supplied implementation | What still needs proving |
|---|---|---|
| Mac host | SwiftUI controls; ScreenCaptureKit source catalog and capture; JPEG encoding; TLS listener | Apple SDK compilation, permissions, actual display/window behavior |
| Transport | Three TLS lanes; exact certificate pin; token authentication; bounded framing and queues; USB pairing scripts | macOS Network.framework ↔ Unity Android SslStream interoperability |
| Panel interface | Runtime panels; desktop UV hit testing; border grab; corner resize; two-hand scale/rotation; mode buttons | Hardware ray calibration, usability, shader orientation and performance |
| Placement | Session lock, free placement, comfortable follow, JSON layout; Meta anchor adapter | Meta SDK integration, persistent save/localize/restart, anchor cleanup UX |
| Passthrough | Explicit provider boundary and Meta layer switch | Install/configure the actual Meta passthrough feature and permissions |
| Input | Pointer/text/shortcut messages; Mac focus checks; consent controls; disconnect release | Accessibility edge cases, real app menus, drags, multiple displays |
| Voice | Quest push-to-talk PCM, optional local whisper.cpp invocation, review/confirm command router | Microphone permissions, actual transcription binary/model and latency |
| Developer tools | Mac setup/build/pair scripts; synthetic TLS server; read-only probe; Swift/Python tests | Target-device build, packaging/signing, release process |

There is **no H.264 hardware decode plugin, virtual display driver, arbitrary AI agent, polished virtual keyboard, desktop audio streaming, raw passthrough-camera capture, or artificial locomotion system** in this starter. These are not silently represented by empty “working” components. See the roadmap.

## First commands on your Mac

Inspect scripts before running them. You need Xcode command-line tools, Python 3, OpenSSL, and Android platform-tools (`adb`); Unity setup is separate.

```bash
cd SpatialMac_Starter
bash scripts/doctor.sh
python3 -m unittest discover -s tests -v
(cd macOS && swift test)
bash scripts/setup-mac.sh
bash scripts/build-mac.sh
open Build/SpatialMacHost.app
```

The setup writes secrets under `~/Library/Application Support/SpatialMac`, never into the repository. Screen sharing and remote control start disabled. Open the host controls, request Screen Recording permission, refresh, approve the desired source, and enable sharing. Accessibility and **Arm remote control** are separate choices.

Next follow `quest/README.md`; it explains why the generated Unity preview scene is not already a configured XR application. Once the APK has been installed and launched once:

```bash
bash scripts/pair-headset.sh
```

Default transport is a loopback-only TLS server reached through `adb reverse tcp:9443 tcp:9443`. This is a development network tunnel over USB, **not DisplayPort video input or Meta Quest Link**. [R12]

## Important reality checks

Your Mac still runs normal macOS applications. The headset presents and controls captured content; it does not install macOS on a Quest or grant each window its own operating-system session. Remote full control is powerful: only pair a headset you own, approve sources explicitly, and leave the Mac emergency-stop control reachable.

A transform saved to JSON is not a real-world anchor. This code distinguishes session-only placements from Meta-backed saved anchors. Unlocalized placements are skipped rather than placed at a guessed location.

The first transport is deliberately JPEG at 1280 pixels wide/15 fps, at most three sources. It is a functional-development format, not a claim of premium 90/120-fps desktop streaming. Native head tracking and streamed desktop frame rates are different budgets.

This project does not replace the Quest system shell, defeat boundary safeguards, or bypass protected Mac content. Use passthrough for spatial awareness, not as assurance that operating around hazards is safe. Do not test this prototype while cooking, carrying hot liquids, using knives, or walking on stairs.

## Existing work

PippinVR already describes an open-source Mac/Quest multi-screen workspace with controller placement and passthrough. Its README lists Quest 3S compatibility but identifies Quest 3 as the tested headset. It is an important candidate to evaluate before rebuilding every component. No code from it is bundled here. See `docs/RESEARCH.md`. [R1]

## Project layout

```text
macOS/                    Swift package, native host, portable tests
quest/UnityAssets/         Copy into an actual Unity project's Assets folder
scripts/                  Developer setup, build, pairing, environment report
tools/                    Reference protocol, read-only probe, mock TLS server
tests/                    Portable tests and a synthetic calibration JPEG
docs/                     Architecture, protocol, safety, research, test plan
START_HERE_FOR_NEXT_CHAT.md  Instructions for the next build agent
```

Reference labels such as [R1] resolve in `docs/RESEARCH.md`. MIT applies to the original starter sources only; separately installed products and SDKs retain their own terms.
