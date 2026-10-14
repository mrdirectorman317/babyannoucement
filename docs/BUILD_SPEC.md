# SpatialMac — complete build specification

**Date:** October 14, 2026. **Target:** a Quest 3S and a Mac. **Status:** designed and partially implemented source starter; hardware validation remains.

## 1. The experience we are building

You put on your headset and open your personal workspace. Your actual Mac applications appear as spatial panels. A browser might be beside your desk, a reference panel near a wall, an editor in front of you, and a music panel off to the side. You can switch between a fully authored virtual environment and passthrough that shows the room around those panels. The application should feel like a spatial interface to your own computer, not like a menu of games.

The primary interaction is literal and consistent. Pinch or trigger on application pixels to click them. Pinch a border to grab the spatial panel. Move your hand to move the panel and turn your wrist to rotate it. Pinch a corner to change its physical size while its opposite corner stays put. Grab with two hands to scale, rotate, and reposition together. A clear state control determines whether it stays in place, locks against accidental movement, persists through a real room anchor, or follows you.

These are product requirements, not a claim that every interaction has been tested. The supplied C# implements the underlying mechanics, but real users and real headset tracking are necessary to tune thresholds, targets, ray placement, feedback, and recovery from tracking loss.

There are two different meanings of “bigger.” Enlarging a panel in meters makes its existing pixels occupy more of your view. Increasing its render/capture resolution increases the detail available in those pixels. The interface should keep those controls separate. A huge low-resolution image does not become a sharp high-resolution monitor merely because it is larger.

## 2. Architecture decision: native space, streamed computer

The Quest should render the room, panel geometry, hands/controllers, local menus, and optional virtual-world assets. The Mac should run applications, capture their output, and accept explicitly permitted input. This is a 2D desktop streaming problem inside a native 3D application—not a full Mac stereoscopic VR streaming runtime.

```text
REAL MAC                                       QUEST 3S
Applications                                   OpenXR / Meta runtime
    |                                               |
ScreenCaptureKit -> encoder -> TLS video ------> decoded panel textures
    |                                               |
Approved source catalog -> TLS control -------> workspace / panel picker
    |                                               |
Input permission gate <- TLS control <--------- pointer, reviewed text
    |                                               |
Optional local transcription <- TLS audio <---- push-to-talk microphone
                                                    |
                                 native world / hands / passthrough / anchors
```

This separation is deliberate. Turning your head should not require a round trip to the Mac before the panel itself moves correctly in your view. A stalled app-video frame should not stall the native world. It also avoids requiring an unsupported desktop VR runtime simply to surround yourself with Mac windows.

The first host uses Apple's ScreenCaptureKit to enumerate and capture shareable displays/windows. The implementation is based on public capture interfaces, not a custom virtual-display driver. The package supplies the native capture path as source; Apple SDK integration and permission behavior still need to be checked on the target Mac. [R5, R6]

## 3. Two rendering modes, one workspace model

**Virtual mode:** show your authored Unity environment, hide passthrough, and draw the panels in that environment. Begin with a quiet studio or dark room with simple geometry. Add attractive environments after the workspace is dependable. You should be able to change the environment without losing panel identities or source subscriptions.

**Passthrough mode:** enable a real Meta passthrough compositor layer, hide the opaque virtual-world geometry, and use camera settings appropriate for the selected pipeline. The supplied `MetaPassthroughProvider` switches an existing configured layer. It does not construct the required SDK setup for you. A transparent Unity background by itself is not camera passthrough. [R7]

This design needs the runtime's passthrough presentation, not raw camera access or uploading camera footage to the Mac. Room scanning, semantic understanding of furniture, and camera-based AI are separate, permission-sensitive features. Do not request them preemptively merely to display windows in the room.

A mode switch must preserve panel selection, panel dimensions, allowed source mapping, and placement intent. If passthrough is unavailable, present a clear error rather than claiming the room is visible. Do not silently disable the system boundary or imply that depth occlusion is implemented when the client only draws simple quads.

## 4. What “full macOS” can and cannot mean

