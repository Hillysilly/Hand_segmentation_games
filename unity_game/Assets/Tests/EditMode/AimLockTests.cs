using NUnit.Framework;
using UnityEngine;

namespace Repulsor.Tests
{
    public class AimLockTests
    {
        const float Frame = 1f / 72f;

        static float Record(AimLock aim, float time, float seconds, Vector3 direction)
        {
            for (float end = time + seconds; time < end; time += Frame)
                aim.Record(time, Vector3.zero, direction);
            return time;
        }

        [Test]
        public void EmptyUntilFirstSample()
        {
            var aim = new AimLock();
            Assert.That(aim.HasAim, Is.False);
            aim.Record(0f, Vector3.one, Vector3.forward);
            Assert.That(aim.HasAim);
            Assert.That(aim.Direction, Is.EqualTo(Vector3.forward));
            Assert.That(aim.Position, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void HoldingSteady_LocksThatAim()
        {
            var aim = new AimLock();
            Record(aim, 0f, 0.5f, Vector3.forward);
            Assert.That(aim.Direction, Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void GlitchInTheLastFrames_IsIgnored()
        {
            // Fingers starting to open: the last few fist frames report a wrong palm direction.
            var aim = new AimLock();
            float time = Record(aim, 0f, 0.5f, Vector3.forward);
            Record(aim, time, 0.08f, Vector3.left);
            Assert.That(aim.Direction, Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void TurningTheHand_FollowsAfterTheLookback()
        {
            var aim = new AimLock();
            float time = Record(aim, 0f, 0.5f, Vector3.forward);
            Record(aim, time, AimLock.LookbackSeconds * 2f, Vector3.left);
            Assert.That(aim.Direction, Is.EqualTo(Vector3.left));
        }

        [Test]
        public void ShorterHistoryThanTheLookback_UsesTheOldestSample()
        {
            var aim = new AimLock();
            aim.Record(0f, Vector3.zero, Vector3.forward);
            aim.Record(Frame, Vector3.zero, Vector3.left);
            Assert.That(aim.Direction, Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void NewFistAfterAGap_ForgetsTheOldAim()
        {
            var aim = new AimLock();
            Record(aim, 0f, 0.5f, Vector3.forward);
            aim.Record(5f, Vector3.zero, Vector3.left);
            Assert.That(aim.Direction, Is.EqualTo(Vector3.left));
        }

        [Test]
        public void Clear_RemovesTheAim()
        {
            var aim = new AimLock();
            Record(aim, 0f, 0.5f, Vector3.forward);
            aim.Clear();
            Assert.That(aim.HasAim, Is.False);
        }
    }
}
