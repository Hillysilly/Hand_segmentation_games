using System;

namespace Repulsor
{
    /// <summary>
    /// When the tight aura (a glow hugging the player's body) is up: for a while after a transformation, for as long
    /// as a powerup lasts, and for good where a character's plan says so (Technoman at level 3). It fades out over
    /// its last second.
    /// </summary>
    public sealed class GlowTimer
    {
        public const float AfterTransform = 6f;
        public const float AfterChargePowerup = 3f;
        public const float FadeSeconds = 1f;

        float left;

        /// <summary>On regardless of the timer.</summary>
        public bool Permanent { get; set; }

        public float SecondsLeft => left;

        /// <summary>How strongly it shows, 0 to 1.</summary>
        public float Strength => Permanent ? 1f : Math.Min(1f, left / FadeSeconds);

        public bool On => Strength > 0f;

        /// <summary>Keeps it up for at least this long from now.</summary>
        public void Trigger(float seconds) => left = Math.Max(left, seconds);

        public void Transformed() => Trigger(AfterTransform);

        public void PoweredUp(PowerupKind kind) => Trigger(kind switch
        {
            PowerupKind.Invincible => Fighter.InvincibleSeconds,
            PowerupKind.DamageBoost => Fighter.DamageBoostSeconds,
            _ => AfterChargePowerup,
        });

        public void Tick(float dt) => left = Math.Max(0f, left - dt);

        public void Clear()
        {
            left = 0f;
            Permanent = false;
        }

        /// <summary>Technoman's plan: a tight aura for good at level 3.</summary>
        public static bool PermanentFor(CharacterId character, int level) => character == CharacterId.Technoman && level >= 3;
    }
}
