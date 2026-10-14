using System;
using System.Text;

namespace SpatialMac {
    public enum VoiceAction { None, Disconnect, Passthrough, Virtual, Recenter, Dim, Brighter, Launch, PanelAction, RoomNew, RoomName, RoomSwitch }
    public struct VoiceCommand {
        public VoiceAction action; public string arg;
        public VoiceCommand(VoiceAction a, string x = null) { action = a; arg = x; }
    }

    // Deterministic, allowlist-only transcript -> command mapping. Exact phrases, or a short fixed prefix plus a name.
    // Anything else is VoiceAction.None, so a misheard or hostile sentence can never do something unexpected.
    public static class VoiceCommandParser {
        public static string Normalize(string transcript) {
            var sb = new StringBuilder(); bool space = true;
            foreach (char c in (transcript ?? "").ToLowerInvariant()) {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) { sb.Append(c); space = false; }
                else if (char.IsWhiteSpace(c) || c == '-' || c == '\'') { if (c == '\'') continue; if (!space) { sb.Append(' '); space = true; } }
            }
            return sb.ToString().Trim();
        }
        static bool Is(string t, params string[] phrases) { foreach (var p in phrases) if (t == p) return true; return false; }

        public static VoiceCommand Parse(string transcript) {
            string t = Normalize(transcript);
            if (t.Length == 0) return new VoiceCommand(VoiceAction.None);
            if (Is(t, "stop", "stop sharing")) return new VoiceCommand(VoiceAction.Disconnect);
            if (Is(t, "show my room", "passthrough")) return new VoiceCommand(VoiceAction.Passthrough);
            if (Is(t, "virtual", "virtual world")) return new VoiceCommand(VoiceAction.Virtual);
            if (Is(t, "recenter", "center", "bring everything back")) return new VoiceCommand(VoiceAction.Recenter);
            if (Is(t, "dim", "dim the room", "dim the lights")) return new VoiceCommand(VoiceAction.Dim);
            if (Is(t, "brighter", "lights on", "undim")) return new VoiceCommand(VoiceAction.Brighter);
            switch (t) {
                case "open safari": return new VoiceCommand(VoiceAction.Launch, "com.apple.Safari");
                case "open finder": return new VoiceCommand(VoiceAction.Launch, "com.apple.finder");
                case "open textedit": return new VoiceCommand(VoiceAction.Launch, "com.apple.TextEdit");
                case "open xcode": return new VoiceCommand(VoiceAction.Launch, "com.apple.dt.Xcode");
                case "open messages": return new VoiceCommand(VoiceAction.Launch, "com.apple.MobileSMS");
            }
            if (Is(t, "follow me", "follow this", "come with me")) return new VoiceCommand(VoiceAction.PanelAction, "follow");
            if (Is(t, "stay here", "lock here")) return new VoiceCommand(VoiceAction.PanelAction, "lock_on");
            if (Is(t, "pin this", "pin this window", "pin here", "remember here", "lock this to this room", "lock this window to this room",
                      "pin this to this room", "pin this window to this room")) return new VoiceCommand(VoiceAction.PanelAction, "pin");
            if (Is(t, "unpin", "unpin this", "unpin this window")) return new VoiceCommand(VoiceAction.PanelAction, "unpin");
            if (Is(t, "unlock")) return new VoiceCommand(VoiceAction.PanelAction, "world");
            if (Is(t, "make this bigger", "bigger")) return new VoiceCommand(VoiceAction.PanelAction, "bigger");
            if (Is(t, "make this smaller", "smaller")) return new VoiceCommand(VoiceAction.PanelAction, "smaller");
            if (Is(t, "bring this here")) return new VoiceCommand(VoiceAction.PanelAction, "bring_here");
            if (Is(t, "new room", "make a new room", "make a new room here", "create a room", "create a new room", "save this room"))
                return new VoiceCommand(VoiceAction.RoomNew);
            string name;
            if (WithPrefix(t, out name, "call this room ", "name this room ", "rename this room to ", "rename this room "))
                return Short(name) ? new VoiceCommand(VoiceAction.RoomName, name) : new VoiceCommand(VoiceAction.None);
            if (WithPrefix(t, out name, "switch to room ", "go to room ", "switch to ", "go to ", "take me to ")) {
                if (name.EndsWith(" room")) name = name.Substring(0, name.Length - 5);
                return Short(name) ? new VoiceCommand(VoiceAction.RoomSwitch, name) : new VoiceCommand(VoiceAction.None);
            }
            return new VoiceCommand(VoiceAction.None);
        }
        static bool WithPrefix(string t, out string rest, params string[] prefixes) {
            foreach (var p in prefixes) if (t.StartsWith(p, StringComparison.Ordinal)) { rest = t.Substring(p.Length).Trim(); return true; }
            rest = ""; return false;
        }
        // Names are short phrases (at most 4 words); a long sentence that merely starts like a command is not a command.
        static bool Short(string s) { return s.Length > 0 && s.Split(' ').Length <= 4; }
    }
}
