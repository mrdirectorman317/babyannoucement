#if SPATIALMAC_XR_HANDS
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
namespace SpatialMac {
    // Enable after installing XR Hands and enabling a compatible OpenXR provider.
    // Configure trackingSpace to the rig's actual tracking-space transform, not
    // an unrelated world origin or the head camera itself.
    public sealed class HandInputSource:MonoBehaviour {
        public PointerRouter router;public Transform trackingSpace,head;public bool left;
        public float pinchEnterMeters=0.018f,pinchExitMeters=0.030f;
        XRHandSubsystem subsystem;bool pinching;float nextSearch;float palmUpFor;bool menuShown;
        readonly List<XRHandSubsystem> list=new List<XRHandSubsystem>();
        void Update() {
            if(subsystem==null||!subsystem.running){
                if(Time.unscaledTime<nextSearch)return;nextSearch=Time.unscaledTime+1;
                list.Clear();SubsystemManager.GetSubsystems(list);subsystem=list.Find(x=>x.running);
                if(subsystem==null){Lose();return;}
            }
            var hand=left?subsystem.leftHand:subsystem.rightHand;
            if(!hand.isTracked||!hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumb)||
               !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose index)||
               !hand.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose palm)){Lose();return;}
            float gap=Vector3.Distance(thumb.position,index.position);
            pinching=pinching?gap<pinchExitMeters:gap<pinchEnterMeters;
            Vector3 pinch=trackingSpace.TransformPoint((thumb.position+index.position)*0.5f);
            // Hands-only menu: hold the LEFT palm up, facing the ceiling, near your chest for half a second.
            // In XR Hands the palm pose's +Y points out of the BACK of the hand, so "palm up" means +Y points down.
            // Thresholds are untested starting values; tune on a headset.
            if(left&&router!=null&&router.session!=null){
                Vector3 backOfHand=trackingSpace.rotation*(palm.rotation*Vector3.up);
                bool palmUp=Vector3.Dot(backOfHand,Vector3.down)>0.75f&&!pinching&&Vector3.Distance(trackingSpace.TransformPoint(palm.position),head.position)<0.7f;
                palmUpFor=palmUp?palmUpFor+Time.unscaledDeltaTime:0;
                if(palmUpFor>0.5f&&!menuShown){menuShown=true;router.session.SummonMenu();}
                if(!palmUp)menuShown=false;
            }
            Quaternion rotation=trackingSpace.rotation*palm.rotation;
            Vector3 shoulder=head.position+head.right*(left?-0.16f:0.16f)-Vector3.up*0.18f;
            Vector3 direction=pinch-shoulder;if(direction.sqrMagnitude<0.01f){Lose();return;}
            router.Feed(new Pose(pinch,rotation),new Ray(pinch,direction.normalized),pinching,Vector2.zero);
        }
        void Lose(){pinching=false;if(router!=null)router.LostTracking();}
        void OnDisable(){Lose();}
    }
}
#endif
