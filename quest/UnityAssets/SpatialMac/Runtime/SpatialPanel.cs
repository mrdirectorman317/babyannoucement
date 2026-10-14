using System;
using UnityEngine;

namespace SpatialMac {
    public enum PlacementMode { World, SessionLocked, Follow, PersistentLocked }
    public enum HitKind { Desktop, Border, Corner, Button }

    // A Mac window/display shown as a floating glass window (visionOS-style):
    //  - rounded desktop pixels on a glass backing
    //  - a grab bar + close button under the window (always faintly visible)
    //  - a floating toolbar ("ornament") and corner handles that appear when you point at the window
    // Panel chrome and desktop pixels are separate hit targets, so moving a window never drags anything on the Mac.
    public sealed class SpatialPanel : MonoBehaviour {
        public uint StreamID, Generation; public SourceDTO Source;
        public PlacementMode Mode { get; private set; } = PlacementMode.World;
        public float Width { get; private set; } = 0.9f;
        public float Height { get; private set; } = 0.50625f;
        public float Aspect { get; private set; } = 16f / 9f;
        public bool Locked => Mode == PlacementMode.SessionLocked || Mode == PlacementMode.PersistentLocked;
        public bool Manipulating;
        public float SafetyOpacity = 1f;
        public string AnchorID = "";
        public DesktopSession Session;
        public PanelManipulator Manipulator { get; private set; }
        public ComfortFollow Follower { get; private set; }
        public float LastFrameAt { get; private set; } = -100;
        public string Codec { get; private set; } = "jpeg";           // what the Mac is actually sending: jpeg | h264 | hevc
        public VideoStreamDecoder Decoder;                           // owned by DesktopSession
        public int PixelWidth { get; private set; }
        public int PixelHeight { get; private set; }
        public long JpegBytes;
        bool showingVideo; string baseTitle = ""; float nextStats;

        const float Pad = 0.028f, Radius = 0.022f;
        static readonly string[] ToolActions = { "toggle_follow", "lock", "remember", "refresh" };
        Transform body, surface; Texture2D texture; Material material; PanelHit desktopHit;
        GlassItem plate, bar, close; GlassItem[] tools = new GlassItem[4]; GlassItem[] corners = new GlassItem[4];
        BoxCollider[] edges = new BoxCollider[4];
        TextMesh title, status;
        float hoverUntil, reveal, born, desktopHover;

        public bool HoverActive => Time.unscaledTime < hoverUntil;
        public void NotifyHover() { hoverUntil = Time.unscaledTime + 0.35f; }

        public void Initialize(DesktopSession session, SourceDTO source, uint id) {
            Session = session; Source = source; StreamID = id; name = "Panel " + source.app + " " + id;
            if (source.bounds != null && source.bounds.height > 0) Aspect = (float)(source.bounds.width / source.bounds.height);
            body = new GameObject("Body").transform; body.SetParent(transform, false);

            plate = GlassItem.Create(body, "Window glass", Vector2.one, Radius + Pad, GlassPalette.FillStrong, GlassPalette.Edge, null, 0, false, GlassUI.QueuePlate);

            var surfaceGO = new GameObject("Desktop pixels");
            surface = surfaceGO.transform; surface.SetParent(body, false);
            surfaceGO.AddComponent<MeshFilter>().sharedMesh = GlassUI.Quad;
            var renderer = surfaceGO.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            Material template = session.panelMaterial;
            if (template == null) { var shader = Resources.Load<Shader>("SpatialMacPanelSurface"); if (shader != null) template = new Material(shader); }
            material = template != null ? new Material(template) : new Material(Shader.Find("Sprites/Default"));
            material.renderQueue = GlassUI.QueueSurface; material.SetFloat("_Radius", Radius);
            renderer.sharedMaterial = material;
            texture = new Texture2D(2, 2, TextureFormat.RGB24, false); material.mainTexture = texture; ClearPixels();
            var box = surfaceGO.AddComponent<BoxCollider>(); box.size = new Vector3(1, 1, 0.02f);
            desktopHit = surfaceGO.AddComponent<PanelHit>(); desktopHit.panel = this; desktopHit.kind = HitKind.Desktop;

            Manipulator = gameObject.AddComponent<PanelManipulator>(); Manipulator.Panel = this;
            Follower = gameObject.AddComponent<ComfortFollow>(); Follower.Panel = this; Follower.Head = session.head;
            BuildChrome();
            SetWidth(Width); RefreshChrome();
            born = Time.unscaledTime;
            SpatialAudio.Play(Cue.Open, transform.position);
        }

