using UnityEngine;

namespace SpatialMac {
    // The "Digital Crown": hold the left X button (or tap Recenter) and your windows swing back in front of you.
    // Only windows that are NOT pinned to a real-room anchor are moved; follow windows re-learn their offset.
    public sealed class RecenterCrown : MonoBehaviour {
        public DesktopSession session;
        public float holdSeconds = 0.8f;
        float heldFor; bool fired;
        public void Feed(bool pressed) {
            if (!pressed) { heldFor = 0; fired = false; return; }
            heldFor += Time.unscaledDeltaTime;
            if (!fired && heldFor >= holdSeconds) { fired = true; Recenter(); }
        }
        public void Recenter() {
            if (session == null || session.head == null) return;
            Vector3 head = session.head.position;
            Vector3 forward = Vector3.ProjectOnPlane(session.head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) return;
            forward.Normalize();
            // Circular mean of where the movable windows currently are (horizontal direction from the head).
            Vector3 sum = Vector3.zero; int count = 0;
            foreach (var p in session.Panels) {
                if (p.Mode == PlacementMode.PersistentLocked) continue;
                Vector3 d = Vector3.ProjectOnPlane(p.transform.position - head, Vector3.up);
                if (d.sqrMagnitude < 0.01f) continue;
                sum += d.normalized; count++;
            }
            if (count == 0) { session.SummonMenu(); return; }
            float delta = Vector3.SignedAngle(sum.sqrMagnitude < 0.0001f ? forward : sum.normalized, forward, Vector3.up);
            var rot = Quaternion.AngleAxis(delta, Vector3.up);
            foreach (var p in session.Panels) {
                if (p.Mode == PlacementMode.PersistentLocked) continue;
                var t = p.transform;
                t.SetPositionAndRotation(head + rot * (t.position - head), rot * t.rotation);
                if (p.Mode == PlacementMode.Follow) p.Follower.CaptureOffset();
            }
            session.SaveLayout();
            session.SummonMenu();
            SpatialAudio.Play(Cue.Open, head + forward);
            session.Status = "Recentered.";
        }
    }
}
