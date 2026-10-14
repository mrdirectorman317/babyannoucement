using System.Collections.Generic;
using UnityEngine;

namespace SpatialMac {
    // One glass card for everything conversational: listening, "you said...", ChatGPT thinking/replying,
    // "confirm this action", and composing a message reply. It decides what to show from DesktopSession state.
    public sealed class AssistantPanel : MonoBehaviour {
        public DesktopSession session;
        enum Mode { Hidden, Listening, Transcript, Thinking, Reply, Confirm, Compose }
        Mode mode = Mode.Hidden;
        string signature = "";
        GameObject card;
        readonly List<GameObject> built = new List<GameObject>();
        Vector3 anchorPos; Quaternion anchorRot;
        float shownAt;
        GlassItem plate;

        Mode Decide() {
            if (session.PendingConfirm != null) return Mode.Confirm;
            if (!string.IsNullOrEmpty(session.ReplyTargetID)) return Mode.Compose;
            if (session.microphone != null && session.microphone.IsRecording) return Mode.Listening;
            if (session.AssistantThinking) return Mode.Thinking;
            if (session.TranscriptFresh && !string.IsNullOrWhiteSpace(session.Transcript)) return Mode.Transcript;
            if (!string.IsNullOrEmpty(session.AssistantText)) return Mode.Reply;
            return Mode.Hidden;
        }
        void Update() {
            if (session == null || session.head == null) return;
            var next = Decide();
            string sig = next + "|" + session.Transcript + "|" + session.AssistantText + "|" + (session.PendingConfirm != null ? session.PendingConfirm.confirmID : "")
                         + "|" + session.ReplyTargetID + "|" + session.AssistantEnabled;
            if (next != mode || sig != signature) {
                bool appearing = mode == Mode.Hidden && next != Mode.Hidden;
                mode = next; signature = sig;
                if (appearing) Place();
                Rebuild();
                if (next == Mode.Listening) SpatialAudio.Play(Cue.Listen, anchorPos);
            }
            if (card != null) {
                float a = Mathf.Clamp01((Time.unscaledTime - shownAt) / 0.2f);
                card.transform.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, a);
            }
        }
        void Place() {
            Vector3 f = Vector3.ProjectOnPlane(session.head.forward, Vector3.up);
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            f.Normalize();
            anchorPos = session.head.position + f * 1.0f + Vector3.down * 0.06f;
            anchorRot = Quaternion.LookRotation(f, Vector3.up);
        }
        void Clear() {
            foreach (var g in built) if (g != null) Destroy(g);
            built.Clear();
            if (card != null) Destroy(card);
            card = null;
        }
        void Rebuild() {
            Clear();
            if (mode == Mode.Hidden) return;
            shownAt = Time.unscaledTime;
            string header, body; var buttons = new List<(string label, System.Action act, bool accent)>();
            switch (mode) {
                case Mode.Listening:
                    header = "Listening…"; body = "Speak now. Tap Done when you finish (12 seconds max).";
                    buttons.Add(("Done", () => session.ToggleSpeech(), true)); break;
                case Mode.Transcript:
                    header = "You said"; body = "“" + session.Transcript + "”";
                    if (session.AssistantEnabled) buttons.Add(("Ask ChatGPT", () => session.AskAssistantWithTranscript(), true));
                    buttons.Add(("Run as command", () => { session.ConfirmVoiceCommand(); session.DismissAssistant(); }, !session.AssistantEnabled));
                    buttons.Add(("Type it", () => { session.TypeTranscript(); session.DismissAssistant(); }, false));
                    buttons.Add(("Discard", () => session.DismissAssistant(), false)); break;
                case Mode.Thinking:
                    header = "ChatGPT"; body = "Thinking…";
                    buttons.Add(("Dismiss", () => session.DismissAssistant(), false)); break;
                case Mode.Reply:
                    header = "ChatGPT"; body = session.AssistantText;
                    buttons.Add(("Ask again", () => { session.DismissAssistant(); session.ToggleSpeech(); }, true));
                    buttons.Add(("Close", () => session.DismissAssistant(), false)); break;
                case Mode.Confirm:
                    header = string.IsNullOrEmpty(session.PendingConfirm.title) ? "Confirm action" : session.PendingConfirm.title;
                    body = session.PendingConfirm.body;
                    buttons.Add(("Approve", () => session.Confirm(true), true));
                    buttons.Add(("Deny", () => session.Confirm(false), false)); break;
                default: // Compose
                    header = "Reply to " + GlassUI.Truncate(session.ReplyTargetName, 28);
                    body = string.IsNullOrWhiteSpace(session.Transcript) ? "Tap Dictate and say your reply. You will see it here before anything is sent." : "“" + session.Transcript + "”";
                    buttons.Add((session.microphone != null && session.microphone.IsRecording ? "Stop" : "Dictate", () => session.ToggleSpeech(), string.IsNullOrWhiteSpace(session.Transcript)));
                    if (!string.IsNullOrWhiteSpace(session.Transcript)) buttons.Add(("Send", () => session.SendReply(), true));
                    buttons.Add(("Cancel", () => session.CancelReply(), false)); break;
            }
            string wrapped = GlassUI.Wrap(body ?? "", 44, 9);
            int lines = 1; foreach (char c in wrapped) if (c == '\n') lines++;
            float width = 0.78f, height = 0.2f + lines * 0.034f;
            card = new GameObject("Assistant card");
            card.transform.SetPositionAndRotation(anchorPos, anchorRot);
            var fillColor = mode == Mode.Confirm ? new Color(0.14f, 0.10f, 0.06f, 0.82f) : GlassPalette.FillStrong;
            plate = GlassItem.Create(card.transform, "Plate", new Vector2(width, height), 0.05f, fillColor, GlassPalette.Edge, null, 0, false, GlassUI.QueuePlate + 5);
            plate.root.transform.localPosition = new Vector3(0, 0, 0.004f);
            float left = -width / 2 + 0.04f, top = height / 2 - 0.045f;
            GlassUI.NewText(card.transform, header, 0.032f, GlassPalette.Text, TextAnchor.MiddleLeft, new Vector3(left, top, -0.008f));
            GlassUI.NewText(card.transform, wrapped, 0.026f, GlassPalette.TextDim, TextAnchor.UpperLeft, new Vector3(left, top - 0.05f, -0.008f));
            float bw = 0.19f, gap = 0.014f, total = buttons.Count * bw + (buttons.Count - 1) * gap, y = -height / 2 + 0.05f;
            for (int i = 0; i < buttons.Count; i++) {
                var (label, act, accent) = buttons[i];
                var b = GlassItem.Create(card.transform, "Btn " + label, new Vector2(bw, 0.055f), 0.0275f, accent ? GlassPalette.Accent : GlassPalette.Fill, GlassPalette.Edge, label, 0.026f, true, GlassUI.QueueChrome);
                b.root.transform.localPosition = new Vector3(-total / 2 + bw / 2 + i * (bw + gap), y, -0.008f);
                var wb = b.root.AddComponent<WorkspaceButton>();
                wb.glow = b.glow; wb.frontReference = card.transform; var action = act; wb.click = () => action();
            }
        }
        void OnDestroy() { Clear(); }
    }
}