The real applications remain on the Mac: browser, editor, Finder, creative applications, terminal, and so on. Their captured output is presented in VR and conventional input is sent back. The Mac must remain awake, reachable, and in a session that can provide the relevant capture. This starter does not operate a locked login screen, create separate user sessions, or bypass protected content.

A spatial panel is not a real 3D extrusion of a native macOS application. It is an image surface plus interaction mapping. System menus, attached popovers, permission dialogs, and other windows may belong to separate captured surfaces. An isolated window capture cannot be assumed to include the whole interaction context.

Start with a full physical-display panel. It includes the ordinary operating-system workspace and is the most useful recovery surface when an isolated window has a popup. Individual-window mode comes next, with conservative focus handling. A production panel picker should offer “open full desktop for this application” whenever interaction becomes ambiguous.

The supplied Mac input router models one global pointer/focus state. It first checks local consent, Accessibility trust, stream generation, source geometry, and the target window. A first click may raise an app without clicking its content; a second click is then required after focus has settled. This is intentional fail-closed behavior, not a polished focus system. There is no safe general shortcut that turns every captured app window into its own independently focused remote session.

## 5. Sources, subscriptions, and identity

A source is a current approved display or window. A panel is a local spatial object associated with a source subscription. Keep those identities separate. The client assigns a positive stream ID. The host assigns a generation whenever it creates/replaces a capture. Every input message and video frame is scoped to the stream/generation pair.

A generation change invalidates clicks against the old image geometry. This is essential when a Mac window moves, resizes, disappears, or is re-created. The initial implementation refuses input after source geometry changes and requires refresh; production can negotiate automatic refresh with an explicit transition state and acknowledgement.

Numeric window IDs are not permanent identities. The layout loader conservatively compares current approved kind/application/title and restores only an unambiguous match. That is still a prototype, not a stable document identity system. Add a user-facing rebind picker, application bundle identifier, and optional document identity where an application can expose it. Never silently bind a saved private window to whichever new window happens to inherit the same numeric ID.

Subscriptions are serialized in this client and capped at three. Closing a panel frees its capture and texture. Pending replies, capture start failures, reconnects, and asynchronous permission changes require race-condition tests. An error response should eventually carry the precise request/stream ID; the initial generic error handling is deliberately listed as a limitation.

## 6. Panel coordinates and click mapping

Use Unity meters with a coherent tracking origin. Each panel stores position, quaternion rotation, width, height, source identity, generation, placement mode, and optional anchor UUID. Do not store arbitrary world coordinates and later label them room coordinates.

The implementation uses a rectangular surface centered at its local origin. The visible front faces the user; rays arriving from the back are rejected for interaction. For a ray hit expressed in panel-local coordinates `(x, y)`:

```text
u = x / panelWidth + 0.5
v = 0.5 - y / panelHeight
```

The protocol defines `(0,0)` as the top-left of the captured image. The Mac maps normalized coordinates into the approved capture bounds. The mapping clamps the last edge just inside the half-open rectangle. Reject non-finite values, stale generations, invalid button enums, and out-of-bounds coordinates before posting events.

Do not guess whether the actual texture needs a vertical flip on the final pipeline. Use the supplied calibration JPEG and compare all four labeled corners. Also test the center, right edge, bottom edge, and a target window on a display with a nonzero or negative origin. Retina scaling must be measured: capture pixels and global desktop coordinate units are not interchangeable.

## 7. Manipulation without accidental computer input

Panel chrome and application pixels are separate hit targets. Moving the panel must never turn into dragging a file on the Mac. Grabbing chrome releases current remote input and enters a local manipulation state. Remote desktop input is suspended while that panel is being manipulated.

### One-hand border grab

At grab start, record the panel pose and tracked hand pose. Compute the change in hand rotation with quaternions, then apply that change to both the panel orientation and its offset from the hand. This allows arbitrary tilt/roll without Euler wraparound jumps. There is no artificial requirement that panels remain upright.

