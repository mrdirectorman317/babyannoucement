#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace SpatialMac.Editor {
    public static class StarterSceneBuilder {
        [MenuItem("Tools/SpatialMac/Create starter scene")]
        public static void Create() {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var rig=new GameObject("REPLACE WITH ONE REAL XR RIG");
            var cameraObject=new GameObject("Desktop preview camera");cameraObject.tag="MainCamera";cameraObject.transform.SetParent(rig.transform,false);
            cameraObject.transform.localPosition=new Vector3(0,1.6f,0);var camera=cameraObject.AddComponent<Camera>();camera.nearClipPlane=0.08f;
            cameraObject.AddComponent<AudioListener>();
            var root=new GameObject("SpatialMac services");var session=root.AddComponent<DesktopSession>();session.head=camera.transform;
            var environment=root.AddComponent<WorkspaceEnvironment>();environment.view=camera;environment.session=session;session.environment=environment;
            var speech=root.AddComponent<PushToTalkMicrophone>();speech.session=session;session.microphone=speech;
            var safety=root.AddComponent<MotionSafety>();safety.session=session;
            var world=new GameObject("Virtual world — replace with your environment");environment.virtualWorld=world;
            var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="Floor";floor.transform.SetParent(world.transform);floor.transform.localScale=Vector3.one*3;
            var sun=new GameObject("Soft key light");sun.transform.SetParent(world.transform);var light=sun.AddComponent<Light>();light.type=LightType.Directional;light.intensity=0.8f;sun.transform.rotation=Quaternion.Euler(40,-25,0);
            var menuObject=new GameObject("Workspace menu");var menu=menuObject.AddComponent<WorkspaceMenu>();menu.session=session;session.menu=menu;
            for(int i=0;i<2;i++) {
                var go=new GameObject(i==0?"Left controller pointer":"Right controller pointer");go.transform.SetParent(root.transform);
                var router=go.AddComponent<PointerRouter>();router.session=session;router.pointerID=i;router.preferredDesktopPointer=i==1;
                var input=go.AddComponent<ControllerInputSource>();input.router=router;input.trackingSpace=rig.transform;input.node=i==0?UnityEngine.XR.XRNode.LeftHand:UnityEngine.XR.XRNode.RightHand;
            }
            var mouseObject=new GameObject("Editor mouse pointer");var mouseRouter=mouseObject.AddComponent<PointerRouter>();mouseRouter.session=session;mouseRouter.pointerID=99;
            var mouse=mouseObject.AddComponent<EditorMouseInput>();mouse.router=mouseRouter;mouse.view=camera;
            Directory.CreateDirectory("Assets/SpatialMac/Generated");
            const string materialPath="Assets/SpatialMac/Generated/PanelSurface.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(material==null){material=new Material(Shader.Find("SpatialMac/PanelSurface"));AssetDatabase.CreateAsset(material,materialPath);}
            session.panelMaterial=material; // Keep shader referenced for the player build.
            EditorSceneManager.SaveScene(scene,"Assets/SpatialMac/Generated/SpatialMacStarter.unity");
            PlayerSettings.companyName="SpatialMac";PlayerSettings.productName="SpatialMac";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android,"org.spatialmac.quest");
            PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android,ScriptingImplementation.IL2CPP);
            AssetDatabase.SaveAssets();Selection.activeGameObject=root;
            Debug.Log("Starter scene created. This is a DESKTOP preview rig. Follow quest/README.md to install/configure one OpenXR/Meta rig, passthrough, hands, and Android permissions. No XR capability was auto-enabled.");
        }
    }
}
#endif
