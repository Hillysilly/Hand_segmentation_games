using NUnit.Framework;
using UnityEngine;

namespace Repulsor.Tests
{
    public class HandShapeTests
    {
        static HandPose Make(float index, float middle, float ring, float little, ThumbPose thumb, bool left = false)
        {
            var hand = new HandPose(left);
            hand.Pose(new Vector3(0.2f, 1.3f, 0.3f), Quaternion.identity, index, middle, ring, little, thumb);
            hand.Analyze();
            return hand;
        }

        [Test]
        public void Fist()
        {
            HandPose hand = Make(1f, 1f, 1f, 1f, ThumbPose.Tucked);
            Assert.That(hand.Fist, Is.True);
            Assert.That(hand.Open || hand.Gun || hand.Cup || hand.Ok || hand.SpiderSign, Is.False);
        }

        [Test]
        public void OpenHand()
        {
            HandPose hand = Make(0f, 0f, 0f, 0f, ThumbPose.Out);
            Assert.That(hand.Open, Is.True);
            Assert.That(hand.Fist || hand.Gun || hand.Cup || hand.Ok || hand.SpiderSign, Is.False);
        }

        [Test]
        public void OkSign()
        {
            HandPose hand = Make(0.5f, 0f, 0f, 0f, ThumbPose.PinchIndex);
            Assert.That(hand.Ok, Is.True);
        }

        [Test]
        public void FingerGun()
        {
            HandPose hand = Make(0f, 0f, 1f, 1f, ThumbPose.Out);
            Assert.That(hand.Gun, Is.True);
            Assert.That(hand.Fist || hand.Open || hand.SpiderSign, Is.False);
        }

        [Test]
        public void FingerGunNeedsTheThumbUp()
        {
            Assert.That(Make(0f, 0f, 1f, 1f, ThumbPose.Tucked).Gun, Is.False);
        }

        [Test]
        public void SpiderSign()
        {
            HandPose hand = Make(0f, 1f, 1f, 0f, ThumbPose.Out);
            Assert.That(hand.SpiderSign, Is.True);
            Assert.That(hand.Gun, Is.False);
        }

        [Test]
        public void Cup()
        {
            HandPose hand = Make(0.45f, 0.45f, 0.45f, 0.45f, ThumbPose.Out);
            Assert.That(hand.Cup, Is.True);
            Assert.That(hand.Fist || hand.Open, Is.False);
        }

        [Test]
        public void TuningLinesChangeThresholds()
        {
            float before = HandPose.ThumbOutAbove;
            try
            {
                int applied = Tuning.Apply(new[] { "# comment", "HandPose.ThumbOutAbove = 0.42", "Nope.Field = 1", "PunchDetector.MinSpeed=3" });
                Assert.That(applied, Is.EqualTo(2));
                Assert.That(HandPose.ThumbOutAbove, Is.EqualTo(0.42f));
                Assert.That(PunchDetector.MinSpeed, Is.EqualTo(3f));
            }
            finally
            {
                HandPose.ThumbOutAbove = before;
                PunchDetector.MinSpeed = 2.5f;
            }
        }

        [Test]
        public void ExtensionsMatchTheClassifiersScale()
        {
            HandPose open = Make(0f, 0f, 0f, 0f, ThumbPose.Out);
            HandPose fist = Make(1f, 1f, 1f, 1f, ThumbPose.Tucked);
            Assert.That(open.Index, Is.GreaterThan(1.7f));
            Assert.That(fist.Index, Is.LessThan(1.1f));
        }
    }

    public class GestureDetectorTests
    {
        const float Frame = 1f / 72f;
        static readonly Stance Body = Stance.From(new Vector3(0f, 1.6f, 0f), Vector3.forward);

        static void Pose(HandPose hand, Vector3 wrist, Vector3 forward, Vector3 back, float curl, ThumbPose thumb)
        {
            hand.Pose(wrist, Quaternion.LookRotation(forward, back), curl, curl, curl, curl, thumb);
            hand.Analyze();
        }

        static void Fist(HandPose hand, Vector3 wrist) => Pose(hand, wrist, Vector3.forward, Vector3.up, 1f, ThumbPose.Tucked);
        static void Open(HandPose hand, Vector3 wrist) => Pose(hand, wrist, Vector3.forward, Vector3.up, 0f, ThumbPose.Out);

