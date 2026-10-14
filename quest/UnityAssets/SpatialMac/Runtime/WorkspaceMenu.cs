using System;
using System.Collections.Generic;
using UnityEngine;
namespace SpatialMac {

    // The "Home View": a glass window with connection state, environment controls, your approved Mac windows
    // and the assistant. Summoned with the right B button, the palm-up hand gesture, or by voice.
    public sealed class WorkspaceMenu : MonoBehaviour {
        public DesktopSession session;
        readonly List<GameObject> generated = new List<GameObject>();
        TextMesh statusText, hintText; int page; bool wasConnected;
        const float W = 1.0f, H = 0.84f;

        public void Rebuild() {
            foreach (var go in generated) { if (go != null) { go.SetActive(false); Destroy(go); } }
            generated.Clear();
            if (session == null) return;
            wasConnected = session.Connected;
            var plate = GlassItem.Create(transform, "Home glass", new Vector2(W, H), 0.06f, GlassPalette.FillStrong, GlassPalette.Edge, null, 0, false, GlassUI.QueuePlate + 5);
            plate.root.transform.localPosition = new Vector3(0, 0, 0.006f); generated.Add(plate.root);

            Text("SpatialMac", 0.044f, GlassPalette.Text, new Vector3(-0.46f, 0.355f, -0.008f));
            var pill = GlassItem.Create(transform, "Connection", new Vector2(0.24f, 0.05f), 0.025f, wasConnected ? GlassPalette.Accent : new Color(0.3f, 0.3f, 0.35f, 0.6f), GlassPalette.Edge, wasConnected ? "Connected" : "Offline", 0.024f, false, GlassUI.QueueChrome);
            pill.root.transform.localPosition = new Vector3(0.36f, 0.355f, -0.008f); generated.Add(pill.root);
            statusText = Text(session.Status, 0.02f, GlassPalette.TextDim, new Vector3(-0.46f, 0.3f, -0.008f));

            Text("ENVIRONMENT", 0.016f, GlassPalette.TextDim, new Vector3(-0.46f, 0.245f, -0.008f));
            string[,] env = { { "Real room", "passthrough" }, { "Virtual world", "virtual" }, { "Recenter", "recenter" }, { "Dim  –", "dim_down" }, { "Dim  +", "dim_up" } };
            for (int i = 0; i < 5; i++) Button(env[i, 0], env[i, 1], null, new Vector3(-0.38f + i * 0.19f, 0.19f, -0.008f), new Vector2(0.17f, 0.055f), false);

            Text("MAC WINDOWS", 0.016f, GlassPalette.TextDim, new Vector3(-0.46f, 0.12f, -0.008f));
            Button("Refresh", "sources", null, new Vector3(0.27f, 0.12f, -0.008f), new Vector2(0.15f, 0.04f), false);
            Button("More ›", "next", null, new Vector3(0.43f, 0.12f, -0.008f), new Vector2(0.15f, 0.04f), false);
            int begin = page * 5, end = Mathf.Min(begin + 5, session.Sources.Length);
            if (begin >= session.Sources.Length) { page = 0; begin = 0; end = Mathf.Min(5, session.Sources.Length); }
            for (int i = begin; i < end; i++) {
                SourceDTO source = session.Sources[i];
                string title = GlassUI.Truncate(source.app + " — " + source.title, 58);
                Button(title, "open", source, new Vector3(0, 0.062f - (i - begin) * 0.065f, -0.008f), new Vector2(0.93f, 0.055f), false, true);
            }
            if (session.Sources.Length == 0) Text(wasConnected ? "No windows approved yet. Check one on your Mac (SpatialMac Host)." : "Start the host on your Mac, then Connect.", 0.022f, GlassPalette.TextDim, new Vector3(-0.44f, 0.04f, -0.008f));

            Text("ASSISTANT", 0.016f, GlassPalette.TextDim, new Vector3(-0.46f, -0.27f, -0.008f));
            Button("Speak", "speech", null, new Vector3(-0.38f, -0.325f, -0.008f), new Vector2(0.17f, 0.055f), true);
            Button("Ask ChatGPT", "ask", null, new Vector3(-0.19f, -0.325f, -0.008f), new Vector2(0.17f, 0.055f), session.AssistantEnabled);
            Button("Restore", "restore", null, new Vector3(0f, -0.325f, -0.008f), new Vector2(0.17f, 0.055f), false);
            Button(wasConnected ? "Disconnect" : "Connect", wasConnected ? "disconnect" : "connect", null, new Vector3(0.19f, -0.325f, -0.008f), new Vector2(0.17f, 0.055f), false);
            Button("Hide", "hide", null, new Vector3(0.38f, -0.325f, -0.008f), new Vector2(0.17f, 0.055f), false);
            hintText = Text("", 0.018f, GlassPalette.TextDim, new Vector3(-0.46f, -0.385f, -0.008f));
            UpdateHint();
        }
        void UpdateHint() {
            if (hintText == null) return;
            string t = string.IsNullOrWhiteSpace(session.Transcript) ? "B button or palm-up summons this window · hold left X to recenter" : "Heard: " + session.Transcript;
            hintText.text = GlassUI.Truncate(t, 92);
        }
        void Update() {
            if (session == null) return;
            if (statusText != null) statusText.text = GlassUI.Truncate(session.Status, 84);
            if (session.Connected != wasConnected) Rebuild();
            else UpdateHint();
        }
        TextMesh Text(string text, float size, Color color, Vector3 pos) {
            var tm = GlassUI.NewText(transform, text, size, color, TextAnchor.MiddleLeft, pos);
            generated.Add(tm.gameObject); return tm;
        }
        void Button(string title, string action, SourceDTO source, Vector3 pos, Vector2 size, bool accent, bool leftAligned = false) {
            var item = GlassItem.Create(transform, title, size, size.y / 2f, accent ? GlassPalette.Accent : GlassPalette.Fill, GlassPalette.Edge, title, size.y * 0.42f, true, GlassUI.QueueChrome);
            item.root.transform.localPosition = pos; generated.Add(item.root);
            if (leftAligned && item.label != null) {
                item.label.anchor = TextAnchor.MiddleLeft; item.label.alignment = TextAlignment.Left;
                item.label.transform.localPosition = new Vector3(-size.x / 2 + 0.03f, 0, -0.004f);
            }
            var button = item.root.AddComponent<WorkspaceButton>();
            button.menu = this; button.action = action; button.source = source; button.glow = item.glow;
        }
        public void Invoke(string action, SourceDTO source) {
            switch (action) {
                case "connect": session.Connect(); break;
                case "disconnect": session.Disconnect(); break;
                case "sources": session.RefreshSources(); break;
                case "next": page++; Rebuild(); break;
                case "open": session.OpenPanel(source); break;
                case "passthrough": session.environment?.SetMode(WorkspaceMode.Passthrough); break;
                case "virtual": session.environment?.SetMode(WorkspaceMode.Virtual); break;
                case "recenter": session.crown?.Recenter(); break;
                case "dim_down": session.dimmer?.Nudge(-0.2f); break;
                case "dim_up": session.dimmer?.Nudge(0.2f); break;
                case "restore": session.RestoreLayout(); break;
                case "speech": session.ToggleSpeech(); break;
                case "ask":
                    if (!string.IsNullOrWhiteSpace(session.Transcript)) session.AskAssistantWithTranscript(); else session.ToggleSpeech();
                    break;
                case "hide": gameObject.SetActive(false); break;
            }
        }
    }
}
