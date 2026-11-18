using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// A looping sound that follows something while it lasts (a charge building, a bow being drawn, lightning):
    /// call <see cref="Set"/> every frame with how far along it is (0 stops it). Its pitch and loudness rise with
    /// the amount. If a frame passes without a Set it goes quiet by itself, so a character switch or the end of a
    /// match never leaves one hanging. At the scene root, like the game's other sound voices (11/17).
    /// </summary>
    public sealed class LoopSound : MonoBehaviour
    {
        AudioSource source;
        int setFrame = -10;

        public static LoopSound Create(string name, AudioClip clip, float minDistance = 0.4f)
        {
            var go = new GameObject("Loop " + name);
            var loop = go.AddComponent<LoopSound>();
            loop.source = go.AddComponent<AudioSource>();
            loop.source.clip = clip;
            loop.source.loop = true;
            loop.source.playOnAwake = false;
            loop.source.spatialBlend = 1f;
            loop.source.minDistance = minDistance;
            loop.source.maxDistance = 20f;
            loop.source.rolloffMode = AudioRolloffMode.Logarithmic;
            loop.source.dopplerLevel = 0f;
            loop.source.volume = 0f;
            return loop;
        }

        /// <summary>
        /// This frame: where it is and how far along (0 to 1; 0 stops it). Pitch goes from <paramref name="lowPitch"/>
        /// to <paramref name="highPitch"/> with the amount, loudness from 40% to all of <paramref name="volume"/>.
        /// </summary>
        public void Set(Vector3 at, float amount, float lowPitch = 0.8f, float highPitch = 1.6f, float volume = 0.6f)
        {
            if (amount <= 0f)
            {
                if (source.isPlaying)
                    source.Stop();
                return;
            }
            setFrame = Time.frameCount;
            transform.position = at;
            amount = Mathf.Clamp01(amount);
            source.pitch = Mathf.Lerp(lowPitch, highPitch, amount);
            source.volume = Mathf.Lerp(0.4f, 1f, amount) * volume;
            if (!source.isPlaying)
                source.Play();
        }

        public bool Playing => source != null && source.isPlaying;

        void LateUpdate()
        {
            if (source.isPlaying && Time.frameCount - setFrame > 1)
                source.Stop();
        }
    }
}
