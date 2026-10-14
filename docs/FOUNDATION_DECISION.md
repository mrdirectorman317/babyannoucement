# Foundation decision (milestone M0)

Question from the handoff: extend **PippinVR** or keep this original Unity shell?

Evidence (README fetched 2026-10-14, https://github.com/triscuitcircuit/PippinVR):
- GPL-3.0. Early stage: ~12 commits, ~9 stars, no forks.
- Mac server in Swift using `CGVirtualDisplay`; Quest client in **C++/CMake/Gradle** (not Unity).
- Tested on Quest 3 (3S "supported"). Controller placement/zoom, passthrough cycling with B. **No hand tracking** mentioned.
- Known issues: remote polling stops after wake; stale displays after the server disconnects.

Decision: **keep this Unity shell (Route B).**
- Our differentiators (Vision-Pro-style panels, notifications, assistant, Siri, hands, anchors) live in the client and host protocol, and PippinVR's client is C++, not Unity.
- GPL-3.0 would force this repo's code under GPL if we copied from it; we copied nothing.
- Its virtual-display approach (`CGVirtualDisplay`) is a useful idea for later (a *real* extra monitor, not just mirroring) but it is a private-ish API and needs separate evaluation.

Revisit if: M1 (a real Mac display on a real Quest) takes unreasonably long, or someone wants to adopt PippinVR's virtual-display server as a capture backend behind our protocol (license-compatible only as a separate process).
