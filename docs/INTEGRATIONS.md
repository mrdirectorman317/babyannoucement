# Integrations: Siri, Messages, ChatGPT, voice

Everything here is **opt-in, off by default, and resets its powers when the headset disconnects**.
Status of each piece is in [Verification status](#verification-status) at the bottom - read that before trusting any of it.

## What you get

| You want | How it works | Needs |
|---|---|---|
| "Hey Siri, show my room" | Siri/Shortcuts call **App Intents** in the Mac host app, which sends an allowlisted `ui_command` to the headset | App built with Xcode (`scripts/make-xcodeproj.sh`) |
| iMessage pop-ups in the headset | Host watches `~/Library/Messages/chat.db` (read-only) for **new** messages and sends a `notification` to the headset | Full Disk Access for the host app |
| Reply to a message from the headset | Dictate -> review the text on the card -> tap Send. Host sends through Messages via a fixed AppleScript | Host toggle "Allow replying from the headset" + Automation permission for Messages |
| Voice input | Push-to-talk on the Quest -> PCM over TLS -> **local whisper.cpp** (default) or **OpenAI speech-to-text** (opt-in toggle) -> transcript you review | `scripts/setup-speech.sh`, or an API key + the cloud toggle |
| ChatGPT as the assistant | Transcript -> "Ask ChatGPT" -> OpenAI Responses API with a *typed tool list* -> reply card in the headset (optionally read aloud) | OpenAI API key (stored in Keychain) |
| ChatGPT/Messages as windows | `Messages` and `ChatGPT` apps are on the launch allowlist; once launched and approved they are just Mac windows you can pin in space | The apps installed |

## Siri and Shortcuts

Siri does not run on the Quest. You talk to Siri on the Mac, iPhone, Apple Watch or AirPods and it drives the headset **through the Mac host app**.

Intents (see `macOS/Sources/SpatialMacHost/SpatialIntents.swift`):
`Open Spatial Workspace`, `Set Headset Environment` (real room / virtual world), `Recenter Workspace`, `Show in Headset` (a note), `Ask the Assistant`, `Emergency Stop`.
Example phrases: "Open my SpatialMac workspace", "Show Real room in SpatialMac", "Recenter SpatialMac", "Ask SpatialMac a question", "Emergency stop SpatialMac".
They also appear in the Shortcuts app, so you can chain them (e.g. a "Focus" shortcut: set environment -> dim -> show a note).

**Important:** Siri only finds the intents if Xcode generated the App Intents metadata. A plain `swift build` / `scripts/build-mac.sh` bundle will *not* show up in Siri. Use:

```bash
bash scripts/make-xcodeproj.sh      # installs xcodegen via Homebrew if needed, builds Build/SpatialMac.app
open Build/SpatialMac.app           # launch it once so macOS registers the shortcuts
```

Deliberately **not** done: a `spatialmac://` URL scheme. Any web page can fire custom URL schemes, so it would be a drive-by control surface.
Deliberately **not** exposed to Siri: anything that acts on your Mac or other people (launching apps, sending messages). Those stay behind the in-headset confirmation.

## Messages in the headset

1. Grant **Full Disk Access** to the host (System Settings > Privacy & Security). `~/Library/Messages` is protected; without it the host says so and does nothing.
2. Host app > Integrations > "Show incoming messages in the headset".
3. Options: show text vs sender-only, look up names in Contacts (asks Contacts permission), allow replying.

How it behaves: starts from the newest message at the moment you turn it on (history is never replayed); polls every 2 s; skips your own messages and tapbacks; shows attachments as "Sent an attachment"; strips invisible/bidi-control characters and truncates (see `TextSanitizer`). Nothing is stored or logged. Message text is **data** - it can never trigger a command, a tool call or a reply.

Replying: only to a message that actually arrived in this session (referenced by an opaque id; the headset can't name an arbitrary recipient), max 500 characters, at most one send per 2 s, only while the host toggle is on (it turns itself off on disconnect/emergency stop). The recipient/chat id never leaves the Mac. The AppleScript is a fixed string; chat id and text are passed as separate process arguments.

## ChatGPT assistant

Flow: hold-to-talk (or the Speak button) -> **You said...** card -> **Ask ChatGPT** -> the host calls the OpenAI Responses API -> reply card (and optional spoken reply).

The model is given only these tools (`AssistantPolicy.swift`; risk tier is decided **in code**, never by the model):

| Tool | Tier | Effect |
|---|---|---|
| `set_environment`, `open_source`, `panel_action`, `set_dim`, `recenter`, `show_notification` | reversible UI | Runs immediately in the headset UI |
| `get_workspace_state`, `get_recent_notifications` | read-only | Returns data to the model. Messages are only readable if you enabled "Let the assistant read recent messages", and are wrapped as untrusted data |
| `launch_app` (allowlisted bundle ids only), `reply_to_message` | **consequential** | Becomes a **Confirm** card in the headset showing the exact action/text. Nothing happens until you tap Approve. One-shot, expires in 60 s, max 4 pending. The model cannot approve its own request |

There is no shell, file, browser or arbitrary-keystroke tool. Prompt injection from a message/web page can at worst *propose* an action that you then see spelled out.

Privacy: your question, and any tool results the model asks for, are sent to OpenAI with your API key. Conversations are chained with `previous_response_id`, which means OpenAI stores responses per its retention policy; use "New conversation" to reset. The key lives in the login Keychain (not in `host-settings.json`). The model name is a setting (default `gpt-5`) - change it if your account uses another. API field names were written from memory of the Responses API; check `OpenAIWire.swift` against the current reference if a request is rejected.

## Voice

- **Local (default):** `bash scripts/setup-speech.sh` installs whisper.cpp (Homebrew) and a small English model and writes the paths into `host-settings.json`. Audio stays on your Mac.
- **OpenAI (opt-in):** toggle "Use OpenAI for speech-to-text". Audio goes to OpenAI.
- Limit 12 s per utterance, 16 kHz mono. Transcripts are always shown for review before anything is typed, run or sent.
- Spoken replies use Android text-to-speech on the headset (`SpeechOutput`, off by default - set `enabledByUser`). It needs a TTS engine on the Quest; untested.

## Triggering ChatGPT / Claude "from the outside"

Not built. Honest options if you want it later: (a) a tiny local MCP/HTTP bridge on the Mac that maps to the same `HostModel.sendUICommand`/`toast` calls (loopback only, bearer token, reversible-UI tools only); (b) OpenAI's Realtime API for a live voice conversation instead of push-to-talk. Neither is included.

## Verification status

| Piece | Status |
|---|---|
| Tool policy, confirmations, sanitizer, attributedBody decoder, OpenAI request/response shapes | **Unit-tested** (Swift, 17 tests) |
| Host (`MessageWatcher`, `MessageSender`, `AssistantService`, `SpatialIntents`, UI) | **Compiles** on a real macOS runner with Xcode (CI), incl. an Xcode app build that runs App Intents metadata extraction |
| Message/assistant/confirm/reply protocol flows | **Tested Python <-> Python** against the demo mock host (`tools/demo_smoke.py`) |
| Unity UI (toasts, assistant card, home window, glass panels) | **Type-checks** against Unity 2021.3 reference assemblies. Never run in Unity: shaders, layout, font sizing and hit-testing are unverified |
| chat.db schema/permissions on *your* macOS, AppleScript `chat id` sending, Siri phrase recognition, OpenAI calls, Android TTS, microphone, Quest rendering | **Not tested** - needs your Mac/headset (see `docs/WHEN_YOU_GET_BACK.md`) |
