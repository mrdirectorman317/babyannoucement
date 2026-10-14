using System.Collections.Generic;
using UnityEngine;

namespace SpatialMac {
    // iMessage-style pop-ups: glass cards that float at the top of your view and "lazy follow" your head.
    // Text comes from the Mac already sanitized, and is treated as data: it is never interpreted as a command.
    public sealed class NotificationFeed : MonoBehaviour {
        public DesktopSession session;
        public int maxVisible = 3;
        sealed class Toast {
            public GameObject root; public GlassItem plate; public GlassItem reply, dismiss;
            public Message msg; public float born, expires; public bool leaving; public float leaveAt;
            public WorkspaceButton body;
            public Vector3 position;
        }
        readonly List<Toast> toasts = new List<Toast>();
        Vector3 anchorForward = Vector3.forward;
        bool anchorReady;

        public void Show(Message m) {
            if (m == null || session == null || session.head == null) return;
            if (!anchorReady) { anchorForward = FlatForward(); anchorReady = true; }
            while (toasts.Count >= maxVisible) Remove(toasts[0]);
            var t = new Toast { msg = m, born = Time.unscaledTime };
            t.expires = t.born + (m.category == "message" ? 9f : 6f);
            t.root = new GameObject("Toast " + m.title);
            t.root.transform.SetParent(transform, false);
            bool canReply = m.replyable && session.ReplyEnabled && !string.IsNullOrEmpty(m.notificationID);
            var size = new Vector2(0.56f, 0.15f);
            t.plate = GlassItem.Create(t.root.transform, "Card", size, 0.04f, GlassPalette.FillStrong, GlassPalette.Edge, null, 0, true, GlassUI.QueueChrome);
            t.body = t.plate.root.AddComponent<WorkspaceButton>();
            t.body.glow = t.plate.glow; t.body.frontReference = t.root.transform;
            t.body.click = () => { if (canReply) session.BeginReply(m.notificationID, m.title); Remove(t); };
            float left = -size.x / 2 + 0.03f;
            string app = string.IsNullOrEmpty(m.appName) ? "SPATIALMAC" : m.appName.ToUpperInvariant();
            GlassUI.NewText(t.root.transform, app, 0.016f, GlassPalette.TextDim, TextAnchor.MiddleLeft, new Vector3(left, 0.052f, -0.01f));
            GlassUI.NewText(t.root.transform, GlassUI.Truncate(m.title, 34), 0.028f, GlassPalette.Text, TextAnchor.MiddleLeft, new Vector3(left, 0.022f, -0.01f));
            GlassUI.NewText(t.root.transform, GlassUI.Wrap(GlassUI.Truncate(m.body, 96), 40, 2), 0.021f, GlassPalette.TextDim, TextAnchor.UpperLeft, new Vector3(left, -0.002f, -0.01f));
            t.dismiss = GlassItem.Create(t.root.transform, "Dismiss", new Vector2(0.045f, 0.045f), 0.0225f, GlassPalette.Fill, GlassPalette.Edge, "×", 0.04f, true, GlassUI.QueueChrome + 5);
            t.dismiss.root.transform.localPosition = new Vector3(size.x / 2 - 0.04f, 0.045f, -0.012f);
            Wire(t.dismiss, t, () => Remove(t));
            if (canReply) {
                t.reply = GlassItem.Create(t.root.transform, "Reply", new Vector2(0.13f, 0.045f), 0.0225f, GlassPalette.Accent, GlassPalette.Edge, "Reply", 0.024f, true, GlassUI.QueueChrome + 5);
                t.reply.root.transform.localPosition = new Vector3(size.x / 2 - 0.09f, -0.045f, -0.012f);
                Wire(t.reply, t, () => { session.BeginReply(m.notificationID, m.title); Remove(t); });
            }
            t.position = TargetFor(toasts.Count);
            t.root.transform.position = t.position + Vector3.up * 0.06f;
            toasts.Add(t);
            SpatialAudio.Play(Cue.Notify, t.position);
        }
        void Wire(GlassItem item, Toast t, System.Action click) {
            var b = item.root.AddComponent<WorkspaceButton>();
            b.glow = item.glow; b.frontReference = t.root.transform; b.click = click;
        }
        Vector3 FlatForward() {
            Vector3 f = Vector3.ProjectOnPlane(session.head.forward, Vector3.up);
            return f.sqrMagnitude < 0.01f ? Vector3.forward : f.normalized;
        }
        Vector3 TargetFor(int slot) {
            return session.head.position + anchorForward * 1.15f + Vector3.up * (0.34f - slot * 0.17f);
        }
        void Remove(Toast t) {
            if (t.leaving) return;
            t.leaving = true; t.leaveAt = Time.unscaledTime;
        }
        void Update() {
            if (session == null || session.head == null || toasts.Count == 0) return;
            float dt = Time.unscaledDeltaTime;
            // lazy follow: ignore small head turns, swing smoothly when you really turn away
            Vector3 f = FlatForward();
            float angle = Vector3.Angle(anchorForward, f);
            anchorForward = Vector3.Slerp(anchorForward, f, 1f - Mathf.Exp(-dt * (angle > 25f ? 3.5f : 0.15f))).normalized;
            int slot = 0;
            for (int i = toasts.Count - 1; i >= 0; i--) {
                var t = toasts[i];
                bool hovered = t.body.Hovered || (t.dismiss.glow != null && t.dismiss.glow.Hovered) || (t.reply != null && t.reply.glow.Hovered);
                if (hovered) t.expires = Mathf.Max(t.expires, Time.unscaledTime + 3f);
                if (!t.leaving && Time.unscaledTime > t.expires) Remove(t);
                float age = Time.unscaledTime - t.born;
                float fadeIn = Mathf.Clamp01(age / 0.25f);
                float fadeOut = t.leaving ? 1f - Mathf.Clamp01((Time.unscaledTime - t.leaveAt) / 0.25f) : 1f;
                if (t.leaving && fadeOut <= 0f) { Destroy(t.root); toasts.RemoveAt(i); continue; }
                t.position = TargetFor(slot++);
                var tr = t.root.transform;
                tr.position = Vector3.Lerp(tr.position, t.position, 1f - Mathf.Exp(-dt * 8f));
                tr.rotation = Quaternion.LookRotation(anchorForward, Vector3.up);
                float a = fadeIn * fadeOut;
                tr.localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, a);
                t.plate.SetAlpha(a); t.dismiss.SetAlpha(a); if (t.reply != null) t.reply.SetAlpha(a);
                foreach (var tm in t.root.GetComponentsInChildren<TextMesh>()) { var c = tm.color; c.a = a * (tm.fontSize == 64 ? 1f : 1f); tm.color = c; }
            }
        }
    }
}
