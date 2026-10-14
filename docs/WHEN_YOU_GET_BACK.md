# When you get back: the shortest path from "code" to "it's on my head"

You need: your Mac, your Quest 3S + USB-C cable, Unity (Unity 6 LTS via Unity Hub), Xcode, and (optionally) an OpenAI API key.

## 0. Preview the whole UI with no Mac and no headset (10 min)
1. Unity Hub -> new 3D project. Copy `quest/UnityAssets/SpatialMac` into `Assets/`. Let it compile (fix any errors first - tell me what they say).
2. Menu **Tools -> SpatialMac -> Create starter scene**.
3. Terminal: `python3 tools/mock_host.py --directory /tmp/spatialmac-mock --create-config --demo`
4. Copy `/tmp/spatialmac-mock/client-config.json` into Unity's `Application.persistentDataPath`, press Play.
5. You should see the glass Home window, a calibration panel after you click it, an iMessage-style toast after ~4 s, and the assistant card when you use *Ask ChatGPT* (demo answers).

## 0b. Isolate the headset video decoder (no Mac needed)
`python3 tools/mock_host.py --directory /tmp/spatialmac-mock --create-config --h264 --h265`, put its `client-config.json` on the headset, `adb reverse tcp:9444 tcp:9444`, open the app and open the test source. Expect a moving pattern at ~60 fps; `Home > Stats` shows the codec in use (HEVC if your headset has a hardware HEVC decoder, else H.264). Press **Home > Video** to cycle Auto / H.264 / JPEG and compare. Details: `docs/VIDEO_PIPELINE.md`.

## 1. Mac host
```bash
bash scripts/doctor.sh
bash scripts/setup-mac.sh            # pairing + TLS identity (secrets stay in ~/Library/Application Support/SpatialMac)
bash scripts/make-xcodeproj.sh       # Xcode-built app (needed for Siri); or scripts/build-mac.sh without Siri
open Build/SpatialMac.app
```
In the app: grant Screen Recording -> Refresh -> tick one display -> "Share only the sources checked" -> Start TLS server. Leave **ARM remote control off** for the first test.

## 2. Headset (M1 gate: one real Mac display in the Quest, view-only)
Follow `quest/README.md` sections A, D, I (Unity + Meta XR + Android build, `bash scripts/pair-headset.sh`). This is the first step that has never been done by anyone - expect to debug here.

## 3. Turn on the fun, one at a time
- **Messages:** give the app Full Disk Access -> Integrations -> Show incoming messages. Send yourself an iMessage.
- **ChatGPT:** Integrations -> paste API key -> Save. Dictation: `bash scripts/setup-speech.sh` (local) or the OpenAI toggle.
- **Siri:** with `Build/SpatialMac.app` opened once, say "Hey Siri, show Real room in SpatialMac" (or find the actions in Shortcuts).

## 4. If something is off
Send me the error text, the Unity Console, or the line from `Console.app` filtered on `SpatialMacHost`. Known weak spots are listed in `docs/KNOWN_LIMITATIONS.md`.
