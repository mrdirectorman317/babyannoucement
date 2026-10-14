using System.Threading.Tasks;
using UnityEngine;
namespace SpatialMac {
    public sealed class AnchorResult {public string id;public Transform root;}
    public abstract class AnchorProvider:MonoBehaviour {
        public abstract bool IsPersistent {get;}
        public abstract Task<AnchorResult> CreateAndSave(Pose pose);
        public abstract Task<Transform> Load(string id);
        public abstract Task<bool> Erase(string id);
    }
}
