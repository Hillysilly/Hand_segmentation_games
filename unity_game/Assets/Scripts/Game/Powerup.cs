using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// A glowing ball that floats near the player. Touching it with either hand applies it. Colour says what it
    /// is: blue charges the meter, gold makes you invincible, red gives 1.5x damage.
    /// </summary>
    public sealed class Powerup : MonoBehaviour
    {
        public const float TouchRadius = 0.16f;
        const float LifeSeconds = 20f;
        const float Size = 0.16f;

        Bubble shell;
        Bubble core;
        Vector3 home;
        float age;

        public PowerupKind Kind { get; private set; }
        public bool Expired => age > LifeSeconds;

        public static Color ColorOf(PowerupKind kind)
        {
            switch (kind)
            {
                case PowerupKind.Invincible: return new Color(1f, 0.85f, 0.25f);
                case PowerupKind.DamageBoost: return new Color(1f, 0.25f, 0.2f);
                default: return new Color(0.3f, 0.75f, 1f);
            }
        }

        public static string NameOf(PowerupKind kind)
        {
            switch (kind)
            {
                case PowerupKind.Invincible: return "INVINCIBLE";
                case PowerupKind.DamageBoost: return "1.5x DAMAGE";
                default: return "+30% POWER";
            }
        }

        public static Powerup Spawn(PowerupKind kind, Vector3 position, Transform parent)
        {
            var go = new GameObject("Powerup " + kind);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var powerup = go.AddComponent<Powerup>();
            powerup.Kind = kind;
            powerup.home = position;
            powerup.shell = Bubble.Create("Shell", go.transform);
            powerup.core = Bubble.Create("Core", go.transform);
            return powerup;
        }

        /// <summary>True if a palm or fingertip of a tracked hand touches it.</summary>
        public bool Touched(HandPose hand) => hand.Tracked
            && (Vector3.Distance(hand.Palm, transform.position) < TouchRadius
                || Vector3.Distance(hand[Joint.IndexTip], transform.position) < TouchRadius);

        void Update()
        {
            age += Time.deltaTime;
            float bob = Mathf.Sin(age * 2.2f) * 0.04f;
            transform.position = home + Vector3.up * bob;
            float pulse = 1f + Mathf.Sin(age * 6f) * 0.08f;
            float fade = Mathf.Clamp01((LifeSeconds - age) / 2f) * Mathf.Clamp01(age * 3f);
            Color color = ColorOf(Kind);
            shell.transform.localScale = Vector3.one * (Size * 2f * pulse);
            shell.Show(color, BubbleStyle.Hex, fade);
            core.transform.localScale = Vector3.one * Size;
            core.Show(Color.Lerp(color, Color.white, 0.5f), BubbleStyle.Energy, fade);
        }
    }
}
