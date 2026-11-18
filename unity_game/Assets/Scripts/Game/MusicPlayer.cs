using UnityEngine;

namespace Repulsor
{
    public enum MusicTrack { None, Menu, Gameplay, Boss, FinalBoss, Pvp }

    /// <summary>
    /// The soundtrack (11/17): menus, the title and character select; the rounds against the robots; TRILOBITE;
    /// GOLIATH; and PVP. Tracks are in Resources/Music, streamed from storage (a four-minute track decompressed would take
    /// about 40 MB on the headset), and loop. A change of track crossfades. Not positional: it plays in your head.
    /// </summary>
    public sealed class MusicPlayer : MonoBehaviour
    {
        const string VolumeKey = "music.volume";
        public const float DefaultVolume = 0.4f;
        public const float VolumeStep = 0.1f;

        /// <summary>The music's own volume, 0 to 1 (the MUSIC bar on the title and pause screens), kept between sessions.</summary>
        public static float Volume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, DefaultVolume));
            set
            {
                PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(Mathf.Round(value * 100f) / 100f));
                PlayerPrefs.Save();
            }
        }
        /// <summary>Paused: the music carries on quieter.</summary>
        public const float PausedShare = 0.4f;
        const float FadeSeconds = 1.5f;

        readonly AudioSource[] sources = new AudioSource[2];
        int current;
        MusicTrack playing = MusicTrack.None;
        float level = 1f;
        float lastVolume = -1f;

        public MusicTrack Playing => playing;

        /// <summary>Where a track starts, and the part it loops (seconds); zeros: from the top, the whole track.</summary>
        public struct Cue
        {
            public float Start, LoopFrom, LoopEnd;
        }

        /// <summary>
        /// GOLIATH's track builds for 40 s before the fight music proper (measured by MusicAnalysis: loudness 0.6
        /// rising to 0.92 at 0:40, fading out from 3:55). It starts at 0:25, 15 seconds before, and loops from
        /// 3:54 back to 0:40 so a long fight never hears the slow intro again (11/17).
        /// </summary>
        /// <summary>
        /// Boss fights play louder than the rounds against the robots (11/17): 1.5x on top of the MUSIC bar. The boss
        /// tracks already measure about 20-25% louder than the gameplay track (MusicAnalysis: rms 0.163 and 0.169 against
        /// 0.136), so together about 1.8x. Capped at full volume.
        /// </summary>
        public const float BossBoost = 1.5f;

        public static float GainOf(MusicTrack track) =>
            track == MusicTrack.Boss || track == MusicTrack.FinalBoss ? BossBoost : 1f;

        public static Cue CueOf(MusicTrack track) => track == MusicTrack.FinalBoss
            ? new Cue { Start = 25f, LoopFrom = 40f, LoopEnd = 234f }
            : default;

        public static MusicPlayer Create(Transform parent)
        {
            var go = new GameObject("Music");
            go.transform.SetParent(parent, false);
            var player = go.AddComponent<MusicPlayer>();
            for (int i = 0; i < 2; i++)
            {
                AudioSource source = go.AddComponent<AudioSource>();
                source.loop = true;
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.volume = 0f;
                source.priority = 0;
                player.sources[i] = source;
            }
            return player;
        }

        /// <summary>Switches to this track (crossfading); the same track again keeps playing where it is.</summary>
        public void Play(MusicTrack track, bool quiet = false)
        {
            level = quiet ? PausedShare : 1f;
            if (track == playing)
                return;
            playing = track;
            AudioClip clip = track == MusicTrack.None ? null : Resources.Load<AudioClip>("Music/" + track);
            current = 1 - current;
            AudioSource next = sources[current];
            next.Stop();
            next.clip = clip;
            next.volume = 0f;
            if (clip != null)
            {
                next.Play();
                Cue cue = CueOf(track);
                if (cue.Start > 0f && cue.Start < clip.length)
                    next.time = cue.Start;
            }
        }

        void Update()
        {
            // A change on the MUSIC bar is heard at once; track changes and pausing fade.
            float volume = Volume;
            if (!Mathf.Approximately(volume, lastVolume))
            {
                foreach (AudioSource source in sources)
                    source.volume = lastVolume > 0f ? source.volume * volume / lastVolume : 0f;
                lastVolume = volume;
            }
            AudioSource now = sources[current];
            Cue loop = CueOf(playing);
            if (loop.LoopEnd > 0f && now.isPlaying && now.time >= loop.LoopEnd)
                now.time = loop.LoopFrom;
            float step = Time.unscaledDeltaTime / FadeSeconds * Mathf.Max(volume, 0.05f);
            for (int i = 0; i < 2; i++)
            {
                AudioSource source = sources[i];
                float target = i == current && source.clip != null ? Mathf.Min(1f, volume * level * GainOf(playing)) : 0f;
                source.volume = Mathf.MoveTowards(source.volume, target, step);
                if (i != current && source.isPlaying && source.volume <= 0f)
                    source.Stop();
            }
        }
    }
}
