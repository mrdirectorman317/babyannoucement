using System;
using System.Collections.Generic;
using UnityEngine;
namespace SpatialMac {

    public sealed class WorkspaceMenu:MonoBehaviour {
        public DesktopSession session;
        readonly List<GameObject> generated=new List<GameObject>();TextMesh statusText;int page;
        public void Rebuild() {
            foreach(var go in generated){if(go!=null){go.SetActive(false);Destroy(go);}}generated.Clear();
            Label("SpatialMac",new Vector3(-0.44f,0.40f,0),0.023f);
            statusText=Label(session.Status,new Vector3(-0.44f,0.32f,0),0.010f);
            string[] actions={"connect","disconnect","sources","next","passthrough","virtual","restore","speech","apply voice","type transcript"};
            for(int i=0;i<actions.Length;i++)Button(actions[i],actions[i],null,new Vector3(-0.33f+(i%5)*0.17f,0.20f-(i/5)*0.08f,0),0.16f);
            int begin=page*5,end=Mathf.Min(begin+5,session.Sources.Length);
            if(begin>=session.Sources.Length){page=0;begin=0;end=Mathf.Min(5,session.Sources.Length);}
            for(int i=begin;i<end;i++){
                SourceDTO source=session.Sources[i];string title=source.app+" — "+source.title;if(title.Length>54)title=title.Substring(0,51)+"…";
                Button(title,"open",source,new Vector3(0,-0.02f-(i-begin)*0.073f,0),0.85f);
            }
            string t=string.IsNullOrWhiteSpace(session.Transcript)?"Choose an approved Mac source. B summons this menu.":"Review: "+session.Transcript;
            Label(t.Length>130?t.Substring(0,130)+"…":t,new Vector3(-0.44f,-0.45f,0),0.010f);
        }
        void Update(){if(statusText!=null){string s=session.Status;statusText.text=s.Length>100?s.Substring(0,100)+"…":s;}}
        TextMesh Label(string text,Vector3 pos,float size) {
            var go=new GameObject("Label");generated.Add(go);go.transform.SetParent(transform,false);go.transform.localPosition=pos+Vector3.back*0.018f;
            var tm=go.AddComponent<TextMesh>();tm.text=text;tm.anchor=TextAnchor.MiddleLeft;tm.fontSize=48;tm.characterSize=size;
            tm.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");tm.GetComponent<Renderer>().material=tm.font.material;return tm;
        }
        void Button(string title,string action,SourceDTO source,Vector3 pos,float width) {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);generated.Add(go);go.name=title;go.transform.SetParent(transform,false);
            go.transform.localPosition=pos;go.transform.localScale=new Vector3(width,0.058f,0.018f);
            var button=go.AddComponent<WorkspaceButton>();button.menu=this;button.action=action;button.source=source;
            Label(title,pos+Vector3.left*(width/2-0.014f),0.010f);
        }
        public void Invoke(string action,SourceDTO source) {
            switch(action){
                case "connect":session.Connect();break;case "disconnect":session.Disconnect();break;
                case "sources":session.RefreshSources();break;case "next":page++;Rebuild();break;
                case "open":session.OpenPanel(source);break;
                case "passthrough":session.environment?.SetMode(WorkspaceMode.Passthrough);break;
                case "virtual":session.environment?.SetMode(WorkspaceMode.Virtual);break;
                case "restore":session.RestoreLayout();break;case "speech":session.ToggleSpeech();break;
                case "apply voice":session.ConfirmVoiceCommand();break;case "type transcript":session.TypeTranscript();break;
            }
        }
    }
}