        void BuildChrome() {
            bar = GlassItem.Create(body, "Grab bar", new Vector2(0.2f, 0.018f), 0.009f, new Color(1, 1, 1, 0.55f), new Color(1, 1, 1, 0.2f), null, 0, true, GlassUI.QueueChrome);
            Hook(bar, HitKind.Border, null, plate);
            close = GlassItem.Create(body, "Close", new Vector2(0.05f, 0.05f), 0.025f, GlassPalette.Fill, GlassPalette.Edge, "×", 0.045f, true, GlassUI.QueueChrome);
            Hook(close, HitKind.Button, "close", close);
            for (int i = 0; i < 4; i++) {
                tools[i] = GlassItem.Create(body, "Tool " + ToolActions[i], new Vector2(0.15f, 0.05f), 0.025f, GlassPalette.Fill, GlassPalette.Edge, ToolActions[i], 0.022f, true, GlassUI.QueueChrome);
                Hook(tools[i], HitKind.Button, ToolActions[i], tools[i]);
            }
            var signs = new[] { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(1, 1) };
            for (int i = 0; i < 4; i++) {
                corners[i] = GlassItem.Create(body, "Corner " + signs[i], new Vector2(0.04f, 0.04f), 0.02f, GlassPalette.Fill, GlassPalette.Edge, null, 0, true, GlassUI.QueueChrome);
                var hit = Hook(corners[i], HitKind.Corner, null, corners[i]); hit.corner = signs[i];
            }
            string[] edgeNames = { "Top", "Bottom", "Left", "Right" };
            for (int i = 0; i < 4; i++) {
                var go = new GameObject("Edge " + edgeNames[i]); go.transform.SetParent(body, false);
                edges[i] = go.AddComponent<BoxCollider>();
                var hit = go.AddComponent<PanelHit>(); hit.panel = this; hit.kind = HitKind.Border; hit.glow = plate.glow;
            }
            title = GlassUI.NewText(body, "", 0.026f, GlassPalette.TextDim, TextAnchor.LowerLeft, Vector3.zero);
            status = GlassUI.NewText(body, "", 0.04f, GlassPalette.TextDim, TextAnchor.MiddleCenter, new Vector3(0, 0, -0.004f));
        }

        PanelHit Hook(GlassItem item, HitKind kind, string action, GlassItem glowItem) {
            var hit = item.root.AddComponent<PanelHit>(); hit.panel = this; hit.kind = kind; hit.action = action;
            hit.glow = glowItem.glow; return hit;
        }

        public void SetWidth(float width) {
            Width = Mathf.Clamp(width, 0.25f, 4f); Height = Width / Mathf.Clamp(Aspect, 0.2f, 5f);
            if (surface == null) return;
            surface.localScale = new Vector3(Width, Height, 1);
            material.SetVector("_Size", new Vector4(Width, Height, 0, 0));
            LayoutChrome();
        }

