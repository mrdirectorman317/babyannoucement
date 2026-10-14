using System.Collections.Generic;
using UnityEngine;

namespace SpatialMac {
    // Original interaction math. No Euler-angle wrapping, no rigidbody throws.
    public sealed class PanelManipulator:MonoBehaviour {
        public SpatialPanel Panel;
        struct Hand { public Pose pose;public Ray ray; }
        readonly Dictionary<int,Hand> hands=new Dictionary<int,Hand>();
        Pose startPanel,startHand;float startWidth,startSpan;
        Vector3 startMid;Quaternion startFrame;
        bool resizing;Vector2 corner;Vector3 fixedCorner;Plane resizePlane;
        public bool Begin(int id,Pose pose,Ray ray,PanelHit hit) {
            if(!PanelRules.CanMove(Panel.Mode)||hands.ContainsKey(id)||hands.Count>=2)return false;
            if(hands.Count>0&&(resizing||hit.kind==HitKind.Corner))return false;
            hands[id]=new Hand{pose=pose,ray=ray};
            Panel.Manipulating=true;
            if(hands.Count==1) {
                resizing=hit.kind==HitKind.Corner;corner=hit.corner;
                Rebase();
                if(resizing){
                    fixedCorner=transform.position-transform.rotation*new Vector3(corner.x*Panel.Width/2,corner.y*Panel.Height/2,0);
                    resizePlane=new Plane(-transform.forward,transform.position);
                }
            } else Rebase();
            return true;
        }
        public void Move(int id,Pose pose,Ray ray) {
            if(!hands.ContainsKey(id))return;
            hands[id]=new Hand{pose=pose,ray=ray};
            if(resizing){Resize(ray);return;}
            if(hands.Count==1) {
                Quaternion delta=pose.rotation*Quaternion.Inverse(startHand.rotation);
                transform.SetPositionAndRotation(pose.position+delta*(startPanel.position-startHand.position),delta*startPanel.rotation);
            } else {
                GetPair(out Hand a,out Hand b);
                float span=Vector3.Distance(a.pose.position,b.pose.position);
                if(span<0.07f)return;
                Vector3 mid=(a.pose.position+b.pose.position)*0.5f;
                Quaternion current=PairFrame(a.pose,b.pose,startFrame);
                Quaternion delta=current*Quaternion.Inverse(startFrame);
                float scale=Mathf.Clamp(span/startSpan,0.25f/startWidth,4f/startWidth);
                Panel.SetWidth(startWidth*scale);
                transform.SetPositionAndRotation(mid+delta*(startPanel.position-startMid)*scale,delta*startPanel.rotation);
            }
        }
        public void End(int id) {
            if(!hands.Remove(id))return;
            if(hands.Count==0){Panel.Manipulating=false;resizing=false;if(Panel.Mode==PlacementMode.Follow)Panel.Follower.CaptureOffset();Panel.Session.SaveLayout();}
            else Rebase(); // Avoid a jump when the second hand lets go.
        }
        public void CancelAll(){hands.Clear();resizing=false;Panel.Manipulating=false;}
        void Rebase() {
            startPanel=new Pose(transform.position,transform.rotation);startWidth=Panel.Width;
            if(hands.Count==1){foreach(var hand in hands.Values){startHand=hand.pose;break;}}
            else {GetPair(out Hand a,out Hand b);startMid=(a.pose.position+b.pose.position)*0.5f;startSpan=Mathf.Max(0.07f,Vector3.Distance(a.pose.position,b.pose.position));startFrame=PairFrame(a.pose,b.pose,transform.rotation);}
        }
        void Resize(Ray ray) {
            if(!resizePlane.Raycast(ray,out float distance)||distance<0)return;
            Vector3 delta=ray.GetPoint(distance)-fixedCorner;
            float w0=startWidth,h0=startWidth/Panel.Aspect;
            float dx=Vector3.Dot(delta,startPanel.rotation*Vector3.right)*corner.x;
            float dy=Vector3.Dot(delta,startPanel.rotation*Vector3.up)*corner.y;
            // Least-squares projection onto the initial diagonal, aspect locked.
            float scale=Mathf.Clamp((dx*w0+dy*h0)/(w0*w0+h0*h0),0.25f/w0,4f/w0);
            Panel.SetWidth(w0*scale);
            transform.SetPositionAndRotation(fixedCorner+startPanel.rotation*new Vector3(corner.x*Panel.Width/2,corner.y*Panel.Height/2,0),startPanel.rotation);
        }
        void GetPair(out Hand a,out Hand b) {
            // Sort IDs so hand enumeration cannot flip the two-hand coordinate frame.
            int low=int.MaxValue,high=int.MinValue;
            foreach(int id in hands.Keys){low=Mathf.Min(low,id);high=Mathf.Max(high,id);}a=hands[low];b=hands[high];
        }
        static Quaternion PairFrame(Pose a,Pose b,Quaternion fallback) {
            Vector3 right=b.position-a.position;if(right.sqrMagnitude<0.0049f)return fallback;right.Normalize();
            Vector3 up=Vector3.ProjectOnPlane(a.rotation*Vector3.up+b.rotation*Vector3.up,right);
            if(up.sqrMagnitude<0.0001f)up=Vector3.ProjectOnPlane(fallback*Vector3.up,right);
            if(up.sqrMagnitude<0.0001f)return fallback;up.Normalize();
            return Quaternion.LookRotation(Vector3.Cross(right,up).normalized,up);
        }
    }
}