```text
rotationDelta = currentHandRotation * inverse(startHandRotation)
newPanelRotation = rotationDelta * startPanelRotation
newPanelPosition = currentHandPosition
                 + rotationDelta * (startPanelPosition - startHandPosition)
```

This starter uses controller/hand ray selection followed by pose-driven movement. It is not a finished direct-touch interaction system. Far-distance translation gain, near-grab snapping, precision mode, and hand-pose filtering need usability work.

### Corner resize

Keep the opposite corner fixed in world space. Intersect the input ray with the panel's original plane. Project the dragged diagonal onto the original width/height diagonal to get an aspect-preserving scale. Clamp the result to the supported size range, then recompute the center from the fixed corner. This prevents the common prototype behavior where resizing unexpectedly moves all four corners.

The current panel width range is 0.25–4 meters. This is a prototype safety/usability bound, not a hardware limit. The aspect ratio follows the source image. A future independent height/width mode should visibly warn when stretching content, rather than silently distorting text.

### Two-hand manipulation

Build an ordered coordinate frame from the two tracked hands. The midpoint provides translation, separation provides scale, and the frame rotation provides orientation. Order the hands by stable pointer IDs so dictionary iteration cannot flip the frame. When one hand releases, rebase from the current pose to prevent a jump. Cancel on tracking loss rather than launching the panel with an unintended throw velocity.

## 8. Hand controls and fallback

The optional `HandInputSource` reads thumb tip, index tip, and palm poses from Unity XR Hands. It uses different enter/exit pinch distances to avoid rapid toggling near a threshold. Joint coordinates must be transformed through the tracking origin; attaching them directly to the camera would double-apply motion. Unity XR Hands provides a subsystem abstraction, but the selected runtime still needs a working hand-tracking provider. [R10]

This starter's shoulder-to-pinch ray is a comprehensible starting approximation, not a tuned replacement for Meta's interaction system. Compare it with the supported Interaction SDK hand-ray/direct-grab prefabs. Avoid enabling both a controller input source and a hand source that continuously feed the same pointer router.

Quest 3S does not supply eye-tracking hardware for eye-gaze selection. Head direction, controller aim, and hand aim can still drive selection; do not label a head-aim implementation “eye tracking.” [R4]

Every important action needs a recovery route. Provide a summonable menu, controller fallback, visible connection state, a physical keyboard/trackpad option, and an accessible stop control on the Mac. The current B-button menu summon is controller-oriented; a hand-only summon affordance must be added before claiming a complete hands-only experience.

## 9. Placement states: do not conflate them

**World:** leave the panel at its current pose in the active application's coordinate system. It is movable.

**Session locked:** leave it at that pose and reject accidental grab/resize. This does not promise that its physical room position survives a tracking-origin change or restart.

**Follow:** maintain a comfortable offset relative to the user's location and mostly yaw orientation. Use interpolation and a yaw dead zone rather than rigidly welding the panel to every tiny head movement. Keep controls reachable and prevent the workspace from blocking the center of vision while moving.

**Persistent locked:** create a real runtime spatial anchor, wait for localization, save it, record its UUID, and parent the panel relative to that anchor. On a later session, load/localize before showing it. A failed localization means “not restored,” not “put it at the old transform anyway.” The supplied Meta adapter follows this lifecycle but has not been run on a headset. [R8, R9]

For a whole-house product, add named rooms/workspaces, room selection, anchor health, localizer progress, explicit re-placement, and a forget/delete command. An anchor API does not automatically solve walking between rooms, changes in lighting/furniture, room-map resets, or headset changes. Test each rather than promising unlimited persistent placement.

Follow mode should be opt-in per panel. The implemented motion-opacity heuristic reduces obstruction during motion in passthrough, but it is not obstacle detection or a safety system. A recipe reference panel is a plausible later use case; do not use the prototype while actively handling heat, blades, or stairs.

## 10. Connectivity and trust

The initial path is USB developer networking. ADB reverse maps a port on the headset's loopback interface to the listening port on the Mac. The same protocol can be transported over an explicitly enabled local Wi-Fi connection. No raw USB graphics protocol is required. [R12]

