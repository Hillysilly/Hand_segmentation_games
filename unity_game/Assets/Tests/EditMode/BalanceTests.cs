using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Repulsor.Tests
{
    /// <summary>
    /// The PVE balance (11/16, unity_game/balance.md): enemy health and damage by round, levels, drops, boss hit
    /// counts, and a simulated run for every character (<see cref="BalanceSim"/>), written to Logs/balance_sim.md.
    /// </summary>
    public class BalanceTests
    {
        [Test]
        public void EnemiesStartAtBaseHealthAndDoubleEveryFiveRounds()
        {
            Assert.That(Balance.EnemyHp(EnemyKind.Robot, 1), Is.EqualTo(15f));
            Assert.That(Balance.EnemyHp(EnemyKind.Robot, 4), Is.EqualTo(15f));
            Assert.That(Balance.EnemyHp(EnemyKind.Robot, 5), Is.EqualTo(30f));
            Assert.That(Balance.EnemyHp(EnemyKind.Robot, 10), Is.EqualTo(60f));
            Assert.That(Balance.EnemyHp(EnemyKind.Robot, 15), Is.EqualTo(120f), "gunners go up three times");
            Assert.That(Balance.EnemyHp(EnemyKind.Dasher, 5), Is.EqualTo(25f), "base health in its first round");
            Assert.That(Balance.EnemyHp(EnemyKind.Dasher, 10), Is.EqualTo(50f));
            Assert.That(Balance.EnemyHp(EnemyKind.Sniper, 12), Is.EqualTo(60f));
            Assert.That(Balance.EnemyHp(EnemyKind.Tank, 15), Is.EqualTo(200f));
            Assert.That(Balance.EnemyHp(EnemyKind.Tank, 19), Is.EqualTo(200f));
            Assert.That(Balance.EnemyHp(EnemyKind.Tank, 20), Is.EqualTo(400f), "GOLIATH's reinforcements");
            Assert.That(Balance.EnemyHp(EnemyKind.Boss, 10), Is.EqualTo(1500f));
            Assert.That(Balance.EnemyHp(EnemyKind.Mech, 20), Is.EqualTo(3000f));
            Assert.That(Balance.EnemyHp(EnemyKind.Mech, 20, 2), Is.EqualTo(6000f), "two players: bosses double too");
            Assert.That(Balance.EnemyHp(EnemyKind.Robot, 1, 2), Is.EqualTo(30f));
        }

        [Test]
        public void EnemyDamageGoesUpEveryFiveRoundsAndWithDifficulty()
        {
            Assert.That(Balance.EnemyDamage(1, Difficulty.Normal), Is.EqualTo(1f));
            Assert.That(Balance.EnemyDamage(4, Difficulty.Normal), Is.EqualTo(1f));
            Assert.That(Balance.EnemyDamage(5, Difficulty.Normal), Is.EqualTo(1.15f).Within(1e-4f));
            Assert.That(Balance.EnemyDamage(20, Difficulty.Normal), Is.EqualTo(1.15f * 1.15f * 1.15f * 1.15f).Within(1e-4f));
            Assert.That(Balance.EnemyDamage(1, Difficulty.Easy), Is.EqualTo(0.75f));
            Assert.That(Balance.EnemyDamage(1, Difficulty.Hard), Is.EqualTo(1.5f));
            Assert.That(Balance.TellScale(Difficulty.Easy), Is.EqualTo(2f));
            Assert.That(Balance.TellScale(Difficulty.Normal), Is.EqualTo(1.5f));
            Assert.That(Balance.TellScale(Difficulty.Hard), Is.EqualTo(1f), "hard is the old timing");
        }

        [Test]
        public void FiveLevelsUnlockMovesAtOneThreeAndFive()
        {
            Assert.That(new[] { 1, 2, 3, 4, 5 }.Select(Balance.TierOf), Is.EqualTo(new[] { 1, 1, 2, 2, 3 }));
            Assert.That(Balance.Boost(1, 1), Is.EqualTo(1f));
            Assert.That(Balance.Boost(1, 2), Is.EqualTo(1.5f));
            Assert.That(Balance.Boost(1, 5), Is.EqualTo(1.5f), "no stacking");
            Assert.That(Balance.Boost(2, 3), Is.EqualTo(1f));
            Assert.That(Balance.Boost(2, 4), Is.EqualTo(1.5f));
            Assert.That(Balance.Boost(3, 5), Is.EqualTo(1f));
            var fighter = new Fighter { MaxLevel = Balance.PveMaxLevel, LevelCosts = Balance.PveLevelCosts };
            fighter.Reset();
            for (int level = 1; level < 5; level++)
            {
                fighter.DealtDamage(Balance.PveLevelCosts[level - 1] - 1f);
                Assert.That(fighter.CanTransform, Is.False, $"level {level}");
                fighter.DealtDamage(1f);
                Assert.That(fighter.TryTransform(), Is.True, $"level {level}");
            }
            Assert.That(fighter.Level, Is.EqualTo(5));
            Assert.That(fighter.MaxHp, Is.EqualTo(300f));
            fighter.DealtDamage(100000f);
            Assert.That(fighter.CanTransform, Is.False, "5 is the top");
        }

        [Test]
        public void PveDeathEmptiesTheMeterButKeepsTheLevel()
        {
            var fighter = new Fighter(3, 5f)
            {
                MaxLevel = Balance.PveMaxLevel, LevelCosts = Balance.PveLevelCosts, LevelsLostOnDeath = Balance.PveLevelsLostOnDeath,
            };
            fighter.Reset();
            for (int i = 0; i < 2; i++)
            {
                fighter.FillMeter();
                fighter.TryTransform();
            }
            fighter.DealtDamage(500f);
            Assert.That(fighter.Meter, Is.GreaterThan(0f));
            fighter.TakeDamage(1000f);
            Assert.That(fighter.IsDead, Is.True);
            Assert.That(fighter.Level, Is.EqualTo(3));
            Assert.That(fighter.Meter, Is.EqualTo(0f));
        }

        [Test]
        public void HealthPacksHealTwentyFiveUpToMax()
        {
            var fighter = new Fighter();
            fighter.TakeDamage(40f);
            fighter.ApplyPowerup(PowerupKind.Health);
            Assert.That(fighter.Hp, Is.EqualTo(85f));
            fighter.ApplyPowerup(PowerupKind.Health);
            Assert.That(fighter.Hp, Is.EqualTo(100f));
        }

        [Test]
        public void TwentyRoundsWithBossesAtTenAndTwenty()
        {
            Assert.That(WavePlan.StoryRounds, Is.EqualTo(20));
            for (int wave = 1; wave <= 20; wave++)
            {
                bool boss = WavePlan.BossRound(wave, 20, out EnemyKind kind);
                Assert.That(boss, Is.EqualTo(wave == 10 || wave == 20), $"round {wave}");
                if (wave == 10)
                    Assert.That(kind, Is.EqualTo(EnemyKind.Boss));
                if (wave == 20)
                    Assert.That(kind, Is.EqualTo(EnemyKind.Mech));
            }
            // A short game (debug) still ends on GOLIATH, with no TRILOBITE.
            Assert.That(WavePlan.BossRound(3, 3, out EnemyKind last) && last == EnemyKind.Mech);
            Assert.That(WavePlan.BossRound(10, 10, out EnemyKind tenth) && tenth == EnemyKind.Mech);
            var introduced = new Dictionary<EnemyKind, int>
            {
                { EnemyKind.Robot, 1 }, { EnemyKind.Drone, 3 }, { EnemyKind.Dasher, 5 },
                { EnemyKind.Rocketeer, 8 }, { EnemyKind.Sniper, 12 }, { EnemyKind.Tank, 15 },
            };
            foreach (var pair in introduced)
            {
                Assert.That(WavePlan.Introduced(pair.Key), Is.EqualTo(pair.Value), pair.Key.ToString());
                // Its first round's first enemy is always it.
                Assert.That(WavePlan.KindFor(pair.Value, 0.0, 0), Is.EqualTo(pair.Key));
                Assert.That(WavePlan.KindFor(pair.Value, 0.999, 0), Is.EqualTo(pair.Key));
            }
        }

        [Test]
        public void LaterRoundsAreAboutAsBigAsRoundNine()
        {
            var random = new Random(3);
            Assert.That(WaveDirector.WaveSize(1, random), Is.EqualTo(3));
            for (int i = 0; i < 50; i++)
            {
                Assert.That(WaveDirector.WaveSize(9, random), Is.InRange(16, 18));
                Assert.That(WaveDirector.WaveSize(15, random), Is.InRange(15, 19));
            }
            var waves = new WaveDirector(20, 10, 1);
            waves.Tick(WaveDirector.CountdownSeconds + 0.1f);
            Assert.That(waves.Wave, Is.EqualTo(10));
            Assert.That(waves.BossWave, Is.True);
            Assert.That(waves.Boss, Is.EqualTo(EnemyKind.Boss));
            Assert.That(waves.FinalBossWave, Is.False);
            Assert.That(waves.ToSpawn, Is.EqualTo(1), "the boss on its own");
        }

        /// <summary>
        /// Each character's best attack against each boss, every hit landing, held beams with both hands: 50-75 hits
        /// (or seconds) to kill TRILOBITE at level 3 and GOLIATH at level 5 (the user's target, 11/16). The table goes to
        /// Logs/balance_boss_hits.md.
        /// </summary>
        [Test]
        public void EveryCharactersBestAttackTakesFiftyToSeventyFiveHitsOnEachBoss()
        {
            var report = new StringBuilder("# Hits to kill each boss (every hit lands, held beams with both hands)\n\n");
            report.Append("| Character | Attack | TRILOBITE (level 3) | GOLIATH (level 5) |\n|---|---|---|---|\n");
            foreach (CharacterId id in Enum.GetValues(typeof(CharacterId)))
            {
                float bestTrilobite = float.MaxValue, bestGoliath = float.MaxValue;
                foreach (BalanceSim.Attack attack in BalanceSim.AttacksOf(id))
                {
                    if (!attack.OnBosses)
                        continue;
                    string trilobite = "-", goliath = "-";
                    if (attack.Tier <= Balance.TierOf(3))
                    {
                        float hits = 1500f / attack.PerUse(3);
                        bestTrilobite = Math.Min(bestTrilobite, hits);
                        trilobite = hits.ToString("0");
                    }
                    float hits5 = 3000f / attack.PerUse(5);
                    bestGoliath = Math.Min(bestGoliath, hits5);
                    goliath = hits5.ToString("0");
                    report.Append($"| {CharacterDef.Get(id).Name} | {attack.Name}{(attack.Held ? " (s)" : "")} | {trilobite} | {goliath} |\n");
                }
                // Webline has nothing that works on bosses but webs (room objects smashed into them aren't modelled).
                if (id == CharacterId.Spider)
                    continue;
                Assert.That(bestTrilobite, Is.InRange(50f, 76f), $"{id} on TRILOBITE");
                Assert.That(bestGoliath, Is.LessThanOrEqualTo(76f), $"{id} on GOLIATH");
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/balance_boss_hits.md", report.ToString());
        }

        /// <summary>
        /// A simulated solo run for every character on every difficulty (several seeds each), using the game's own
        /// waves, health, damage, levels and meter. The report (Logs/balance_sim.md) has run length, the round each
        /// level arrives, deaths and boss fight times. Only loose checks here: it's a guide, not a verdict.
        /// </summary>
        [Test]
        public void SimulatedRunsReport()
        {
            const int Seeds = 6;
            var report = new StringBuilder();
            var traces = new StringBuilder("\n## Round by round (normal)\n");
            report.Append("# Balance simulation (solo, 20 rounds)\n\n");
            report.Append(BalanceSim.Assumptions).Append("\n");
            foreach (Difficulty difficulty in new[] { Difficulty.Normal, Difficulty.Easy, Difficulty.Hard })
            {
                report.Append($"\n## {Balance.NameOf(difficulty)}\n\n");
                report.Append("| Character | Won | Minutes | L2 round | L3 round | L4 round | L5 round | Deaths | TRILOBITE s | GOLIATH s | Taken |\n");
                report.Append("|---|---|---|---|---|---|---|---|---|---|---|\n");
                foreach (CharacterId id in Enum.GetValues(typeof(CharacterId)))
                {
                    var runs = new List<BalanceSim.Result>();
                    for (int seed = 0; seed < Seeds; seed++)
                        runs.Add(new BalanceSim(id, difficulty, seed).Run());
                    string Round(int level)
                    {
                        var reached = runs.Where(r => r.LevelRound[level] > 0).Select(r => (float)r.LevelRound[level]).ToList();
                        return reached.Count == 0 ? "never" : reached.Average().ToString("0.0") + (reached.Count < runs.Count ? $" ({reached.Count}/{runs.Count})" : "");
                    }
                    string Avg(Func<BalanceSim.Result, float> f, string format) =>
                        runs.Select(f).Where(v => v > 0f).DefaultIfEmpty(0f).Average().ToString(format);
                    report.Append($"| {CharacterDef.Get(id).Name} | {runs.Count(r => r.Won)}/{runs.Count} | {Avg(r => r.Seconds / 60f, "0.0")} | "
                        + $"{Round(2)} | {Round(3)} | {Round(4)} | {Round(5)} | {runs.Average(r => r.Deaths):0.0} | "
                        + $"{Avg(r => r.TrilobiteSeconds, "0")} | {Avg(r => r.GoliathSeconds, "0")} | {runs.Average(r => r.Taken):0} |\n");
                    if (difficulty == Difficulty.Normal && (id == CharacterId.Technoman || id == CharacterId.Archer))
                        traces.Append($"\n### {CharacterDef.Get(id).Name}, seed 0\n\n```\n{runs[0].Trace}```\n");
                    if (difficulty == Difficulty.Normal)
                        foreach (BalanceSim.Result run in runs)
                            Assert.That(run.Seconds, Is.LessThan(60f * 60f), $"{id} seed {run.Seed} never ended");
                }
            }
            Directory.CreateDirectory("Logs");
            // What-ifs on normal, against the build (death keeps your level, no healing between rounds).
            var whatIf = new StringBuilder("\n## What-ifs (normal)\n\n| Character | As built (death keeps level) | Old: death back to level 1 | Death costs 1 level | Heal 50% between rounds (not built) |\n|---|---|---|---|---|\n");
            foreach (CharacterId id in Enum.GetValues(typeof(CharacterId)))
            {
                whatIf.Append($"| {CharacterDef.Get(id).Name} ");
                foreach ((float heal, int lost) in new[] { (0f, Balance.PveLevelsLostOnDeath), (0f, 99), (0f, 1), (0.5f, Balance.PveLevelsLostOnDeath) })
                {
                    var runs = new List<BalanceSim.Result>();
                    for (int seed = 0; seed < Seeds; seed++)
                        runs.Add(new BalanceSim(id, Difficulty.Normal, seed) { RoundHeal = heal, LevelsLostOnDeath = lost }.Run());
                    var l5 = runs.Where(r => r.LevelRound[5] > 0).ToList();
                    whatIf.Append($"| won {runs.Count(r => r.Won)}/{runs.Count}, {runs.Average(r => r.Seconds) / 60f:0.0} min, "
                        + $"{runs.Average(r => r.Deaths):0.0} deaths, L5 {(l5.Count > 0 ? "round " + l5.Average(r => r.LevelRound[5]).ToString("0") : "never")} ");
                }
                whatIf.Append("|\n");
            }
            File.WriteAllText("Logs/balance_sim.md", report.ToString() + whatIf + traces);
        }
    }

    /// <summary>
    /// A classical-AI player for balancing (no learning): stands still, shoots the nearest robot with its
    /// character's best attack for what it's fighting, levels up as soon as it can, and dodges each enemy attack
    /// with a chance set by the difficulty's wind-up times. The waves, enemy health and damage, the meter, levels,
    /// max HP, healing, deaths, drops and powerups are the game's own (WaveDirector, Balance, Fighter); the attack
    /// rates and the player's aim are assumptions (<see cref="Assumptions"/>).
    /// </summary>
    public sealed class BalanceSim
    {
        public const float Step = 0.05f;
        public const float HitRate = 0.8f;
        public const float RetargetSeconds = 0.4f;
        public const float WalkInSeconds = 1.5f;
        public const float TransformSeconds = 1f;

        public static readonly string Assumptions =
            "- The player stands still, hits 80% of shots, takes 0.4 s to switch target and 1 s to transform. Area damage only counts on its target.\n"
            + "- Attack rates (per hand unless two-handed): snap blasts 1.3 s (full charge), finger-gun blaster 0.6 s, crossbow 2.5 s, gust 0.8 s,\n"
            + "  webs 0.6 s held, spikes 1.2 s, dragon fist 0.8 s, sword slash 0.7 s (melee: 60% of the time in reach);\n"
            + "  two-handed: ki ball 2.5 s, bow 2 s, fire pillar 3 s, tornado 4 s (5 s of 12/s), poison bomb every 15 s; lightning 5 s on, 3 s off.\n"
            + "- Enemies: walk in for 1.5 s, attack at their spec's rate; the player dodges 45% (hard), 60% (normal), 72% (easy) of attacks.\n"
            + "  Hit damage: gunner/dasher 6, drone 4, rocketeer 10, sniper 14, tank 6, TRILOBITE 9, GOLIATH 14, times the round and difficulty scale.\n"
            + "- Bosses call in two reinforcements once, at 60% HP. Powerups every 30-45 s, drops as in the game.\n";

        /// <summary>One way a character attacks.</summary>
        public sealed class Attack
        {
            public string Name;
            public int Tier;
            /// <summary>Base damage of one use (held: per second, both hands together).</summary>
            public float Damage;
            /// <summary>Seconds between uses, per hand (held: 1).</summary>
            public float Period = 1f;
            /// <summary>How many can go at once: 2 for one-handed attacks, 1 for two-handed or held-with-both.</summary>
            public int Hands = 2;
            public bool Held;
            public bool OnBosses = true;
            /// <summary>Share of the time it can be used (melee: only in reach; lightning: its rests).</summary>
            public float Uptime = 1f;
            /// <summary>Extra damage at a level (Technoman's explosion, Terra's stuck rocks).</summary>
            public Func<int, float> Extra;

            public float PerUse(int level) => Damage * Balance.Boost(Tier, level) + (Extra?.Invoke(level) ?? 0f);
            public float PerSecond(int level) => PerUse(level) * Hands / Period * Uptime;
        }

        public static List<Attack> AttacksOf(CharacterId id)
        {
            var list = new List<Attack>();
            Attack Snap(string name) => new Attack { Name = name, Tier = 1, Damage = Balance.Snap, Period = 1.3f };
            switch (id)
            {
                case CharacterId.Technoman:
                    list.Add(new Attack { Name = "laser", Tier = 1, Damage = Balance.LaserPerHand * 2f, Hands = 1, Held = true });
                    list.Add(new Attack
                    {
                        Name = "repulsor blast", Tier = 2, Damage = Balance.RepulsorBlast, Period = 1.3f,
                        Extra = level => Balance.TierOf(level) >= 3 ? Balance.RepulsorBlast * Balance.Boost(2, level) * Balance.BlastSplash : 0f,
                    });
                    break;
                case CharacterId.KiFighter:
                    list.Add(Snap("comet"));
                    list.Add(new Attack { Name = "ki ball", Tier = 2, Damage = Balance.KiBallMax, Period = 2.5f, Hands = 1 });
                    list.Add(new Attack { Name = "dragon fist", Tier = 3, Damage = Balance.DragonFist, Period = 0.8f });
                    break;
                case CharacterId.Mage:
                    list.Add(Snap("fireball"));
                    list.Add(new Attack { Name = "flames", Tier = 2, Damage = Balance.FlamesPerHand * 2f, Hands = 1, Held = true });
                    list.Add(new Attack { Name = "fire pillar", Tier = 3, Damage = Balance.FirePillar, Period = 3f, Hands = 1 });
                    break;
                case CharacterId.Saber:
                    list.Add(new Attack { Name = "blaster", Tier = 1, Damage = Balance.Snap, Period = 0.6f });
                    list.Add(new Attack { Name = "sword slash", Tier = 2, Damage = Balance.SlashMax, Period = 0.7f, Uptime = 0.6f });
                    list.Add(new Attack { Name = "force throw", Tier = 3, Damage = Balance.WebFling, Period = 3f, Hands = 1, OnBosses = false });
                    break;
                case CharacterId.Archer:
                    list.Add(new Attack { Name = "crossbow", Tier = 1, Damage = Balance.Crossbow, Period = 2.5f });
                    list.Add(new Attack { Name = "bow", Tier = 2, Damage = Balance.BowMax, Period = 2f, Hands = 1 });
                    list.Add(new Attack { Name = "poison bomb", Tier = 3, Damage = Balance.PoisonPerSecond * Balance.PoisonSeconds, Period = 15f, Hands = 1 });
                    break;
                case CharacterId.Spider:
                    list.Add(new Attack { Name = "webs", Tier = 1, Damage = Balance.Web, Period = Balance.WebRepeat,
                        Extra = level => Balance.TierOf(level) >= 3 ? Balance.Web * (Balance.BlackWeb - 1f) * Balance.Boost(1, level) : 0f });
                    list.Add(new Attack { Name = "web fling", Tier = 2, Damage = Balance.WebFling, Period = 4f, Hands = 1, OnBosses = false });
                    break;
                case CharacterId.Elementalist:
                    list.Add(new Attack { Name = "gust", Tier = 1, Damage = Balance.Snap, Period = 0.8f });
                    list.Add(new Attack { Name = "lightning", Tier = 2, Damage = Balance.LightningPerBolt * 2f, Hands = 1, Held = true, Uptime = 5f / 8f });
                    list.Add(new Attack { Name = "tornado", Tier = 3, Damage = Balance.TornadoPerSecond * 5f, Period = 4f, Hands = 1 });
                    break;
                case CharacterId.Earth:
                    list.Add(new Attack
                    {
                        Name = "rock orb", Tier = 1, Damage = Balance.Snap, Period = 1.3f,
                        Extra = level => Balance.TierOf(level) >= 3 ? Balance.StuckRock : 0f,
                    });
                    list.Add(new Attack { Name = "spikes", Tier = 2, Damage = Balance.Spikes, Period = 1.2f });
                    break;
            }
            return list;
        }

        sealed class Foe
        {
            public EnemyKind Kind;
            public float Hp;
            public float ReadyAt;
            public float NextAttack;
            public bool Boss;
            public bool Summoned;
        }

        public sealed class Result
        {
            public int Seed;
            public bool Won;
            public float Seconds;
            public int Deaths;
            public float Taken;
            public readonly int[] LevelRound = new int[6];
            public float TrilobiteSeconds;
            public float GoliathSeconds;
            /// <summary>Per round: when it ended, the level, HP and deaths so far.</summary>
            public readonly StringBuilder Trace = new StringBuilder();
        }

        readonly CharacterId id;
        readonly Difficulty difficulty;
        readonly Random random;
        readonly int seed;
        readonly List<Attack> attacks;

        /// <summary>What-if: share of max HP healed when a round is cleared.</summary>
        public float RoundHeal;
        /// <summary>What-if: levels a death costs (the game: none in PVE).</summary>
        public int LevelsLostOnDeath = Balance.PveLevelsLostOnDeath;

        public BalanceSim(CharacterId id, Difficulty difficulty, int seed)
        {
            this.id = id;
            this.difficulty = difficulty;
            this.seed = seed;
            random = new Random(seed * 7919 + (int)id * 31 + (int)difficulty);
            attacks = AttacksOf(id);
        }

        float Range(float a, float b) => a + (float)random.NextDouble() * (b - a);

        float DodgeChance => difficulty == Difficulty.Easy ? 0.72f : difficulty == Difficulty.Hard ? 0.45f : 0.6f;

        static float HitDamage(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Drone: return 4f;
                case EnemyKind.Rocketeer: return 10f;
                case EnemyKind.Sniper: return 14f;
                case EnemyKind.Boss: return 9f;
                case EnemyKind.Mech: return 14f;
                default: return 6f;
            }
        }

        Attack Best(int level, bool boss)
        {
            int tier = Balance.TierOf(level);
            Attack best = null;
            foreach (Attack attack in attacks)
            {
                if (attack.Tier > tier || (boss && !attack.OnBosses))
                    continue;
                if (best == null || attack.PerSecond(level) > best.PerSecond(level))
                    best = attack;
            }
            return best;
        }

        public Result Run()
        {
            var result = new Result { Seed = seed };
            var fighter = new Fighter(3, 5f)
            {
                MaxLevel = Balance.PveMaxLevel, LevelCosts = Balance.PveLevelCosts, LevelsLostOnDeath = LevelsLostOnDeath,
            };
            fighter.Reset();
            fighter.Died += () => result.Deaths++;
            var waves = new WaveDirector(WavePlan.StoryRounds, 1, seed);
            var foes = new List<Foe>();
            float time = 0f, nextUse = 0f, transformUntil = 0f;
            float nextPowerup = Range(20f, 30f);
            float bossStart = -1f;
            Foe target = null;
            int tracedWave = 0;

            while (time < 3600f)
            {
                time += Step;
                waves.Tick(Step);
                if (waves.State == WaveState.Between && waves.Wave != tracedWave)
                {
                    tracedWave = waves.Wave;
                    fighter.Heal(fighter.MaxHp * RoundHeal);
                    result.Trace.Append($"round {waves.Wave} done at {time / 60f:0.0} min, level {fighter.Level}, "
                        + $"HP {fighter.Hp:0}/{fighter.MaxHp:0}, deaths {result.Deaths}, taken {result.Taken:0}\n");
                }
                fighter.Tick(Step);
                if (fighter.IsOut)
                    waves.AllPlayersOut();
                if (waves.State == WaveState.Won || waves.State == WaveState.Lost)
                    break;

                // Spawns, as the game does them.
                while (waves.TakeSpawn())
                {
                    EnemyKind kind = waves.BossWave ? waves.Boss
                        : WavePlan.KindFor(Math.Max(1, waves.Wave), random.NextDouble(), waves.SpawnedThisWave - 1);
                    foes.Add(new Foe
                    {
                        Kind = kind, Hp = Balance.EnemyHp(kind, waves.Wave), Boss = EnemySpec.IsBoss(kind),
                        ReadyAt = time + WalkInSeconds, NextAttack = time + WalkInSeconds + Range(1.2f, 2.4f),
                    });
                    if (EnemySpec.IsBoss(kind))
                        bossStart = time;
                }

                // Powerups and transforming.
                if (time >= nextPowerup)
                {
                    nextPowerup = time + Range(30f, 45f);
                    if (!fighter.IsDead)
                        fighter.ApplyPowerup((PowerupKind)random.Next(0, 3));
                }
                if (fighter.CanTransform && time >= transformUntil)
                {
                    fighter.TryTransform();
                    transformUntil = time + TransformSeconds;
                    if (result.LevelRound[fighter.Level] == 0)
                        result.LevelRound[fighter.Level] = waves.Wave;
                }

                // Enemies attack.
                float scale = Balance.EnemyDamage(waves.Wave, difficulty);
                foreach (Foe foe in foes)
                {
                    if (time < foe.NextAttack || foe.Hp <= 0f)
                        continue;
                    EnemySpec spec = EnemySpec.Of(foe.Kind);
                    foe.NextAttack = time + Range(spec.AttackMin, spec.AttackMax);
                    if (fighter.IsDead || random.NextDouble() < DodgeChance)
                        continue;
                    float lost = fighter.TakeDamage(HitDamage(foe.Kind) * scale);
                    result.Taken += lost;
                }

                // The player attacks the nearest (here: oldest) ready enemy with its best attack.
                if (fighter.IsDead || time < transformUntil)
                    continue;
                if (target == null || target.Hp <= 0f)
                {
                    target = foes.FirstOrDefault(f => f.Hp > 0f && time >= f.ReadyAt);
                    if (target != null)
                        nextUse = Math.Max(nextUse, time + RetargetSeconds);
                }
                if (target == null || time < nextUse)
                    continue;
                Attack attack = Best(fighter.Level, target.Boss);
                if (attack == null)
                    continue;
                float interval = attack.Held ? Step : attack.Period / attack.Hands / attack.Uptime;
                nextUse = time + interval;
                if (!attack.Held && random.NextDouble() > HitRate)
                    continue;
                float damage = attack.Held ? attack.PerSecond(fighter.Level) * Step * HitRate : attack.PerUse(fighter.Level);
                damage *= fighter.DamageMultiplier;
                float dealt = Math.Min(damage, target.Hp);
                target.Hp -= dealt;
                fighter.DealtDamage(dealt);
                if (target.Boss && !target.Summoned && target.Hp < Balance.EnemyHp(target.Kind, waves.Wave) * 0.6f)
                {
                    target.Summoned = true;
                    for (int i = 0; i < 2; i++)
                    {
                        EnemyKind kind = WavePlan.BossHelper(waves.Wave, random.NextDouble());
                        waves.AddExtra();
                        foes.Add(new Foe { Kind = kind, Hp = Balance.EnemyHp(kind, waves.Wave), ReadyAt = time + WalkInSeconds, NextAttack = time + 3f });
                    }
                }
                if (target.Hp <= 0f)
                {
                    if (target.Boss && bossStart >= 0f)
                    {
                        if (target.Kind == EnemyKind.Boss)
                            result.TrilobiteSeconds = time - bossStart;
                        else
                            result.GoliathSeconds = time - bossStart;
                    }
                    waves.EnemyKilled();
                    double roll = random.NextDouble();
                    if (roll < Balance.HealthDropChance)
                        fighter.ApplyPowerup(PowerupKind.Health);
                    else if (roll < Balance.HealthDropChance + Balance.PowerupDropChance)
                        fighter.ApplyPowerup((PowerupKind)random.Next(0, 3));
                    foes.Remove(target);
                    target = null;
                }
            }
            result.Won = waves.State == WaveState.Won;
            result.Seconds = time;
            return result;
        }
    }
}
