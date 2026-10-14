using UnityEngine;
namespace SpatialMac {
    public static class VoiceIntentRouter {
        // Deterministic starter, not a general AI. A transcript never executes
        // itself: the user presses "Run as command" or "Type it" after review.
        // The phrase table lives in VoiceCommandParser (unit-tested). Free-form requests go to the ChatGPT assistant instead.
        public static void Apply(string transcript,DesktopSession session) {
            var cmd=VoiceCommandParser.Parse(transcript);
            switch(cmd.action) {
                case VoiceAction.Disconnect:session.Disconnect();return;
                case VoiceAction.Passthrough:session.environment?.SetMode(WorkspaceMode.Passthrough);return;
                case VoiceAction.Virtual:session.environment?.SetMode(WorkspaceMode.Virtual);return;
                case VoiceAction.Recenter:session.crown?.Recenter();return;
                case VoiceAction.Dim:session.dimmer?.Nudge(0.25f);return;
                case VoiceAction.Brighter:session.dimmer?.SetLevel(0);return;
                case VoiceAction.Launch:session.LaunchAllowedApp(cmd.arg);return;
                case VoiceAction.RoomNew:RoomsOrSay(session)?.CreateRoomHere();return;
                case VoiceAction.RoomName:RoomsOrSay(session)?.RenameActive(cmd.arg);return;
                case VoiceAction.RoomSwitch:RoomsOrSay(session)?.SwitchByName(cmd.arg);return;
                case VoiceAction.PanelAction:
                    if(!ApplyPanelAction(session,session.Selected,cmd.arg))session.Status="Select a panel before applying a panel command.";
                    return;
                default:session.Status="No supported command matched. Nothing was executed.";return;
            }
        }
        static RoomManager RoomsOrSay(DesktopSession s){if(s.rooms==null){s.Status="Rooms are not available.";return null;}return s.rooms;}

        // Shared by voice commands and by Mac-originated ui_command messages.
        // Locked windows (session lock or room pin) refuse anything that would move or resize them, whoever asks.
        public static bool ApplyPanelAction(DesktopSession session,SpatialPanel p,string action) {
            if(p==null||session==null)return false;
            switch(action) {
                case "follow":session.PanelAction(p,"follow");return true;
                case "world":session.ChangePlacement(p,PlacementMode.World);return true;
                case "lock":session.PanelAction(p,"lock");return true;
                case "lock_on":
                    if(p.Locked){session.Status=PanelRules.LockedMessage(p.Mode);return true;}   // already locked: never downgrade a pin
                    session.ChangePlacement(p,PlacementMode.SessionLocked);return true;
                case "remember":case "pin":session.RememberPanel(p);return true;
                case "unpin":session.PanelAction(p,"unpin");return true;
                case "refresh":session.RefreshPanel(p);return true;
                case "close":session.ClosePanel(p);return true;
                case "bigger":case "smaller":
                    if(!PanelRules.CanResize(p.Mode)){session.Status=PanelRules.LockedMessage(p.Mode);return true;}
                    p.SetWidth(action=="bigger"?p.Width*1.2f:p.Width/1.2f);break;
                case "bring_here":
                    if(!PanelRules.CanBringHere(p.Mode)){session.Status=PanelRules.LockedMessage(p.Mode);return true;}
                    session.ChangePlacement(p,PlacementMode.World);Vector3 d=Vector3.ProjectOnPlane(session.head.forward,Vector3.up).normalized;
                    if(d.sqrMagnitude<0.01f)d=Vector3.forward;
                    p.transform.SetPositionAndRotation(session.head.position+d*1.25f,Quaternion.LookRotation(d,Vector3.up));break;
                default:return false;
            }
            session.SaveLayout();return true;
        }
    }
}