        void LayoutChrome() {
            float w = Width, h = Height;
            plate.root.transform.localPosition = new Vector3(0, 0, 0.006f);
            plate.Resize(new Vector2(w + 2 * Pad, h + 2 * Pad));
            float e = Pad * 0.55f;
            SetEdge(0, new Vector3(0, h / 2 + e, -0.002f), new Vector3(w + Pad, Pad * 0.9f, 0.03f));
            SetEdge(1, new Vector3(0, -h / 2 - e, -0.002f), new Vector3(w + Pad, Pad * 0.9f, 0.03f));
            SetEdge(2, new Vector3(-w / 2 - e, 0, -0.002f), new Vector3(Pad * 0.9f, h, 0.03f));
            SetEdge(3, new Vector3(w / 2 + e, 0, -0.002f), new Vector3(Pad * 0.9f, h, 0.03f));
            var signs = new[] { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(1, 1) };
            for (int i = 0; i < 4; i++) {
                corners[i].root.transform.localPosition = new Vector3(signs[i].x * (w / 2 + Pad * 0.65f), signs[i].y * (h / 2 + Pad * 0.65f), -0.006f);
                corners[i].collider.size = new Vector3(0.085f, 0.085f, 0.03f);
            }
            float barW = Mathf.Clamp(w * 0.28f, 0.16f, 0.38f), barY = -(h / 2 + Pad + 0.05f);
            bar.Resize(new Vector2(barW, 0.018f)); bar.collider.size = new Vector3(barW + 0.06f, 0.06f, 0.03f);
            bar.root.transform.localPosition = new Vector3(0, barY, -0.004f);
            close.root.transform.localPosition = new Vector3(-(barW / 2 + 0.055f), barY, -0.004f); close.collider.size = new Vector3(0.075f, 0.075f, 0.03f);
            float toolW = 0.15f, gap = 0.012f, total = 4 * toolW + 3 * gap, toolY = barY - 0.078f;
            for (int i = 0; i < 4; i++) tools[i].root.transform.localPosition = new Vector3(-total / 2 + toolW / 2 + i * (toolW + gap), toolY, -0.004f);
            title.transform.localPosition = new Vector3(-w / 2, h / 2 + Pad + 0.014f, -0.004f);
        }
        void SetEdge(int i, Vector3 pos, Vector3 size) { edges[i].transform.localPosition = pos; edges[i].size = size; }

        void RefreshChrome() {
            if (title == null) return;
            string tag = Mode == PlacementMode.Follow ? "  ·  following you" : Mode == PlacementMode.SessionLocked ? "  ·  locked" : Mode == PlacementMode.PersistentLocked ? "  ·  pinned to room" : "";
            baseTitle = GlassUI.Truncate(Source.app + (string.IsNullOrEmpty(Source.title) || Source.title == "Untitled" ? "" : " — " + Source.title), 60) + tag;
            title.text = baseTitle;
            SetTool(0, Mode == PlacementMode.Follow ? "Following" : "Follow", Mode == PlacementMode.Follow);
            SetTool(1, Locked ? "Locked" : "Lock", Locked);
            SetTool(2, Mode == PlacementMode.PersistentLocked ? "Pinned" : "Pin to room", Mode == PlacementMode.PersistentLocked);
            SetTool(3, "Refresh", false);
        }
        void SetTool(int i, string text, bool active) {
            if (tools[i] == null) return;
            tools[i].label.text = text; tools[i].glow.baseHover = active ? 0.5f : 0f;
        }

        public void SetPlacement(PlacementMode mode) {
            if (mode != PlacementMode.PersistentLocked && transform.parent != null) transform.SetParent(null, true);
            Mode = mode; RefreshChrome();
            if (mode == PlacementMode.Follow) Follower.CaptureOffset();
        }

        public void SetCodec(string codec) { Codec = string.IsNullOrEmpty(codec) ? "jpeg" : codec; if (Codec == "jpeg") UseJpeg(); }
        void UseJpeg() {
            if (!showingVideo || material == null) return;
            showingVideo = false; material.SetFloat("_Mode", 0f); material.mainTexture = texture;
        }
        // Newest hardware-decoded frame: the shader converts YUV -> RGB, so no CPU pixel conversion happens here.
        public void ApplyVideo(VideoStreamDecoder d) {
            if (d == null || d.YTex == null || material == null) return;
            if (!showingVideo) { showingVideo = true; material.SetFloat("_Mode", d.Planar ? 2f : 1f); }
            material.SetTexture("_MainTex", d.YTex); material.SetTexture("_UVTex", d.UVTex); material.SetTexture("_VTex", d.VTex != null ? d.VTex : d.UVTex);
            material.SetVector("_YUVScale", d.Scale);
            PixelWidth = d.Width; PixelHeight = d.Height;
            if (d.Width > 0 && d.Height > 0) { float ratio = (float)d.Width / d.Height; if (Mathf.Abs(Aspect - ratio) > 0.01f) { Aspect = ratio; SetWidth(Width); } }
            LastFrameAt = Time.unscaledTime;
        }
        public void Apply(JpegFrame frame) {
            if (frame.Generation != Generation || frame.StreamID != StreamID) return;
            UseJpeg();
            JpegBytes += frame.Jpeg.Length;
            PixelWidth = frame.Width; PixelHeight = frame.Height;
            if (!ImageConversion.LoadImage(texture, frame.Jpeg, false)) return;
            if (texture.width != frame.Width || texture.height != frame.Height) { Session.Status = "Decoded dimensions do not match frame metadata."; return; }
            float ratio = (float)frame.Width / frame.Height;
            if (Mathf.Abs(Aspect - ratio) > 0.01f) { Aspect = ratio; SetWidth(Width); }
            LastFrameAt = Time.unscaledTime;
        }

