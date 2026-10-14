using UnityEngine;
namespace SpatialMac {
    public sealed class WorkspaceButton:MonoBehaviour {
        public WorkspaceMenu menu;public string action;public SourceDTO source;
        public void Invoke(){menu.Invoke(action,source);}
    }
}
