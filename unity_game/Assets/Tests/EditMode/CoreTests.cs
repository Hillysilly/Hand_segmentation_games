using NUnit.Framework;

namespace Repulsor.Tests
{
    public class FighterTests
    {
        const float Frame = 1f / 72f;

        static void Run(Fighter fighter, float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame)
                fighter.Tick(Frame);
        }

        [Test]
        public void StartsAtLevelOneWithFullHp()
        {
            var fighter = new Fighter();
            Assert.That(fighter.Hp, Is.EqualTo(100f));
            Assert.That(fighter.Level, Is.EqualTo(1));
            Assert.That(fighter.Lives, Is.EqualTo(3));
            Assert.That(fighter.Meter, Is.EqualTo(0f));
        }

        [Test]
        public void DamageLowersHp()
        {
            var fighter = new Fighter();
            Assert.That(fighter.TakeDamage(10f), Is.EqualTo(10f));
            Assert.That(fighter.Hp, Is.EqualTo(90f));
        }

        [Test]
        public void TakingDamageDoesNotFillTheMeter()
        {
            var fighter = new Fighter();
            fighter.TakeDamage(30f);
            Assert.That(fighter.Meter, Is.EqualTo(0f));
        }

        [Test]
        public void DealingDamageFillsTheMeter()
        {
            var fighter = new Fighter();
            fighter.DealtDamage(Fighter.MeterPerLevel / 2f);
            Assert.That(fighter.Meter, Is.EqualTo(0.5f).Within(1e-4));
            fighter.DealtDamage(Fighter.MeterPerLevel);
            Assert.That(fighter.Meter, Is.EqualTo(1f));
            Assert.That(fighter.CanTransform, Is.True);
        }

        [Test]
        public void TransformingRaisesMaxHpAndHealsHalf()
        {
            var fighter = new Fighter();
            fighter.TakeDamage(80f);
            fighter.DealtDamage(Fighter.MeterPerLevel);
            Assert.That(fighter.TryTransform(), Is.True);
            Assert.That(fighter.Level, Is.EqualTo(2));
            Assert.That(fighter.MaxHp, Is.EqualTo(150f));
            Assert.That(fighter.Hp, Is.EqualTo(20f + 75f));
            Assert.That(fighter.Meter, Is.EqualTo(0f));
        }

        [Test]
        public void TransformHealStopsAtMaxHp()
        {
            var fighter = new Fighter();
            fighter.DealtDamage(Fighter.MeterPerLevel);
            fighter.TryTransform();
            Assert.That(fighter.Hp, Is.EqualTo(150f));
        }

        [Test]
        public void LevelThreeIsTheTop()
        {
            var fighter = new Fighter();
            for (int i = 0; i < 2; i++)
            {
                fighter.DealtDamage(Fighter.MeterPerLevel);
                Assert.That(fighter.TryTransform(), Is.True);
            }
            Assert.That(fighter.MaxHp, Is.EqualTo(200f));
            fighter.DealtDamage(Fighter.MeterPerLevel);
            Assert.That(fighter.Meter, Is.EqualTo(0f));
            Assert.That(fighter.TryTransform(), Is.False);
            Assert.That(fighter.Level, Is.EqualTo(3));
        }

        [Test]
        public void CannotTransformWithoutAFullMeter()
        {
            var fighter = new Fighter();
            fighter.DealtDamage(Fighter.MeterPerLevel * 0.9f);
            Assert.That(fighter.TryTransform(), Is.False);
        }

        [Test]
        public void DyingLosesALifeAndResetsLevelAndMeter()
        {
            var fighter = new Fighter();
            fighter.DealtDamage(Fighter.MeterPerLevel);
            fighter.TryTransform();
            fighter.DealtDamage(Fighter.MeterPerLevel / 2f);
            fighter.TakeDamage(500f);
            Assert.That(fighter.IsDead, Is.True);
            Assert.That(fighter.Lives, Is.EqualTo(2));
            Assert.That(fighter.Level, Is.EqualTo(1));
            Assert.That(fighter.Meter, Is.EqualTo(0f));
        }

