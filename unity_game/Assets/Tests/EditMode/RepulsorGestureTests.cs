using NUnit.Framework;

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
    }
}
