# The experience: making it feel like a spatial computer

Design target: the *feel* of Apple Vision Pro on a Quest 3S - calm, glassy, direct. Where the hardware can't do something, this says so.

## Design language

| Idea | How it is built here |
|---|---|
| **Windows are glass** | Each Mac window is a rounded desktop-pixel surface on a translucent rounded backing with a hairline edge (`SpatialMacGlass.shader`, `SpatialMacPanelSurface.shader`). Unlit and cheap on purpose: no blur/grab-pass, which is too costly on Quest. It reads as frosted glass, not true background blur |
| **Grab bar + close button** | A pill under every window moves it; a round button next to it closes it (`SpatialPanel`) |
| **Controls appear when you point** | The toolbar (Follow / Lock / Pin to room / Refresh) and corner resize handles fade in when your ray is on the window and fade out again; colliders switch off when invisible so unseen controls can't be clicked |
| **Everything responds** | Hover brightens a surface and ticks softly; press flashes and clicks; windows scale/fade in when opened (`GlassGlow`, `SpatialAudio`) |
| **Sound in space** | Tiny synthesized cues (hover, select, open, close, notify, error, listen) positioned at the thing you touched. No audio assets |
| **Notifications** | Glass cards float at the top of your view and *lazy follow* - small head turns are ignored, big ones swing them back smoothly (`NotificationFeed`) |
| **One conversational card** | Listening -> "You said" -> ChatGPT -> Confirm -> Compose-reply all appear in the same place (`AssistantPanel`) |
| **Immersion dial** | Dim the room (passthrough or virtual) with Dim -/+ or "dim the room" (`ImmersionDimmer`) |
| **Digital Crown** | Hold left **X** for 0.8 s (or Recenter in the Home window) and your windows swing back in front of you (`RecenterCrown`) |
| **Home view** | A glass window summoned with right **B**, a palm-up left hand, or by voice (`WorkspaceMenu`) |

## Controls cheat-sheet

| Do this | Result |
|---|---|
| Trigger / pinch on the window pixels | Click on the Mac |
| Trigger / pinch on the thin border or the bar under the window | Grab: move + rotate freely |
| Trigger / pinch on a corner dot | Resize (opposite corner stays put) |
| Both hands grabbing | Scale + rotate + move together |
| Right **B** | Home window |
| Left palm up for ~0.5 s (hands) | Home window (tune thresholds on device) |
| Hold left **X** | Recenter everything |

## What is intentionally different from Vision Pro

- **No eye tracking** on Quest 3S: pointing is controller or hand ray, so "look and pinch" becomes "point and pinch". Don't call head-aim "eye tracking".
- **Panels are flat streams of a 2D Mac**, not native spatial apps. There is no depth occlusion by your real room and no real window-edge blur.
- **Latency/resolution** are bounded by the JPEG debug transport (1280 px, 15 fps) until the hardware video path (M8) exists. This is the biggest gap between "looks like" and "feels like" a Vision Pro monitor.
- Hand tracking quality, pinch thresholds and the palm-up gesture are starting values.

## Tuning knobs (all in code, none validated on hardware yet)

`SpatialPanel` (`Pad`, `Radius`, reveal speed), `GlassPalette` (colors), `NotificationFeed` (position `TargetFor`, lifetime), `RecenterCrown.holdSeconds`, `HandInputSource` (`pinchEnterMeters`, palm-up dot-product/time), `SpatialAudio.volume`, `GlassUI.NewText` (text size: `characterSize = lineHeight / 6.4` is an estimate - if text is too big/small on device, change that one constant).
