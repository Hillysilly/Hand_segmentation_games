using System.Collections.Generic;
using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// The PVE balance in play (11/16, numbers in <see cref="Balance"/>): five levels mapped onto each character's
    /// three ability levels, attack damage by level, enemy health and damage by round, difficulty, and drops.
    /// PVP keeps its three levels and old damage numbers; the Variant Lab's practice uses levels 1-3 as ability
    /// levels, at base damage.
    /// </summary>
    public sealed partial class Arena
    {
        const string DifficultyKey = "match.difficulty";

        /// <summary>
        /// How long enemies wind up their attacks against the old timings (<see cref="Balance.TellScale"/>). Set at
        /// the match start from the difficulty (the client gets the host's); read by enemy lasers, the dasher, and
        /// both bosses' tells.
        /// </summary>
        public static float TellScale { get; private set; } = 1f;

        float enemyDamageScale = 1f;
        readonly List<Powerup> drops = new List<Powerup>();

        /// <summary>The character's ability level (1-3) for the player's level: which moves they have.</summary>
        int Tier => Pvp || practicing ? player.Level : Balance.TierOf(player.Level);

        /// <summary>The same for another player's level (their looks).</summary>
        int TierFor(int level) => Pvp ? level : Balance.TierOf(level);

        /// <summary>
        /// An attack's damage: the PVP number in PVP; otherwise the PVE number at the level's multiplier for an attack
        /// from ability level <paramref name="tier"/> (practice: no multiplier). Powerups go on top (player.Outgoing).
        /// </summary>
        float Dmg(float pvp, float pve, int tier) =>
            Pvp ? pvp : pve * (practicing ? 1f : Balance.Boost(tier, player.Level));

        Difficulty PickedDifficulty
        {
            get => (Difficulty)Mathf.Clamp(PlayerPrefs.GetInt(DifficultyKey, (int)Difficulty.Normal), 0, 2);
            set
            {
                PlayerPrefs.SetInt(DifficultyKey, (int)value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>At the match start: the level rules, and the difficulty's enemy timing.</summary>
        void ApplyBalance()
        {
            player.MaxLevel = Pvp ? Balance.PvpMaxLevel : Balance.PveMaxLevel;
            player.LevelCosts = Pvp ? new[] { Fighter.MeterPerLevel } : Balance.PveLevelCosts;
            player.LevelsLostOnDeath = Pvp ? 99 : Balance.PveLevelsLostOnDeath;
            player.HpScale = Pvp ? Balance.PvpHpScale : 1f;
            TellScale = Pvp ? 1f : Balance.TellScale(settings.Difficulty);
            SpiderSignDetector.Repeat = Pvp ? 0.3f : Balance.WebRepeat;
            enemyDamageScale = 1f;
            ClearDrops();
            if (!Pvp)
                Log($"Balance: {Balance.NameOf(settings.Difficulty)}, enemy wind-ups x{TellScale:0.0}, {settings.Waves} rounds, "
                    + $"level costs {string.Join("/", player.LevelCosts)}");
        }

        /// <summary>Every frame in PVE: enemy damage for the round.</summary>
        void TickBalance()
        {
            if (waves != null)
                enemyDamageScale = Balance.EnemyDamage(waves.Wave, settings.Difficulty);
        }

        /// <summary>A new enemy's health for this round (two players: double).</summary>
        void SetEnemyHealth(Enemy enemy)
        {
            if (Pvp || waves == null)
                return;
            enemy.SetHealth(Balance.EnemyHp(enemy.Kind, Mathf.Max(1, waves.Wave), Multiplayer ? 2 : 1));
        }

        /// <summary>A killed enemy sometimes leaves a health pack or a powerup where it fell.</summary>
        void MaybeDrop(IHittable target)
        {
            if (Pvp || phase != Phase.Playing || !(target is Enemy enemy) || enemy == null || enemy.Practice)
                return;
            float roll = Random.value;
            PowerupKind kind;
            if (roll < Balance.HealthDropChance)
                kind = PowerupKind.Health;
            else if (roll < Balance.HealthDropChance + Balance.PowerupDropChance)
                kind = (PowerupKind)Random.Range(0, 3);
            else
                return;
            Vector3 at = enemy.Center;
            at.y = Mathf.Clamp(head.position.y - 0.35f, 0.8f, 1.5f);
            drops.Add(Powerup.Spawn(kind, at, foes));
            Log($"{enemy.Kind} dropped {kind}");
        }

        /// <summary>Drops wait where they fell: walk over and touch one to take it.</summary>
        void TickDrops()
        {
            for (int i = drops.Count - 1; i >= 0; i--)
            {
                Powerup drop = drops[i];
                if (drop == null)
                {
                    drops.RemoveAt(i);
                    continue;
                }
                if (drop.Expired)
                {
                    Destroy(drop.gameObject);
                    drops.RemoveAt(i);
                }
                else if (!player.IsDead && (drop.Touched(input.Left) || drop.Touched(input.Right)))
                {
                    player.ApplyPowerup(drop.Kind);
                    hud.Message(Powerup.NameOf(drop.Kind), "", 1.2f);
                    game.PlayAt(drop.transform.position, chimeSound);
                    Log($"Drop taken: {drop.Kind}, HP {player.Hp:0}");
                    Destroy(drop.gameObject);
                    drops.RemoveAt(i);
                }
            }
        }

        void ClearDrops()
        {
            foreach (Powerup drop in drops)
                if (drop != null)
                    Destroy(drop.gameObject);
            drops.Clear();
        }

        /// <summary>What a level up says: the new move, or that the moves got stronger.</summary>
        string LevelUpText(int level)
        {
            if (Pvp)
                return Character.Controls[Mathf.Clamp(level - 1, 0, Character.Controls.Length - 1)][0];
            int tier = Balance.TierOf(level);
            bool newMove = Balance.TierOf(level - 1) != tier;
            if (newMove)
                return Character.Controls[Mathf.Clamp(tier - 1, 0, Character.Controls.Length - 1)][0];
            return tier == 1 ? "Your attacks hit 1.5x harder" : "Your level 3 moves hit 1.5x harder";
        }

#if UNITY_EDITOR
        /// <summary>For tests: an attack's damage now, and the enemy damage multiplier.</summary>
        public float TestDamage(float pvp, float pve, int tier) => Dmg(pvp, pve, tier);
        public float TestEnemyDamageScale => enemyDamageScale;
        public int TestDrops => drops.Count;
        public void TestDifficulty(Difficulty difficulty) => settings.Difficulty = difficulty;
#endif
    }
}