The Mac initially binds only 127.0.0.1. Setup creates a local self-signed TLS identity, a 256-bit random pairing secret, and an exact SHA-256 certificate pin. The Quest verifies the pin before sending the token. Every lane must authenticate and belong to the same client ID; the host admits one active client workspace. Three separate sockets keep queued video from blocking a control message at the application TCP queue level.

TLS here is a meaningful baseline, not a security certification. Development credentials are stored in local files with restricted host permissions. For distribution, use platform credential storage, pair/revoke UI, identity rotation, signed updates, and an independently reviewed transport. Do not expose port 9443 to the public internet, add automatic port forwarding, or disable certificate verification to make a prototype connect.

LAN mode is explicit: stop the Mac host, set `bindLAN` true in private host settings, set the client's host to the Mac's local address, copy the updated config, and restart. Keep the same certificate pin. Validate firewall permission narrowly. Return to loopback mode when done. Discovery/mDNS and friendly QR pairing are future work, not included.

## 11. Performance: three different budgets

First budget: native headset rendering. World geometry, panel meshes, hands, and tracking must remain responsive at the selected runtime refresh rate.

Second budget: desktop capture and video. Your Mac app may update slowly or quickly; the video pipeline introduces capture/encode/network/decode/upload work independently of native head pose rendering.

Third budget: input feedback. A pinch changes a button locally, sends an event, the app reacts, and a returned video frame shows the result. Measure this end-to-end rather than advertising only the encoder duration.

The debug implementation uses ScreenCaptureKit output converted into JPEG images and Unity texture loading. Defaults are 1280-pixel-wide capture at 15 fps; the host clamps capture to 5–30 fps and limits active sources to three. These are starter policy choices, not headset specifications. Main-thread JPEG upload and allocation churn are reasons to replace this path, not optimize it indefinitely.

The planned production path is a hardware video pipeline: VideoToolbox compression on the Mac, a framed media transport or a carefully integrated WebRTC stack, and Android hardware decode into GPU-consumable textures. Apple's VideoToolbox supplies compression-session APIs; Android MediaCodec supplies codec interfaces. Actual profile, level, chroma, decoder concurrency, color handling, and latency need negotiated capability tests. [R13, R14]

Prefer latest-frame backpressure. Drop superseded frames instead of replaying seconds of stale video. Allocate bandwidth to the selected/readable panel first; throttle small peripheral panels. A future visible-region scheduler can pause hidden panels while keeping their state. Do not decode an unlimited number of full-resolution desktop streams just because the user can place many panels geometrically.

Quest 3S's display/lens specifications impose a visual ceiling independent of Mac rendering horsepower: Meta lists 1832×1920 pixels per eye and Fresnel optics, with supported refresh modes including 72, 90 and 120 Hz. A stronger Mac does not alter those physical optics. Use appropriate text sizes, panel distance, angular size, and high-contrast UI, and measure long-session readability on this exact headset. [R3]

Frame timestamps are host-monotonic values. They are useful for ordering/debugging but cannot be subtracted directly from a different machine's clock to manufacture a latency number. Use synchronized instrumentation or an external camera test. Report sustained frame timing and thermal behavior, not only a brief peak.

## 12. Voice and the assistant layer

Start with push-to-talk, a visible recording state, and a short maximum utterance. The supplied client records mono audio, resamples toward a 16 kHz PCM16 payload, and sends up to 12 seconds. The Mac invokes a specifically configured local whisper.cpp executable/model, not arbitrary executable paths received from the headset. Review transcription before applying it. [R15]

Supported starter intents are deterministic: show the room, virtual world, open an allowlisted app, follow me, stay here, remember here, unlock, enlarge/shrink, and bring the selected panel forward. “Type transcript” is separate from “apply command.” Unknown phrases do nothing. This avoids turning a misheard sentence into a surprise action.

A later assistant should expose typed tools: list approved windows, open a chosen panel, set placement, resize, launch an approved app, search explicitly shared files, and propose a task. Split tools into read-only, reversible UI changes, and externally consequential actions. Require confirmation for sending messages, deleting files, purchases, installing software, changing security settings, or broad computer control.