        [Test]
        public void RespawnsAfterTheTimerWithFullHpAndBriefProtection()
        {
            var fighter = new Fighter(3, 5f);
            fighter.TakeDamage(500f);
            Run(fighter, 4.8f);
            Assert.That(fighter.IsDead, Is.True);
            Run(fighter, 0.3f);
            Assert.That(fighter.IsDead, Is.False);
            Assert.That(fighter.Hp, Is.EqualTo(100f));
            Assert.That(fighter.TakeDamage(10f), Is.EqualTo(0f), "spawn protection");
        }

        [Test]
        public void NoRespawnWithoutLives()
        {
            var fighter = new Fighter(1, 1f);
            fighter.TakeDamage(500f);
            Assert.That(fighter.IsOut, Is.True);
            Run(fighter, 3f);
            Assert.That(fighter.IsDead, Is.True);
        }

        [Test]
        public void DeadFightersTakeNoDamage()
        {
            var fighter = new Fighter();
            fighter.TakeDamage(500f);
            Assert.That(fighter.TakeDamage(10f), Is.EqualTo(0f));
            Assert.That(fighter.Lives, Is.EqualTo(2));
        }

        [Test]
        public void HealsOnlyAfterFiveQuietSeconds()
        {
            var fighter = new Fighter();
            fighter.TakeDamage(50f);
            Run(fighter, 4.9f);
            Assert.That(fighter.Hp, Is.EqualTo(50f));
            Run(fighter, 2f);
            Assert.That(fighter.Hp, Is.GreaterThan(54f).And.LessThan(57f));
        }

        [Test]
        public void AttackingDelaysHealing()
        {
            var fighter = new Fighter();
            fighter.TakeDamage(50f);
            Run(fighter, 4f);
            fighter.DealtDamage(1f);
            Run(fighter, 4f);
            Assert.That(fighter.Hp, Is.EqualTo(50f));
        }

        [Test]
        public void HealingStopsAtMax()
        {
            var fighter = new Fighter();
            fighter.TakeDamage(1f);
            Run(fighter, 10f);
            Assert.That(fighter.Hp, Is.EqualTo(100f));
        }

        [Test]
        public void ShieldBlocksDamage()
        {
            var fighter = new Fighter();
            fighter.Shield.Hold(true);
            fighter.Tick(Frame);
            Assert.That(fighter.Shield.Up, Is.True);
            Assert.That(fighter.TakeDamage(20f), Is.EqualTo(0f));
            Assert.That(fighter.Hp, Is.EqualTo(100f));
        }

        [Test]
        public void InvincibilityBlocksDamageThenWearsOff()
        {
            var fighter = new Fighter();
            fighter.ApplyPowerup(PowerupKind.Invincible);
            Assert.That(fighter.TakeDamage(20f), Is.EqualTo(0f));
            Run(fighter, Fighter.InvincibleSeconds + 0.1f);
            Assert.That(fighter.TakeDamage(20f), Is.EqualTo(20f));
        }

        [Test]
        public void DamageBoostMultipliesOutgoingDamage()
        {
            var fighter = new Fighter();
            Assert.That(fighter.Outgoing(4f), Is.EqualTo(4f));
            fighter.ApplyPowerup(PowerupKind.DamageBoost);
            Assert.That(fighter.Outgoing(4f), Is.EqualTo(6f));
            Run(fighter, Fighter.DamageBoostSeconds + 0.1f);
            Assert.That(fighter.Outgoing(4f), Is.EqualTo(4f));
        }

