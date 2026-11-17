using System;
using System.Collections.Generic;

namespace Repulsor
{
    /// <summary>
    /// Every kind of enemy. The numbers are sent over the network as a byte, so only add to the end.
    /// Robot and Drone keep their old numbers. The Brawler (the yellow melee robot) is no longer sent in waves
    /// (11/15: the user didn't like it); it stays so the numbers don't shift.
    /// </summary>
    public enum EnemyKind : byte { Robot, Drone, Brawler, Rocketeer, Dasher, Sniper, Tank, Boss, Mech }

    /// <summary>
    /// What an enemy kind is: body, health, how it moves and how it fights. Plain data for tests. <see cref="Hp"/> is
    /// the base health in the round it first appears (it doubles every five rounds after, <see cref="Balance.EnemyHp"/>).
    /// </summary>
    public sealed class EnemySpec
    {
        public EnemyKind Kind;
        public string Name;
        /// <summary>One line on how it fights, for the Variant Lab's gallery.</summary>
        public string Blurb;
        /// <summary>Resources/Enemies prefab.</summary>
        public string Model;
        public float Hp;
        /// <summary>Standing height in metres (drones: their width).</summary>
        public float Height;
        /// <summary>Hit capsule radius in metres.</summary>
        public float Radius;
        public float Speed;
        /// <summary>It walks in until it's this far from you.</summary>
        public float Range;
        public bool Flies;
        /// <summary>Seconds between attacks, picked between these.</summary>
        public float AttackMin, AttackMax;
        /// <summary>
        /// Heights above the floor of its hit capsule's axis ends; 0 means from <see cref="Radius"/> up to
        /// <see cref="Height"/> minus it (a standing body).
        /// </summary>
        public float HitLow, HitHigh;

        public static readonly EnemySpec[] All =
        {
            new EnemySpec { Kind = EnemyKind.Robot, Name = "GUNNER", Blurb = "Walks in and fires lasers", Model = "Stan",
                Hp = 15f, Height = 1.3f, Radius = 0.24f, Speed = 0.35f, Range = 1.6f, AttackMin = 2.4f, AttackMax = 3.4f },
            new EnemySpec { Kind = EnemyKind.Drone, Name = "DRONE", Blurb = "Weaves at head height, quick lasers", Model = "EyeDrone",
                Hp = 10f, Height = 0.4f, Radius = 0.2f, Speed = 0.45f, Range = 2.4f, Flies = true, AttackMin = 3f, AttackMax = 4.5f },
            new EnemySpec { Kind = EnemyKind.Brawler, Name = "BRAWLER", Blurb = "Runs at you and punches. Step back", Model = "Classic",
                Hp = 45f, Height = 1.25f, Radius = 0.32f, Speed = 0.8f, Range = 0.75f, AttackMin = 1.6f, AttackMax = 2.2f },
            new EnemySpec { Kind = EnemyKind.Rocketeer, Name = "ROCKETEER", Blurb = "Fires missiles that blow up robots too. Shoot them down", Model = "George",
                Hp = 40f, Height = 1.45f, Radius = 0.27f, Speed = 0.3f, Range = 2.8f, AttackMin = 4f, AttackMax = 5.5f },
            new EnemySpec { Kind = EnemyKind.Dasher, Name = "DASHER", Blurb = "Dashes side to side, fires twice", Model = "Leela",
                Hp = 25f, Height = 1.2f, Radius = 0.22f, Speed = 0.5f, Range = 2f, AttackMin = 2.6f, AttackMax = 3.4f },
            new EnemySpec { Kind = EnemyKind.Sniper, Name = "SNIPER", Blurb = "Hangs back, long aim, big laser", Model = "Mike",
                Hp = 60f, Height = 1.35f, Radius = 0.24f, Speed = 0.3f, Range = 3.3f, AttackMin = 4.5f, AttackMax = 6f },
            new EnemySpec { Kind = EnemyKind.Tank, Name = "TANK", Blurb = "Slow and tough, fires a fan of three lasers", Model = "QuadShell",
                Hp = 200f, Height = 0.8f, Radius = 0.4f, Speed = 0.22f, Range = 2.2f, AttackMin = 3.5f, AttackMax = 4.5f },
            new EnemySpec { Kind = EnemyKind.Boss, Name = "TRILOBITE", Blurb = "Round 10's boss: laser fans, tracking strikes, missiles", Model = "Trilobite",
                Hp = 1500f, Height = 2.3f, Radius = 0.75f, Speed = 0.25f, Range = 3.2f, AttackMin = 1.5f, AttackMax = 2.5f,
                HitLow = 1.0f, HitHigh = 1.7f },
            new EnemySpec { Kind = EnemyKind.Mech, Name = "GOLIATH", Blurb = "The story's last boss: swings, arm lasers, shoulder rockets, a jump that sends a shockwave",
                Model = "MechBoss", Hp = 3000f, Height = 2.7f, Radius = 0.55f, Speed = 0.45f, Range = 2f, AttackMin = 1f, AttackMax = 2f },
        };