        public bool TryUV(Ray ray, out Vector2 uv) {
            uv = default;
            if (Vector3.Dot(ray.direction, transform.forward) <= 0.001f) return false; // Do not click the back of a panel.
            var plane = new Plane(-transform.forward, transform.position);
            if (!plane.Raycast(ray, out float distance) || distance < 0) return false;
            var p = transform.InverseTransformPoint(ray.GetPoint(distance));
            uv = new Vector2(p.x / Width + 0.5f, 0.5f - p.y / Height); return true;
        }

        // Static ScreenCaptureKit content need not produce fresh pixels every second.
        // Usability depends on current subscription/connection; old generations are rejected.
        public bool HasUsablePixels => LastFrameAt > -50 && Generation > 0 && Session != null && Session.Connected && Session.SharingEnabled;
        public void ClearPixels() {
            LastFrameAt = -100;
            UseJpeg();
            if (texture == null) return;
            texture.Reinitialize(2, 2, TextureFormat.RGB24, false);
            texture.SetPixels(new[] { Color.black, Color.black, Color.black, Color.black }); texture.Apply(false, false);
        }

        void Update() {
            if (material == null || body == null) return;
            float dt = Time.unscaledDeltaTime;
            float k = Mathf.Clamp01((Time.unscaledTime - born) / 0.28f), ease = 1f - (1f - k) * (1f - k) * (1f - k);
            body.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, ease);
            bool selected = Session != null && Session.Selected == this;
            reveal = Mathf.MoveTowards(reveal, (HoverActive || selected || Manipulating) ? 1f : 0f, dt * 6f);
            float fade = ease * SafetyOpacity;
            // desktop glow follows pointer-on-pixels; chrome glow handled by GlassGlow
            desktopHover = Mathf.MoveTowards(desktopHover, desktopHit.Hovered ? 1f : 0f, dt * 8f);
            material.SetFloat("_Opacity", (HasUsablePixels ? 1f : 0.45f) * fade);
            material.SetFloat("_Hover", Mathf.Max(desktopHover * 0.6f, selected ? 0.25f : 0f));
            plate.glow.opacity = fade; plate.glow.baseHover = (selected ? 0.3f : 0f) + (Manipulating ? 0.5f : 0f);
            float always = Mathf.Lerp(0.5f, 1f, reveal) * fade;
            bar.SetAlpha(always); close.SetAlpha(always);
            for (int i = 0; i < 4; i++) { tools[i].SetAlpha(reveal * fade); corners[i].SetAlpha(reveal * fade); }
            var tc = GlassPalette.TextDim; tc.a *= Mathf.Lerp(0.55f, 1f, reveal) * fade; title.color = tc;
            if (Session != null && Session.ShowStats) {
                if (Time.unscaledTime >= nextStats) { nextStats = Time.unscaledTime + 0.5f; title.text = baseTitle + "\n" + Session.Stats.Line(this); }
            } else if (title.text != baseTitle) title.text = baseTitle;
            string s = Generation == 0 ? "Starting stream…" : (!HasUsablePixels ? "Waiting for the Mac…" : "");
            if (status.text != s) status.text = s;
            var sc = GlassPalette.TextDim; sc.a *= fade; status.color = sc;
        }
        void OnDestroy() { if (texture != null) Destroy(texture); if (material != null) Destroy(material); }
    }
}
