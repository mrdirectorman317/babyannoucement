using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SpatialMac {
    // ---- Saved data (public fields + lists only, so Unity's JsonUtility can read and write it) -----------------------
    [Serializable] public class SourceKey {
        public string kind = "", app = "", title = "";
        public SourceKey() { }
        public SourceKey(string kind, string app, string title) { this.kind = kind ?? ""; this.app = app ?? ""; this.title = title ?? ""; }
        // Identity used to re-find a window later. Numeric window ids change every run, so this is deliberately strict:
        // kind + app + title must all match exactly.
        public bool SameAs(SourceKey o) { return o != null && kind == o.kind && app == o.app && title == o.title; }
    }
    [Serializable] public class PoseData {
        public float px, py, pz, rx, ry, rz, rw = 1f;      // position + quaternion, in the ROOM ANCHOR's local space
        public PoseData() { }
        public PoseData(float px, float py, float pz, float rx, float ry, float rz, float rw) { this.px = px; this.py = py; this.pz = pz; this.rx = rx; this.ry = ry; this.rz = rz; this.rw = rw; }
    }
    [Serializable] public class PanelRecord {
        public SourceKey source = new SourceKey();
        public PoseData pose = new PoseData();
        public float width = 0.9f;
    }
    [Serializable] public class Room {
        public string id = "", name = "", anchorId = "";
        public long lastUsedUtcTicks;
        public List<PanelRecord> panels = new List<PanelRecord>();
    }
    [Serializable] public class RoomBookData {
        public int version = 1;
        public string activeRoomId = "";
        public List<Room> rooms = new List<Room>();
        public List<string> pendingErase = new List<string>();   // anchors of forgotten rooms that still need deleting from the headset
    }

    public sealed class RestorePlan {
        public readonly List<PanelRecord> Matched = new List<PanelRecord>();
        public readonly List<int> MatchedIndex = new List<int>();     // index into the `available` list, parallel to Matched
        public readonly List<PanelRecord> Missing = new List<PanelRecord>();     // window not open/approved on the Mac right now
        public readonly List<PanelRecord> Ambiguous = new List<PanelRecord>();   // more than one window matches; never guess
        public int Invalid;                                                      // corrupt records skipped
    }

    // The rooms model: named rooms, each with one anchor and the windows pinned in it. No engine types, so it is tested
    // in quest/mathtests. All validation happens here; the Unity side only moves transforms.
    public sealed class RoomBook {
        public const int MaxRooms = 12, MaxPanelsPerRoom = 8, MaxNameLength = 24, MaxTextLength = 120, MaxPending = 32;
        public const float MinWidth = 0.25f, MaxWidth = 4f, MaxDistance = 1000f;
        public readonly RoomBookData Data;
        public RoomBook() { Data = new RoomBookData(); }
        public RoomBook(RoomBookData data) { Data = data ?? new RoomBookData(); }

        public IList<Room> Rooms { get { return Data.rooms; } }
        public Room Active { get { return Find(Data.activeRoomId); } }
        public Room Find(string id) { return string.IsNullOrEmpty(id) ? null : Data.rooms.FirstOrDefault(r => r.id == id); }

        // ---- names ---------------------------------------------------------------------------------------------------
        public static string SanitizeName(string raw) {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var sb = new StringBuilder(); bool space = true;
            foreach (char c in raw) {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '\'' || c == '&') { sb.Append(c); space = false; }
                else if (char.IsWhiteSpace(c) || c == '_' || c == '.') { if (!space) { sb.Append(' '); space = true; } }
                // everything else (control characters, bidi marks, symbols, emoji) is dropped
            }
            string s = sb.ToString().Trim();
            if (s.Length > MaxNameLength) s = s.Substring(0, MaxNameLength).TrimEnd();
            return s == s.ToLowerInvariant() ? TitleCase(s) : s;       // dictated names arrive all lower-case
        }
        static string TitleCase(string s) {
            var chars = s.ToCharArray(); bool start = true;
            for (int i = 0; i < chars.Length; i++) {
                if (chars[i] == ' ') start = true;
                else if (start) { chars[i] = char.ToUpperInvariant(chars[i]); start = false; }
            }
            return new string(chars);
        }
        public string UniqueName(string name, Room except = null) {
            Func<string, bool> taken = n => Data.rooms.Any(r => r != except && string.Equals(r.name, n, StringComparison.OrdinalIgnoreCase));
            if (!taken(name)) return name;
            for (int i = 2; i < 1000; i++) {
                string suffix = " " + i;
                string candidate = (name.Length + suffix.Length > MaxNameLength ? name.Substring(0, MaxNameLength - suffix.Length).TrimEnd() : name) + suffix;
                if (!taken(candidate)) return candidate;
            }
            return name;
        }
        // Exact (case-insensitive) match, else a UNIQUE prefix ("kit" -> "Kitchen"). Ambiguous prefixes match nothing.
        public Room FindByName(string query) {
            string q = SanitizeName(query);
            if (q.Length == 0) return null;
            var exact = Data.rooms.FirstOrDefault(r => string.Equals(r.name, q, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            var prefix = Data.rooms.Where(r => r.name.StartsWith(q, StringComparison.OrdinalIgnoreCase)).ToList();
            return prefix.Count == 1 ? prefix[0] : null;
        }

        // ---- rooms ---------------------------------------------------------------------------------------------------
        static bool ValidAnchor(string id) { Guid g; return !string.IsNullOrEmpty(id) && Guid.TryParse(id, out g); }

        public Room Create(string requestedName, string anchorId, DateTime nowUtc, out string error) {
            error = null;
            if (Data.rooms.Count >= MaxRooms) { error = "Room limit reached (" + MaxRooms + "). Forget one first."; return null; }
            if (!string.IsNullOrEmpty(anchorId) && !ValidAnchor(anchorId)) { error = "Invalid anchor id."; return null; }
            if (!string.IsNullOrEmpty(anchorId) && Data.rooms.Any(r => r.anchorId == anchorId)) { error = "That anchor already belongs to a room."; return null; }
            string name = SanitizeName(requestedName);
            if (name.Length == 0) name = "Room " + (Data.rooms.Count + 1);
            var room = new Room { id = Guid.NewGuid().ToString("N"), name = UniqueName(name), anchorId = anchorId ?? "", lastUsedUtcTicks = nowUtc.Ticks };
            Data.rooms.Add(room);
            Data.activeRoomId = room.id;
            return room;
        }
        public bool Rename(Room room, string newName, out string error) {
            error = null;
            if (room == null || !Data.rooms.Contains(room)) { error = "No such room."; return false; }
            string name = SanitizeName(newName);
            if (name.Length == 0) { error = "A room name needs letters or numbers."; return false; }
            room.name = UniqueName(name, room);
            return true;
        }
        public bool SetActive(Room room, DateTime nowUtc) {
            if (room == null || !Data.rooms.Contains(room)) return false;
            Data.activeRoomId = room.id; room.lastUsedUtcTicks = nowUtc.Ticks; return true;
        }
        public void ClearActive() { Data.activeRoomId = ""; }
        // Removing a room queues its anchor for deletion (it can only be erased from the headset when localized in that room).
        public bool Delete(Room room) {
            if (room == null || !Data.rooms.Remove(room)) return false;
            if (Data.activeRoomId == room.id) Data.activeRoomId = "";
            if (ValidAnchor(room.anchorId) && !Data.pendingErase.Contains(room.anchorId) && Data.pendingErase.Count < MaxPending) Data.pendingErase.Add(room.anchorId);
            return true;
        }
        public void MarkErased(string anchorId) { Data.pendingErase.Remove(anchorId); }
        public Room Next(Room from) {
            if (Data.rooms.Count == 0) return null;
            int i = from == null ? -1 : Data.rooms.IndexOf(from);
            return Data.rooms[(i + 1) % Data.rooms.Count];
        }
        // Which room are we in? Among rooms whose anchor the headset just recognized, the most recently used one.
        public Room BestRoom(IEnumerable<string> localizedAnchorIds) {
            var set = new HashSet<string>(localizedAnchorIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return Data.rooms.Where(r => r.anchorId.Length > 0 && set.Contains(r.anchorId)).OrderByDescending(r => r.lastUsedUtcTicks).FirstOrDefault();
        }
        public IEnumerable<Room> ByRecency() { return Data.rooms.OrderByDescending(r => r.lastUsedUtcTicks); }

        // ---- pinned windows ------------------------------------------------------------------------------------------
        public static bool Finite(float f) { return !float.IsNaN(f) && !float.IsInfinity(f); }
        public static PoseData Normalize(PoseData p) {
            if (p == null) return null;
            float[] v = { p.px, p.py, p.pz, p.rx, p.ry, p.rz, p.rw };
            if (!v.All(Finite)) return null;
            if (Math.Abs(p.px) > MaxDistance || Math.Abs(p.py) > MaxDistance || Math.Abs(p.pz) > MaxDistance) return null;
            double n = Math.Sqrt(p.rx * p.rx + p.ry * p.ry + p.rz * p.rz + p.rw * p.rw);
            if (n < 0.5 || n > 1.5) return null;                      // not a rotation
            return new PoseData(p.px, p.py, p.pz, (float)(p.rx / n), (float)(p.ry / n), (float)(p.rz / n), (float)(p.rw / n));
        }
        static string Clip(string s) { s = s ?? ""; return s.Length > MaxTextLength ? s.Substring(0, MaxTextLength) : s; }
        public static bool ValidRecord(PanelRecord r) {
            return r != null && r.source != null && r.pose != null && r.source.app.Length > 0 && r.source.kind.Length > 0
                && Normalize(r.pose) != null && Finite(r.width) && r.width >= MinWidth && r.width <= MaxWidth;
        }
        public bool CanPin(Room room, out string reason) {
            reason = null;
            if (room == null || !Data.rooms.Contains(room)) reason = "There is no active room. Say \"new room\" first.";
            else if (room.anchorId.Length == 0) reason = "This room has no saved anchor, so windows can't be pinned to it.";
            return reason == null;
        }
        public bool Pin(Room room, SourceKey source, PoseData pose, float width, out string error) {
            if (!CanPin(room, out error)) return false;
            var key = new SourceKey(Clip(source == null ? "" : source.kind), Clip(source == null ? "" : source.app), Clip(source == null ? "" : source.title));
            var normalized = Normalize(pose);
            var record = new PanelRecord { source = key, pose = normalized, width = width };
            if (!ValidRecord(record)) { error = "That window's position or size isn't valid."; return false; }
            int existing = room.panels.FindIndex(p => p.source.SameAs(key));
            if (existing >= 0) { room.panels[existing] = record; return true; }
            if (room.panels.Count >= MaxPanelsPerRoom) { error = "This room already has " + MaxPanelsPerRoom + " pinned windows."; return false; }
            room.panels.Add(record); return true;
        }
        public bool Unpin(Room room, SourceKey source) {
            return room != null && source != null && room.panels.RemoveAll(p => p.source.SameAs(source)) > 0;
        }
        public PanelRecord FindPinned(Room room, SourceKey source) {
            return room == null || source == null ? null : room.panels.FirstOrDefault(p => p.source.SameAs(source));
        }

        // Decide, for each pinned record, whether exactly one currently-approved window matches.
        public RestorePlan PlanRestore(Room room, IList<SourceKey> available) {
            var plan = new RestorePlan();
            if (room == null) return plan;
            available = available ?? new List<SourceKey>();
            foreach (var rec in room.panels) {
                if (!ValidRecord(rec)) { plan.Invalid++; continue; }
                var hits = new List<int>();
                for (int i = 0; i < available.Count; i++) if (rec.source.SameAs(available[i])) hits.Add(i);
                if (hits.Count == 1) { plan.Matched.Add(rec); plan.MatchedIndex.Add(hits[0]); }
                else if (hits.Count == 0) plan.Missing.Add(rec);
                else plan.Ambiguous.Add(rec);
            }
            return plan;
        }

        // Defensive clean-up of loaded (possibly hand-edited or corrupt) data. Returns how many things were dropped or fixed.
        public int Repair() {
            int dropped = 0;
            if (Data.rooms == null) { Data.rooms = new List<Room>(); dropped++; }
            if (Data.pendingErase == null) Data.pendingErase = new List<string>();
            var seenIds = new HashSet<string>(); var seenAnchors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var keep = new List<Room>();
            foreach (var r in Data.rooms) {
                if (r == null || string.IsNullOrEmpty(r.id) || !seenIds.Add(r.id) || keep.Count >= MaxRooms) { dropped++; continue; }
                if (r.anchorId == null) r.anchorId = "";
                if (r.anchorId.Length > 0 && (!ValidAnchor(r.anchorId) || !seenAnchors.Add(r.anchorId))) { r.anchorId = ""; dropped++; }
                if (r.panels == null) r.panels = new List<PanelRecord>();
                if (r.anchorId.Length == 0 && r.panels.Count > 0) { dropped += r.panels.Count; r.panels.Clear(); }   // pins without an anchor mean nothing
                int before = r.panels.Count;
                var good = new List<PanelRecord>();
                foreach (var p in r.panels) {
                    if (!ValidRecord(p)) continue;
                    p.source = new SourceKey(Clip(p.source.kind), Clip(p.source.app), Clip(p.source.title)); p.pose = Normalize(p.pose);
                    if (good.Any(g => g.source.SameAs(p.source)) || good.Count >= MaxPanelsPerRoom) continue;
                    good.Add(p);
                }
                dropped += before - good.Count; r.panels = good;
                keep.Add(r);
            }
            Data.rooms = keep;
            // names: sanitize, then make unique IN ORDER (the first room keeps its name; later duplicates get a number)
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Data.rooms) {
                string n = SanitizeName(r.name);
                if (n.Length == 0) n = "Room";
                string candidate = n;
                for (int i = 2; !usedNames.Add(candidate) && i < 1000; i++) {
                    string suffix = " " + i;
                    candidate = (n.Length + suffix.Length > MaxNameLength ? n.Substring(0, MaxNameLength - suffix.Length).TrimEnd() : n) + suffix;
                }
                r.name = candidate;
            }
            if (Data.pendingErase.Count > 0) {
                var clean = Data.pendingErase.Where(ValidAnchor).Distinct(StringComparer.OrdinalIgnoreCase).Where(a => !seenAnchors.Contains(a)).Take(MaxPending).ToList();
                dropped += Data.pendingErase.Count - clean.Count; Data.pendingErase = clean;
            }
            if (Find(Data.activeRoomId) == null) Data.activeRoomId = "";
            return dropped;
        }
    }
}
