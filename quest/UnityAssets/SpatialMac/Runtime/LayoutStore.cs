using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
namespace SpatialMac {
    [Serializable] public class SavedPanel {
        public SourceDTO source;public Vector3 position;public Quaternion rotation;
        public float width;public PlacementMode mode;public string anchorID;
    }
    [Serializable] public class SavedLayout {public int version=1;public string sessionID;public SavedPanel[] panels;}
    public static class LayoutStore {
        static string Pathname=>Path.Combine(Application.persistentDataPath,"layout.json");
        public static void Save(IEnumerable<SpatialPanel> panels,string sessionID) {
            var save=new SavedLayout{sessionID=sessionID,panels=panels.Select(p=>new SavedPanel{
                source=p.Source,width=p.Width,mode=p.Mode,anchorID=p.AnchorID,
                position=p.Mode==PlacementMode.PersistentLocked?p.transform.localPosition:p.transform.position,
                rotation=p.Mode==PlacementMode.PersistentLocked?p.transform.localRotation:p.transform.rotation}).ToArray()};
            string temp=Pathname+".tmp";File.WriteAllText(temp,JsonUtility.ToJson(save,true));
            // Same-volume rename. A crash between delete and move can lose the
            // layout but must not create a false physical anchor. Harden with a
            // platform atomic replace + backup before general distribution.
            if(File.Exists(Pathname))File.Delete(Pathname);File.Move(temp,Pathname);
        }
        public static async Task Restore(DesktopSession session) {
            try {
                if(!File.Exists(Pathname)){session.Status="No saved layout.";return;}
                var saved=JsonUtility.FromJson<SavedLayout>(File.ReadAllText(Pathname));
                if(saved==null||saved.version!=1||saved.panels==null||saved.panels.Length>3)throw new FormatException("Unsupported layout.");
                int restored=0,skipped=0;
                foreach(var record in saved.panels) {
                    if(record.source==null||!Valid(record)){skipped++;continue;}
                    bool persistent=record.mode==PlacementMode.PersistentLocked&&!string.IsNullOrEmpty(record.anchorID);
                    if(!persistent&&record.mode!=PlacementMode.Follow&&saved.sessionID!=session.SessionID){skipped++;continue;}
                    // Never bind by old numeric window ID alone. Require an
                    // unambiguous CURRENT approved source. Title matching is
                    // conservative and needs a user-facing rebind picker later.
                    var matches=session.Sources.Where(s=>s.kind==record.source.kind&&s.app==record.source.app&&s.title==record.source.title).ToArray();
                    if(matches.Length!=1){skipped++;continue;}
                    Transform anchor=null;
                    if(persistent){
                        if(session.anchorProvider==null||!session.anchorProvider.IsPersistent){skipped++;continue;}
                        try{anchor=await session.anchorProvider.Load(record.anchorID);}catch{skipped++;continue;}
                    }
                    SpatialPanel panel=session.OpenPanel(matches[0]);if(panel==null){skipped++;continue;}
                    panel.SetWidth(record.width);
                    if(persistent){panel.transform.SetParent(anchor,false);panel.transform.localPosition=record.position;panel.transform.localRotation=record.rotation;panel.AnchorID=record.anchorID;}
                    else if(record.mode!=PlacementMode.Follow)panel.transform.SetPositionAndRotation(record.position,record.rotation);
                    panel.SetPlacement(record.mode);restored++;
                }
                session.Status=$"Restored {restored}; skipped {skipped} unlocalized, ambiguous, or session-only placements.";
            }catch(Exception e){session.Status="Layout restore failed: "+e.Message;}
        }
        static bool Valid(SavedPanel r){
            float[] values={r.width,r.position.x,r.position.y,r.position.z,r.rotation.x,r.rotation.y,r.rotation.z,r.rotation.w};
            if(values.Any(v=>float.IsNaN(v)||float.IsInfinity(v)))return false;
            float norm=r.rotation.x*r.rotation.x+r.rotation.y*r.rotation.y+r.rotation.z*r.rotation.z+r.rotation.w*r.rotation.w;
            return r.width>=0.25f&&r.width<=4&&r.position.magnitude<1000&&norm>0.9f&&norm<1.1f;
        }
    }
}
