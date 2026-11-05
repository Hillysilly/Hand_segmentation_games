using UnityEngine;

namespace Repulsor
{
    /// <summary>Sounds synthesized at startup, like the web prototype's audio.js: no audio assets needed.</summary>
    public static class Sfx
    {
        const int SampleRate = 44100;

        /// <summary>A downward sweep: fired blast.</summary>
        public static AudioClip Zap() => Render("zap", 0.22f, (t, u, rng) =>
            Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(1500f, 260f, u) * t) * (1f - u) * 0.6f);

        /// <summary>A noise burst over a low thump: target destroyed.</summary>
        public static AudioClip Boom() => Render("boom", 0.45f, (t, u, rng) =>
        {
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Pow(1f - u, 3f);
            float thump = Mathf.Sin(2f * Mathf.PI * 70f * t) * (1f - u);
            return (noise * 0.5f + thump * 0.5f) * 0.8f;
        });

        /// <summary>Rising then falling filtered noise: shockwave leaving the hand.</summary>
        public static AudioClip Whoosh() => Render("whoosh", 0.6f, (t, u, rng) =>
        {
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float thump = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(120f, 45f, u) * t);
            return (noise * 0.25f + thump * 0.6f) * Mathf.Sin(Mathf.PI * u);
        });

        /// <summary>A short tick: weapon switched.</summary>
        public static AudioClip Click() => Render("click", 0.06f, (t, u, rng) =>
            Mathf.Sin(2f * Mathf.PI * 1800f * t) * (1f - u) * 0.4f);

        /// <summary>A crack and a falling sweep: laser switching on.</summary>
        public static AudioClip Ignite() => Render("ignite", 0.4f, (t, u, rng) =>
        {
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Pow(1f - u, 6f);
            float sweep = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(700f, 90f, u) * t) * Mathf.Pow(1f - u, 2f);
            return noise * 0.5f + sweep * 0.5f;
        });

        /// <summary>A deep throbbing roar for the laser. Whole cycles only, so it loops without a click.</summary>
        public static AudioClip Hum() => Render("hum", 1f, (t, u, rng) =>
        {
            float throb = 0.8f + 0.2f * Mathf.Sin(2f * Mathf.PI * 9f * t);
            float tone = Mathf.Sin(2f * Mathf.PI * 55f * t)
                + 0.7f * Mathf.Sin(2f * Mathf.PI * 110f * t)
                + 0.35f * Mathf.Sin(2f * Mathf.PI * 331f * t)
                + 0.15f * Mathf.Sin(2f * Mathf.PI * 993f * t);
            return tone * throb * 0.2f;
        });

        /// <summary>A low rumbling roar for the aura. Noise plus whole cycles, so it loops without a click.</summary>
        public static AudioClip Flame() => Render("flame", 2f, (t, u, rng) =>
        {
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float rumble = Mathf.Sin(2f * Mathf.PI * 48f * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * 73f * t);
            float swell = 0.8f + 0.2f * Mathf.Sin(2f * Mathf.PI * 3f * t);
            return (noise * 0.07f + rumble * 0.16f) * swell;
        });

        /// <summary>A crackling hiss for where the laser is burning. Loops, with no tone to give the seam away.</summary>
        public static AudioClip Sizzle() => Render("sizzle", 1f, (t, u, rng) =>
        {
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            return noise * (0.1f + 0.1f * Mathf.Sin(2f * Mathf.PI * 23f * t));
        });

        delegate float Wave(float seconds, float progress, System.Random rng);

        static AudioClip Render(string name, float seconds, Wave wave)
        {
            var rng = new System.Random(1);
            var samples = new float[(int)(seconds * SampleRate)];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = wave(i / (float)SampleRate, i / (float)samples.Length, rng);

            AudioClip clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
