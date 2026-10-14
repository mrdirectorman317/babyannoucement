using System;
using UnityEngine;

namespace SpatialMac {
    public enum PlacementMode { World, SessionLocked, Follow, PersistentLocked }
    public enum HitKind { Desktop, Border, Corner, Button }

    public sealed class SpatialPanel : MonoBehaviour {
        public uint StreamID,Generation; public SourceDTO Source;
        public PlacementMode Mode {get;private set;}=PlacementMode.World;
        public float Width {get;private set;}=0.9f;
        public float Height {get;private set;}=0.50625f;
        public float Aspect {get;private set;}=16f/9f;
        public bool Locked=>Mode==PlacementMode.SessionLocked||Mode==PlacementMode.PersistentLocked;
        public bool Manipulating;
        public float SafetyOpacity=1f;
        public string AnchorID="";
        public DesktopSession Session;
        public PanelManipulator Manipulator {get;private set;}
        public ComfortFollow Follower {get;private set;}
        Transform surface,chrome;Texture2D texture;Material material;TextMesh label;
        public float LastFrameAt {get;private set;}=-100;
        public void Initialize(DesktopSession session,SourceDTO source,uint id) {
            Session=session;Source=source;StreamID=id;name="Panel "+source.app+" "+id;
            if(source.bounds!=null&&source.bounds.height>0)Aspect=(float)(source.bounds.width/source.bounds.height);
            surface=GameObject.CreatePrimitive(PrimitiveType.Quad).transform;surface.name="Desktop pixels";surface.SetParent(transform,false);
            var hit=surface.gameObject.AddComponent<PanelHit>();hit.panel=this;hit.kind=HitKind.Desktop;
            material=session.panelMaterial!=null?new Material(session.panelMaterial):new Material(Shader.Find("SpatialMac/PanelSurface"));surface.GetComponent<Renderer>().material=material;
            texture=new Texture2D(2,2,TextureFormat.RGB24,false);material.mainTexture=texture;
            chrome=new GameObject("Chrome").transform;chrome.SetParent(transform,false);
            Manipulator=gameObject.AddComponent<PanelManipulator>();Manipulator.Panel=this;
            Follower=gameObject.AddComponent<ComfortFollow>();Follower.Panel=this;Follower.Head=session.head;
            SetWidth(Width);CreateChrome();RefreshChrome();
        }
        public void SetWidth(float width) {
            Width=Mathf.Clamp(width,0.25f,4f);Height=Width/Mathf.Clamp(Aspect,0.2f,5f);
            if(surface!=null)surface.localScale=new Vector3(Width,Height,1);
            RefreshChrome();
        }
        void CreateChrome() {
            MakeZone("Top",HitKind.Border);MakeZone("Bottom",HitKind.Border);MakeZone("Left",HitKind.Border);MakeZone("Right",HitKind.Border);
            foreach(var c in new[]{new Vector2(-1,-1),new Vector2(-1,1),new Vector2(1,-1),new Vector2(1,1)}) {
                var z=MakeZone("Corner "+c,HitKind.Corner);z.corner=c;
            }
            foreach(string action in new[]{"follow","world","lock","remember","refresh","close"}){
                var z=MakeZone(action,HitKind.Button);z.action=action;
                var text=new GameObject("Label");text.transform.SetParent(z.transform,false);text.transform.localPosition=new Vector3(0,0,-0.6f);
                var tm=text.AddComponent<TextMesh>();tm.text=action;tm.anchor=TextAnchor.MiddleCenter;tm.alignment=TextAlignment.Center;
                tm.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");tm.fontSize=48;tm.characterSize=0.14f;
                tm.GetComponent<Renderer>().material=tm.font.material;
            }
            var title=new GameObject("Title");title.transform.SetParent(chrome,false);label=title.AddComponent<TextMesh>();
            label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.fontSize=48;label.characterSize=0.012f;
            label.anchor=TextAnchor.LowerLeft;label.GetComponent<Renderer>().material=label.font.material;
        }
        PanelHit MakeZone(string title,HitKind kind) {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=title;go.transform.SetParent(chrome,false);
            var z=go.AddComponent<PanelHit>();z.panel=this;z.kind=kind;return z;
        }
        void RefreshChrome() {
            if(chrome==null)return;int buttonIndex=0;
            foreach(var z in chrome.GetComponentsInChildren<PanelHit>()){
                float w=Width,h=Height;
                if(z.kind==HitKind.Border) {
                    if(z.name=="Top"||z.name=="Bottom"){z.transform.localPosition=new Vector3(0,(z.name=="Top"?1:-1)*(h/2+0.012f),0);z.transform.localScale=new Vector3(w,0.018f,0.014f);}
                    else{z.transform.localPosition=new Vector3((z.name=="Right"?1:-1)*(w/2+0.012f),0,0);z.transform.localScale=new Vector3(0.018f,h,0.014f);}
                } else if(z.kind==HitKind.Corner){z.transform.localPosition=new Vector3(z.corner.x*(w/2+0.012f),z.corner.y*(h/2+0.012f),-0.008f);z.transform.localScale=Vector3.one*0.043f;}
                else if(z.kind==HitKind.Button){float bw=Mathf.Max(0.09f,w/6f-0.007f);z.transform.localPosition=new Vector3(-w/2+bw/2+buttonIndex*(bw+0.007f),-h/2-0.065f,-0.01f);z.transform.localScale=new Vector3(bw,0.045f,0.014f);buttonIndex++;}
            }
            if(label!=null){label.transform.localPosition=new Vector3(-Width/2,Height/2+0.043f,-0.01f);label.text=Source.app+" — "+Mode;}
        }
        public void SetPlacement(PlacementMode mode) {
            if(mode!=PlacementMode.PersistentLocked&&transform.parent!=null)transform.SetParent(null,true);
            Mode=mode;RefreshChrome();
            if(mode==PlacementMode.Follow)Follower.CaptureOffset();
        }
        public void Apply(JpegFrame frame) {
            if(frame.Generation!=Generation||frame.StreamID!=StreamID)return;
            if(!ImageConversion.LoadImage(texture,frame.Jpeg,false))return;
            if(texture.width!=frame.Width||texture.height!=frame.Height){Session.Status="Decoded dimensions do not match frame metadata.";return;}
            float ratio=(float)frame.Width/frame.Height;
            if(Mathf.Abs(Aspect-ratio)>0.01f){Aspect=ratio;SetWidth(Width);}
            LastFrameAt=Time.unscaledTime;
        }
        public bool TryUV(Ray ray,out Vector2 uv) {
            uv=default;
            if(Vector3.Dot(ray.direction,transform.forward)<=0.001f)return false; // Do not click the back of a panel.
            var plane=new Plane(-transform.forward,transform.position);
            if(!plane.Raycast(ray,out float distance)||distance<0)return false;
            var p=transform.InverseTransformPoint(ray.GetPoint(distance));
            uv=new Vector2(p.x/Width+0.5f,0.5f-p.y/Height);return true;
        }
        // Static ScreenCaptureKit content need not produce fresh pixels every second.
        // Usability depends on current subscription/connection; old generations are rejected.
        public bool HasUsablePixels=>LastFrameAt>-50&&Generation>0&&Session!=null&&Session.Connected&&Session.SharingEnabled;
        public void ClearPixels(){
            LastFrameAt=-100;
            if(texture==null)return;
            texture.Reinitialize(2,2,TextureFormat.RGB24,false);
            texture.SetPixels(new[]{Color.black,Color.black,Color.black,Color.black});texture.Apply(false,false);
        }
        void Update(){if(material!=null)material.SetFloat("_Opacity",(HasUsablePixels?1f:0.45f)*SafetyOpacity);}
        void OnDestroy(){if(texture!=null)Destroy(texture);if(material!=null)Destroy(material);}
    }
}
