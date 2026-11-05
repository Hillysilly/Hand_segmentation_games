using NUnit.Framework;
using UnityEngine;

namespace Repulsor.Tests
{
    public class PinchDetectorTests
    {
        const float Frame = 1f / 72f;
        const float Up = 1.8f;
        const float Curled = 1.1f;

        static int Pinches(PinchDetector pinch, float seconds, float distance, float others = Up)
        {
            int count = 0;
            for (float t = 0f; t < seconds; t += Frame)
                if (pinch.Step(distance, others, others, others, Frame))
                    count++;
            return count;
        }

        [Test]
        public void HeldPinch_TriggersOnce()
        {
            Assert.That(Pinches(new PinchDetector(), 1f, 0.01f), Is.EqualTo(1));
        }

        [Test]
        public void TooBriefAPinch_DoesNotTrigger()
        {
            Assert.That(Pinches(new PinchDetector(), 0.05f, 0.01f), Is.EqualTo(0));
        }

        [Test]
        public void ReleasingAndPinchingAgain_TriggersAgain()
        {
            var pinch = new PinchDetector();
            Assert.That(Pinches(pinch, 0.3f, 0.01f), Is.EqualTo(1));
            Pinches(pinch, 0.1f, 0.1f);
            Assert.That(Pinches(pinch, 0.3f, 0.01f), Is.EqualTo(1));
        }

        [Test]
        public void MakingAFist_DoesNotTrigger()
        {
            // In a fist the thumb can rest on the index finger, but the other fingers are curled.
            Assert.That(Pinches(new PinchDetector(), 1f, 0.01f, Curled), Is.EqualTo(0));
        }

        [Test]
        public void WobbleBetweenEngageAndRelease_StaysOnePinch()
        {
            var pinch = new PinchDetector();
            Assert.That(Pinches(pinch, 0.3f, 0.01f), Is.EqualTo(1));
            Assert.That(Pinches(pinch, 0.3f, 0.03f), Is.EqualTo(0));
            Assert.That(Pinches(pinch, 0.3f, 0.01f), Is.EqualTo(0));
        }
    }

    public class LaserTriggerTests
    {
        const float Frame = 1f / 72f;

        static void Hold(LaserTrigger laser, bool palmOut, float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame)
                laser.Step(palmOut, Frame);
        }

        [Test]
        public void StartsOff_AndNeedsTheHold()
        {
            var laser = new LaserTrigger();
            Assert.That(laser.Firing, Is.False);
            Hold(laser, true, 0.2f);
            Assert.That(laser.Firing, Is.False);
        }

        [Test]
        public void HoldingThePalmOut_Fires()
        {
            var laser = new LaserTrigger();
            Hold(laser, true, 0.5f);
            Assert.That(laser.Firing);
        }

        [Test]
        public void BriefDropout_KeepsFiring()
        {
            var laser = new LaserTrigger();
            Hold(laser, true, 0.5f);
            Hold(laser, false, 0.05f);
            Assert.That(laser.Firing);
        }

        [Test]
        public void ClosingTheHand_Stops()
        {
            var laser = new LaserTrigger();
            Hold(laser, true, 0.5f);
            Hold(laser, false, 0.3f);
            Assert.That(laser.Firing, Is.False);
        }