        /// <summary>The bosses: one spawn each, a routine of their own, a bar across the top of the view.</summary>
        public static bool IsBoss(EnemyKind kind) => kind == EnemyKind.Boss || kind == EnemyKind.Mech;

        public static EnemySpec Of(EnemyKind kind) => All[(int)kind];
    }

    /// <summary>
    /// Which enemies a wave sends. The story is 20 rounds (11/16): TRILOBITE is round 10's boss and GOLIATH round 20's.
    /// New kinds join at rounds 1, 3, 5, 8, 12 and 15; each is the first enemy of its first round.
    /// </summary>
    public static class WavePlan
    {
        /// <summary>The story's length, and its last boss.</summary>
        public const int StoryRounds = 20;
        public const EnemyKind FinalBoss = EnemyKind.Mech;
        public const EnemyKind MidBoss = EnemyKind.Boss;
        public const int MidBossRound = 10;

        /// <summary>The boss a round brings, if it's a boss round (the last round is GOLIATH's; round 10 TRILOBITE's in a longer game).</summary>
        public static bool BossRound(int wave, int waveCount, out EnemyKind boss)
        {
            bool last = wave == waveCount && waveCount >= 2;
            boss = last ? FinalBoss : MidBoss;
            return last || (wave == MidBossRound && waveCount > MidBossRound);
        }

        /// <summary>The round each kind first appears in.</summary>
        public static int Introduced(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Robot: return 1;
                case EnemyKind.Drone: return 3;
                case EnemyKind.Dasher: return 5;
                case EnemyKind.Rocketeer: return 8;
                case EnemyKind.Sniper: return 12;
                case EnemyKind.Tank: return 15;
                default: return int.MaxValue;
            }
        }

        /// <summary>The kinds that first appear in this round (the game announces them).</summary>
        public static List<EnemyKind> NewIn(int wave)
        {
            var list = new List<EnemyKind>();
            foreach (EnemyKind kind in Enum.GetValues(typeof(EnemyKind)))
                if (Introduced(kind) == wave)
                    list.Add(kind);
            return list;
        }

        /// <summary>
        /// The kind for one spawn of a wave: its first spawn is the round's new kind, if it has one (so it always
        /// appears); the rest from a roll between 0 and 1.
        /// </summary>
        public static EnemyKind KindFor(int wave, double roll, int spawnIndex)
        {
            if (spawnIndex == 0)
            {
                List<EnemyKind> fresh = NewIn(wave);
                if (fresh.Count > 0)
                    return fresh[0];
            }
            return KindFor(wave, roll);
        }

        /// <summary>
        /// The kind for one spawn of a wave, from a roll between 0 and 1: gunners (1), drones (3), dashers (5),
        /// rocketeers (8), snipers (12), tanks (15). A kind is rarer in its first round and common after (except the
        /// gunners, who are the whole of round 1).
        /// </summary>
        public static EnemyKind KindFor(int wave, double roll)
        {
            var pool = new List<(EnemyKind kind, float weight)>();
            void Add(EnemyKind kind, float weight)
            {
                int since = wave - Introduced(kind);
                if (since >= 0)
                    pool.Add((kind, since == 0 ? weight * 0.7f : weight));
            }
            Add(EnemyKind.Robot, wave == 1 ? 4f : 2.6f);
            Add(EnemyKind.Drone, 2f);
            Add(EnemyKind.Dasher, 2.2f);
            Add(EnemyKind.Rocketeer, 1.3f);
            Add(EnemyKind.Sniper, 1.1f);
            Add(EnemyKind.Tank, 0.9f);
            float total = 0f;
            foreach (var entry in pool)
                total += entry.weight;
            double pick = Math.Max(0.0, Math.Min(0.999999, roll)) * total;
            foreach (var entry in pool)
            {
                if (pick < entry.weight)
                    return entry.kind;
                pick -= entry.weight;
            }
            return EnemyKind.Robot;
        }

        /// <summary>The kinds the boss calls in to help, from a roll between 0 and 1.</summary>
        public static EnemyKind BossHelper(double roll) =>
            roll < 0.4 ? EnemyKind.Drone : roll < 0.75 ? EnemyKind.Robot : EnemyKind.Dasher;

        /// <summary>
        /// The boss's reinforcements in a boss round: any kind met so far (so GOLIATH's can be 400 HP tanks), from a
        /// roll between 0 and 1.
        /// </summary>
        public static EnemyKind BossHelper(int wave, double roll) => KindFor(wave, roll);
    }
}
