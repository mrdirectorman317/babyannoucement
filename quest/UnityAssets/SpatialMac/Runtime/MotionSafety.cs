using UnityEngine;
namespace SpatialMac {
    public sealed class MotionSafety:MonoBehaviour {
        public DesktopSession session;Vector3 previous;float velocity;
        void Start(){if(session.head!=null)previous=session.head.position;}
        void Update(){
            if(session.head==null)return;float dt=Mathf.Max(0.001f,Time.unscaledDeltaTime);
            float speed=(session.head.position-previous).magnitude/dt;previous=session.head.position;
            velocity=Mathf.Lerp(velocity,speed,1-Mathf.Exp(-dt/0.15f));
            bool pass=session.environment!=null&&session.environment.Mode==WorkspaceMode.Passthrough;
            float opacity=pass?Mathf.Lerp(1,0.18f,Mathf.InverseLerp(0.25f,0.9f,velocity)):1;
            foreach(var panel in session.Panels)panel.SafetyOpacity=opacity;
            // Not a collision detector or a safety guarantee. Keep system boundary enabled.
        }
    }
}
