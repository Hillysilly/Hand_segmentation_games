using System;

namespace Repulsor
{
    /// <summary>Sent as a byte; only add to the end. Health only comes from enemy drops (11/16).</summary>
    public enum PowerupKind { Charge, Invincible, DamageBoost, Health }

    /// <summary>
    /// One player's health, transformation meter, level and lives. Plain C# with no Unity calls, so the
    /// rules run in EditMode tests and the host can run them for every player in multiplayer.
    /// </summary>
    public sealed class Fighter
    {
        public const float BaseHp = 100f;
        public const float HealDelaySeconds = 5f;
        public const float HealPerSecond = 3f;
        public const float MeterPerLevel = 60f;          // damage dealt to fill the meter once (PVP; PVE uses Balance)
        public const float TransformHealFraction = 0.5f; // of the new max HP
        public const float InvincibleSeconds = 8f;
        public const float DamageBoostSeconds = 10f;
        public const float DamageBoost = 1.5f;
        public const float ChargePowerup = 0.3f;         // of a full meter
        public const float SpawnProtectionSeconds = 2f;

        public Fighter(int lives = 3, float respawnSeconds = 5f)
        {
            StartingLives = lives;
            RespawnSeconds = respawnSeconds;
            Reset();
        }

        public int StartingLives { get; set; }
        /// <summary>The top level: 3 in PVP, 5 in PVE (11/16, <see cref="Balance"/>).</summary>
        public int MaxLevel { get; set; } = Balance.PvpMaxLevel;
        /// <summary>Damage to fill the meter for each level up (the last entry repeats).</summary>
        public float[] LevelCosts { get; set; } = { MeterPerLevel };
        public float MeterCost => LevelCosts[Math.Min(Level - 1, LevelCosts.Length - 1)];
        /// <summary>Levels a death takes away: all of them in PVP, none in PVE (<see cref="Balance.PveLevelsLostOnDeath"/>).</summary>
        public int LevelsLostOnDeath { get; set; } = 99;
        public float RespawnSeconds { get; set; }

        public float Hp { get; private set; }
        public int Level { get; private set; }
        /// <summary>0 to 1. Full means the player can transform with the power-up pose.</summary>
        public float Meter { get; private set; }
        public int Lives { get; private set; }
        public bool IsDead { get; private set; }
        /// <summary>Seconds until respawn while dead, 0 otherwise.</summary>
        public float RespawnIn { get; private set; }
        /// <summary>Dead with no lives left: watches until the game ends.</summary>
        public bool IsOut => IsDead && Lives <= 0;
        public float InvincibleFor { get; private set; }
        public float BoostFor { get; private set; }
        public float SinceCombat { get; private set; }
        public float DamageDealt { get; private set; }
        public float DamageTaken { get; private set; }
        /// <summary>Debug: never loses HP.</summary>
        public bool God { get; set; }

        public ShieldGauge Shield { get; } = new ShieldGauge();

        public float MaxHp => MaxHpAt(Level) * HpScale;
        /// <summary>Times every level's max HP (PVP: 1.5, <see cref="Balance.PvpHpScale"/>).</summary>
        public float HpScale { get; set; } = 1f;
        public bool CanTransform => !IsDead && Level < MaxLevel && Meter >= 1f;
        public bool Invincible => God || InvincibleFor > 0f;
        public float DamageMultiplier => BoostFor > 0f ? DamageBoost : 1f;
        public bool Healing => !IsDead && SinceCombat >= HealDelaySeconds && Hp < MaxHp;

        public static float MaxHpAt(int level) => BaseHp * (1f + 0.5f * (level - 1));

        /// <summary>Heals this much (a health pack), up to max HP.</summary>
        public void Heal(float amount)
        {
            if (!IsDead && amount > 0f)
                Hp = Math.Min(MaxHp, Hp + amount);
        }

        public event Action<float> Damaged;
        public event Action Died;
        public event Action Respawned;
        public event Action<int> Transformed;

