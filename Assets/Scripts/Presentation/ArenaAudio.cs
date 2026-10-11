using Unity.Netcode;
using UnityEngine;

namespace RiftRumble
{
    /// <summary>Original synthesized cues, generated once and played through a bounded local source pool.</summary>
    public sealed class ArenaAudio : MonoBehaviour
    {
        private const int SampleRate = 22050;
        private const int VoiceCount = 10;
        private static ArenaAudio instance;
        private readonly AudioSource[] voices = new AudioSource[VoiceCount];
        private readonly AudioClip[] clips = new AudioClip[11];
        private int nextVoice;
        public static float Volume { get; set; } = .65f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; Volume = .65f; }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(this); return; }
            instance = this;
            if (!CanPlay()) return;
            for (var i = 0; i < VoiceCount; i++)
            {
                var source = new GameObject("Sound voice " + i).AddComponent<AudioSource>();
                source.transform.SetParent(transform, false);
                source.playOnAwake = false;
                source.dopplerLevel = 0;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 8f;
                source.maxDistance = 60f;
                voices[i] = source;
            }
            clips[0] = Make("Bront gauntlet", .18f, 150, 48, .45f);
            clips[1] = Make("Vex pulse", .16f, 1120, 210, .08f);
            clips[2] = Make("Pip throw", .22f, 210, 620, .12f);
            clips[3] = Make("Seismic impact", .22f, 100, 34, .55f);
            clips[4] = Make("Pulse impact", .10f, 740, 160, .4f);
            clips[5] = Make("Gravity blast", .38f, 90, 24, .8f);
            clips[6] = Make("Damage", .09f, 250, 70, .5f);
            clips[7] = Make("Gem pickup", .19f, 740, 1120, 0, true);
            clips[8] = Make("Power pickup", .23f, 390, 690, 0, true);
            clips[9] = Make("Victory", .65f, 392, 600, 0, true);
            clips[10] = Make("Defeat", .45f, 330, 130, .04f);
        }

        private static bool CanPlay()
        {
            var network = NetworkManager.Singleton;
            return !Application.isBatchMode && !(network != null && network.IsServer && !network.IsClient);
        }

        private static void Play(int clip, Vector3 position, float volume, bool spatial, float pitch = 1f)
        {
            if (!CanPlay() || Volume <= 0) return;
            if (instance == null) new GameObject("Arena audio").AddComponent<ArenaAudio>();
            if (instance.clips[clip] == null) return;
            var index = instance.nextVoice;
            for (var i = 0; i < VoiceCount; i++)
            {
                var candidate = (instance.nextVoice + i) % VoiceCount;
                if (instance.voices[candidate].isPlaying) continue;
                index = candidate;
                break;
            }
            instance.nextVoice = (index + 1) % VoiceCount;
            var source = instance.voices[index];
            source.Stop();
            source.transform.position = position;
            source.clip = instance.clips[clip];
            source.spatialBlend = spatial ? .6f : 0;
            source.volume = Mathf.Clamp01(volume * Volume);
            source.pitch = pitch;
            source.Play();
        }

        public static void PlayShot(BrawlerId character, Vector3 position, bool super)
            => Play(Mathf.Clamp((int)character, 0, 2), position, super ? .65f : .42f, true, super ? .83f : 1);
        public static void PlayImpact(BrawlerId character, Vector3 position)
            => Play(3 + Mathf.Clamp((int)character, 0, 2), position, character == BrawlerId.Pip ? .65f : .34f, true);
        public static void PlayHit(Vector3 position) => Play(6, position, .28f, true);
        public static void PlayPickup(bool power) => Play(power ? 8 : 7, Vector3.zero, .42f, false);
        public static void PlayResult(bool won) => Play(won ? 9 : 10, Vector3.zero, .7f, false);

        private static AudioClip Make(string name, float duration, float startFrequency, float endFrequency, float noise, bool notes = false)
        {
            var count = Mathf.CeilToInt(duration * SampleRate);
            var samples = new float[count];
            var random = new System.Random(name.Length * 109 + count);
            var phase = 0f;
            var filteredNoise = 0f;
            for (var i = 0; i < count; i++)
            {
                var t = (float)i / count;
                var frequency = notes ? startFrequency * Mathf.Pow(2, Mathf.Floor(t * 3) * 4f / 12f)
                    : Mathf.Lerp(startFrequency, endFrequency, t);
                phase += 2f * Mathf.PI * frequency / SampleRate;
                filteredNoise = Mathf.Lerp(filteredNoise, (float)random.NextDouble() * 2 - 1, .28f);
                var wave = Mathf.Sin(phase) * .8f + Mathf.Sin(phase * 2f) * .2f;
                var attack = Mathf.Min(1f, i / (SampleRate * .004f));
                var envelope = attack * Mathf.Pow(1 - t, notes ? 1.1f : 2.4f);
                samples[i] = (wave * (1 - noise) + filteredNoise * noise) * envelope * .6f;
            }
            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            foreach (var clip in clips) if (clip != null) Destroy(clip);
        }
    }
}
