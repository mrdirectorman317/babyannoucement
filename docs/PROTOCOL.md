# SpatialMac protocol v1

This is an original debug transport, not an implementation of PippinVR, RDP, VNC, Quest Link, or WebRTC. All three lanes use the same port with separate authenticated TLS connections.

## Pairing and lane establishment

Client configuration fields: `host`, `port`, `certificateSHA256` (64 lowercase hex characters), `token` (64 hex characters representing 32 random bytes), `clientID` (UUID string), and `autoConnect` (boolean). Host setup generates them locally. No live credentials are included in this repository.

Connect TLS, validate the exact leaf SHA-256 pin, then send JSON hello:

```json
{"type":"hello","token":"<private token>","clientID":"<paired UUID>","role":"control"}
```

The host replies with `{"type":"welcome","role":"control"}`. Establish `video` and `audio` lanes only after the control lane has authenticated. The host accepts one active client ID and at most one socket for each role. Handshakes time out. Keep the secret out of logging and URLs.

The control lane carries source catalog, consent state, subscriptions, heartbeat, input, errors, and transcripts. The host sends JPEG on the video lane. The optional audio lane carries client-to-host PCM utterances. It does not carry system playback audio.

## Binary framing

Every frame begins with exactly 12 bytes:

| Offset | Size | Meaning |
|---|---:|---|
| 0 | 4 | ASCII `SMAC` |
| 4 | 1 | Protocol version, currently 1 |
| 5 | 1 | Kind: 1 JSON, 2 JPEG, 3 PCM |
| 6 | 2 | Reserved; must be zero |
| 8 | 4 | Unsigned payload length, big-endian |

A JSON frame containing two payload bytes has header hex `534d41430101000000000002`.

Limits: JSON 65,536 bytes in the general wire format; Mac inbound JSON is further capped at 16,384. JPEG payload is capped at 8,388,608. PCM payload is capped at 384,008. Validate the header before allocating the payload. TCP reads may be fragmented; read exactly the required length. EOF mid-frame is an error. Unknown kind/version/reserved flags are not ignored.

## JPEG payload

A 24-byte metadata prefix precedes JPEG data:

| Offset | Type | Meaning |
|---|---|---|
| 0 | u32 BE | Positive stream ID |
| 4 | u32 BE | Frame sequence |
| 8 | u16 BE | Pixel width, 1–4096 |
| 10 | u16 BE | Pixel height, 1–4096 |
| 12 | u32 BE | Positive generation |
| 16 | u64 BE | Host-monotonic capture time in microseconds |
| 24 | bytes | JPEG beginning with `FF D8` |

Check decoded dimensions against metadata. The signature check is not a comprehensive malicious-image validator; the authenticated host must be trusted, and production decoders need resource limits. Do not compare the host timestamp directly to a headset wall clock. Frame sequence wrapping must be handled if ordering logic is added.

Client backpressure retains the most recent pending frame per allowed stream and applies at most one image per render frame. The host similarly retains at most one queued replacement per stream while sending. Static captured content may remain unchanged; subscription/transport liveness is distinct from pixel update frequency.

## PCM payload

First u32 BE: sample rate, exactly 16000. Second u32 BE: sample count, no more than 192000. The remainder is exactly `sampleCount * 2` bytes of **little-endian signed PCM16 mono**. Maximum utterance length is 12 seconds. No WAV header travels over the wire; the host constructs its own temporary WAV for its fixed transcription binary. Zero-length/too-short speech can be rejected by the transcription layer.

## Source catalog and consent

A source has `id`, `kind`, `title`, `app`, `pid`, and `bounds:{x,y,width,height}`. Current kinds are `display` and `window`. Source IDs look like `display:1` or `window:123`; they are session identities, not durable document references.

`permissions` reports `sharingEnabled`, `controlEnabled`, and `speechEnabled`. `catalog` contains only locally approved sources when sharing is enabled. The client cannot self-approve new sources or turn on Mac remote control through a network command.

## Subscription exchange

