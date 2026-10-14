using System.Text.RegularExpressions;
using UnityEngine;
namespace SpatialMac {
    public static class VoiceIntentRouter {
        // Deterministic starter, not a general AI. A transcript never executes
        // itself: the user presses Apply Voice or Type Transcript after review.
        public static void Apply(string transcript,DesktopSession session) {
            string text=Regex.Replace((transcript??"").ToLowerInvariant(),@"[^a-z0-9\s]","");text=Regex.Replace(text,@"\s+"," ").Trim();
            SpatialPanel p=session.Selected;
            switch(text) {
                case "stop":case "stop sharing":session.Disconnect();return;
                case "show my room":case "passthrough":session.environment?.SetMode(WorkspaceMode.Passthrough);return;
                case "virtual":case "virtual world":session.environment?.SetMode(WorkspaceMode.Virtual);return;
                case "open safari":session.LaunchAllowedApp("com.apple.Safari");return;
                case "open finder":session.LaunchAllowedApp("com.apple.finder");return;
                case "open textedit":session.LaunchAllowedApp("com.apple.TextEdit");return;
                case "open xcode":session.LaunchAllowedApp("com.apple.dt.Xcode");return;
            }
            if(p==null){session.Status="Select a panel before applying a panel command.";return;}
            switch(text) {
                case "follow me":case "follow this":case "come with me":session.PanelAction(p,"follow");return;
                case "stay here":case "lock here":p.SetPlacement(PlacementMode.SessionLocked);break;
                case "remember here":session.RememberPanel(p);return;
                case "unlock":p.SetPlacement(PlacementMode.World);break;
                case "make this bigger":case "bigger":p.SetWidth(p.Width*1.2f);break;
                case "make this smaller":case "smaller":p.SetWidth(p.Width/1.2f);break;
                case "bring this here":
                    p.SetPlacement(PlacementMode.World);Vector3 d=Vector3.ProjectOnPlane(session.head.forward,Vector3.up).normalized;
                    if(d.sqrMagnitude<0.01f)d=Vector3.forward;
                    p.transform.SetPositionAndRotation(session.head.position+d*1.25f,Quaternion.LookRotation(d,Vector3.up));break;
                default:session.Status="No supported command matched. Nothing was executed.";return;
            }
            session.SaveLayout();session.Status="Applied: "+text;
        }
    }
}
