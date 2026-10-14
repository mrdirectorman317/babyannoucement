using System;
using System.Linq;
using UnityEngine;

namespace SpatialMac {
    // Executes `ui_command` messages from the Mac (Siri/Shortcuts, the ChatGPT assistant). A strict allowlist:
    // each command only changes headset UI state, and every argument is validated again here.
    public static class UICommandRouter {
        public static void Apply(Message m, DesktopSession s) {
            if (m == null || s == null) return;
            switch (m.command) {
                case "set_environment":
                    if (m.arg == "passthrough") s.environment?.SetMode(WorkspaceMode.Passthrough);
                    else if (m.arg == "virtual") s.environment?.SetMode(WorkspaceMode.Virtual);
                    break;
                case "open_panel": OpenByName(s, m.arg); break;
                case "panel_action":
                    if (!VoiceIntentRouter.ApplyPanelAction(s, s.Selected, m.arg)) s.Status = "Select a window first, then try again.";
                    break;
                case "set_dim":
                    if (!double.IsNaN(m.value) && !double.IsInfinity(m.value)) s.dimmer?.SetLevel((float)Math.Max(0, Math.Min(1, m.value)));
                    break;
                case "recenter": s.crown?.Recenter(); break;
                default: s.Status = "Ignored an unknown command from the Mac."; break;
            }
        }
        static void OpenByName(DesktopSession s, string query) {
            if (string.IsNullOrWhiteSpace(query)) return;
            string q = query.Trim().ToLowerInvariant();
            var matches = s.Sources.Where(x => (x.app ?? "").ToLowerInvariant().Contains(q) || (x.title ?? "").ToLowerInvariant().Contains(q)).ToArray();
            if (matches.Length == 1) s.OpenPanel(matches[0]);
            else s.Status = matches.Length == 0 ? "No approved window matches “" + query + "”." : matches.Length + " windows match “" + query + "”. Pick one from the menu.";
        }
    }
}
