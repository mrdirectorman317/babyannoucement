# Rooms and window locking

A **room** is a named place in your home ("office", "kitchen"). Each room owns **one** saved spatial anchor. Windows you **pin** to a room are stored relative to that anchor, so they reappear at the same spot when the headset recognizes the room. Switching rooms closes the old room's pinned windows and brings up the new room's.

## Placement modes (what "locked" means)

| Mode | Saved? | Move / resize / bring-here / recenter | How you get it |
|---|---|---|---|
| World | no | allowed | default, or Unlock |
| Follow | no | allowed (re-place it any time) | Follow / "follow me" |
| Session lock | no | **refused** | Lock / "stay here" |
| Pinned to room | yes (in the room) | **refused** | Pin to room / "pin this" |

`PanelRules.cs` is the single source of truth. A hand grab, a voice command, a Mac-originated `ui_command` and the ChatGPT assistant all ask it, so "locked" means the same thing for all of them. Notable rules:
- Leaving a pin (Unlock, Follow, Lock-toggle, Bring-here) always **drops the saved record** first, so a "zombie pin" cannot come back next session.
- "Stay here" on an already-pinned window is a no-op (it never silently downgrades a pin).
- Closing a pinned window removes it from the room's window list only if you Unpin; closing alone does not delete the room.

## Voice

"new room" / "save this room" · "call this room office" / "rename this room to den" · "switch to kitchen" / "go to the office room" / "take me to garage" · "pin this" / "unpin" · "stay here" · "unlock". Names are at most 4 words. Anything else is ignored (`VoiceCommandParser`, allowlist only). The assistant has a `switch_room` tool (reversible UI) and `pin`/`unpin` panel actions.

## Limits and storage
12 rooms, 8 pinned windows per room, 24-character names (control characters stripped, duplicates made unique). Rooms live in `rooms.json` under the app's persistent data path. Saves go to a temp file and are then swapped in; an unreadable file is kept as `rooms.json.bad` and you start fresh. Rooms are saved **from the room book**, never rebuilt from whichever windows happen to be open, so an empty session cannot erase them. Restoring matches a pinned window to an approved Mac window by exact kind + app + title; none or several matches means that pin is skipped (reported as missing/ambiguous), never guessed.

## Forgetting
"Forget" is a two-press action (second press within 4 s). The headset can only erase an anchor it can currently localize, so deletions are **queued** (`pendingErase`) and retried when you are in range; the room disappears from your list immediately.

## What is tested, and what is not
**Tested (119 xunit tests, in CI):** lock rules for every mode, name sanitizing/uniqueness, create/rename/delete/limits, pin/unpin/duplicate handling, restore planning (missing/ambiguous/invalid), repair of corrupt data, voice phrase parsing including hostile and long inputs, text truncation/wrapping.
**Not tested (needs the headset):** anchor save/load/localize/erase through the Meta SDK, room auto-detection, how reliably two rooms are told apart, panel re-parenting visuals, and the menu layout. `RoomManager` and the menu are type-checked against Unity's API only.

## Tradeoffs
- Switching rooms is deliberately simple (close and reopen) rather than keeping windows from several rooms alive.
- Auto-detect tries the 4 most recently used rooms (3.5 s each); with a slow or ambiguous localization you may need "Find room" or "switch to ...".
- The old cross-session "Follow" persistence and `layout.json` were dropped; nothing reads `layout.json` anymore.
