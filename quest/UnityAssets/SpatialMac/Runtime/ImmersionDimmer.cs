using UnityEngine;

namespace SpatialMac {
    // The "immersion dial": dims the surroundings so windows pop, in passthrough or in a virtual world.
    public sealed class ImmersionDimmer : MonoBehaviour {
        public DesktopSession session;
        public float Level { get; private set; }
        float target;
        Material material;
        Transform veil;
        void Awake() {
            var shader = Resources.Load<Shader>("SpatialMacVeil");
            if (shader == null) { enabled = false; return; }
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Dimming veil";
            DestroyImmediate(sphere.GetComponent<Collider>());
            veil = sphere.transform; veil.SetParent(transform, false); veil.localScale = Vector3.one * 8f;
            material = new Material(shader);
            var r = sphere.GetComponent<MeshRenderer>();
            r.sharedMaterial = material; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            sphere.SetActive(false);
        }
        public void SetLevel(float level) { target = Mathf.Clamp(level, 0f, 0.92f); }
        public void Nudge(float delta) { SetLevel(target + delta); if (session != null) session.Status = "Dimming " + Mathf.RoundToInt(target * 100) + "%"; }
        void LateUpdate() {
            if (material == null) return;
            Level = Mathf.MoveTowards(Level, target, Time.unscaledDeltaTime * 1.2f);
            bool on = Level > 0.005f;
            if (veil.gameObject.activeSelf != on) veil.gameObject.SetActive(on);
            if (!on) return;
            if (session != null && session.head != null) veil.position = session.head.position;   // follows position only, never rotation
            material.SetFloat("_Level", Level);
        }
        void OnDestroy() { if (material != null) Destroy(material); }
    }
}
