using NUnit.Framework;
using UnityEngine;

namespace Repulsor.Tests
{
    public class AimTests
    {
        sealed class Dummy : IHittable
        {
            public Vector3 Center { get; set; }
            public float Radius { get; set; } = 0.3f;
            public bool Alive { get; set; } = true;
            public Team Team { get; set; } = Team.Enemies;
            public float Hp => 10f;
            public float MaxHp => 10f;
            public float TakeHit(float damage, Vector3 from) => 0f;
            public Vector3 BarPoint => Center;
        }

        Dummy dummy;

        [SetUp]
        public void SetUp()
        {
            Hittables.All.Clear();
            dummy = new Dummy { Center = new Vector3(0.5f, 1.4f, 4f) };
            Hittables.Add(dummy);
        }

        [TearDown]
        public void TearDown() => Hittables.All.Clear();

        [Test]
        public void DragonFistLocksOnFurtherOffThanAssist()
        {
            Vector3 from = new Vector3(0f, 1.3f, 0f);
            // About 30 degrees off: too far for shot assist, inside the dragon fist's lock-on.
            Vector3 punch = Quaternion.Euler(0f, -30f, 0f) * (dummy.Center - from).normalized;
            Assert.That(Aim.Target(from, punch, Aim.AssistDegrees, Aim.AssistRange), Is.Null);
            Assert.That(Aim.Target(from, punch, Aim.LockDegrees, Aim.AssistRange), Is.SameAs(dummy));
        }

        [Test]
        public void LockOnPicksTheEnemyNearestTheLine()
        {
            var other = new Dummy { Center = new Vector3(-1.5f, 1.4f, 4f) };
            Hittables.Add(other);
            Vector3 from = new Vector3(0f, 1.3f, 0f);
            Vector3 punch = (other.Center - from).normalized;
            Assert.That(Aim.Target(from, punch, Aim.LockDegrees, Aim.AssistRange), Is.SameAs(other));
        }

        [Test]
        public void FlamesBurnOnlyInsideTheirShortCone()
        {
            Vector3 from = new Vector3(0f, 1.4f, 0f);
            Vector3 toward = (dummy.Center - from).normalized;
            dummy.Center = from + toward * (Flamethrower.Reach * 0.8f);
            Assert.That(Flamethrower.InCone(from, toward, Flamethrower.Reach, dummy), Is.True);
            Assert.That(Flamethrower.InCone(from, Quaternion.Euler(0f, 40f, 0f) * toward, Flamethrower.Reach, dummy), Is.False, "off to the side");
            dummy.Center = from + toward * (Flamethrower.Reach + 1f);
            Assert.That(Flamethrower.InCone(from, toward, Flamethrower.Reach, dummy), Is.False, "out of reach");
        }

        [Test]
        public void ShotsNearAnEnemyBendOntoIt()
        {
            Vector3 from = new Vector3(0f, 1.3f, 0f);
            Vector3 aimed = Aim.Assist(from, Vector3.forward);
            Assert.That(Vector3.Angle(aimed, dummy.Center - from), Is.LessThan(0.01f));
        }

        [Test]
        public void ShotsWellWideAreLeftAlone()
        {
            Vector3 wide = Quaternion.AngleAxis(40f, Vector3.up) * Vector3.forward;
            Assert.That(Aim.Assist(new Vector3(0f, 1.3f, 0f), wide), Is.EqualTo(wide));
        }

        [Test]
        public void PlayersAndTheDeadAreNotAimedAt()
        {
            dummy.Team = Team.Players;
            Assert.That(Aim.Assist(new Vector3(0f, 1.3f, 0f), Vector3.forward), Is.EqualTo(Vector3.forward));
            dummy.Team = Team.Enemies;
            dummy.Alive = false;
            Assert.That(Aim.Assist(new Vector3(0f, 1.3f, 0f), Vector3.forward), Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void AFistAimsAlongTheArmNotThePalm()
        {
            // A fist held out in front at chest height, palm facing down.
            var hand = new HandPose(false);
            hand.Pose(new Vector3(0.15f, 1.25f, 0.45f), Quaternion.identity, 0.9f, 0.9f, 0.9f, 0.9f, ThumbPose.Tucked);
            hand.Tracked = true;
            hand.Analyze();
            Stance body = Stance.From(new Vector3(0f, 1.6f, 0f), Vector3.forward);
            Vector3 aim = Aim.Arm(body, hand, hand.Palm, Vector3.down);
            Assert.That(Vector3.Angle(aim, Vector3.forward), Is.LessThan(30f), "should point ahead, not at the floor");
            Assert.That(aim.z, Is.GreaterThan(0.8f));
        }

        [Test]
        public void UntrackedHandsKeepThePalmDirection()
        {
            var hand = new HandPose(true);
            Stance body = Stance.From(new Vector3(0f, 1.6f, 0f), Vector3.forward);
            Assert.That(Aim.Arm(body, hand, Vector3.zero, Vector3.up), Is.EqualTo(Vector3.up));
        }
    }

    public class PracticeLevelTests
    {
        [Test]
        public void SetLevelJumpsWithFullHpAndNoMeter()
        {
            var fighter = new Fighter(3, 5f);
            fighter.Reset();
            fighter.SetLevel(3);
            Assert.That(fighter.Level, Is.EqualTo(3));
            Assert.That(fighter.Hp, Is.EqualTo(Fighter.MaxHpAt(3)));
            Assert.That(fighter.Meter, Is.EqualTo(0f));
            fighter.SetLevel(9);
            Assert.That(fighter.Level, Is.EqualTo(Fighter.MaxLevel));
        }
    }
}
