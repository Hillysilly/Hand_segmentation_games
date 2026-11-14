using NUnit.Framework;

namespace Repulsor.Tests
{
    public class GlowTimerTests
    {
        [Test]
        public void StartsOff()
        {
            var glow = new GlowTimer();
            Assert.That(glow.On, Is.False);
            Assert.That(glow.Strength, Is.EqualTo(0f));
        }

        [Test]
        public void ATransformationLightsItForAWhileThenItFades()
        {
            var glow = new GlowTimer();
            glow.Transformed();
            Assert.That(glow.Strength, Is.EqualTo(1f));
            glow.Tick(GlowTimer.AfterTransform - GlowTimer.FadeSeconds * 0.5f);
            Assert.That(glow.Strength, Is.EqualTo(0.5f).Within(1e-4f));
            glow.Tick(1f);
            Assert.That(glow.On, Is.False);
        }

        [Test]
        public void PowerupsLastAsLongAsTheirEffect()
        {
            var glow = new GlowTimer();
            glow.PoweredUp(PowerupKind.DamageBoost);
            Assert.That(glow.SecondsLeft, Is.EqualTo(Fighter.DamageBoostSeconds));
            glow.Clear();
            glow.PoweredUp(PowerupKind.Invincible);
            Assert.That(glow.SecondsLeft, Is.EqualTo(Fighter.InvincibleSeconds));
            glow.Clear();
            glow.PoweredUp(PowerupKind.Charge);
            Assert.That(glow.SecondsLeft, Is.EqualTo(GlowTimer.AfterChargePowerup));
        }

        [Test]
        public void AShortTriggerDoesNotCutALongOneShort()
        {
            var glow = new GlowTimer();
            glow.PoweredUp(PowerupKind.DamageBoost);
            glow.PoweredUp(PowerupKind.Charge);
            Assert.That(glow.SecondsLeft, Is.EqualTo(Fighter.DamageBoostSeconds));
        }

        [Test]
        public void PermanentStaysOn()
        {
            var glow = new GlowTimer { Permanent = true };
            glow.Tick(100f);
            Assert.That(glow.Strength, Is.EqualTo(1f));
            Assert.That(GlowTimer.PermanentFor(CharacterId.Technoman, 3), Is.True);
            Assert.That(GlowTimer.PermanentFor(CharacterId.Technoman, 2), Is.False);
            Assert.That(GlowTimer.PermanentFor(CharacterId.Saber, 3), Is.False);
        }

        [Test]
        public void TheFighterAnnouncesPowerups()
        {
            var fighter = new Fighter();
            PowerupKind? got = null;
            fighter.PoweredUp += kind => got = kind;
            fighter.ApplyPowerup(PowerupKind.Invincible);
            Assert.That(got, Is.EqualTo(PowerupKind.Invincible));
        }
    }
}
