# Existing projects, platform documentation, and source notes

**Checked October 14, 2026.** URLs are included so the next developer can inspect primary sources rather than relying on an earlier chat's unsupported claims. Product features and project status can change. A repository announcement is not an independent hardware review.

## Prior art and the correction

**PippinVR [R1]** is the closest open-source lead found for this request. Its README describes multiple Mac virtual screens, controller placement/zoom, passthrough, and an ADB-based USB workflow. It lists Quest 3S but identifies Quest 3 as its tested headset. Its architecture uses a Swift Mac server, C++ Quest client, and `CGVirtualDisplay`. Treat that API dependency as a compatibility/distribution question to investigate. The repository declares GPL-3.0 and lists known issues around wake/disconnected stale displays. Evaluate it first; do not assume it already implements the requested hand gestures, voice assistant, or whole-house anchors. No PippinVR source was copied into this original starter.

**Virtual Desktop [R2]** explicitly offers a macOS desktop-streaming path and lists Quest 3S. Its Windows requirement concerns PCVR game streaming. This corrects the earlier blanket implication that a Mac cannot stream to a Quest. Desktop streaming and a full stereoscopic game-streaming runtime are different capabilities.

**Immersed [R16]** is another commercial spatial-workspace reference with a Mac path. It can help benchmark usability and monitor workflows. No claim is made here about current free-tier limits, pricing, open-source availability, or suitability for the user's desired custom fork.

A PippinVR developer announcement was also located on Reddit and Meta's developer forum [R17, R18]. Those posts establish that a community project has been announced, not that it is production-ready or independently tested on this user's headset. The available evidence does not support saying “nobody has built this.”

## Build-versus-extend decision

Before implementing a production codec, compare two concrete routes. Route A: build and test PippinVR, then determine whether extending its existing client or transport would be less work. Route B: keep this original Unity workspace shell and integrate a verified Mac capture/streaming path. Assess implementation effort, SDK dependence, license compatibility, private APIs, input reliability, performance, and maintenance. The custom interaction layer is the user's differentiator; rebuilding every solved transport component may not be necessary.

Do not infer that every public GitHub repository is open source or that a project's listed target is hardware-tested. Read its actual license, source, issues, and build scripts at the commit you select.

## Primary references

**[R1] PippinVR repository and README**  
https://github.com/triscuitcircuit/PippinVR

**[R2] Virtual Desktop official requirements and FAQ**  
https://www.vrdesktop.net/

**[R3] Meta Quest 3S official product specifications**  
https://www.meta.com/quest/quest-3s/

**[R4] Meta eye-tracking documentation / supported hardware**  
https://developers.meta.com/vr/documentation/unity/move-eye-tracking/

**[R5] Apple ScreenCaptureKit framework**  
https://developer.apple.com/documentation/screencapturekit

**[R6] Apple capture sample and stream configuration**  
https://developer.apple.com/documentation/screencapturekit/capturing-screen-content-in-macos  
https://developer.apple.com/documentation/screencapturekit/scstreamconfiguration  
https://developer.apple.com/videos/play/wwdc2022/10156/

**[R7] Meta Unity passthrough documentation**  
https://developers.meta.com/vr/documentation/unity/unity-passthrough/

**[R8] Meta spatial-anchor overview and persistence**  
https://developers.meta.com/vr/documentation/unity/unity-spatial-anchors-overview/  
https://developers.meta.com/vr/documentation/unity/unity-spatial-anchors-persist-content/  
https://developers.meta.com/vr/documentation/unity/unity-spatial-anchors-best-practices/

**[R9] Meta OVRSpatialAnchor result-based API reference and sample**  
https://developers.meta.com/vr/reference/unity/v71/class_o_v_r_spatial_anchor/  
https://github.com/oculus-samples/Unity-SharedSpatialAnchors

**[R10] Unity XR Hands documentation**  
https://docs.unity3d.com/Packages/com.unity.xr.hands@1.7/manual/index.html

**[R11] Unity XR project creation**  
https://docs.unity3d.com/Manual/xr-create-projects.html

**[R12] Android ADB and reverse forwarding**  
https://developer.android.com/tools/adb  
https://developer.android.com/develop/ui/views/layout/webapps/access-local-server

**[R13] Apple VideoToolbox compression sessions**  
https://developer.apple.com/documentation/videotoolbox/vtcompressionsession

**[R14] Android MediaCodec API reference**  
https://developer.android.com/reference/android/media/MediaCodec

**[R15] whisper.cpp repository / local transcription implementation**  
https://github.com/ggml-org/whisper.cpp

**[R16] Immersed official site and supported computers**  
https://immersed.com/  
https://immersed.helpscoutdocs.com/article/7-supported-computers-headsets

**[R17] PippinVR developer's Reddit announcement — self-report, not an independent review**  
https://www.reddit.com/r/virtualreality/comments/1wplsvh/pippinvr_gplv3_open_source_macos_virtual_screen/

**[R18] PippinVR Meta community announcement — self-report**  
https://communityforums.atmeta.com/discussions/What_I_am_Building/pippinvr-gplv3-open-source-virtual-screen-software-for-mac/1384666

## Uncertainty retained

No community project was installed or hardware-tested in this session. No popularity, maturity, current pricing, or future support guarantee is inferred from these sources. The starter's own architectural decisions and interaction math are proposals/implementations, not capabilities certified by the cited vendors.
