# Quest / Unity integration guide

## Read this distinction first

`UnityAssets/` contains source assets to import. It is **not a complete Unity project with a verified package lock**. The Editor command creates a preview scene with a normal Camera. It does not install OpenXR, configure Meta, create a production XR rig, grant permissions, or build a tested APK.

Use a separate project first. Do not paste this into a working game and let an agent replace its XR settings, input actions, render pipeline, or Android manifest without review.

## A. Establish a version baseline

Install a Unity 6 LTS editor that is supported by the chosen Meta XR package release, plus Android Build Support, Android SDK/NDK tools, and OpenJDK from Unity Hub. Exact versions must be chosen and recorded on the build machine. “Newest Unity” plus “newest Meta package” is not itself a compatibility proof. [R11]

Use the render pipeline supported by your Meta sample/baseline. This source includes a simple unlit panel shader, but stereo/pipeline correctness must be verified in the actual player. Do not add a large environment yet.

Record Unity editor version, render pipeline version, OpenXR version, Meta XR Core/Interaction versions, XR Hands version/provider, Android target/min API, SDK/NDK/JDK versions, Quest firmware, and graphics API in `docs/VERSIONS_TO_PIN.md` after testing. No generated lockfile is included because no target Unity installation was available here.

## B. Import and compile the basic scripts

Copy `quest/UnityAssets/SpatialMac` to `Assets/SpatialMac` in the new Unity project. Let scripts compile before enabling optional symbols. The `Editor/` folder must remain inside an Editor-only folder. The `link.xml` preserves the protocol/layout DTOs for the default Assembly-CSharp assembly; update it if you introduce assembly definitions.

Keep both `SPATIALMAC_META` and `SPATIALMAC_XR_HANDS` undefined initially. They gate adapters whose types require installed SDK packages. The core preview should compile without either symbol. Fix compiler errors rather than deleting safety checks.

Choose **Tools → SpatialMac → Create starter scene** in the Unity menu (inspect the actual MenuItem string in `StarterSceneBuilder.cs` if the menu name changes). The resulting scene is saved under `Assets/SpatialMac/Generated/SpatialMacStarter.unity`. The generator also creates a material asset so the panel shader is retained in a player build.

The preview includes services, a normal camera, a simple world, panel menu, controller input sources, and an Editor mouse source. A mouse tests selection and corner resizing; it has no wrist pose, so it does not validate six-degree-of-freedom rotation. EditorMouseInput uses the legacy Input API; use the compatible Active Input Handling setting or replace that Editor-only component with your project's input setup.

## C. Prove the synthetic fixture before screen capture

Run the loopback-only simulator from the repository root:

```bash
python3 tools/mock_host.py --directory /tmp/spatialmac-mock --create-config
```

That command creates a short-lived **test** certificate and pairing file and listens on port 9444. Do not use the mock key as the real host's identity. It shows a synthetic 640×360 calibration image and explicitly refuses remote input.

Copy `/tmp/spatialmac-mock/client-config.json` into Unity's `Application.persistentDataPath` for the Editor project. Determine the exact path from the runtime rather than guessing from a product name. For example, temporarily log `Application.persistentDataPath`, but never log the token/config contents. Restart Play mode so DesktopSession reloads configuration.

Select the calibration source in the world menu. Verify that all four corner labels are correctly oriented, the panel is visible from the intended front, the menu receives ray clicks, border grabs do not click image pixels, and disconnect removes the displayed content. A stable synthetic image should remain usable even when the host is not sending constantly changing pixels.

The probe provides an independent read-only transport check:

```bash
python3 tools/probe_host.py \
  --config /tmp/spatialmac-mock/client-config.json \
  --frame display:1 --output /tmp/spatialmac-calibration.jpg
```

## D. Replace the preview camera with ONE actual XR rig

Follow the selected Meta/OpenXR package's supported setup. Import a minimal working sample or use the package's XR rig/building block. Verify that you can build an empty tracked scene to Quest before integrating desktop streaming. Do not stack a Meta camera rig on top of the preview Camera or create two main AudioListeners.

Delete or disable the preview Camera rig. Assign:

| Component field | Assign to |
|---|---|
| DesktopSession.head | Tracked center-eye/head transform |
| WorkspaceEnvironment.view | The actual XR camera |
| ControllerInputSource.trackingSpace | Tracking origin, not the center-eye camera |
| HandInputSource.trackingSpace | Same coherent tracking origin |
| HandInputSource.head | Tracked head transform |
| PointerRouter.session | Main DesktopSession |
| MotionSafety.session | Main DesktopSession |
| DesktopSession.panelMaterial | Generated PanelSurface material |

Controller `devicePosition/deviceRotation` in this starter are a generic XR input path. Depending on your provider, grip pose and aim pose differ. Calibrate the ray or adapt to the supported aim transform before judging click accuracy. Feed poses in meters and apply the tracking-origin transform once, not twice.

Use Android, IL2CPP, ARM64, and the selected SDK's recommended project-validation settings. Run OpenXR/Meta project validation. Configure supported refresh, stereo rendering and graphics settings using the selected SDK rather than hardcoding settings borrowed from another headset.

## E. Passthrough integration

Install/configure the Meta passthrough building block or equivalent supported feature. Confirm that its sample can show your real room. Then enable `SPATIALMAC_META`, add `MetaPassthroughProvider`, assign its `OVRPassthroughLayer`, and assign that provider to WorkspaceEnvironment. Use the compositor placement and camera-alpha/render-pipeline configuration required by the installed Meta package. [R7]

