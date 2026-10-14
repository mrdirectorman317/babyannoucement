using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace SpatialMac {
    // Rooms: named places in your home. Each room has ONE saved spatial anchor, and windows you "pin" to the room are
    // stored relative to it, so they come back at the same spot when the headset recognizes the room again.
    // Switching rooms closes the previous room's pinned windows and brings up the new room's.
    //
    // All decisions (names, limits, matching, validation) live in RoomBook, which is unit-tested. This class only moves
    // transforms and talks to the anchor provider. The anchor calls (Meta) are NOT device-tested.
    public sealed class RoomManager : MonoBehaviour {
        public DesktopSession session;
        public int detectTimeoutMs = 3500, switchTimeoutMs = 8000;
        public RoomBook Book { get; private set; } = new RoomBook();
        Transform activeRoot;                       // the localized anchor of the active room; null if we are not located in it
        bool busy, detectedOnce;
        string startupNote;
        float forgetArmedUntil;
        string StorePath { get { return Path.Combine(Application.persistentDataPath, "rooms.json"); } }

        public bool Available { get { return session != null && session.anchorProvider != null && session.anchorProvider.IsPersistent; } }
        public bool Located { get { return Book.Active != null && activeRoot != null; } }
        public string Label {
            get {
                var r = Book.Active;
                if (r == null) return Book.Rooms.Count == 0 ? "No rooms yet" : "Room: not located";
                return TextLayout.Truncate(r.name, 14) + (Located ? "" : " ?");
            }
        }
        public bool ForgetArmed { get { return Time.unscaledTime < forgetArmedUntil; } }

        void Awake() { Load(); }
        void Say(string text) { if (session != null) session.Status = text; }
        void Refresh() { if (session != null && session.menu != null) session.menu.Rebuild(); }

        // ---- storage ---------------------------------------------------------------------------------------------------
        void Load() {
            try {
                if (!File.Exists(StorePath)) return;
                var data = JsonUtility.FromJson<RoomBookData>(File.ReadAllText(StorePath));
                Book = new RoomBook(data);
                int fixedUp = Book.Repair();
                if (fixedUp > 0) startupNote = "Saved rooms had " + fixedUp + " problem(s); the bad parts were dropped.";
            } catch (Exception) {
                try { File.Copy(StorePath, StorePath + ".bad", true); } catch (Exception) { }
                Book = new RoomBook();
                startupNote = "Saved rooms were unreadable. A copy was kept as rooms.json.bad and you are starting fresh.";
            }
        }
        // Rooms are saved from the ROOM BOOK, never rebuilt from whichever windows happen to be open, so a session in which
        // nothing was opened cannot erase them. Writes go to a temp file, then replace the old file.
        public void Save() {
            try {
                string tmp = StorePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(Book.Data, true));
                try {
                    if (File.Exists(StorePath)) File.Replace(tmp, StorePath, StorePath + ".bak"); else File.Move(tmp, StorePath);
                } catch (Exception) { File.Copy(tmp, StorePath, true); File.Delete(tmp); }
            } catch (Exception e) { Say("Couldn't save rooms: " + e.Message); }
        }

        void Update() {
            if (session == null) return;
            if (startupNote != null) { Say(startupNote); startupNote = null; }
            if (!session.Connected) { detectedOnce = false; return; }
            if (!detectedOnce && Available && session.Sources.Length > 0 && Book.Rooms.Count > 0 && !busy) { detectedOnce = true; DetectRoom(); }
        }

        static SourceKey KeyOf(SourceDTO s) { return new SourceKey(s.kind, s.app, s.title); }
        static SourceKey KeyOf(SpatialPanel p) { return KeyOf(p.Source); }
        static async Task<T> WithTimeout<T>(Task<T> task, int ms) {
            if (await Task.WhenAny(task, Task.Delay(ms)) != task) throw new TimeoutException("Timed out");
            return await task;
        }
        void CloseBoundPanels() {
            foreach (var p in session.Panels.ToList()) if (PanelRules.IsRoomBound(p.Mode)) session.ClosePanel(p);
        }

        // ---- creating / naming / switching --------------------------------------------------------------------------
        public async void CreateRoomHere(string name = null) { await CreateRoomHereAsync(name); }
        async Task<bool> CreateRoomHereAsync(string name) {
            if (!Available) { Say("Rooms need the Meta spatial-anchor setup (quest/README, section G). Session lock still works."); return false; }
            if (busy) { Say("One moment - still working on the last room action."); return false; }
            busy = true;
            try {
                var head = session.head;
                var result = await session.anchorProvider.CreateAndSave(new Pose(head.position, Quaternion.Euler(0, head.eulerAngles.y, 0)));
                string error; var previous = Book.Active;
                var room = Book.Create(name, result.id, DateTime.UtcNow, out error);
                if (room == null) {
                    try { await session.anchorProvider.Erase(result.id); } catch (Exception) { }
                    Say(error); return false;
                }
                if (previous != null) CloseBoundPanels();
                activeRoot = result.root; Save();
                Say("Saved " + room.name + ". Pin windows to it, or say \"call this room ...\".");
                Refresh(); return true;
            } catch (Exception e) { Say("Couldn't save this room: " + e.Message); return false; }
            finally { busy = false; }
        }
        public void RenameActive(string name) {
            string error;
            if (Book.Active == null) { Say("There is no active room to rename."); return; }
            if (!Book.Rename(Book.Active, name, out error)) { Say(error); return; }
            Save(); Say("This room is now \"" + Book.Active.name + "\"."); Refresh();
        }
        public void SwitchByName(string name) {
            var room = Book.FindByName(name);
            if (room == null) { Say("No room called \"" + TextLayout.Truncate(name, 24) + "\"."); return; }
            SwitchTo(room);
        }
        public void CycleRoom() {
            if (Book.Rooms.Count == 0) { Say("No rooms yet. Say \"new room\" to save this one."); return; }
            SwitchTo(Book.Next(Book.Active));
        }
        public async void SwitchTo(Room target) { await SwitchToAsync(target); }
        async Task SwitchToAsync(Room target) {
            if (target == null) return;
            if (!Available) { Say("Rooms need the Meta spatial-anchor setup (quest/README, section G)."); return; }
            if (busy) { Say("One moment - still working on the last room action."); return; }
            busy = true;
            try {
                Say("Going to " + target.name + "...");
                CloseBoundPanels(); activeRoot = null;
                if (target.anchorId.Length == 0) { Book.ClearActive(); Say(target.name + " has no saved anchor."); return; }
                try {
                    activeRoot = await WithTimeout(session.anchorProvider.Load(target.anchorId), switchTimeoutMs);
                } catch (Exception) {
                    // Not located: say so. Never put windows where we merely guess the room is.
                    Book.ClearActive(); Save();
                    Say("Couldn't recognise " + target.name + " from here. Walk into that room and try Find room, or its map may have been reset.");
                    return;
                }
                Book.SetActive(target, DateTime.UtcNow); Save();
                await RestoreActiveInner();
            } finally { busy = false; Refresh(); }
        }

        // Which saved room am I in? Try the most recently used rooms; the first anchor that localizes wins.
        public async void DetectRoom() {
            if (!Available || busy || Book.Rooms.Count == 0) return;
            busy = true;
            try {
                Say("Looking for a room you've saved...");
                foreach (var room in Book.ByRecency().Where(r => r.anchorId.Length > 0).Take(4).ToList()) {
                    try {
                        var root = await WithTimeout(session.anchorProvider.Load(room.anchorId), detectTimeoutMs);
                        CloseBoundPanels(); activeRoot = root; Book.SetActive(room, DateTime.UtcNow); Save();
                        await RestoreActiveInner();
                        return;
                    } catch (Exception) { /* not this one */ }
                }
                Say("No saved room recognised from here. Say \"new room\" to save this one.");
            } finally { busy = false; Refresh(); }
            await TryErasePending();
        }
        public void RestoreOrDetect() { if (Located) RestoreActive(); else DetectRoom(); }
        public async void RestoreActive() { await RestoreActiveInner(); }
        async Task RestoreActiveInner() {
            var room = Book.Active;
            if (room == null || activeRoot == null) { Say("No located room to restore. Try Find room."); return; }
            var plan = Book.PlanRestore(room, session.Sources.Select(KeyOf).ToList());
            int restored = 0, noSlot = 0;
            for (int i = 0; i < plan.Matched.Count; i++) {
                var rec = plan.Matched[i];
                var panel = session.OpenPanel(session.Sources[plan.MatchedIndex[i]]);
                if (panel == null) { noSlot++; continue; }
                panel.SetWidth(rec.width);
                panel.transform.SetParent(activeRoot, false);
                panel.transform.localPosition = new Vector3(rec.pose.px, rec.pose.py, rec.pose.pz);
                panel.transform.localRotation = new Quaternion(rec.pose.rx, rec.pose.ry, rec.pose.rz, rec.pose.rw);
                panel.AnchorID = room.anchorId;
                panel.SetPlacement(PlacementMode.PersistentLocked);
                restored++;
            }
            string msg = room.name + ": restored " + restored + " window(s)";
            if (plan.Missing.Count > 0) msg += "; " + plan.Missing.Count + " not open/approved on the Mac";
            if (plan.Ambiguous.Count > 0) msg += "; " + plan.Ambiguous.Count + " ambiguous (several windows match)";
            if (noSlot > 0) msg += "; " + noSlot + " didn't fit (3-window limit)";
            if (plan.Invalid > 0) msg += "; " + plan.Invalid + " unreadable";
            Say(msg + ".");
        }

        // ---- pinning windows ---------------------------------------------------------------------------------------------
        public async void PinPanel(SpatialPanel panel) {
            if (panel == null) { Say("Select a window first."); return; }
            if (!Available) { Say("Pinning to a room needs the Meta spatial-anchor setup. \"Lock\" freezes it for this session only."); return; }
            if (!Located && !await CreateRoomHereAsync(null)) return;     // no located room yet: save this place as a new room first
            string reason;
            var room = Book.Active;
            if (!Book.CanPin(room, out reason)) { Say(reason); return; }
            var t = panel.transform;
            Vector3 lp = activeRoot.InverseTransformPoint(t.position);
            Quaternion lr = Quaternion.Inverse(activeRoot.rotation) * t.rotation;
            string error;
            if (!Book.Pin(room, KeyOf(panel), new PoseData(lp.x, lp.y, lp.z, lr.x, lr.y, lr.z, lr.w), panel.Width, out error)) { Say(error); return; }
            t.SetParent(activeRoot, true);
            panel.AnchorID = room.anchorId;
            panel.SetPlacement(PlacementMode.PersistentLocked);
            Save(); Say("Pinned " + panel.Source.app + " to " + room.name + ".");
        }
        // Always drops the saved record, even if the window is gone, so no zombie pin can come back next session.
        public void Unpin(SpatialPanel panel) {
            if (panel == null) return;
            if (Book.Active != null) Book.Unpin(Book.Active, KeyOf(panel));
            if (panel.Mode == PlacementMode.PersistentLocked) { panel.AnchorID = ""; panel.SetPlacement(PlacementMode.World); }
            Save(); Say("Unpinned " + panel.Source.app + ".");
        }

        // ---- forgetting ------------------------------------------------------------------------------------------------
        // Two-step (a second press within 4 s) because it deletes the room's saved window layout.
        public void ForgetActive() {
            var room = Book.Active;
            if (room == null) { Say("There is no active room to forget."); return; }
            if (!ForgetArmed) { forgetArmedUntil = Time.unscaledTime + 4f; Say("Press Forget again to delete \"" + room.name + "\" and its pinned windows."); Refresh(); return; }
            forgetArmedUntil = 0;
            CloseBoundPanels(); activeRoot = null;
            Book.Delete(room); Save(); Say("Forgot " + room.name + ".");
            Refresh();
            TryErasePendingFireAndForget();
        }
        async void TryErasePendingFireAndForget() { await TryErasePending(); }
        // The headset can only erase an anchor it can currently localize, so deletions are queued and retried.
        async Task TryErasePending() {
            if (!Available) return;
            foreach (string id in Book.Data.pendingErase.ToList()) {
                try {
                    bool ok = await WithTimeout(session.anchorProvider.Erase(id), detectTimeoutMs);
                    if (ok) { Book.MarkErased(id); Save(); }
                } catch (Exception) { /* not in that room right now: stays queued */ }
            }
        }
    }
}
