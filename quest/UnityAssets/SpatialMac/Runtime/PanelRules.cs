namespace SpatialMac {
    public enum PlacementMode { World, SessionLocked, Follow, PersistentLocked }

    // The one place that decides what a window in each placement mode may do. Everything that moves, resizes, recenters
    // or re-places a window asks here, so "locked" means the same thing for a hand, a voice command and the assistant.
    //   World            free window you placed; movable
    //   Follow           trails you; movable (you can re-place it)
    //   SessionLocked    "Lock": frozen for this session only (not saved)
    //   PersistentLocked "Pinned to room": frozen AND part of a saved room, restored at the same spot
    public static class PanelRules {
        public static bool IsLocked(PlacementMode m) { return m == PlacementMode.SessionLocked || m == PlacementMode.PersistentLocked; }
        public static bool CanMove(PlacementMode m) { return !IsLocked(m); }
        public static bool CanResize(PlacementMode m) { return !IsLocked(m); }
        public static bool CanBringHere(PlacementMode m) { return !IsLocked(m); }
        public static bool CanRecenter(PlacementMode m) { return !IsLocked(m); }
        public static bool IsRoomBound(PlacementMode m) { return m == PlacementMode.PersistentLocked; }
        // Leaving PersistentLocked must also drop the saved record, or a "zombie pin" returns next session.
        public static bool LeavingRoomBinding(PlacementMode from, PlacementMode to) { return from == PlacementMode.PersistentLocked && to != PlacementMode.PersistentLocked; }
        public static string LockedMessage(PlacementMode m) {
            return m == PlacementMode.PersistentLocked ? "That window is pinned to this room. Unpin it first."
                 : m == PlacementMode.SessionLocked ? "That window is locked. Unlock it first." : "";
        }
    }
}
