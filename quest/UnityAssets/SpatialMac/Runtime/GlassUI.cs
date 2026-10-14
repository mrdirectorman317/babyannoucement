using UnityEngine;

namespace SpatialMac {
    public static class GlassPalette {
        public static readonly Color Fill = new Color(0.10f, 0.11f, 0.14f, 0.58f);
        public static readonly Color FillStrong = new Color(0.07f, 0.08f, 0.11f, 0.78f);
        public static readonly Color Edge = new Color(1f, 1f, 1f, 0.34f);
        public static readonly Color Accent = new Color(0.30f, 0.58f, 1f, 0.82f);
        public static readonly Color Danger = new Color(0.95f, 0.30f, 0.30f, 0.80f);
        public static readonly Color Text = new Color(0.96f, 0.97f, 1f, 1f);
        public static readonly Color TextDim = new Color(0.78f, 0.80f, 0.88f, 0.85f);
    }

    // Animates a glass material's hover/press response. Touch() is called every frame the pointer is on it.
    public sealed class GlassGlow : MonoBehaviour {
        public Material material;
        public float baseHover, opacity = 1f;
        float hoverUntil, hover, press;
        public bool Hovered => Time.unscaledTime < hoverUntil;
        public void Touch() { hoverUntil = Time.unscaledTime + 0.15f; }
        public void Press() { press = 1f; }
        void Update() {
            if (material == null) return;
            float dt = Time.unscaledDeltaTime;
            hover = Mathf.MoveTowards(hover, Hovered ? 1f : 0f, dt * 9f);
            press = Mathf.MoveTowards(press, 0f, dt * 5f);
            material.SetFloat("_Hover", Mathf.Max(hover, baseHover));
            material.SetFloat("_Press", press);
            material.SetFloat("_Opacity", opacity);
        }
        void OnDestroy() { if (material != null) Destroy(material); }
    }

    // Marks a glass surface that must swallow pointer rays so you can't click a Mac window through a card.
    public sealed class GlassBlocker : MonoBehaviour { public Transform front; }

    // A rounded glass rectangle with optional label and collider. The root is unscaled so text is never distorted.
    public sealed class GlassItem {
        public GameObject root;
        public GlassGlow glow;
        public TextMesh label;
        public BoxCollider collider;
        public Vector2 size;
        float radius;
        Color labelColor = GlassPalette.Text;
        Transform plate;
        public void Resize(Vector2 newSize) {
            size = newSize;
            if (plate != null) plate.localScale = new Vector3(size.x, size.y, 1f);
            if (glow != null && glow.material != null) glow.material.SetVector("_Size", new Vector4(size.x, size.y, 0, 0));
            if (collider != null) collider.size = new Vector3(size.x, size.y, 0.03f);
        }
        // Make this surface block rays. `front` is the transform whose +Z points away from the viewer.
        public void MakeBlocker(Transform front) {
            if (collider == null) collider = root.AddComponent<BoxCollider>();
            Resize(size);
            var b = root.AddComponent<GlassBlocker>(); b.front = front;
        }
        public void SetLabelColor(Color c) { labelColor = c; if (label != null) label.color = c; }
        // Fade everything; colliders switch off when nearly invisible so unseen controls cannot be clicked.
        public void SetAlpha(float a) {
            if (glow != null) glow.opacity = a;
            if (label != null) { var c = labelColor; c.a *= a; label.color = c; }
            if (collider != null) collider.enabled = a > 0.3f;
        }
        public static GlassItem Create(Transform parent, string name, Vector2 size, float radius, Color fill, Color edge,
                                       string text, float lineHeight, bool collide, int queue) {
            var item = new GlassItem { size = size, radius = radius };
            item.root = new GameObject(name);
            item.root.transform.SetParent(parent, false);
            var plate = new GameObject("Plate");
            plate.transform.SetParent(item.root.transform, false);
            item.plate = plate.transform;
            plate.AddComponent<MeshFilter>().sharedMesh = GlassUI.Quad;
            var mr = plate.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var mat = GlassUI.NewGlassMaterial(fill, edge, size, radius, queue);
            mr.sharedMaterial = mat;
            item.glow = item.root.AddComponent<GlassGlow>();
            item.glow.material = mat;
            if (collide) {
                item.collider = item.root.AddComponent<BoxCollider>();
            }
            if (!string.IsNullOrEmpty(text)) {
                item.label = GlassUI.NewText(item.root.transform, text, lineHeight, GlassPalette.Text, TextAnchor.MiddleCenter, new Vector3(0, 0, -0.004f));
            }
            item.Resize(size);
            return item;
        }
    }

    public static class GlassUI {
        static Mesh quad;
        static Shader glass;
        static Font font;
        public const int QueuePlate = 2990, QueueSurface = 3000, QueueChrome = 3010, QueueText = 3100;

        // Unit quad centered on the origin, front facing -Z (the user looks along +Z at it).
        public static Mesh Quad {
            get {
                if (quad != null) return quad;
                quad = new Mesh { name = "SpatialMacQuad" };
                quad.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0) };
                quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
                quad.normals = new[] { -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward };
                quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                quad.RecalculateBounds();
                return quad;
            }
        }
        public static Shader GlassShader {
            get {
                if (glass == null) glass = Resources.Load<Shader>("SpatialMacGlass");
                return glass;
            }
        }
        public static Font Font {
            get {
                if (font != null) return font;
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return font;
            }
        }
        public static Material NewGlassMaterial(Color fill, Color edge, Vector2 size, float radius, int queue) {
            var shader = GlassShader;
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var m = new Material(shader) { renderQueue = queue };
            m.SetColor("_Color", fill); m.SetColor("_EdgeColor", edge);
            m.SetVector("_Size", new Vector4(size.x, size.y, 0, 0)); m.SetFloat("_Radius", radius);
            return m;
        }
        // lineHeight is the approximate world height (meters) of one line of text.
        public static TextMesh NewText(Transform parent, string text, float lineHeight, Color color, TextAnchor anchor, Vector3 localPos) {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            tm.font = Font;
            tm.fontSize = 64;
            tm.characterSize = lineHeight / 6.4f;   // fontSize * characterSize * 0.1 ~= line height in meters
            tm.anchor = anchor;
            tm.alignment = anchor == TextAnchor.MiddleCenter || anchor == TextAnchor.UpperCenter || anchor == TextAnchor.LowerCenter
                ? TextAlignment.Center : (anchor == TextAnchor.MiddleRight || anchor == TextAnchor.UpperRight || anchor == TextAnchor.LowerRight ? TextAlignment.Right : TextAlignment.Left);
            tm.color = color;
            tm.richText = false;
            tm.text = text ?? "";
            var r = go.GetComponent<MeshRenderer>();
            if (Font != null) { var mat = new Material(Font.material) { renderQueue = QueueText }; r.sharedMaterial = mat; }
            return tm;
        }
        // Greedy word wrap for TextMesh (which has no wrapping of its own). See TextLayout (unit-tested).
        public static string Wrap(string text, int columns, int maxLines) { return TextLayout.Wrap(text, columns, maxLines); }
        public static string Truncate(string s, int max) { return TextLayout.Truncate(s, max); }
    }
}
