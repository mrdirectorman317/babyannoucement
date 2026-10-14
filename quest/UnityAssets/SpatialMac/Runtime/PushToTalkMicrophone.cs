using System;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif
namespace SpatialMac {
    public sealed class PushToTalkMicrophone:MonoBehaviour {
        public DesktopSession session;public bool IsRecording {get;private set;}
        AudioClip clip;string device;float began;
        public void Begin() {
            if(IsRecording)return;
            if(!session.SpeechEnabled){session.Status="Enable local speech on the Mac first.";return;}
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!Permission.HasUserAuthorizedPermission(Permission.Microphone)){
                Permission.RequestUserPermission(Permission.Microphone);session.Status="Grant microphone permission, then press Speech again.";return;
            }
#endif
            if(Microphone.devices.Length==0){session.Status="No microphone available.";return;}
            device=Microphone.devices[0];clip=Microphone.Start(device,false,12,16000);
            if(clip==null){session.Status="Microphone could not start.";return;}
            IsRecording=true;began=Time.unscaledTime;session.Status="Recording (up to 12 seconds). Press Speech again to finish.";
        }
        public void StopAndSend() {
            if(!IsRecording||clip==null)return;IsRecording=false;
            int position=Microphone.GetPosition(device);
            if(position<=0&&Time.unscaledTime-began>=11.8f)position=clip.samples;
            Microphone.End(device);
            if(position<=0){Destroy(clip);clip=null;session.Status="No audio captured.";return;}
            var interleaved=new float[clip.samples*clip.channels];
            if(!clip.GetData(interleaved,0)){Destroy(clip);clip=null;session.Status="Cannot read microphone samples.";return;}
            int count=Mathf.Min(192000,Mathf.FloorToInt(position*16000f/clip.frequency));
            var bytes=new byte[8+count*2];Wire.Put32(bytes,0,16000);Wire.Put32(bytes,4,(uint)count);
            for(int i=0;i<count;i++){
                double pos=i*(double)clip.frequency/16000;int a=Mathf.Min(position-1,(int)pos),b=Mathf.Min(position-1,a+1);
                float t=(float)(pos-a),sample=0;
                for(int c=0;c<clip.channels;c++)sample+=Mathf.Lerp(interleaved[a*clip.channels+c],interleaved[b*clip.channels+c],t);
                short pcm=(short)Mathf.RoundToInt(Mathf.Clamp(sample/clip.channels,-1,1)*32767);
                bytes[8+i*2]=(byte)(pcm&255);bytes[9+i*2]=(byte)((pcm>>8)&255);
            }
            Destroy(clip);clip=null;session.SendSpeech(bytes);
        }
        void Update(){if(IsRecording&&Time.unscaledTime-began>=12)StopAndSend();}
        void OnDisable(){if(IsRecording){Microphone.End(device);IsRecording=false;}if(clip!=null){Destroy(clip);clip=null;}}
    }
}
