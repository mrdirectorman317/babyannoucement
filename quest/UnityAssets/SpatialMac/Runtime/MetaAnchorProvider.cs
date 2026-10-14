#if SPATIALMAC_META
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
namespace SpatialMac {
    // Integration adapter, not device-tested here. Uses the documented
    // SaveAnchorAsync/EraseAnchorAsync result-based API, not the old bool API.
    // Match your installed Meta XR Core version before enabling this symbol.
    public sealed class MetaAnchorProvider:AnchorProvider {
        readonly Dictionary<string,OVRSpatialAnchor> live=new Dictionary<string,OVRSpatialAnchor>();
        public override bool IsPersistent=>true;
        public override async Task<AnchorResult> CreateAndSave(Pose pose) {
            var go=new GameObject("SpatialMac anchor");go.transform.SetPositionAndRotation(pose.position,pose.rotation);
            var anchor=go.AddComponent<OVRSpatialAnchor>();
            try {
                await WaitForLocalization(anchor);
                var result=await anchor.SaveAnchorAsync();
                if(!result.Success)throw new InvalidOperationException("Runtime rejected persistent anchor save: "+result);
                string id=anchor.Uuid.ToString();live[id]=anchor;return new AnchorResult{id=id,root=go.transform};
            }catch{Destroy(go);throw;}
        }
        public override async Task<Transform> Load(string id) {
            if(live.TryGetValue(id,out OVRSpatialAnchor existing)&&existing!=null){await WaitForLocalization(existing);return existing.transform;}
            if(!Guid.TryParse(id,out Guid uuid))throw new FormatException("Invalid anchor UUID.");
            var unbound=new List<OVRSpatialAnchor.UnboundAnchor>();
            var result=await OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[]{uuid},unbound);
            if(!result.Success||unbound.Count!=1)throw new InvalidOperationException("Anchor unavailable on this headset.");
            var item=unbound[0];
            if(!item.Localized&&!await item.LocalizeAsync())throw new InvalidOperationException("Could not relocalize the room.");
            var go=new GameObject("Restored SpatialMac anchor");var component=go.AddComponent<OVRSpatialAnchor>();
            item.BindTo(component); // Bind BEFORE the new component's Start creates another anchor.
            try{await WaitForLocalization(component);live[id]=component;return go.transform;}catch{Destroy(go);throw;}
        }
        public override async Task<bool> Erase(string id) {
            Transform root=await Load(id);var anchor=root.GetComponent<OVRSpatialAnchor>();
            var result=await anchor.EraseAnchorAsync();if(!result.Success)return false;
            live.Remove(id);Destroy(root.gameObject);return true;
        }
        static async Task WaitForLocalization(OVRSpatialAnchor anchor) {
            float deadline=Time.realtimeSinceStartup+12;
            while(anchor!=null&&(!anchor.Created||!anchor.Localized)){
                if(Time.realtimeSinceStartup>deadline)throw new TimeoutException("Anchor localization timed out.");
                await Task.Delay(25); // Continue on Unity's synchronization context.
            }
            if(anchor==null)throw new InvalidOperationException("Anchor creation failed.");
        }
    }
}
#endif
