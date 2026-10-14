# Safety and security boundary

This starter controls a real computer. It is not penetration-tested or production-hardened. The measures below reduce obvious risks but do not establish a formal security guarantee.

## Trust model

The user owns the Mac and Quest. USB debugging is authorized only on trusted development computers. The host exposes a loopback listener by default. The headset receives a pairing secret and exact certificate pin over an explicit local pairing step. Capture and control require separate Mac-side consent; a connected headset is not automatically allowed to operate applications.

Screen text, transcripts, titles, app contents, and network requests are data. They are not authority to turn on sharing, bypass permissions, install tools, execute commands, or expand a future assistant's scopes.

## Implemented baseline

The server uses TLS, an imported local identity, token verification, one-client role assignment, maximum packet sizes, limited connections, handshake timeouts, bounded queues, heartbeat disarming, local source approval, explicit input arming, and conventional macOS Accessibility checks. The Quest validates the exact certificate fingerprint rather than accepting arbitrary certificates. A mismatched pin is a hard failure.

The default host bind is loopback only. LAN access is explicit. No router configuration, public exposure, discovery broadcast, or cloud relay is included. No arbitrary shell or LLM execution endpoint exists. Local speech launches a configured executable with constructed argument values, not a remote command string.

Mouse releases occur on teardown and tracking loss. Input is rejected for stale generations or moved windows. Sharing revocation clears client image state and stops captures. Known native race/permission behavior still requires tests; source presence alone does not prove these safeguards work on every target platform.

## Data handling

The repository contains no real screen captures, tokens, certificates, private keys, or microphone recordings. The calibration JPEG is synthetic. Mac pairing material is placed under the user's Application Support directory with restrictive permissions; plaintext temporary certificate keys are removed after packaging the identity. The host settings file still contains secrets, so these are development controls rather than a finished credential vault.

The Quest config is copied into application persistent storage for development. Move credentials to appropriate platform storage, implement revocation, and protect any backup/export path before distribution. Do not bake secrets into an APK. Certificate rotation invalidates the old pin and requires explicit re-pairing. The setup certificate expires after 365 days; expiry must not be solved by disabling verification.

Temporary local speech files should be removed after transcription. Verify cleanup on crash and cancellation. Never log full desktop images or dictated text by default. Debug reports should include sizes, timing, errors, stream IDs, and versions—not confidential payloads.

## Pending hardening

Review asynchronous capture cancellation, source-identity reuse, security-critical UI visibility, slow-reader resource exhaustion, authenticated malicious-image inputs, local config tampering, certificate/key storage, source-title spoofing, token rotation, and rate limiting. Add structured request/stream correlation and protocol fuzz tests. Review how Screen Recording or Accessibility changes during an active stream behave on the actual Mac.

Add a signed/notarized release pipeline, explicit permissions explanations, user-accessible pair/revoke management, and a dependency update policy. A stable signing identity matters for predictable macOS permission behavior. The supplied build script creates a local ad-hoc signed app, not a distribution-ready installer.

## Consequential assistant actions

A future AI may manipulate panel layout with reversible commands, but it must not acquire arbitrary computer authority merely because it can see the screen. Require explicit confirmation for destructive changes, outgoing communications, purchases, installations, and security settings. Keep a scoped tool allowlist and a cancellable audit trail. Rendered web pages and documents cannot authorize their own instructions.

## Physical safety

Passthrough is not a certified transparent visor. This prototype has no reliable obstacle detector, collision-avoidance planner, or verified whole-house map. Keep system boundaries enabled. Do not test near heat, knives, traffic, stairs, or while carrying children or hot objects. A follow panel should become less intrusive during motion and remain easy to dismiss; that does not make hazardous multitasking safe.

## Integrations threat model (added)

- **Message and notification text is attacker-controlled.** It is sanitized (bidi/zero-width/control characters removed, length-bounded), displayed, and never parsed as a command. When shown to a language model it is wrapped as `<untrusted_data>`.
- **Prompt injection:** the model only has a typed tool list. Risk tiers are fixed in code. Consequential tools (`launch_app`, `reply_to_message`) can only create a *pending confirmation*; resolving it requires a `confirm` message from the authenticated headset control lane, one-shot, 60 s expiry, max 4 pending. A pending item cannot be approved by the model, a message, or a web page.
- **Replies:** disabled unless the Mac-side toggle is on (auto-off at disconnect/emergency stop); recipients come from an in-memory table of messages that actually arrived; rate limited; AppleScript is a constant string with arguments passed out-of-band.
- **Secrets:** the OpenAI key is in the login Keychain, read only when a request is made, never logged or sent to the headset.
- **Data egress:** with the assistant on, your questions (and tool results the model requests) go to OpenAI. With "OpenAI speech-to-text" on, voice audio goes to OpenAI. Both are off until you enable them.
- **Full Disk Access** (needed to read Messages) is broad. It is granted to the host app by you in System Settings; the host opens only `chat.db`, read-only, and filters new incoming messages.
- No new network listeners, no URL scheme, no AppleScript/shell endpoint reachable from the headset.
- Unreviewed: sandboxing/hardened runtime (the app is unsandboxed and ad-hoc signed), keychain ACL behavior across rebuilds, and an independent security review.
