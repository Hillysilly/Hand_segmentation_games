using NUnit.Framework;
using UnityEngine;

namespace Repulsor.Tests
{
    public class RepulsorGestureTests
    {
        const float Frame = 1f / 72f;

        static void Hold(RepulsorGesture repulsor, HandGesture gesture, float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame)
                Assert.That(repulsor.Step(gesture, Frame), Is.EqualTo(0f), "fired during the hold");
        }

        [Test]
        public void FistThenOpen_Fires()
        {
            var repulsor = new RepulsorGesture();
            Hold(repulsor, HandGesture.Fist, 0.6f);
            Assert.That(repulsor.Step(HandGesture.Open, Frame), Is.GreaterThan(0.5f));
        }

        [Test]
        public void FiringUsesUpTheCharge()
        {
            var repulsor = new RepulsorGesture();
            Hold(repulsor, HandGesture.Fist, 0.6f);
            repulsor.Step(HandGesture.Open, Frame);
            Assert.That(repulsor.Charge, Is.EqualTo(0f));
            Assert.That(repulsor.Step(HandGesture.Open, Frame), Is.EqualTo(0f));
        }

        [Test]
        public void ChargeStopsAtFull()
        {
            var repulsor = new RepulsorGesture();
            Hold(repulsor, HandGesture.Fist, 5f);
            Assert.That(repulsor.Charge, Is.EqualTo(1f));
        }

        [Test]
        public void OpenHandWithoutCharging_DoesNotFire()
        {
            Assert.That(new RepulsorGesture().Step(HandGesture.Open, Frame), Is.EqualTo(0f));
        }

        [Test]
        public void TooBriefAFist_DoesNotFire()
        {
            var repulsor = new RepulsorGesture();
            Hold(repulsor, HandGesture.Fist, 0.1f);
            Assert.That(repulsor.Step(HandGesture.Open, Frame), Is.EqualTo(0f));
        }

        [Test]
        public void AmbiguousFramesBetweenFistAndOpen_StillFire()
        {
            var repulsor = new RepulsorGesture();
            Hold(repulsor, HandGesture.Fist, 0.6f);
            Hold(repulsor, HandGesture.None, 0.1f);
            Assert.That(repulsor.Step(HandGesture.Open, Frame), Is.GreaterThan(0f));
        }

        [Test]
        public void ChargeDrainsWhenTheFistIsReleasedWithoutFiring()
        {
            var repulsor = new RepulsorGesture();
            Hold(repulsor, HandGesture.Fist, 0.6f);
            Hold(repulsor, HandGesture.None, 2f);
            Assert.That(repulsor.Charge, Is.EqualTo(0f));
        }

        [TestCase(1.8f, 1.9f, 1.8f, 1.7f, HandGesture.Open)]
        [TestCase(1.0f, 1.0f, 1.1f, 1.1f, HandGesture.Fist)]
        [TestCase(1.8f, 1.1f, 1.1f, 1.1f, HandGesture.None)]   // pointing
        [TestCase(1.8f, 1.8f, 1.1f, 1.1f, HandGesture.None)]   // peace sign
        [TestCase(1.4f, 1.4f, 1.4f, 1.4f, HandGesture.None)]   // half closed
        [TestCase(1.8f, 1.8f, 1.8f, 1.4f, HandGesture.Open)]   // lazy little finger
        [TestCase(1.1f, 1.1f, 1.1f, 1.4f, HandGesture.Fist)]   // loose fist
        public void Classify(float index, float middle, float ring, float little, HandGesture expected)
        {
            Assert.That(GestureClassifier.Classify(index, middle, ring, little), Is.EqualTo(expected));
        }

        [Test]
        public void AHandStaysInItsShapeThroughAWobble()
        {
            // An index finger dipping just under the open line: open from nothing needs 1.5, but an open hand stays open.
            Assert.That(GestureClassifier.Classify(1.45f, 1.8f, 1.8f, 1.8f, HandGesture.None), Is.EqualTo(HandGesture.Open), "three open is enough");
            Assert.That(GestureClassifier.Classify(1.45f, 1.45f, 1.8f, 1.8f, HandGesture.None), Is.EqualTo(HandGesture.None));
            Assert.That(GestureClassifier.Classify(1.45f, 1.45f, 1.8f, 1.8f, HandGesture.Open), Is.EqualTo(HandGesture.Open), "sticky");
            // A fist loosening a little stays a fist; from nothing it wouldn't count.
            Assert.That(GestureClassifier.Classify(1.36f, 1.36f, 1.1f, 1.1f, HandGesture.None), Is.EqualTo(HandGesture.None));
            Assert.That(GestureClassifier.Classify(1.36f, 1.36f, 1.1f, 1.1f, HandGesture.Fist), Is.EqualTo(HandGesture.Fist));
            // But a real change still gets through: a fist snapped open is open at once.
            Assert.That(GestureClassifier.Classify(1.8f, 1.8f, 1.8f, 1.8f, HandGesture.Fist), Is.EqualTo(HandGesture.Open));
            Assert.That(GestureClassifier.Classify(1.0f, 1.0f, 1.0f, 1.0f, HandGesture.Open), Is.EqualTo(HandGesture.Fist));
        }

        [Test]
        public void AShapeIsHeldThroughAMomentOfNeither()
        {
            var hold = new GestureHold();
            const float frame = 1f / 72f;
            Assert.That(hold.Step(HandGesture.Fist, frame), Is.EqualTo(HandGesture.Fist));
            // Three frames of nothing (about 40 ms, the flicker seen on the headset): still a fist.
            for (int i = 0; i < 3; i++)
                Assert.That(hold.Step(HandGesture.None, frame), Is.EqualTo(HandGesture.Fist));
            // Straight to open: no delay.
            Assert.That(hold.Step(HandGesture.Open, frame), Is.EqualTo(HandGesture.Open));
            // Really let go: gone after 80 ms.
            HandGesture shown = HandGesture.Open;
            for (int i = 0; i < 8; i++)
                shown = hold.Step(HandGesture.None, frame);
            Assert.That(shown, Is.EqualTo(HandGesture.None));
        }

        [Test]
        public void AFistChargeSurvivesAFlickerAndStillFires()
        {
            var hold = new GestureHold();
            var repulsor = new RepulsorGesture();
            const float frame = 1f / 72f;
            float fired = 0f;
            // Half a second of fist with a two-frame flicker of "neither" every tenth of a second, then open.
            for (int i = 0; i < 36; i++)
                fired = Mathf.Max(fired, repulsor.Step(hold.Step(i % 7 >= 5 ? HandGesture.None : HandGesture.Fist, frame), frame));
            float charged = repulsor.Charge;
            fired = Mathf.Max(fired, repulsor.Step(hold.Step(HandGesture.Open, frame), frame));
            Assert.That(charged, Is.GreaterThan(0.4f), "the flickers cost nothing");
            Assert.That(fired, Is.GreaterThan(0.4f));
        }

    }
}
