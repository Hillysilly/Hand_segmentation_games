using System;

namespace Repulsor
{
    /// <summary>
    /// Every kind of enemy. The numbers are sent over the network as a byte, so only add to the end.
    /// Robot and Drone keep their old numbers. Robot (the yellow gunner) is no longer sent in waves (11/14: the
    /// user didn't like it); it stays so the numbers don't shift.
    /// </summary>
    public enum EnemyKind : byte { Robot, Drone, Brawler, Rocketeer, Dasher, Sniper, Tank, Boss }

    /// <summary>What an enemy kind is: body, health, how it moves and how it fights. Plain data for tests.</summary>
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
                Hp = 30f, Height = 1.3f, Radius = 0.24f, Speed = 0.35f, Range = 1.6f, AttackMin = 2.4f, AttackMax = 3.4f },
            new EnemySpec { Kind = EnemyKind.Drone, Name = "DRONE", Blurb = "Weaves at head height, quick lasers", Model = "EyeDrone",
                Hp = 15f, Height = 0.4f, Radius = 0.2f, Speed = 0.45f, Range = 1.4f, Flies = true, AttackMin = 3f, AttackMax = 4.5f },
            new EnemySpec { Kind = EnemyKind.Brawler, Name = "BRAWLER", Blurb = "Runs at you and punches. Step back", Model = "Classic",
                Hp = 45f, Height = 1.25f, Radius = 0.32f, Speed = 0.8f, Range = 0.75f, AttackMin = 1.6f, AttackMax = 2.2f },
            new EnemySpec { Kind = EnemyKind.Rocketeer, Name = "ROCKETEER", Blurb = "Fires missiles that blow up robots too. Shoot them down", Model = "George",
                Hp = 40f, Height = 1.45f, Radius = 0.27f, Speed = 0.3f, Range = 2.8f, AttackMin = 4f, AttackMax = 5.5f },
            new EnemySpec { Kind = EnemyKind.Dasher, Name = "DASHER", Blurb = "Dashes side to side, fires twice", Model = "Leela",
                Hp = 25f, Height = 1.2f, Radius = 0.22f, Speed = 0.5f, Range = 2f, AttackMin = 2.6f, AttackMax = 3.4f },
            new EnemySpec { Kind = EnemyKind.Sniper, Name = "SNIPER", Blurb = "Hangs back, long aim, big laser", Model = "Mike",
                Hp = 30f, Height = 1.35f, Radius = 0.24f, Speed = 0.3f, Range = 3.3f, AttackMin = 4.5f, AttackMax = 6f },
            new EnemySpec { Kind = EnemyKind.Tank, Name = "TANK", Blurb = "Slow and tough, fires a fan of three lasers", Model = "QuadShell",
                Hp = 90f, Height = 0.8f, Radius = 0.4f, Speed = 0.22f, Range = 2.2f, AttackMin = 3.5f, AttackMax = 4.5f },
            new EnemySpec { Kind = EnemyKind.Boss, Name = "TRILOBITE", Blurb = "The boss: laser fans, tracking strikes, missiles", Model = "Trilobite",
                Hp = 700f, Height = 2.3f, Radius = 0.75f, Speed = 0.25f, Range = 3.2f, AttackMin = 1.5f, AttackMax = 2.5f,
                HitLow = 1.0f, HitHigh = 1.7f },
        };

        public static EnemySpec Of(EnemyKind kind) => All[(int)kind];
    }

    /// <summary>Which enemies a wave sends: new kinds join as the waves go on, and the last wave is the boss.</summary>
    public static class WavePlan
    {
        /// <summary>
        /// The kind for one spawn of a wave, from a roll between 0 and 1. Wave 1 is dashers and drones; brawlers
        /// join at 2, rocketeers at 3, snipers and tanks at 4.
        /// </summary>
        public static EnemyKind KindFor(int wave, double roll)
        {
            var pool = new System.Collections.Generic.List<(EnemyKind kind, float weight)>
            {
                (EnemyKind.Dasher, 3f),
                (EnemyKind.Drone, Math.Min(2.5f, 0.6f + 0.6f * wave)),
            };
            if (wave >= 2)
                pool.Add((EnemyKind.Brawler, 1.6f));
            if (wave >= 3)
                pool.Add((EnemyKind.Rocketeer, 1.2f));
            if (wave >= 4)
            {
                pool.Add((EnemyKind.Sniper, 1f));
                pool.Add((EnemyKind.Tank, 0.8f));
            }
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
            return EnemyKind.Dasher;
        }

        /// <summary>The kinds the boss calls in to help, from a roll between 0 and 1.</summary>
        public static EnemyKind BossHelper(double roll) =>
            roll < 0.45 ? EnemyKind.Drone : roll < 0.75 ? EnemyKind.Dasher : EnemyKind.Brawler;
    }
}
