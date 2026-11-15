using NUnit.Framework;
using UnityEngine;

namespace Repulsor.Tests
{
    /// <summary>The Elementalist's and Earth character's gestures, on made-up hands.</summary>
    public class ElementGestureTests
    {
        const float Frame = 1f / 72f;
        // Head at 1.6 m facing +Z: waist is around 1.0 m, chest around 1.3 m.
        static readonly Stance Body = Stance.From(new Vector3(0f, 1.6f, 0f), Vector3.forward);

        static HandPose Hand(bool left, Vector3 wrist, float curl, ThumbPose thumb = ThumbPose.Out)
        {
            var hand = new HandPose(left);
            // Fingers forward, palm down.
            hand.Pose(wrist, Quaternion.LookRotation(Vector3.forward, Vector3.up), curl, curl, curl, curl, thumb);
            hand.Analyze();
            return hand;
        }

        [Test]
        public void APalmShovedForwardIsAGust()
        {
            HandPose hand = Hand(false, new Vector3(0.15f, 1.3f, 0.3f), 0f);
            var gust = new PalmPushDetector();
            hand.Velocity = new Vector3(0f, 0f, 0.6f);
            Assert.That(gust.Step(hand, Body, Frame, out _, out _), Is.False, "too slow");
            hand.Velocity = new Vector3(0f, 0f, 2f);
            Assert.That(gust.Step(hand, Body, Frame, out _, out Vector3 direction), Is.True);
            Assert.That(direction.z, Is.GreaterThan(0.8f));
            Assert.That(gust.Step(hand, Body, Frame, out _, out _), Is.False, "cooldown");
        }

        [Test]
        public void AFistPunchIsNotAGust()
        {
            HandPose hand = Hand(false, new Vector3(0.15f, 1.3f, 0.3f), 1f, ThumbPose.Tucked);
            hand.Velocity = new Vector3(0f, 0f, 2.5f);
            Assert.That(new PalmPushDetector().Step(hand, Body, Frame, out _, out _), Is.False);
        }

        [Test]
        public void BothHandsOutFlatHoldLightning()
        {
            HandPose left = Hand(true, new Vector3(-0.2f, 1.25f, 0.4f), 0f);
            HandPose right = Hand(false, new Vector3(0.2f, 1.25f, 0.4f), 0f);
            var flat = new FlatHandsDetector();
            for (int i = 0; i < 30; i++)
                flat.Step(left, right, Body, Frame);
            Assert.That(flat.Holding, Is.True);

            // Hands down by your sides: no.
            HandPose low = Hand(true, new Vector3(-0.25f, 0.8f, 0.05f), 0f);
            Assert.That(FlatHandsDetector.IsPose(low, Body), Is.False);
        }

        [Test]
        public void CirclingHandsChargeATornadoAndAPushThrowsIt()
        {
            HandPose left = Hand(true, new Vector3(-0.15f, 1.25f, 0.3f), 0f);
            HandPose right = Hand(false, new Vector3(0.15f, 1.25f, 0.3f), 0f);
            var swirl = new SwirlDetector();
            // Circling: moving sideways and up and down, not forward.
            for (int i = 0; i < 100; i++)
            {
                float a = i * 0.2f;
                left.Velocity = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.7f;
                right.Velocity = -left.Velocity;
                Assert.That(swirl.Step(left, right, Body, Frame, out _, out _), Is.False);
            }
            Assert.That(swirl.Charge, Is.GreaterThan(SwirlDetector.MinCharge));
            left.Velocity = right.Velocity = new Vector3(0f, 0f, 1.5f);
            Assert.That(swirl.Step(left, right, Body, Frame, out Vector3 direction, out float power), Is.True);
            Assert.That(direction.z, Is.GreaterThan(0.7f));
            Assert.That(power, Is.GreaterThan(0.3f));
        }

