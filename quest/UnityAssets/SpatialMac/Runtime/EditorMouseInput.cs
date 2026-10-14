#if UNITY_EDITOR
using UnityEngine;
namespace SpatialMac {
    public sealed class EditorMouseInput:MonoBehaviour {
        public PointerRouter router;public Camera view;
        void Update(){if(router==null||view==null)return;Ray ray=view.ScreenPointToRay(Input.mousePosition);
            // A mouse has no tracked wrist: this tests rays, corner resizing, and
            // desktop clicks. Test true 6-DoF rotation with a controller/hand.
            router.Feed(new Pose(ray.GetPoint(0.6f),view.transform.rotation),ray,Input.GetMouseButton(0),Input.mouseScrollDelta*0.5f);
        }
    }
}
#endif