        static void CrossArms(HandPose left, HandPose right)
        {
            Pose(left, new Vector3(0.08f, 1.25f, 0.3f), new Vector3(0.6f, 0.8f, 0f), Vector3.forward, 0f, ThumbPose.Out);
            Pose(right, new Vector3(-0.08f, 1.25f, 0.3f), new Vector3(-0.6f, 0.8f, 0f), Vector3.forward, 0f, ThumbPose.Out);
        }

        [Test]
        public void CrossedWristsRaiseTheShield()
        {
            var left = new HandPose(true);
            var right = new HandPose(false);
            CrossArms(left, right);
            var x = new XArmsDetector();
            Assert.That(x.Step(left, right, Body, Frame), Is.False, "needs a moment");
            for (int i = 0; i < 12; i++)
                x.Step(left, right, Body, Frame);
            Assert.That(x.Held, Is.True);
        }

        [Test]
        public void HandsSideBySideAreNotAnX()
        {
            var left = new HandPose(true);
            var right = new HandPose(false);
            Open(left, new Vector3(-0.1f, 1.25f, 0.35f));
            Open(right, new Vector3(0.1f, 1.25f, 0.35f));
            Assert.That(XArmsDetector.IsCrossed(left, right, Body), Is.False);
        }

        [Test]
        public void DoubleOkFiresOnceAfterTwoSeconds()
        {
            var left = new HandPose(true);
            var right = new HandPose(false);
            left.Pose(new Vector3(-0.2f, 1.3f, 0.3f), Quaternion.identity, 0.5f, 0f, 0f, 0f, ThumbPose.PinchIndex);
            right.Pose(new Vector3(0.2f, 1.3f, 0.3f), Quaternion.identity, 0.5f, 0f, 0f, 0f, ThumbPose.PinchIndex);
            left.Analyze();
            right.Analyze();
            var ok = new DoubleOkDetector();
            int fired = 0;
            for (float t = 0f; t < 1.9f; t += Frame)
                fired += ok.Step(left, right, Frame) ? 1 : 0;
            Assert.That(fired, Is.EqualTo(0));
            for (float t = 0f; t < 2f; t += Frame)
                fired += ok.Step(left, right, Frame) ? 1 : 0;
            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public void PowerUpPoseIsFistsLowAtTheSides()
        {
            var left = new HandPose(true);
            var right = new HandPose(false);
            Fist(left, new Vector3(-0.25f, 0.85f, 0.1f));
            Fist(right, new Vector3(0.25f, 0.85f, 0.1f));
            var power = new PowerUpDetector();
            bool fired = false;
            for (float t = 0f; t < 1.1f; t += Frame)
                fired |= power.Step(left, right, Body, Frame);
            Assert.That(fired, Is.True);
        }

        [Test]
        public void FistsInFrontAreNotThePowerUpPose()
        {
            var left = new HandPose(true);
            var right = new HandPose(false);
            Fist(left, new Vector3(-0.1f, 1.3f, 0.4f));
            Fist(right, new Vector3(0.1f, 1.3f, 0.4f));
            Assert.That(PowerUpDetector.IsPose(left, right, Body), Is.False);
        }

        [Test]
        public void HandsTogetherChargeThenPushToFire()
        {
            var left = new HandPose(true);
            var right = new HandPose(false);
            Pose(left, new Vector3(-0.04f, 1.3f, 0.3f), Vector3.forward, Vector3.left, 0.2f, ThumbPose.Out);
            Pose(right, new Vector3(0.04f, 1.3f, 0.3f), Vector3.forward, Vector3.right, 0.2f, ThumbPose.Out);
            var charge = new TwoHandChargeDetector();
            for (float t = 0f; t < 1f; t += Frame)
                Assert.That(charge.Step(left, right, Body, Frame, out _, out _), Is.False);
            Assert.That(charge.Charge, Is.EqualTo(0.5f).Within(0.02));

            left.Velocity = right.Velocity = new Vector3(0f, 0f, 2f);
            Assert.That(charge.Step(left, right, Body, Frame, out Vector3 direction, out float power), Is.True);
            Assert.That(power, Is.EqualTo(0.5f).Within(0.02));
            Assert.That(direction.z, Is.GreaterThan(0.95f));
        }

        [Test]
        public void TriangleAimsAtTheFloorAndFistsFire()
        {
            var left = new HandPose(true);
            var right = new HandPose(false);
            Open(left, new Vector3(-0.08f, 1.36f, 0.3f));
            Open(right, new Vector3(0.08f, 1.36f, 0.3f));
            left.Joints[(int)Joint.IndexTip] = new Vector3(-0.01f, 1.4f, 0.4f);
            right.Joints[(int)Joint.IndexTip] = new Vector3(0.01f, 1.4f, 0.4f);
            left.Joints[(int)Joint.ThumbTip] = new Vector3(-0.01f, 1.32f, 0.4f);
            right.Joints[(int)Joint.ThumbTip] = new Vector3(0.01f, 1.32f, 0.4f);

            var triangle = new TriangleDetector();
            for (float t = 0f; t < 0.4f; t += Frame)
                triangle.Step(left, right, Body, Frame, out _);
            Assert.That(triangle.Aiming, Is.True);
            Assert.That(triangle.Target.y, Is.EqualTo(0f));
            Assert.That(triangle.Target.z, Is.EqualTo(2.67f).Within(0.1));

            Fist(left, new Vector3(-0.08f, 1.36f, 0.3f));
            Fist(right, new Vector3(0.08f, 1.36f, 0.3f));
            Assert.That(triangle.Step(left, right, Body, Frame, out Vector3 target), Is.True);
            Assert.That(target.z, Is.EqualTo(2.67f).Within(0.1));
        }

        [Test]
        public void BowDrawsAndLooses()
        {
            var left = new HandPose(true);
            var right = new HandPose(false);
            var bow = new BowDetector();
            Fist(left, new Vector3(-0.03f, 1.4f, 0.5f));
            Fist(right, new Vector3(0.03f, 1.4f, 0.5f));
            for (float t = 0f; t < 0.3f; t += Frame)
                bow.Step(left, right, Body, Frame, out _, out _, out _);
            Assert.That(bow.Formed, Is.True);

            Fist(right, new Vector3(0.1f, 1.4f, 0.1f));
            bow.Step(left, right, Body, Frame, out _, out _, out _);
            Assert.That(bow.FrontIsLeft, Is.True);
            Assert.That(bow.Draw, Is.GreaterThan(0.5f));

            Open(right, new Vector3(0.1f, 1.4f, 0.1f));
            Assert.That(bow.Step(left, right, Body, Frame, out _, out _, out _), Is.False, "two frames open");
            Assert.That(bow.Step(left, right, Body, Frame, out _, out Vector3 direction, out float power), Is.True);
            Assert.That(direction.z, Is.GreaterThan(0.9f));
            Assert.That(power, Is.GreaterThan(0.5f));
            Assert.That(bow.Formed, Is.False);
        }

        [Test]
        public void FingerGunFiresOnAnUpwardFlickAlongTheOldAim()
        {
            var hand = new HandPose(false);
            var gun = new FingerGunDetector();
            Vector3 wrist = new Vector3(0.15f, 1.3f, 0.3f);
            for (float t = 0f; t < 0.3f; t += Frame)
            {
                hand.Pose(wrist, Quaternion.LookRotation(Vector3.forward, Vector3.right), 0f, 0f, 1f, 1f, ThumbPose.Out);
                hand.Analyze();
                Assert.That(gun.Step(hand, Body, Frame, out _, out _), Is.False);
            }
            Assert.That(gun.Aiming, Is.True);

            bool fired = false;
            Vector3 direction = default;
            for (int i = 1; i <= 6 && !fired; i++)
            {
                Quaternion up = Quaternion.AngleAxis(-6f * i, Vector3.right);
                hand.Pose(wrist, up * Quaternion.LookRotation(Vector3.forward, Vector3.right), 0f, 0f, 1f, 1f, ThumbPose.Out);
                hand.Analyze();
                fired = gun.Step(hand, Body, Frame, out _, out direction);
            }
            Assert.That(fired, Is.True);
            Assert.That(direction.z, Is.GreaterThan(0.95f));
            Assert.That(Mathf.Abs(direction.y), Is.LessThan(0.25f));
        }

        [Test]
        public void SlowFingerGunTurnsDoNotFire()
        {
            var hand = new HandPose(false);
            var gun = new FingerGunDetector();
            Vector3 wrist = new Vector3(0.15f, 1.3f, 0.3f);
            for (int i = 0; i < 72; i++)
            {
                Quaternion up = Quaternion.AngleAxis(-0.4f * i, Vector3.right);
                hand.Pose(wrist, up * Quaternion.LookRotation(Vector3.forward, Vector3.right), 0f, 0f, 1f, 1f, ThumbPose.Out);
                hand.Analyze();
                Assert.That(gun.Step(hand, Body, Frame, out _, out _), Is.False);
            }
        }

        [Test]
        public void FastFistForwardIsAPunch()
        {
            var hand = new HandPose(false);
            Fist(hand, new Vector3(0.2f, 1.3f, 0.3f));
            var punch = new PunchDetector();
            hand.Velocity = new Vector3(0f, 0f, 1f);
            Assert.That(punch.Step(hand, Body, Frame, out _, out _), Is.False);
            hand.Velocity = new Vector3(0f, 0f, 3f);
            Assert.That(punch.Step(hand, Body, Frame, out _, out Vector3 direction), Is.True);
            Assert.That(direction.z, Is.GreaterThan(0.99f));
            Assert.That(punch.Step(hand, Body, Frame, out _, out _), Is.False, "cooldown");
        }

        [Test]
        public void SpiderSignShootsOncePerSign()
        {
            var hand = new HandPose(false);
            var web = new SpiderSignDetector();
            int shots = 0;
            for (int i = 0; i < 60; i++)
            {
                hand.Pose(new Vector3(0.2f, 1.3f, 0.3f), Quaternion.identity, 0f, 1f, 1f, 0f, ThumbPose.Out);
                hand.Analyze();
                shots += web.Step(hand, Body, Frame, out _, out _) ? 1 : 0;
            }
            Assert.That(shots, Is.EqualTo(1));
        }

        [Test]
        public void CupGrabsAndAFastSwingThrows()
        {
            var hand = new HandPose(false);
            var force = new ForceGrabDetector();
            GrabEvent seen = GrabEvent.None;
            for (float t = 0f; t < 0.4f; t += Frame)
            {
                Pose(hand, new Vector3(0.2f, 1.3f, 0.3f), Vector3.forward, Vector3.up, 0.45f, ThumbPose.Out);
                GrabEvent e = force.Step(hand, Body, Frame, out _);
                if (e != GrabEvent.None)
                    seen = e;
            }
            Assert.That(seen, Is.EqualTo(GrabEvent.Grab));
            Assert.That(force.Holding, Is.True);

            hand.Velocity = new Vector3(0f, 0.5f, 2.5f);
            Assert.That(force.Step(hand, Body, Frame, out Vector3 velocity), Is.EqualTo(GrabEvent.Throw));
            Assert.That(velocity.z, Is.EqualTo(2.5f));
        }

        [Test]
        public void SaberNeedsAHeldFist()
        {
            var hand = new HandPose(false);
            var saber = new SaberGripDetector();
            Fist(hand, new Vector3(0.2f, 1.3f, 0.3f));
            Assert.That(saber.Step(hand, Frame), Is.False);
            for (int i = 0; i < 12; i++)
                saber.Step(hand, Frame);
            Assert.That(saber.On, Is.True);
            Assert.That(Vector3.Dot(saber.BladeDirection, hand.ThumbSide), Is.GreaterThan(0.8f));
        }

        [Test]
        public void UnderhandThrowFromLowFist()
        {
            var hand = new HandPose(false);
            var bomb = new UnderhandThrowDetector();
            Fist(hand, new Vector3(0.2f, 0.95f, 0.2f));
            hand.Velocity = new Vector3(0f, 1.2f, 1.6f);
            Assert.That(bomb.Step(hand, Body, Frame, out _, out _), Is.True);
        }
    }
}
