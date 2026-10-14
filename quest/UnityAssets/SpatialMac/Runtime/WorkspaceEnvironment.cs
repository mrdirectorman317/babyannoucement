using UnityEngine;
namespace SpatialMac {
    public enum WorkspaceMode { Virtual, Passthrough }

    public sealed class WorkspaceEnvironment:MonoBehaviour {
        public Camera view;public GameObject virtualWorld;public PassthroughProvider provider;public DesktopSession session;
        public WorkspaceMode Mode {get;private set;}=WorkspaceMode.Virtual;
        public void SetMode(WorkspaceMode mode) {
            if(mode==WorkspaceMode.Passthrough&&(provider==null||!provider.Available)){
                if(session!=null)session.Status="Passthrough provider is not configured. A transparent camera alone cannot show the real room.";return;
            }
            Mode=mode;bool pass=mode==WorkspaceMode.Passthrough;
            if(virtualWorld!=null)virtualWorld.SetActive(!pass);
            if(provider!=null)provider.SetVisible(pass);
            if(view!=null){view.clearFlags=pass?CameraClearFlags.SolidColor:CameraClearFlags.Skybox;view.backgroundColor=pass?new Color(0,0,0,0):new Color(0.035f,0.04f,0.06f,1);}
        }
    }

}
