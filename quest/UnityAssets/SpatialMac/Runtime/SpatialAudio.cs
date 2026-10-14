using UnityEngine;

namespace SpatialMac {
    public enum Cue { Hover, Select, Open, Close, Notify, Error, Listen }

    // Tiny synthesized UI sounds, spatialized at the thing you touched. No audio assets required.
    public sealed class SpatialAudio : MonoBehaviour {
        public static SpatialAudio Instance { get; private set; }
        public float volume = 0.5f;
        AudioClip[] clips;
        AudioSource[] pool;
        int next;
        void Awake() {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            clips = new AudioClip[7];
            clips[(int)Cue.Hover] = Tone("hover", 0.04f, 0.18f, 2100);
            clips[(int)Cue.Select] = Tone("select", 0.09f, 0.35f, 900, 1500);
            clips[(int)Cue.Open] = Tone("open", 0.22f, 0.32f, 520, 780, 1170);
            clips[(int)Cue.Close] = Tone("close", 0.16f, 0.30f, 780, 520);
            clips[(int)Cue.Notify] = Tone("notify", 0.45f, 0.35f, 988, 1480, 1976);
            clips[(int)Cue.Error] = Tone("error", 0.22f, 0.30f, 220, 190);
            clips[(int)Cue.Listen] = Tone("listen", 0.14f, 0.30f, 660, 990);
            pool = new AudioSource[6];
            for (int i = 0; i < pool.Length; i++) {
                var go = new GameObject("UI audio " + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.spatialBlend = 1f; s.playOnAwake = false; s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 0.5f; s.maxDistance = 6f; s.dopplerLevel = 0f;
                pool[i] = s;
            }
        }
        void OnDestroy() { if (Instance == this) Instance = null; }
        public static void Play(Cue cue, Vector3 position, float gain = 1f) {
            if (Instance != null) Instance.PlayAt(cue, position, gain);
        }
        void PlayAt(Cue cue, Vector3 position, float gain) {
            var s = pool[next++ % pool.Length];
            s.transform.position = position;
            s.PlayOneShot(clips[(int)cue], volume * gain);
        }
        // Decaying sine "notes" played in sequence (frequencies in Hz), a soft attack to avoid clicks.
        static AudioClip Tone(string name, float seconds, float amplitude, params float[] notes) {
            const int rate = 44100;
            int n = Mathf.CeilToInt(seconds * rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) {
                float t = i / (float)n;
                float f = notes[Mathf.Min(notes.Length - 1, (int)(t * notes.Length))];
                float local = (t * notes.Length) % 1f;
                float attack = Mathf.Clamp01(i / (rate * 0.004f));
                float decay = Mathf.Exp(-4f * local) * (1f - t * 0.4f);
                data[i] = Mathf.Sin(2f * Mathf.PI * f * i / rate) * amplitude * attack * decay;
            }
            var clip = AudioClip.Create("spatialmac-" + name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
