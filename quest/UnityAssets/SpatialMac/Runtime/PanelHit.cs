using UnityEngine;

namespace SpatialMac {
    public class PanelHit : MonoBehaviour {
        public SpatialPanel panel; public HitKind kind; public Vector2 corner; public string action;
        public GlassGlow glow;          // optional: lights up while hovered
        float hoverUntil;
        public bool Hovered => Time.unscaledTime < hoverUntil;
        // Called by PointerRouter every frame a ray is on this target.
        public void Touch() {
            bool wasHovered = Hovered;
            hoverUntil = Time.unscaledTime + 0.15f;
            if (!wasHovered && kind != HitKind.Desktop) SpatialAudio.Play(Cue.Hover, transform.position, 0.5f);
            if (glow != null) glow.Touch();
            if (panel != null) panel.NotifyHover();
        }
        public void Press() {
            if (glow != null) glow.Press();
            SpatialAudio.Play(Cue.Select, transform.position);
        }
    }
}
