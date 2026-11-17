using System;

namespace Repulsor
{
    /// <summary>How hard the robots are (PVE). The host picks; it's sent with the match start.</summary>
    public enum Difficulty : byte { Easy, Normal, Hard }

    /// <summary>
    /// The PVE balance (11/16, unity_game/balance.md): five levels, enemy health and damage by round, difficulty,
    /// drops, and every attack's damage. PVP keeps its own numbers (three levels, the old damage) and doesn't read
    /// these. Plain C# so the EditMode tests and the balance simulation use the same numbers as the game.
    ///
    /// Levels: 1 is the old level 1; 2 is the same attacks at 1.5x; 3 adds the old level 2 ability; 4 makes that
    /// 1.5x too; 5 adds the old level 3 ability. Attack damage was set so each character's best attack takes 50-75
    /// hits (or seconds of a held beam, both hands) to kill TRILOBITE at level 3 and GOLIATH at level 5.
    /// </summary>
    public static class Balance
    {
        public const int PveMaxLevel = 5;
        public const int PvpMaxLevel = 3;

        /// <summary>Which of the character's three ability levels (as in <see cref="CharacterDef.Controls"/>) a PVE level has.</summary>
        public static int TierOf(int level) => level <= 2 ? 1 : level <= 4 ? 2 : 3;

        /// <summary>The damage multiplier for an attack from ability level <paramref name="tier"/> at PVE level <paramref name="level"/>.</summary>
        public static float Boost(int tier, int level)
        {
            if (tier == 1)
                return level >= 2 ? LevelBoost : 1f;
            if (tier == 2)
                return level >= 4 ? LevelBoost : 1f;
            return 1f;
        }

        public const float LevelBoost = 1.5f;

        /// <summary>
        /// Damage dealt to fill the meter for each level up (1 to 2, 2 to 3, 3 to 4, 4 to 5), from the enemy health a
        /// solo player meets: level 2 around round 5, level 3 before the round 10 boss, level 4 around round 15, level 5
        /// before the round 20 boss. In co-op the enemies have double health and each player deals about half.
        /// </summary>
        public static readonly float[] PveLevelCosts = { 250f, 1200f, 5500f, 8500f }; // 11/16 playtest: was 400/1500/6000/8500

        /// <summary>A death in PVE empties the meter but keeps your level (PVP: back to level 1). No healing between rounds.</summary>
        public const int PveLevelsLostOnDeath = 0;

        /// <summary>PVP players have this much more HP at every level (11/16: 150 / 225 / 300).</summary>
        public const float PvpHpScale = 1.5f;

        /// <summary>Max HP at a level: 100, +50 a level (300 at level 5).</summary>
        public static float MaxHp(int level) => 100f + 50f * (level - 1);

        // ---- Attack damage (PVE). Level 1 attacks are x1.5 from level 2; level 2 abilities x1.5 from level 4. ----

        /// <summary>Snap blasts and finger guns: Ryuu's comet, the fireball, the rock orb, Lumen's blaster, the gust.</summary>
        public const float Snap = 8f;
        /// <summary>
        /// A web, and how often a held web sign repeats. Webs keep firing while the sign is held (PVP: every 0.3 s),
        /// so they're priced like a held beam: 4 every 0.6 s is about 6.7 a second a hand, like the laser.
        /// </summary>
        public const float Web = 4f;
        public const float WebRepeat = 0.6f;
        /// <summary>Technoman's hand laser, per hand per second.</summary>
        public const float LaserPerHand = 6f;
        /// <summary>The Valkyr's crossbow bolt (reloads in 2.5 s).</summary>
        public const float Crossbow = 15f; // 11/16 playtest: was 12