        [Test]
        public void WavingOrShakingTheHandsDoesNotChargeATornado()
        {
            HandPose left = Hand(true, new Vector3(-0.15f, 1.25f, 0.3f), 0f);
            HandPose right = Hand(false, new Vector3(0.15f, 1.25f, 0.3f), 0f);
            var swirl = new SwirlDetector();
            // Side to side, fast, for three seconds: back and forth, never round.
            for (int i = 0; i < 216; i++)
            {
                float wave = Mathf.Sin(i * 0.35f);
                left.Velocity = new Vector3(wave, 0.05f * Mathf.Sin(i * 1.3f), 0f) * 1.2f;
                right.Velocity = new Vector3(-wave, 0.05f * Mathf.Sin(i * 1.3f), 0f) * 1.2f;
                swirl.Step(left, right, Body, Frame, out _, out _);
            }
            Assert.That(swirl.Charge, Is.LessThan(SwirlDetector.MinCharge), "waving is not circling");
            // Held still: nothing either.
            left.Velocity = right.Velocity = Vector3.zero;
            for (int i = 0; i < 100; i++)
                swirl.Step(left, right, Body, Frame, out _, out _);
            Assert.That(swirl.Charge, Is.LessThan(0.05f));
        }

        [Test]
        public void SlowCirclesOfEachHandChargeATornado()
        {
            HandPose left = Hand(true, new Vector3(-0.15f, 1.25f, 0.3f), 0f);
            HandPose right = Hand(false, new Vector3(0.15f, 1.25f, 0.3f), 0f);
            var swirl = new SwirlDetector();
            // A circle a second, each hand its own way round, a little wobbly: two seconds is enough.
            for (int i = 0; i < 144; i++)
            {
                float a = i * Mathf.PI * 2f / 72f;
                float wobble = 0.08f * Mathf.Sin(i * 1.7f);
                left.Velocity = new Vector3(Mathf.Cos(a) + wobble, Mathf.Sin(a), 0.1f) * 0.6f;
                right.Velocity = new Vector3(-Mathf.Cos(a), Mathf.Sin(a) + wobble, 0.1f) * 0.6f;
                swirl.Step(left, right, Body, Frame, out _, out _);
            }
            Assert.That(swirl.Charge, Is.GreaterThan(SwirlDetector.MinCharge));
        }

        [Test]
        public void APushWithoutChargingIsNoTornado()
        {
            HandPose left = Hand(true, new Vector3(-0.15f, 1.25f, 0.3f), 0f);
            HandPose right = Hand(false, new Vector3(0.15f, 1.25f, 0.3f), 0f);
            left.Velocity = right.Velocity = new Vector3(0f, 0f, 1.5f);
            Assert.That(new SwirlDetector().Step(left, right, Body, Frame, out _, out _), Is.False);
        }

        [Test]
        public void AFistRaisedFastFromTheWaistIsAnUppercut()
        {
            var uppercut = new UppercutDetector();
            HandPose hand = Hand(false, new Vector3(0.2f, 0.95f, 0.2f), 1f, ThumbPose.Tucked);
            hand.Velocity = Vector3.zero;
            Assert.That(uppercut.Step(hand, Body, Frame, out _), Is.False, "low, not moving");
            hand = Hand(false, new Vector3(0.2f, 1.4f, 0.25f), 1f, ThumbPose.Tucked);
            hand.Velocity = new Vector3(0f, 2f, 0.3f);
            Assert.That(uppercut.Step(hand, Body, Frame, out Vector3 direction), Is.True);
            Assert.That(direction.z, Is.GreaterThan(0.9f));
        }

        [Test]
        public void ARaisedFistWithoutStartingLowIsNotAnUppercut()
        {
            var uppercut = new UppercutDetector();
            HandPose hand = Hand(false, new Vector3(0.2f, 1.4f, 0.25f), 1f, ThumbPose.Tucked);
            hand.Velocity = new Vector3(0f, 2f, 0.3f);
            Assert.That(uppercut.Step(hand, Body, Frame, out _), Is.False);
        }

        [Test]
        public void ThumbAndFingerFlickedApartIsASnap()
        {
            var snap = new SnapDetector();
            HandPose pinched = Hand(false, new Vector3(0.2f, 1.3f, 0.3f), 0.5f, ThumbPose.PinchIndex);
            Assert.That(snap.Step(pinched, Frame), Is.False);
            HandPose open = Hand(false, new Vector3(0.2f, 1.3f, 0.3f), 0f, ThumbPose.Out);
            Assert.That(snap.Step(open, Frame), Is.True);
            Assert.That(snap.Step(pinched, Frame) || snap.Step(open, Frame), Is.False, "cooldown");
        }
    }
}