An LLM response must never become raw shell text executed by the host. Screen text, web pages, project files, and app notifications are untrusted content; they cannot authorize tool calls. The assistant needs a policy boundary, explicit scopes, cancellation, an audit trail, and sensitive-field protections. The “Jarvis” layer should be added after the workspace reliably recovers from connection loss.

## 13. Virtual worlds and the user's existing game

Build worlds as separate Unity scenes/prefabs with a stable workspace root that survives scene changes. Keep room-like scale coherent, distinguish navigation geometry from decoration, and preserve readable panel lighting. Your workspace should work in a simple test room before adding forests, waterfalls, particles, and cinematic lighting.

A reasonable first custom scene is an observatory or quiet island: baked lighting, restrained geometry, small material count, a clear working zone, a summon point, and a lightweight ambient backdrop. These are design suggestions, not performance guarantees. Profile the actual Quest build with video active; an empty-scene frame rate says little about the final workload.

For “Just Another Sword Game,” inspect the actual project. Reusable 3D meshes/materials can become environment assets. Sprites, incomplete backsides, camera-dependent effects, and side-view collision assumptions need redesign. Do not convert a 2.5D level to first person by replacing one camera and declaring it finished.

Artificial locomotion is intentionally not wired into this starter; controller sticks currently scroll desktop panels. When added, use an explicit navigation mode with teleport/snap-turn first. Keep panel scrolling and world movement from fighting over the same input. Physical movement within the tracked space is different from joystick locomotion. Avoid automatic camera roll or gravity flips in a productivity workspace.

## 14. Development milestones

**M0 — choose foundation.** Test whether PippinVR can already provide the desired baseline, and compare a fork/extension against the original Unity shell. Record the decision and licensing/dependency implications. [R1]

**M1 — view-only desktop over USB.** Compile host, approve one physical display, pair with pinned TLS, render it in an actual Quest scene, verify orientation, and clear it on disconnect. No remote input yet.

**M2 — safe conventional control.** Add pointer ownership, click/drag/scroll, a physical keyboard workflow, source geometry validation, and emergency stop. Test Finder and a plain text editor before complex creative tools.

**M3 — independent spatial panels.** Add more sources, chrome manipulation, arbitrary rotation, corner resize, follow/session lock, and clear selected-panel feedback. Keep the three-source debug cap.

**M4 — isolated windows and recovery.** Prove focus, popups, menus, display rearrangement, app restarts, screen-scale changes, permissions, and source rebind. Keep full desktop fallback available.

**M5 — hands and passthrough.** Integrate real runtime providers, test hand-only menu summoning, tracking loss, low-light failure, mode switching, and motion comfort.

**M6 — genuine persistence.** Save anchors and relative layout, quit/restart the app, restart the headset, move between tested rooms, reject failed localization, and implement delete/rebind/cleanup UX.

**M7 — reviewed voice.** Install a known local transcription binary/model, test microphone permissions and noisy rooms, keep transcripts short and reviewed, and prove unsupported commands do nothing.

**M8 — production video and assistant.** Replace JPEG with native low-latency video, measure it, harden pairing/updates, and only then expand to scoped assistant actions and custom environments.

No phase has a promised duration. Every phase ends with target-hardware evidence, not a generated screenshot or an unexecuted code listing.

## 15. Release definition

The minimum useful product is not “all features compiled.” It is a reliable one- or two-panel Mac workstation that reconnects predictably, never leaves stuck input, handles ordinary app dialogs, preserves the user's intent, and explains what it is doing. An honest “anchor unavailable—place this panel again” is better than a panel appearing in the wrong room. A clear “remote control is off” is better than silently granting authority after reconnect.

Ship only after dependency versions are pinned, real capture/input tests pass, consent and revocation are usable, sensitive material is not logged, sustained headset performance is measured, and anchor behavior is proven. Keep the user's ambition intact while making each claim correspond to an observed result.
