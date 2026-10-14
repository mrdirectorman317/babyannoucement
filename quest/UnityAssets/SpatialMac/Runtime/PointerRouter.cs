using UnityEngine;
namespace SpatialMac {
    public sealed class PointerRouter:MonoBehaviour {
        public DesktopSession session;public int pointerID;public bool preferredDesktopPointer;
        readonly RaycastHit[] hits=new RaycastHit[96];LineRenderer laser;
        bool previousPinch;SpatialPanel heldPanel,desktopDrag;Vector2 lastUV;float lastMove;
        public void Feed(Pose hand,Ray ray,bool pinching,Vector2 scroll) {
            if(session==null)return;
            if(laser==null){laser=gameObject.AddComponent<LineRenderer>();laser.positionCount=2;laser.startWidth=laser.endWidth=0.002f;laser.material=new Material(Shader.Find("Sprites/Default"));}
            laser.enabled=true;laser.SetPosition(0,ray.origin);
            int count=Physics.RaycastNonAlloc(ray,hits,10f);PanelHit target=null;WorkspaceButton menuTarget=null;float best=10f;
            for(int i=0;i<count;i++){
                if(hits[i].distance>=best)continue;
                var z=hits[i].collider.GetComponent<PanelHit>();
                var b=hits[i].collider.GetComponent<WorkspaceButton>();
                var block=hits[i].collider.GetComponent<GlassBlocker>();
                if(z!=null&&Vector3.Dot(ray.direction,z.panel.transform.forward)>0){best=hits[i].distance;target=z;menuTarget=null;}
                else if(b!=null&&Vector3.Dot(ray.direction,b.Front.forward)>0){best=hits[i].distance;menuTarget=b;target=null;}
                else if(block!=null&&block.front!=null&&Vector3.Dot(ray.direction,block.front.forward)>0){best=hits[i].distance;target=null;menuTarget=null;}
            }
            laser.SetPosition(1,ray.GetPoint(best));
            if(target!=null)target.Touch();else if(menuTarget!=null)menuTarget.Touch();
            if(pinching&&!previousPinch) {
                if(menuTarget!=null)menuTarget.Invoke();
                else if(target!=null) {
                    if(target.kind!=HitKind.Desktop)target.Press();
                    if(target.kind==HitKind.Button)session.PanelAction(target.panel,target.action);
                    else if(target.kind==HitKind.Desktop&&target.panel.HasUsablePixels&&target.panel.TryUV(ray,out Vector2 uv)&&session.TryAcquirePointer(pointerID)) {
                        lastUV=Clamp(uv);
                        if(Send(target.panel,"down",lastUV))desktopDrag=target.panel;else session.ReleasePointer(pointerID);
                    } else if(target.kind==HitKind.Border||target.kind==HitKind.Corner) {
                        session.ReleaseAll();
                        if(target.panel.Manipulator.Begin(pointerID,hand,ray,target))heldPanel=target.panel;
                    }
                }
            }
            if(pinching&&heldPanel!=null)heldPanel.Manipulator.Move(pointerID,hand,ray);
            if(desktopDrag!=null) {
                if(!session.ControlEnabled||!desktopDrag.HasUsablePixels){Release();previousPinch=pinching;return;}
                if(desktopDrag.TryUV(ray,out Vector2 uv))lastUV=Clamp(uv);
                if(Time.unscaledTime-lastMove>1f/30f){Send(desktopDrag,"move",lastUV);lastMove=Time.unscaledTime;}
            } else if(!pinching&&target!=null&&target.kind==HitKind.Desktop&&
                      (session.PointerOwner==pointerID||(session.PointerOwner<0&&preferredDesktopPointer))) {
                if(target.panel.TryUV(ray,out Vector2 uv)){
                    if(Time.unscaledTime-lastMove>1f/30f){Send(target.panel,"move",Clamp(uv));lastMove=Time.unscaledTime;}
                    if(scroll.sqrMagnitude>0.01f)session.SendInput(new Message("pointer"){streamID=target.panel.StreamID,generation=target.panel.Generation,
                        action="scroll",u=Mathf.Clamp01(uv.x),v=Mathf.Clamp01(uv.y),dx=scroll.x*20,dy=scroll.y*20},target.panel);
                }
            }
            if(!pinching&&previousPinch)Release();previousPinch=pinching;
        }
        static Vector2 Clamp(Vector2 uv)=>new Vector2(Mathf.Clamp01(uv.x),Mathf.Clamp01(uv.y));
        bool Send(SpatialPanel panel,string action,Vector2 uv)=>session.SendInput(new Message("pointer"){streamID=panel.StreamID,generation=panel.Generation,action=action,u=uv.x,v=uv.y,button=0},panel);
        public void LostTracking(){Release();previousPinch=false;if(laser!=null)laser.enabled=false;}
        void Release(){
            if(heldPanel!=null){heldPanel.Manipulator.End(pointerID);heldPanel=null;}
            if(desktopDrag!=null){Send(desktopDrag,"up",lastUV);desktopDrag=null;session.ReleasePointer(pointerID);}
        }
        void OnDisable(){LostTracking();}
        void OnDestroy(){if(laser!=null&&laser.material!=null)Destroy(laser.material);}
    }
}
