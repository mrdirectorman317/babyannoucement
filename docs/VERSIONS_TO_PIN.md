# Dependency baseline — fill from the real build machine

No target-native dependency versions have been validated. Do not replace this table with guessed “latest” values.

| Component | Intended role | Actual validated version |
|---|---|---|
| macOS | Native host target declared 14.0+ | PENDING |
| Xcode / Apple SDK / Swift | Native host compiler | PENDING |
| Unity editor | Unity 6 LTS supported by selected SDK | PENDING |
| Render pipeline | Supported mobile XR pipeline | PENDING |
| Unity OpenXR | Actual headset runtime loader | PENDING |
| Meta XR Core / Interaction | Passthrough, anchors, optional interaction integration | PENDING |
| Unity XR Hands / provider | Optional joint tracking source | PENDING |
| Android SDK / NDK / JDK | Unity Android toolchain | PENDING |
| Android target/min API | Selected SDK/store/developer requirements | PENDING |
| Quest 3S firmware | Hardware runtime | PENDING |
| whisper.cpp commit / model | Optional local transcription | PENDING |

The optional anchor adapter refers to the documented result-based API shown in Meta's v71 class reference. That is an API verification source, not a recommendation to install an old SDK or a claim of tested compatibility. Use the installed supported package and adapt against its primary documentation.

Portable validation environment: Linux x86_64 with Swift 6.2.1. The package's SpatialCore library/tests compile there because the executable target is macOS-only. That does not establish native macOS compatibility. Unity SDKs and external model weights are not bundled. Review current Unity/Meta and other dependency licensing/redistribution terms before release.