The adapter merely changes the configured layer's hidden state. The application does not request raw camera frames or send your camera feed to the Mac. Switch between passthrough and virtual modes with no Mac connected first. A failure should show the “provider not configured” status, not a fake transparency effect.

## F. Hand tracking integration

Install XR Hands and a compatible provider/runtime configuration; enable the hand tracking feature/manifest settings required by Meta. Verify tracked joints in a sample. Only then enable `SPATIALMAC_XR_HANDS`. [R10]

Add one HandInputSource per hand, assign trackingSpace/head/router, and set `left` appropriately. Use stable left/right pointer IDs. Avoid simultaneous controller and hand components feeding the same router. Initially switch explicitly between controller and hand testing; later add a provider arbiter that releases interactions before switching ownership.

Pinch thresholds in source are tuning values, not universal calibration. Test slow pinch, pinching off-screen, turning the wrist, crossing hands, tracking loss while dragging, and bringing a hand close to a panel. The starter has no fully designed hand-only menu-summon gesture; add a reachable wrist/palm button or another explicit UI before removing controller access.

## G. Persistent anchors

With Meta XR Core configured, enable its required spatial-anchor feature/capabilities and permissions. Add MetaAnchorProvider to the service object and assign DesktopSession.anchorProvider. The optional adapter was written against the documented result-based `SaveAnchorAsync`, `EraseAnchorAsync`, and unbound-anchor load/localize flow. Verify the exact installed SDK signatures before enabling the symbol. The cited v71 reference is an API reference, not a claim that v71 is the correct latest package for your build. [R8, R9]

First prove the official anchor sample on the headset. Then place one SpatialMac panel, choose **remember**, wait for success, quit, relaunch, reconnect/reapprove the source, and choose **restore**. The source must still be unambiguously identifiable and approved. The anchor must localize. Session-only panels deliberately do not restore using old unanchored world coordinates after a new application session.

Anchor delete/forget management is unfinished. Closing a panel currently removes the panel/subscription, not a complete user-facing saved-anchor record. Add this lifecycle UI and cleanup before repeated real-house experiments generate orphaned saved anchors.

## H. Network and microphone permissions

The player needs Android Internet permission for the TLS sockets. Enable Unity's applicable Internet Access setting or merge the necessary permission through the supported build process. For voice, also configure `android.permission.RECORD_AUDIO` and use the runtime permission request in the microphone component. Do not replace an SDK-generated manifest with an incomplete hand-written file.

Meta-specific permissions/features depend on the selected installed SDK and should come from its validation/building blocks. Do not invent permission names from an outdated blog. Ordinary passthrough-layer rendering and raw camera access are different scopes.

Start with voice disabled. Configure the Mac's local speech binary/model paths and enable speech only after microphone transport works. The current implementation does not stream desktop audio to the Quest.

## I. Build, sideload, pair

Enable the headset's supported developer workflow and authorize USB debugging on your own Mac. Build an APK with application identifier `org.spatialmac.quest` (the scene generator sets this unless you change it). Install and launch the app once so its persistent data directory is created.

```bash
adb devices
adb install -r /absolute/path/to/SpatialMac.apk
# Launch the app once in the headset, then:
bash scripts/pair-headset.sh
```

The pairing script uses one authorized device or ANDROID_SERIAL. It copies the private client config and installs `adb reverse tcp:9443 tcp:9443`. Restart the app to load it. Do not commit the config into Unity Assets or StreamingAssets. Do not print it to logs.

On the Mac, enable one approved display capture. The initial target is **view only**. Verify USB disconnect, host quit, permission revocation, resume from sleep, and app pause before arming remote input. The client supports reconnect-on-resume, but this does not auto-launch the app when the headset boots or replace the Quest system home.

## J. Minimum headset checklist

Confirm actual tracking, correct image orientation, readable text, controller ray alignment, global-pointer behavior, local border manipulation, corner anchoring, follow offsets, explicit passthrough, proper disconnect cleanup, and an accessible way to summon the menu. Test with a normal keyboard/trackpad before building a virtual keyboard. Keep the system boundary active.

Record device footage and frame timings. Unity Editor success is not Quest IL2CPP success. A successful APK build is not proof that TLS, hand tracking, or saved anchors work at runtime. Complete the hardware test matrix in `docs/TEST_PLAN.md` before claiming those milestones.


---

# Addendum: new components (integration pass)

New runtime scripts: `GlassUI` (palette, glass items, text), `SpatialAudio`, `NotificationFeed`, `AssistantPanel`, `ImmersionDimmer`, `RecenterCrown`, `SpeechOutput`, `UICommandRouter`. `DesktopSession.Start()` adds any that are missing, so existing scenes keep working.

**Shaders now live in `Runtime/Resources/`** (`SpatialMacGlass`, `SpatialMacPanelSurface`, `SpatialMacVeil`) so they are included in player builds and loaded with `Resources.Load`. They are plain built-in-pipeline unlit shaders; if your project uses URP, they should still render (no `LightMode`), but verify - this is untested.

**Preview the UI without a Mac or headset:** run `python3 tools/mock_host.py --directory /tmp/spatialmac-mock --create-config --demo`, copy its `client-config.json` to `Application.persistentDataPath`, press Play. The demo sends an iMessage-style toast after 4 s, answers "Ask ChatGPT" with canned replies, and shows a Confirm card for text containing "open". It never captures or controls anything.

**Controls added:** hold left `X` (primaryButton) = recenter; palm-up left hand (needs `SPATIALMAC_XR_HANDS`) = Home window.

**C# compile check without Unity:** `dotnet build quest/typecheck` (and `-p:Android=true`). It uses the older 2021.3 Unity reference assemblies, so it can miss Unity-6-only API differences.