        public void Reset()
        {
            Level = 1;
            Hp = MaxHp;
            Meter = 0f;
            Lives = StartingLives;
            IsDead = false;
            RespawnIn = 0f;
            InvincibleFor = 0f;
            BoostFor = 0f;
            SinceCombat = HealDelaySeconds;
            DamageDealt = 0f;
            DamageTaken = 0f;
            Shield.Reset();
        }

        /// <summary>Applies a hit. Returns the HP actually lost (0 if blocked, invincible or dead).</summary>
        public float TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f)
                return 0f;
            SinceCombat = 0f;
            if (Invincible || Shield.Up)
                return 0f;

            float lost = Math.Min(amount, Hp);
            Hp -= lost;
            DamageTaken += lost;
            Damaged?.Invoke(lost);
            if (Hp <= 0f)
                Die();
            return lost;
        }

        /// <summary>
        /// Damage after powerups, for damage dealt outside RepulsorGame.Strike's own scaling (poison). Hits through
        /// Strike get the boost there (RepulsorGame.DamageScale), so don't apply it twice.
        /// </summary>
        public float Outgoing(float baseDamage) => baseDamage * DamageMultiplier;

        /// <summary>Records damage this player dealt: it fills the transformation meter.</summary>
        public void DealtDamage(float amount)
        {
            if (IsDead || amount <= 0f)
                return;
            SinceCombat = 0f;
            DamageDealt += amount;
            if (Level < MaxLevel)
                Meter = Math.Min(1f, Meter + amount / MeterCost);
        }

        /// <summary>Spends a full meter to go up a level. Heals half the new max HP.</summary>
        public bool TryTransform()
        {
            if (!CanTransform)
                return false;
            Level++;
            Meter = 0f;
            Hp = Math.Min(MaxHp, Hp + MaxHp * TransformHealFraction);
            Transformed?.Invoke(Level);
            return true;
        }

        public void ApplyPowerup(PowerupKind kind)
        {
            switch (kind)
            {
                case PowerupKind.Charge:
                    if (Level < MaxLevel)
                        Meter = Math.Min(1f, Meter + ChargePowerup);
                    break;
                case PowerupKind.Invincible:
                    InvincibleFor = InvincibleSeconds;
                    break;
                case PowerupKind.DamageBoost:
                    BoostFor = DamageBoostSeconds;
                    break;
                case PowerupKind.Health:
                    Heal(Balance.HealthPack);
                    break;
            }
        }

        public void Tick(float dt)
        {
            InvincibleFor = Math.Max(0f, InvincibleFor - dt);
            BoostFor = Math.Max(0f, BoostFor - dt);

            if (IsDead)
            {
                if (Lives > 0)
                {
                    RespawnIn = Math.Max(0f, RespawnIn - dt);
                    if (RespawnIn <= 0f)
                        Respawn();
                }
                return;
            }

            Shield.Tick(dt);
            SinceCombat += dt;
            if (SinceCombat >= HealDelaySeconds)
                Hp = Math.Min(MaxHp, Hp + HealPerSecond * dt);
        }

        /// <summary>Debug: fills the meter.</summary>
        public void FillMeter() => Meter = Level < MaxLevel ? 1f : 0f;

        /// <summary>Practice (Variant Lab): jumps straight to a level at full HP with an empty meter. No transform event.</summary>
        public void SetLevel(int level)
        {
            Level = Math.Max(1, Math.Min(MaxLevel, level));
            Hp = MaxHp;
            Meter = 0f;
            IsDead = false;
            RespawnIn = 0f;
        }

        void Die()
        {
            Hp = 0f;
            IsDead = true;
            Lives--;
            // Dying empties the meter. In PVP it also throws away the transformations (back to level 1); in PVE you
            // keep your level (LevelsLostOnDeath 0, set by the Arena from Balance).
            Level = Math.Max(1, Level - LevelsLostOnDeath);
            Meter = 0f;
            BoostFor = 0f;
            Shield.Reset();
            RespawnIn = Lives > 0 ? RespawnSeconds : 0f;
            Died?.Invoke();
        }

        void Respawn()
        {
            IsDead = false;
            Hp = MaxHp;
            SinceCombat = HealDelaySeconds;
            InvincibleFor = SpawnProtectionSeconds;
            Respawned?.Invoke();
        }
    }
}
