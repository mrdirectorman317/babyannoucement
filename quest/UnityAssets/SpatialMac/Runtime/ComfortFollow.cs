using UnityEngine;
namespace SpatialMac {
    public sealed class ComfortFollow:MonoBehaviour {
        public SpatialPanel Panel;public Transform Head;
        [Range(0.08f,1f)]public float responseSeconds=0.28f;
        [Range(0f,40f)]public float yawDeadZone=15f;
        Vector3 offset;Quaternion relativeRotation;float followedYaw;
        public void CaptureOffset() {
            if(Head==null)return;
            followedYaw=Head.eulerAngles.y;Quaternion yaw=Quaternion.Euler(0,followedYaw,0);
            offset=Quaternion.Inverse(yaw)*(transform.position-Head.position);
            offset.z=Mathf.Clamp(offset.z,0.75f,3f);offset.x=Mathf.Clamp(offset.x,-2f,2f);
            relativeRotation=Quaternion.Inverse(yaw)*transform.rotation;
        }
        void LateUpdate() {
            if(Panel==null||Head==null||Panel.Mode!=PlacementMode.Follow||Panel.Manipulating)return;
            float wanted=Head.eulerAngles.y;
            if(Mathf.Abs(Mathf.DeltaAngle(followedYaw,wanted))>yawDeadZone)followedYaw=wanted;
            Quaternion yaw=Quaternion.Euler(0,followedYaw,0);
            float a=1f-Mathf.Exp(-Time.unscaledDeltaTime/Mathf.Max(0.01f,responseSeconds));
            transform.position=Vector3.Lerp(transform.position,Head.position+yaw*offset,a);
            transform.rotation=Quaternion.Slerp(transform.rotation,yaw*relativeRotation,a);
        }
    }
}