```json
{"type":"subscribe","sourceID":"display:1","streamID":1,"fps":15,"maxWidth":1280}
```

```json
{"type":"subscribed","sourceID":"display:1","streamID":1,"generation":1}
```

Client accepts JPEG/input for the acknowledged generation. Refresh replaces the capture and obtains a new generation. `unsubscribe`/`unsubscribed` release a stream. There may be frames in flight during transitions; old generations must not become clickable again. The current generic error envelope should be extended with request correlation for production.

## Input

Pointer fields: `type:"pointer"`, `streamID`, `generation`, `action` in `move/down/up/scroll`, normalized `u/v`, optional `button` 0 left or 1 right, scroll `dx/dy`. Host scroll values are limited to magnitude 100. Only one client pointer owns an active desktop drag. Chrome gestures do not send application pointer events.

Text fields: `type:"text"`, stream/generation, `text` no longer than 512 UTF-16 code units. A physical pointer click must have established the intended focus. The host inserts Unicode through events without changing the clipboard. This is not an IME/composition implementation.

Shortcut fields: `type:"shortcut"`, stream/generation, `action` in copy/paste/undo/redo/find/select_all/escape/enter/backspace/tab. Arbitrary keyCode/modifiers fields are not trusted as a generic keyboard injection endpoint.

Launch fields: `type:"launch"`, `appID` in the explicit host allowlist: Finder, Safari, TextEdit, Xcode. The client cannot supply a shell command or executable path. Extend the local allowlist deliberately.

`release_all` releases held mouse buttons. Valid mouse-up is treated as a release even after geometry changes. Disconnect, app pause, tracking loss, permission revocation, and heartbeat expiry must also release input. Client ping cadence is approximately one second; the host disarms input after three seconds without heartbeat, while the client disconnects after its configured pong timeout.

## Extension discipline

Negotiate a new version before introducing encoded-video packet types, alpha/chroma modes, ROI updates, audio playback, virtual displays, or arbitrary capabilities. Reject unknown operations by default. Add golden byte tests in Swift/Python and equivalent Unity tests. Do not reinterpret old fields in ways that make an old client send input to a new geometry.

## Integration messages (additive; framing version stays 1)

All travel as JSON on the **control** lane. Unknown fields are ignored by both ends; new optional `Message` fields are
`title, body, appName, category, notificationID, confirmID, command, arg, value, replyable, assistantEnabled, messagesEnabled, replyEnabled`.
`permissions` now also carries `assistantEnabled`, `messagesEnabled`, `replyEnabled`.

| Direction | `type` | Fields | Notes |
|---|---|---|---|
| Mac -> Quest | `notification` | `notificationID, title, body, appName, category` (`message`\|`custom`), `replyable` | Text pre-sanitized and truncated (title 60, body 240). Display only - never executed |
| Mac -> Quest | `ui_command` | `command, arg?, value?` | Allowlist: `set_environment`(`passthrough`\|`virtual`), `open_panel`(name), `panel_action`(`follow,world,lock,refresh,close,bring_here,bigger,smaller`), `set_dim`(0..1), `recenter`. Quest re-validates every argument |
| Mac -> Quest | `assistant_state` | `action:"thinking"`, `requestID` | |
| Mac -> Quest | `assistant_reply` | `requestID, text` (<= 4000) | |
| Mac -> Quest | `confirm_request` | `confirmID, title, body` | Human must approve in the headset |
| Quest -> Mac | `assistant_ask` | `requestID, text` (<= 2000) | Needs an API key on the Mac |
| Quest -> Mac | `confirm` | `confirmID` (UUID), `action:"approve"\|"deny"` | One-shot, expires after 60 s |
| Quest -> Mac | `message_reply` | `notificationID, text` (<= 500) | Only if `replyEnabled`; recipient is looked up on the Mac, never supplied by the headset |
| Quest -> Mac | `dismiss` | `notificationID` | No-op today |

`launch` allowlist now: Finder, Safari, TextEdit, Xcode, Messages, Notes, Calendar, Music, ChatGPT (`com.openai.chat`).