        [Test]
        public void Reset_Stops()
        {
            var laser = new LaserTrigger();
            Hold(laser, true, 0.5f);
            laser.Reset();
            Assert.That(laser.Firing, Is.False);
        }
    }

    public class BeamLengthTests
    {
        const float Frame = 1f / 72f;

        static float Step(BeamLength beam, float seconds, bool surface, float distance)
        {
            float length = 0f;
            for (float t = 0f; t < seconds; t += Frame)
                length = beam.Step(surface, distance, Frame);
            return length;
        }

        [Test]
        public void NothingFound_UsesFullLength()
        {
            var beam = new BeamLength();
            Assert.That(beam.Step(false, 0f, Frame), Is.EqualTo(BeamLength.MaxLength));
            Assert.That(beam.Hitting, Is.False);
        }

        [Test]
        public void SurfaceFound_StopsThereOnTheFirstFrame()
        {
            var beam = new BeamLength();
            Assert.That(beam.Step(true, 2f, Frame), Is.EqualTo(2f));
            Assert.That(beam.Hitting);
        }

        [Test]
        public void NearerSurface_ShortensAtOnce()
        {
            var beam = new BeamLength();
            Step(beam, 0.5f, true, 3f);
            Assert.That(beam.Step(true, 1f, Frame), Is.EqualTo(1f));
        }

        [Test]
        public void FartherSurface_ExtendsSmoothly()
        {
            var beam = new BeamLength();
            Step(beam, 0.5f, true, 1f);
            float next = beam.Step(true, 3f, Frame);
            Assert.That(next, Is.GreaterThan(1f).And.LessThan(3f));
            Assert.That(Step(beam, 0.5f, true, 3f), Is.EqualTo(3f).Within(0.01f));
        }

        [Test]
        public void BriefGap_KeepsTheSurface()
        {
            var beam = new BeamLength();
            Step(beam, 0.5f, true, 2f);
            Assert.That(Step(beam, 0.15f, false, 0f), Is.EqualTo(2f).Within(0.01f));
            Assert.That(beam.Hitting);
        }

        [Test]
        public void LongGap_ReturnsToFullLength()
        {
            var beam = new BeamLength();
            Step(beam, 0.5f, true, 2f);
            Assert.That(Step(beam, 1f, false, 0f), Is.EqualTo(BeamLength.MaxLength).Within(0.05f));
            Assert.That(beam.Hitting, Is.False);
        }

        [Test]
        public void FarSurface_IsCappedAtFullLength()
        {
            Assert.That(new BeamLength().Step(true, 20f, Frame), Is.EqualTo(BeamLength.MaxLength));
        }
    }

    public class WeaponHitTests
    {
        [Test]
        public void Aura_GoesBehindSomethingWithSpaceBesideIt()
        {
            Assert.That(GameMath.AuraOffset(2f, true, 4f, true, 3.8f, 0.3f, 0.15f, 0.4f), Is.EqualTo(0.3f));
        }

        [Test]
        public void Aura_GoesBehindWhenOnlyOneSideIsOpen()
        {
            Assert.That(GameMath.AuraOffset(2f, true, 2f, true, 3f, 0.3f, 0.15f, 0.4f), Is.EqualTo(0.3f));
        }

        [Test]
        public void Aura_GoesBehindWhenASideFindsNothing()
        {
            Assert.That(GameMath.AuraOffset(2f, true, 2f, false, 0f, 0.3f, 0.15f, 0.4f), Is.EqualTo(0.3f));
        }

        [Test]
        public void Aura_GoesInFrontOfAFlatWall()
        {
            Assert.That(GameMath.AuraOffset(2f, true, 2.1f, true, 2.15f, 0.3f, 0.15f, 0.4f), Is.EqualTo(-0.15f));
        }

        [Test]
        public void SurfaceNormal_PicksTheAxisAlongTheRay_FacingBack()
        {
            Vector3 normal = GameMath.SurfaceNormal(new Vector3(0.2f, 0f, 1f).normalized,
                Vector3.up, Vector3.forward, Vector3.right);
            Assert.That(normal, Is.EqualTo(Vector3.back));
        }

        [Test]
        public void SurfaceNormal_KeepsAnAxisAlreadyFacingBack()
        {
            Vector3 normal = GameMath.SurfaceNormal(Vector3.down, Vector3.up, Vector3.forward, Vector3.right);
            Assert.That(normal, Is.EqualTo(Vector3.up));
        }

        [Test]
        public void Ray_ThroughSphere_ReportsWhereItEnters()
        {
            Assert.That(GameMath.RayHitsSphere(Vector3.zero, Vector3.forward, new Vector3(0, 0, 3), 0.5f, out float distance));
            Assert.That(distance, Is.EqualTo(2.5f).Within(0.001f));
        }

        [Test]
        public void Ray_PassingWide_Misses()
        {
            Assert.That(GameMath.RayHitsSphere(Vector3.zero, Vector3.forward, new Vector3(1, 0, 3), 0.5f, out _), Is.False);
        }

        [Test]
        public void Ray_WithSphereBehind_Misses()
        {
            Assert.That(GameMath.RayHitsSphere(Vector3.zero, Vector3.forward, new Vector3(0, 0, -3), 0.5f, out _), Is.False);
        }

        [Test]
        public void Ray_StartingInsideSphere_HitsImmediately()
        {
            Assert.That(GameMath.RayHitsSphere(Vector3.zero, Vector3.forward, new Vector3(0, 0, 0.1f), 0.5f, out float distance));
            Assert.That(distance, Is.EqualTo(0f));
        }

        [Test]
        public void Cone_ReachesTargetAheadOnceItArrives()
        {
            var target = new Vector3(0, 0, 2f);
            Assert.That(GameMath.ConeReaches(Vector3.zero, Vector3.forward, 0.1f, 28f, 1f, target, 0.2f), Is.False);
            Assert.That(GameMath.ConeReaches(Vector3.zero, Vector3.forward, 0.1f, 28f, 2f, target, 0.2f));
        }

        [Test]
        public void Cone_IsWiderFurtherAway()
        {
            // 1 m to the side: outside the cone close up, inside it further along.
            Assert.That(GameMath.ConeReaches(Vector3.zero, Vector3.forward, 0.1f, 28f, 5f, new Vector3(1, 0, 0.5f), 0.1f), Is.False);
            Assert.That(GameMath.ConeReaches(Vector3.zero, Vector3.forward, 0.1f, 28f, 5f, new Vector3(1, 0, 2f), 0.1f));
        }

        [Test]
        public void Cone_IgnoresTargetsBehind()
        {
            Assert.That(GameMath.ConeReaches(Vector3.zero, Vector3.forward, 0.1f, 28f, 5f, new Vector3(0, 0, -2f), 0.2f), Is.False);
        }
    }
}