        /// <summary>Technoman's level 2 repulsor blast (full charge).</summary>
        public const float RepulsorBlast = 27f;
        /// <summary>Ryuu's ki ball, from a quick one to a full charge.</summary>
        public const float KiBallMin = 8f, KiBallMax = 27f;
        /// <summary>The Emberwitch's flamethrower, per hand per second.</summary>
        public const float FlamesPerHand = 13.5f;
        /// <summary>Lumen's sword: a slow push to a fast slash.</summary>
        public const float SlashMin = 10f, SlashMax = 27f;
        /// <summary>The Valkyr's bow, by how far it's drawn.</summary>
        public const float BowMin = 8f, BowMax = 22f;
        /// <summary>Webline: something webbed swung into enemies, and flung at one.</summary>
        public const float WebSwing = 12f, WebFling = 27f;
        /// <summary>Tempest's chain lightning, per bolt per second (one from each hand).</summary>
        public const float LightningPerBolt = 10f;
        /// <summary>Terra's rock spikes.</summary>
        public const float Spikes = 27f;

        /// <summary>Technoman level 3: blasts also explode for this share of their damage.</summary>
        public const float BlastSplash = 0.45f;
        public const float DragonFist = 25f;
        public const float FirePillar = 80f;
        public const float Crush = 50f;
        /// <summary>The Valkyr's poison bomb: this much a second for this long, to every enemy in the cloud; doesn't stack.</summary>
        public const float PoisonPerSecond = 25f, PoisonSeconds = 4f;
        /// <summary>
        /// Webline's top level (black webs): its webs, swing and fling hit this much harder on top of the 1.5x, so 2.5x
        /// their level 1 damage at level 5 (11/16 playtest: was 1.4, about 2.1x).
        /// </summary>
        public const float BlackWeb = 2.5f / LevelBoost;
        /// <summary>Spider Boy's damage in PVP: webs, swing, fling and thrown furniture (11/16: 20% less).</summary>
        public const float SpiderPvpDamage = 0.8f;
        public const float TornadoPerSecond = 12f;
        /// <summary>Terra level 3: each stuck rock's explosion.</summary>
        public const float StuckRock = 20f;

        // ---- Enemies ----

        /// <summary>
        /// A regular enemy's health in a round: its base health in the round it first appears, doubling at each of
        /// rounds 5, 10, 15 and 20 after that (round 10 and 20's are the bosses' reinforcements). Bosses don't double.
        /// Two players: double, bosses too.
        /// </summary>
        public static float EnemyHp(EnemyKind kind, int wave, int players = 1)
        {
            float hp = EnemySpec.Of(kind).Hp;
            if (!EnemySpec.IsBoss(kind))
            {
                int first = WavePlan.Introduced(kind);
                if (first == int.MaxValue)
                    first = 1;
                for (int at = 5; at <= wave; at += 5)
                    if (at > first)
                        hp *= 2f;
            }
            return hp * Math.Max(1, players);
        }

        /// <summary>Enemy damage goes up this much at each of rounds 5, 10, 15 and 20.</summary>
        public const float DamageStep = 1.15f;

        /// <summary>Enemy damage multiplier for a round and difficulty: 75% easy, 100% normal, 150% hard.</summary>
        public static float EnemyDamage(int wave, Difficulty difficulty)
        {
            float scale = 1f;
            for (int at = 5; at <= wave; at += 5)
                scale *= DamageStep;
            return scale * (difficulty == Difficulty.Easy ? 0.75f : difficulty == Difficulty.Hard ? 1.5f : 1f);
        }

        /// <summary>
        /// How long enemies take to wind up their attacks (aim lines, fist wind-ups, the boss's tells), against the
        /// numbers before 11/16: easy 2x, normal 1.5x, hard as they were.
        /// </summary>
        public static float TellScale(Difficulty difficulty) =>
            difficulty == Difficulty.Easy ? 2f : difficulty == Difficulty.Hard ? 1f : 1.5f;

        public static string NameOf(Difficulty difficulty) =>
            difficulty == Difficulty.Easy ? "EASY" : difficulty == Difficulty.Hard ? "HARD" : "NORMAL";

        // ---- Drops ----

        /// <summary>A killed enemy drops a health pack this often, and a random powerup this often.</summary>
        public const float HealthDropChance = 0.05f;
        public const float PowerupDropChance = 0.05f;
        public const float HealthPack = 25f;
    }
}
