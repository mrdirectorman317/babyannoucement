using UnityEngine;

namespace SpatialMac {
    public sealed class OneEuroVector3 {
        readonly OneEuroFilter x, y, z;
        public OneEuroVector3(double minCutoff = 1.5, double beta = 1.0, double derivativeCutoff = 1.0) {
            x = new OneEuroFilter(minCutoff, beta, derivativeCutoff);
            y = new OneEuroFilter(minCutoff, beta, derivativeCutoff);
            z = new OneEuroFilter(minCutoff, beta, derivativeCutoff);
        }
        public void Reset() { x.Reset(); y.Reset(); z.Reset(); }
        public Vector3 Filter(Vector3 v, float dt) {
            return new Vector3((float)x.Filter(v.x, dt), (float)y.Filter(v.y, dt), (float)z.Filter(v.z, dt));
        }
    }
}
