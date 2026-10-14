using UnityEngine;

namespace SpatialMac {
    public sealed class WorkspaceButton : MonoBehaviour {
        public WorkspaceMenu menu; public string action; public SourceDTO source;
        public System.Action click;              // generic callback (toasts, assistant card, ...)
        public Transform frontReference;         // whose +Z points away from the user; defaults to the menu
        public GlassGlow glow;
        float hoverUntil;
        public Transform Front => frontReference != null ? frontReference : (menu != null ? menu.transform : transform);
        public void Touch() {
            bool was = Time.unscaledTime < hoverUntil;
            hoverUntil = Time.unscaledTime + 0.15f;
            if (!was) SpatialAudio.Play(Cue.Hover, transform.position, 0.5f);
            if (glow != null) glow.Touch();
        }
        public void Invoke() {
            if (glow != null) glow.Press();
            SpatialAudio.Play(Cue.Select, transform.position);
            if (click != null) click(); else if (menu != null) menu.Invoke(action, source);
        }
    }
}
