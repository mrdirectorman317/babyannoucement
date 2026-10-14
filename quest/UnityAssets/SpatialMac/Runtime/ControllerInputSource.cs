using UnityEngine;
using UnityEngine.XR;
namespace SpatialMac {
    public sealed class ControllerInputSource:MonoBehaviour {
        public PointerRouter router;public Transform trackingSpace;public XRNode node=XRNode.RightHand;
        bool previousMenu;
        void OnEnable(){if(router!=null)router.haptic=Buzz;}
        void Buzz(float amplitude,float seconds){var d=InputDevices.GetDeviceAtXRNode(node);if(d.isValid)d.SendHapticImpulse(0,amplitude,seconds);}
        void Update() {
            if(router==null||trackingSpace==null)return;
            var device=InputDevices.GetDeviceAtXRNode(node);
            if(!device.isValid||!device.TryGetFeatureValue(CommonUsages.isTracked,out bool tracked)||!tracked||
               !device.TryGetFeatureValue(CommonUsages.devicePosition,out Vector3 p)||!device.TryGetFeatureValue(CommonUsages.deviceRotation,out Quaternion q)) {router.LostTracking();return;}
            device.TryGetFeatureValue(CommonUsages.secondaryButton,out bool menu);
            if(node==XRNode.RightHand&&menu&&!previousMenu)router.session.SummonMenu();previousMenu=menu;
            if(node==XRNode.LeftHand){device.TryGetFeatureValue(CommonUsages.primaryButton,out bool crownDown);router.session.crown?.Feed(crownDown);}
            device.TryGetFeatureValue(CommonUsages.triggerButton,out bool down);
            device.TryGetFeatureValue(CommonUsages.primary2DAxis,out Vector2 stick);
            var pose=new Pose(trackingSpace.TransformPoint(p),trackingSpace.rotation*q);
            // Controller and hand providers must not feed the SAME router concurrently.
            router.Feed(pose,new Ray(pose.position,pose.rotation*Vector3.forward),down,stick*Time.unscaledDeltaTime*8);
        }
    }
}
