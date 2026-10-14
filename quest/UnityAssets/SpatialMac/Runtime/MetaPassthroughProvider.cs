#if SPATIALMAC_META
using UnityEngine;
namespace SpatialMac {
    // Add/configure a Passthrough Building Block first. This class only switches
    // its existing compositor layer; it does not request raw camera images.
    public sealed class MetaPassthroughProvider:PassthroughProvider {
        public OVRPassthroughLayer layer;
        public override bool Available=>layer!=null;
        public override void SetVisible(bool visible){if(layer!=null)layer.hidden=!visible;}
    }
}
#endif
