using System;
using UnityEngine;

namespace Gameplay.Juice
{
    /// <summary>
    /// Placeholder sound effects synthesised in code (no audio files): bell, chimes, thuds, ticks, wind and skid loops.
    /// One-shots play in 2D through a small pool of AudioSources so overlapping sounds can have their own pitch.
    /// </summary>
    public static class JuiceAudio
    {
        private const int Rate = 44100;
        private const int PoolSize = 6;

        private static AudioSource[] _pool;
        private static int _next;
        private static readonly System.Random Noise = new(12345);

        public static readonly AudioClip Ding = Make("Ding", 1.2f, t =>
            (Sine(1568f, t) * 0.6f + Sine(2352f, t) * 0.3f + Sine(3136f, t) * 0.1f) * Mathf.Exp(-3f * t));

        public static readonly AudioClip Pickup = Make("Pickup", 0.35f, t =>
        {
            float local = t < 0.1f ? t : t - 0.1f;
            return Sine(t < 0.1f ? 880f : 1320f, t) * Mathf.Exp(-9f * local) * 0.6f;
        });

        public static readonly AudioClip Fail = Make("Fail", 0.6f, t =>
            Mathf.Sign(Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(330f, 165f, t / 0.6f) * t)) * 0.25f * Mathf.Exp(-3f * t));

        public static readonly AudioClip Thud = Make("Thud", 0.45f, LowPass(0.08f, t =>
            Sine(80f - 50f * t, t) * Mathf.Exp(-9f * t) + White() * 0.8f * Mathf.Exp(-18f * t)));

        public static readonly AudioClip Bonk = Make("Bonk", 0.2f, t =>
            Sine(520f * (1f - 1.5f * t), t) * Mathf.Exp(-22f * t) * 0.8f);

        public static readonly AudioClip Tick = Make("Tick", 0.008f, t => White() * Mathf.Exp(-600f * t) * 0.8f);

        public static readonly AudioClip Whoosh = Make("Whoosh", 0.5f, LowPass(0.15f, t =>
            White() * Mathf.Sin(Mathf.PI * t / 0.5f) * 0.8f));

        /// <summary>Looping wind noise, for speed.</summary>
        public static readonly AudioClip WindLoop = Make("Wind", 2f, LowPass(0.03f, _ => White() * 3f));

        /// <summary>Looping tyre hiss, for skids.</summary>
        public static readonly AudioClip SkidLoop = Make("Skid", 1f, HighPass(0.6f, LowPass(0.5f, _ => White() * 0.6f)));

        /// <summary>Plays a sound once (2D). <paramref name="pitch"/> 1 = normal; small random changes keep repeats from sounding robotic.</summary>
        public static void Play(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (!clip || volume <= 0f)
                return;

            if (_pool == null || !_pool[0])
            {
                var host = new GameObject("Juice Audio");
                UnityEngine.Object.DontDestroyOnLoad(host);
                _pool = new AudioSource[PoolSize];
                for (int i = 0; i < PoolSize; i++)
                {
                    _pool[i] = host.AddComponent<AudioSource>();
                    _pool[i].playOnAwake = false;
                    _pool[i].spatialBlend = 0f;
                }
            }

            AudioSource source = _pool[_next];
            _next = (_next + 1) % PoolSize;
            source.pitch = pitch;
            source.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        /// <summary>Adds a looping 2D AudioSource to <paramref name="host"/>, playing silently until its volume is raised.</summary>
        public static AudioSource CreateLoop(GameObject host, AudioClip clip)
        {
            var source = host.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.volume = 0f;
            source.spatialBlend = 0f;
            source.playOnAwake = false;
            source.Play();
            return source;
        }

        #region Synthesis

        private static AudioClip Make(string name, float seconds, Func<float, float> sample)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(seconds * Rate));
            var data = new float[count];
            for (int i = 0; i < count; i++)
                data[i] = Mathf.Clamp(sample(i / (float)Rate), -1f, 1f);

            AudioClip clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Sine(float frequency, float t) => Mathf.Sin(2f * Mathf.PI * frequency * t);
        private static float White() => (float)(Noise.NextDouble() * 2.0 - 1.0);

        /// <summary>One-pole low-pass filter (smaller alpha = darker).</summary>
        private static Func<float, float> LowPass(float alpha, Func<float, float> source)
        {
            float state = 0f;
            return t => state += alpha * (source(t) - state);
        }

        private static Func<float, float> HighPass(float alpha, Func<float, float> source)
        {
            float low = 0f;
            return t =>
            {
                float x = source(t);
                low += (1f - alpha) * (x - low);
                return x - low;
            };
        }

        #endregion
    }
}