        [Test]
        public void ChargePowerupAddsToTheMeter()
        {
            var fighter = new Fighter();
            fighter.ApplyPowerup(PowerupKind.Charge);
            Assert.That(fighter.Meter, Is.EqualTo(Fighter.ChargePowerup).Within(1e-4));
        }
    }

    public class ShieldGaugeTests
    {
        const float Frame = 1f / 72f;

        static void Run(ShieldGauge shield, bool held, float seconds)
        {
            shield.Hold(held);
            for (float t = 0f; t < seconds; t += Frame)
                shield.Tick(Frame);
        }

        [Test]
        public void BreaksAfterAFullGaugeOfHolding()
        {
            var shield = new ShieldGauge();
            Run(shield, true, ShieldGauge.HoldSeconds - 0.1f);
            Assert.That(shield.Up, Is.True);
            Run(shield, true, 0.2f);
            Assert.That(shield.Up, Is.False);
            Assert.That(shield.Broken, Is.True);
        }

        [Test]
        public void StaysDownForTheCooldownThenComesBackFull()
        {
            var shield = new ShieldGauge();
            Run(shield, true, ShieldGauge.HoldSeconds + 0.1f);
            Run(shield, true, ShieldGauge.BreakCooldown - 0.2f);
            Assert.That(shield.Up, Is.False);
            Run(shield, false, 0.3f);
            Assert.That(shield.Broken, Is.False);
            Assert.That(shield.Fraction, Is.EqualTo(1f));
        }

        [Test]
        public void RechargesWhenLetGo()
        {
            var shield = new ShieldGauge();
            Run(shield, true, 2f);
            float low = shield.Remaining;
            Run(shield, false, 1f);
            Assert.That(shield.Remaining, Is.EqualTo(low + ShieldGauge.RechargePerSecond).Within(0.03));
        }

        [Test]
        public void NeedsSomeGaugeToGoUpAgain()
        {
            var shield = new ShieldGauge();
            Run(shield, true, ShieldGauge.HoldSeconds - 0.15f);
            Run(shield, false, 0.1f);
            Run(shield, true, Frame);
            Assert.That(shield.Up, Is.False);
        }
    }

    public class WaveDirectorTests
    {
        const float Frame = 1f / 72f;

        static int Run(WaveDirector waves, float seconds, bool killAll)
        {
            int spawned = 0;
            for (float t = 0f; t < seconds; t += Frame)
            {
                waves.Tick(Frame);
                while (waves.TakeSpawn())
                {
                    spawned++;
                    if (killAll)
                        waves.EnemyKilled();
                }
            }
            return spawned;
        }

        [Test]
        public void CountsDownThenStartsWaveOne()
        {
            var waves = new WaveDirector(5);
            Assert.That(waves.State, Is.EqualTo(WaveState.Countdown));
            Run(waves, WaveDirector.CountdownSeconds + 0.1f, false);
            Assert.That(waves.State, Is.EqualTo(WaveState.Fighting));
            Assert.That(waves.Wave, Is.EqualTo(1));
        }

        [Test]
        public void SpawnsAreSpacedAndCapped()
        {
            var waves = new WaveDirector(5);
            int spawned = Run(waves, 60f, false);
            Assert.That(spawned, Is.LessThanOrEqualTo(WaveDirector.MaxAlive));
            Assert.That(waves.Alive, Is.EqualTo(spawned));
        }

        [Test]
        public void ClearingAWaveStartsABreakThenTheNext()
        {
            var waves = new WaveDirector(5);
            int spawned = Run(waves, WaveDirector.CountdownSeconds + 8f, true);
            Assert.That(spawned, Is.EqualTo(WaveDirector.EnemiesInWave(1)));
            Assert.That(waves.State, Is.EqualTo(WaveState.Between));
            Run(waves, WaveDirector.BetweenWavesSeconds + 0.1f, false);
            Assert.That(waves.Wave, Is.EqualTo(2));
        }

        [Test]
        public void WinsAfterTheLastWave()
        {
            var waves = new WaveDirector(2);
            Run(waves, 120f, true);
            Assert.That(waves.State, Is.EqualTo(WaveState.Won));
            // The second (last) wave is the boss on its own.
            Assert.That(waves.Kills, Is.EqualTo(WaveDirector.EnemiesInWave(1) + 1));
        }

        [Test]
        public void TheLastWaveIsTheBoss()
        {
            var waves = new WaveDirector(3, 3);
            Assert.That(waves.BossWave, Is.False, "not during the countdown");
            Run(waves, WaveDirector.CountdownSeconds + 0.1f, false);
            Assert.That(waves.Wave, Is.EqualTo(3));
            Assert.That(waves.BossWave, Is.True);
            Assert.That(waves.ToSpawn + waves.Alive, Is.EqualTo(1));
            // Its helpers count toward the wave, so it isn't won while they're up.
            waves.AddExtra();
            waves.AddExtra();
            waves.EnemyKilled();
            waves.Tick(0.1f);
            Assert.That(waves.State, Is.EqualTo(WaveState.Fighting));
        }

        [Test]
        public void EarlyWavesOnlySendTheSimpleRobots()
        {
            for (double roll = 0; roll < 1; roll += 0.01)
            {
                EnemyKind kind = WavePlan.KindFor(1, roll);
                Assert.That(kind, Is.EqualTo(EnemyKind.Robot), "round 1 is gunners");
                Assert.That(WavePlan.KindFor(2, roll) == EnemyKind.Robot || WavePlan.KindFor(2, roll) == EnemyKind.Drone, "round 2 adds drones");
                Assert.That(WavePlan.KindFor(5, roll), Is.Not.EqualTo(EnemyKind.Boss));
            }
            var seen = new System.Collections.Generic.HashSet<EnemyKind>();
            for (double roll = 0; roll < 1; roll += 0.005)
                seen.Add(WavePlan.KindFor(9, roll));
            Assert.That(seen, Is.EquivalentTo(new[] { EnemyKind.Robot, EnemyKind.Drone, EnemyKind.Dasher,
                EnemyKind.Rocketeer, EnemyKind.Sniper, EnemyKind.Tank }));
            // One new kind at a time, spread over the story, never in the boss round.
            int newRounds = 0;
            for (int wave = 1; wave <= WavePlan.StoryRounds; wave++)
            {
                Assert.That(WavePlan.NewIn(wave).Count, Is.LessThanOrEqualTo(1), $"round {wave}");
                if (WavePlan.NewIn(wave).Count > 0)
                    newRounds++;
            }
            Assert.That(newRounds, Is.EqualTo(6));
            Assert.That(WavePlan.NewIn(WavePlan.StoryRounds), Is.Empty);
            Assert.That(WavePlan.Introduced(EnemyKind.Tank), Is.LessThan(WavePlan.StoryRounds - 1), "every kind gets a round before the boss");
            // The yellow melee robot isn't sent any more, nor called in by the boss.
            for (double roll = 0; roll < 1; roll += 0.01)
            {
                Assert.That(WavePlan.BossHelper(roll), Is.Not.EqualTo(EnemyKind.Brawler));
                for (int wave = 1; wave < WavePlan.StoryRounds; wave++)
                    Assert.That(WavePlan.KindFor(wave, roll), Is.Not.EqualTo(EnemyKind.Brawler));
            }
        }

        [Test]
        public void EveryEnemyKindHasASpecInOrder()
        {
            Assert.That(EnemySpec.All.Length, Is.EqualTo(System.Enum.GetValues(typeof(EnemyKind)).Length));
            for (int i = 0; i < EnemySpec.All.Length; i++)
            {
                Assert.That((int)EnemySpec.All[i].Kind, Is.EqualTo(i));
                Assert.That(EnemySpec.All[i].Radius * 2f, Is.LessThanOrEqualTo(EnemySpec.All[i].Height + 0.01f), EnemySpec.All[i].Name);
            }
            Assert.That(EnemySpec.Of(EnemyKind.Boss).Height, Is.InRange(2.1f, 2.45f), "7 to 8 feet");
        }

        [Test]
        public void LosesWhenEveryoneIsOut()
        {
            var waves = new WaveDirector(5);
            Run(waves, 5f, false);
            waves.AllPlayersOut();
            Assert.That(waves.State, Is.EqualTo(WaveState.Lost));
        }
    }

    public class ScoringTests
    {
        [Test]
        public void KillsWavesAndDamageAddUp()
        {
            Assert.That(Scoring.Score(10, 2, 100f, false, 300f), Is.EqualTo(10 * 100 + 2 * 250 + 200));
        }

        [Test]
        public void FasterWinsScoreMore()
        {
            int slow = Scoring.Score(20, 5, 300f, true, 500f);
            int fast = Scoring.Score(20, 5, 300f, true, 200f);
            Assert.That(fast, Is.GreaterThan(slow));
            Assert.That(slow, Is.GreaterThan(Scoring.Score(20, 5, 300f, false, 500f)));
        }
    }

    public class RoundTrackerTests
    {
        [Test]
        public void BestOfThree()
        {
            var rounds = new RoundTracker(2, 2);
            Assert.That(rounds.RoundWon(0), Is.False);
            Assert.That(rounds.RoundWon(1), Is.False);
            Assert.That(rounds.Round, Is.EqualTo(3));
            Assert.That(rounds.RoundWon(1), Is.True);
            Assert.That(rounds.Winner, Is.EqualTo(1));
        }
    }
}
