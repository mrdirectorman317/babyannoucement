using System;
using UnityEngine;

namespace SpatialMac {
    // Reads assistant replies aloud with Android's built-in text-to-speech (works only on the headset build,
    // and only if the device has a TTS engine). Silently does nothing elsewhere. Off by default.
    public sealed class SpeechOutput : MonoBehaviour {
        public bool enabledByUser = false;
#if UNITY_ANDROID && !UNITY_EDITOR
        sealed class InitListener : AndroidJavaProxy {
            public volatile int status = -1;
            public InitListener() : base("android.speech.tts.TextToSpeech$OnInitListener") { }
            public void onInit(int s) { status = s; }
        }
        AndroidJavaObject tts; InitListener listener;
        void EnsureTts() {
            if (tts != null) return;
            try {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer")) {
                    var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    listener = new InitListener();
                    tts = new AndroidJavaObject("android.speech.tts.TextToSpeech", activity, listener);
                }
            } catch (Exception) { tts = null; }
        }
        public void Speak(string text) {
            if (!enabledByUser || string.IsNullOrWhiteSpace(text)) return;
            EnsureTts();
            if (tts == null || listener == null || listener.status != 0) return;   // 0 == TextToSpeech.SUCCESS
            try { tts.Call<int>("speak", text, 0, (AndroidJavaObject)null, "spatialmac"); } catch (Exception) { }
        }
        public void Stop() { try { if (tts != null) tts.Call<int>("stop"); } catch (Exception) { } }
        void OnDestroy() { try { if (tts != null) { tts.Call("shutdown"); tts.Dispose(); } } catch (Exception) { } }
#else
        public void Speak(string text) { }
        public void Stop() { }
#endif
    }
}
