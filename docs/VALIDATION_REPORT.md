# Validation report

Generated October 14, 2026 in a Linux x86_64 development container.

## Executed and observed

| Check | Result | Scope |
|---|---|---|
| Swift `swift test` | **9 XCTest cases passed** | SpatialCore framing, metadata and input-validation helpers |
| Python unittest | **17 cases passed** | Reference wire format, fragmented reads, invalid inputs, PCM and scalar resize math |
| Python integration smoke | **Passed** | Pinned TLS control/video connections, source catalog, subscription, exact synthetic JPEG transfer, unsubscribe |
| Wrong certificate pin | **Rejected** | Python client aborted before sending its pairing token |
| Swift host `-frontend -parse` | **Passed** | Syntax only, not Apple API type checking or linking |
| Shell `bash -n` | **Passed** | Four developer scripts; not executed on macOS/headset |
| Python source compilation | **Passed** | Tools and tests parse/compile as Python |
| Unity component filename check | **Passed** | Supplied attachable component classes match their script filenames |
| C# preprocessor balance | **Passed** | Matching conditional blocks only, not C# compilation |
| Info.plist and link.xml parsing | **Passed** | XML/plist well-formedness |

**Total portable unit cases: 26.** The additional integration smoke is separate. Swift's separate Testing runner may also print “0 tests”; the nine executed cases are XCTest cases, as shown in its suite output.

## What this does NOT prove

The Linux Swift package excludes the macOS executable target. It cannot verify ScreenCaptureKit, AppKit, Network.framework, Accessibility or CGEvent semantics. Swift syntax parsing does not type-check Apple APIs.

No C# compiler, Unity installation, Meta SDK, Android build environment, Mac capture session or Quest headset was available to execute the target-native code. The protocol integration test runs **Python ↔ Python**, not Swift ↔ Unity. The audio socket, real microphone, local transcription, native video, hand tracking, passthrough, saved anchors, sustained frame rate and real input safety remain unverified.

No APK, DMG or notarized application is supplied. The source should be compiled, debugged and tested on the target machines using the supplied acceptance plan. Native integration errors may remain.

## Reproduce the portable checks

```bash
python3 -m unittest discover -s tests -v
(cd macOS && swift test)
python3 tools/integration_smoke.py
for f in scripts/*.sh; do bash -n "$f"; done
python3 -m compileall -q tools tests
```

The integration check creates and deletes temporary test keys outside the repository. It requires OpenSSL and Python, listens only on loopback, uses a synthetic fixture, and never posts input to a computer.

## Selected recorded output

### Python unit tests

```text
----------------------------------------------------------------------
Ran 17 tests in 0.002s

OK
```

### Swift portable unit tests

```text
Test Suite 'All tests' started at 2026-10-14 15:52:03.036
	 Executed 9 tests, with 0 failures (0 unexpected) in 0.106 (0.106) seconds
	 Executed 9 tests, with 0 failures (0 unexpected) in 0.106 (0.106) seconds
Test Suite 'All tests' passed at 2026-10-14 15:52:03.145
	 Executed 9 tests, with 0 failures (0 unexpected) in 0.106 (0.106) seconds
```

### Python TLS integration

```text
PASS: control and video TLS lanes exercised through hello/catalog/subscribe/frame/unsubscribe.
PASS: exact synthetic JPEG bytes transferred through pinned TLS.
PASS: incorrect certificate pin rejected before sending pairing token.
NOT TESTED: native macOS/Unity implementation, audio lane, headset or real Mac screen capture.
```

### Swift host syntax parse

```text
Command returned exit code 0 without output.
```
