using System.Text.RegularExpressions;
using UnityEngine;
namespace SpatialMac {
    public static class VoiceIntentRouter {
        // Deterministic starter, not a general AI. A transcript never executes
        // itself: the user presses "Run as command" or "Type it" after review.
        // (Free-form requests go to the ChatGPT assistant instead, which only gets a typed, validated tool list.)
        public static void Apply(string transcript,DesktopSession session) {
            string text=Regex.Replace((transcript??"").ToLowerInvariant(),@"[^a-z0-9\s]","");text=Regex.Replace(text,@"\s+"," ").Trim();
            SpatialPanel p=session.Selected;
            switch(text) {
                case "stop":case "stop sharing":session.Disconnect();return;
                case "show my room":case "passthrough":session.environment?.SetMode(WorkspaceMode.Passthrough);return;
                case "virtual":case "virtual world":session.environment?.SetMode(WorkspaceMode.Virtual);return;
                case "recenter":case "center":case "bring everything back":session.crown?.Recenter();return;
                case "dim":case "dim the room":case "dim the lights":session.dimmer?.Nudge(0.25f);return;
                case "brighter":case "lights on":case "undim":session.dimmer?.SetLevel(0);return;
                case "open safari":session.LaunchAllowedApp("com.apple.Safari");return;
                case "open finder":session.LaunchAllowedApp("com.apple.finder");return;
                case "open textedit":session.LaunchAllowedApp("com.apple.TextEdit");return;
                case "open xcode":session.LaunchAllowedApp("com.apple.dt.Xcode");return;
                case "open messages":session.LaunchAllowedApp("com.apple.MobileSMS");return;
            }
            string action=null;
            switch(text) {
                case "follow me":case "follow this":case "come with me":action="follow";break;
                case "stay here":case "lock here":action="lock_on";break;
                case "remember here":action="remember";break;
                case "unlock":action="world";break;
                case "make this bigger":case "bigger":action="bigger";break;
                case "make this smaller":case "smaller":action="smaller";break;
                case "bring this here":action="bring_here";break;
            }
            if(action==null){session.Status="No supported command matched. Nothing was executed.";return;}
            if(!ApplyPanelAction(session,p,action))session.Status="Select a panel before applying a panel command.";
            else if(action!="remember")session.Status="Applied: "+text;
        }

        // Shared by voice commands and by Mac-originated ui_command messages.
        public static bool ApplyPanelAction(DesktopSession session,SpatialPanel p,string action) {
            if(p==null||session==null)return false;
            switch(action) {
                case "follow":session.PanelAction(p,"follow");return true;
                case "world":p.SetPlacement(PlacementMode.World);break;
                case "lock":session.PanelAction(p,"lock");return true;
                case "lock_on":p.SetPlacement(PlacementMode.SessionLocked);break;
                case "remember":session.RememberPanel(p);return true;
                case "refresh":session.RefreshPanel(p);return true;
                case "close":session.ClosePanel(p);return true;
                case "bigger":p.SetWidth(p.Width*1.2f);break;
                case "smaller":p.SetWidth(p.Width/1.2f);break;
                case "bring_here":
                    p.SetPlacement(PlacementMode.World);Vector3 d=Vector3.ProjectOnPlane(session.head.forward,Vector3.up).normalized;
                    if(d.sqrMagnitude<0.01f)d=Vector3.forward;
                    p.transform.SetPositionAndRotation(session.head.position+d*1.25f,Quaternion.LookRotation(d,Vector3.up));break;
                default:return false;
            }
            session.SaveLayout();return true;
        }
    }
}
