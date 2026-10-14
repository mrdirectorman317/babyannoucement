# Test plan and acceptance gates

All rows below are **pending target-hardware execution** unless the validation report explicitly records them as executed. Run in a separate project with non-sensitive source windows.

## Portable checks

```bash
python3 -m unittest discover -s tests -v
(cd macOS && swift test)
for f in scripts/*.sh; do bash -n "$f"; done
python3 -m py_compile tools/*.py tests/*.py
```

Protocol tests cover header golden bytes, fragmented reads, truncation, limits, JPEG metadata/signature, PCM length, NaN rejection, coordinate validation, and a scalar corner-resize reference. They do not execute Unity interaction math or Apple capture APIs.

## Mac host

Build with the real macOS SDK. Launch without Screen Recording and confirm the UI explains the missing permission. Grant it, refresh, approve one display, and start sharing. Confirm unapproved sources are absent from the network catalog. Keep Accessibility off during view-only testing.

Test stop/start, sleep/wake, app quit, permission removal, two attempted clients, bad tokens, bad certificate pins, unknown roles, oversized messages, an empty source list, closed windows, and capture start failures. No failure should silently enable sharing/control. Source approval must not accidentally transfer to an unrelated recycled window identity.

Test Retina and non-Retina displays, rotated displays, negative display coordinates, different scale factors, dragging a Mac window across monitors, changing resolution mid-stream, and keeping an unchanged window still for several minutes. Verify all four calibration corners and click targets.

## Remote input

Arm input explicitly on the Mac. Use a text editor and Finder with disposable test files. Verify first-click activation, subsequent content click, drag, release outside the panel, scroll, Unicode text, and allowed shortcuts. Verify malformed inputs, stale generations, moved windows, and unapproved sources are rejected.

Hold a drag and unplug USB. Repeat with network loss, app pause, tracking loss, source revocation, capture-stop error, Mac sleep, and emergency stop. Confirm a mouse-up reaches the Mac or local teardown releases it; there must be no stuck drag or surprise input after reconnect. Reconnection must remain disarmed.

Open menus, popovers, sheets, and another overlapping app window. When isolated-window control cannot safely target them, use the full desktop fallback. Record cases that require better focus/geometry handling rather than weakening the guard globally.

## Quest rendering and manipulation

Build an actual ARM64 IL2CPP player and verify TLS on-device. Check stereo texture orientation and all four image corners. Confirm only the visible panel front accepts desktop input. Click world-menu buttons without clicking a panel behind them.

Move/rotate a border-grabbed panel; verify no Mac pointer-down is sent. Resize each corner and verify its opposite corner stays fixed. Test two-hand scale/rotation and releasing either hand first. Try crossing hands, minimum/maximum scale, large wrist rotation, and tracking loss while manipulating. Locked panels must reject spatial manipulation but remain usable as displays.

Test follow mode while seated and while making small safe movements in an unobstructed space. It should not jitter with tiny head motions or lurch when switching modes. Motion-opacity behavior is a visual aid, not an obstacle test. Keep the system boundary on.

## Passthrough and anchors

Prove passthrough with an official sample, then switch within this workspace repeatedly. Verify local panels persist through a mode switch and no hidden world geometry blocks the room. Denied/missing passthrough support must produce an error.

Prove one official persistent anchor, then create one app panel anchor. Quit/relaunch, restart the headset, relocalize, and restore. Test different lighting, temporarily looking away, leaving/reentering a tested room, deleting/resetting room data, and a missing/ambiguous Mac source. On failure, show a recoverable state and do not guess where the panel belongs.

Rooms (see `docs/ROOMS.md`): create two rooms in different spaces; pin a window in each; relaunch and walk between them (auto-detect should pick the right one, "Find room" otherwise); "switch to <name>" closes one room's windows and opens the other's; try grabbing, resizing, Recenter and "bring this here" on a locked and on a pinned window (all must refuse, with a message); Unpin then relaunch (must not come back); Forget a room twice, then re-enter it and confirm the anchor erase queue drains; corrupt `rooms.json` and confirm a `.bad` copy and a clean start.

Test explicit forgetting and cleanup once that UI is implemented. Report drift as an observed measurement under stated conditions; do not promise whole-house accuracy from a single successful restart.

## Voice

Deny microphone permission; grant it later; record silence; record 1/5/12-second speech; exceed the limit; disconnect during recording; quit during transcription. Verify recording is visible and temporary files are removed. Confirm transcript review occurs before typing or command application. Unknown phrases and harmful/action-like text must do nothing.

Use local test sentences containing private-looking dummy data and verify logs do not retain their contents. The optional model is local by design; verify no new cloud integration has been introduced without explicit user choice.

## Performance and session endurance

Measure native CPU/GPU frame times, dropped/reprojected frames, encode/decode/upload time, allocation rate, network traffic, input-to-visible-response delay, memory growth, and 30-minute thermal behavior. Compare one and three panels, static and scrolling content, passthrough and virtual modes, controllers and hands, and the actual custom environment.

Label timestamps correctly. Host-monotonic capture time alone is not end-to-end latency. Use a high-speed external camera or synchronized instrumentation. Check that slow video does not cause control backlog and that disconnect removes stale images.

## Sign-off record

Each gate needs OS/firmware/tool versions, exact commit, test procedure, observed result, logs without secrets, and device footage where appropriate. A passing build is not a passing usability or safety test. Do not mark a feature complete merely because its class exists.
