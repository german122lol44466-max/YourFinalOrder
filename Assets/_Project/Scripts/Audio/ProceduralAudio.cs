using UnityEngine;

namespace YourFinalOrder.Audio
{
    /// <summary>
    /// Звуки-заглушки, синтезированные в коде, пока нет настоящих ассетов.
    /// </summary>
    public static class ProceduralAudio
    {
        const int Rate = 44100;

        static AudioClip footstep, heartbeat, growl, scream;

        public static AudioClip Footstep => footstep ??= Create("Footstep", 0.16f, (t, s) =>
        {
            s.Low += 0.12f * (s.Noise() - s.Low);
            return s.Low * Mathf.Exp(-t * 28f) * 2.5f;
        });

        public static AudioClip Heartbeat => heartbeat ??= Create("Heartbeat", 0.9f, (t, s) =>
        {
            float v = Thump(t);
            if (t > 0.26f) v += 0.7f * Thump(t - 0.26f);
            return v;
        });

        public static AudioClip Growl => growl ??= Create("Growl", 3f, (t, s) =>
        {
            s.Low += 0.03f * (s.Noise() - s.Low);
            float mod = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 1.5f * t) * Mathf.Sin(2f * Mathf.PI * 0.33f * t);
            float body = Mathf.Sin(2f * Mathf.PI * (46f + 6f * Mathf.Sin(2f * Mathf.PI * 0.5f * t)) * t);
            // Плавные края, чтобы петля не щёлкала
            float edge = Mathf.Clamp01(t / 0.2f) * Mathf.Clamp01((3f - t) / 0.2f);
            return (s.Low * 4f + body * 0.35f) * mod * edge;
        });

        public static AudioClip Scream => scream ??= Create("Scream", 1.4f, (t, s) =>
        {
            float f = Mathf.Lerp(420f, 160f, t / 1.4f);
            s.Phase += f / Rate;
            float saw = 2f * (s.Phase - Mathf.Floor(s.Phase + 0.5f));
            float vib = 1f + 0.3f * Mathf.Sin(2f * Mathf.PI * 11f * t);
            float env = Mathf.Clamp01(t / 0.05f) * Mathf.Exp(-t * 1.6f);
            return (saw * 0.6f + s.Noise() * 0.35f) * vib * env;
        });

        static float Thump(float t) => Mathf.Sin(2f * Mathf.PI * 52f * t) * Mathf.Exp(-t * 16f);

        class SynthState
        {
            public float Low;
            public float Phase;
            readonly System.Random rng = new(1234);
            public float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);
        }

        static AudioClip Create(string name, float seconds, System.Func<float, SynthState, float> gen)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            var state = new SynthState();
            for (int i = 0; i < n; i++)
                data[i] = Mathf.Clamp(gen(i / (float)Rate, state), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
