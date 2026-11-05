using NUnit.Framework;
using UnityEngine;

namespace Repulsor.Tests
{
    public class GameMathTests
    {
        [Test]
        public void Segment_ThroughSphere_Hits()
        {
            // Both ends are outside the sphere: a fast blast mustn't skip through it.
            Assert.That(GameMath.SegmentHitsSphere(new Vector3(0, 0, -5), new Vector3(0, 0, 5), Vector3.zero, 0.2f));
        }

        [Test]
        public void Segment_PassingWide_Misses()
        {
            Assert.That(GameMath.SegmentHitsSphere(new Vector3(1, 0, -5), new Vector3(1, 0, 5), Vector3.zero, 0.2f), Is.False);
        }

        [Test]
        public void Segment_StoppingShort_Misses()
        {
            Assert.That(GameMath.SegmentHitsSphere(new Vector3(0, 0, -5), new Vector3(0, 0, -1), Vector3.zero, 0.2f), Is.False);
        }

        [Test]
        public void Segment_WithNoLength_ChecksThePoint()
        {
            Assert.That(GameMath.SegmentHitsSphere(Vector3.zero, Vector3.zero, new Vector3(0.1f, 0, 0), 0.2f));
        }

        [Test]
        public void SpawnPoint_CentreOfRange_IsStraightAhead()
        {
            Vector3 p = GameMath.SpawnPoint(new Vector3(0, 1.6f, 0), Vector3.forward, 55f, 1f, 3f, -0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
            Assert.That(Vector3.Distance(p, new Vector3(0, 1.6f, 2f)), Is.LessThan(0.001f));
        }

        [Test]
        public void SpawnPoint_IgnoresHeadPitch()
        {
            Vector3 lookingDown = new Vector3(0, -1, 1).normalized;
            Vector3 p = GameMath.SpawnPoint(Vector3.zero, lookingDown, 55f, 2f, 2f, 0f, 0f, 0.5f, 0f, 0f);
            Assert.That(Vector3.Distance(p, new Vector3(0, 0, 2f)), Is.LessThan(0.001f));
        }

        [Test]
        public void WheelColor_MiddleIsWhite()
        {
            Color c = GameMath.WheelColor(Vector2.zero);
            Assert.That(c.r, Is.EqualTo(1f).Within(0.001f));
            Assert.That(c.g, Is.EqualTo(1f).Within(0.001f));
            Assert.That(c.b, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void WheelColor_RightEdgeIsRed()
        {
            Color c = GameMath.WheelColor(Vector2.right);
            Assert.That(c.r, Is.EqualTo(1f).Within(0.001f));
            Assert.That(c.g, Is.EqualTo(0f).Within(0.001f));
            Assert.That(c.b, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void WheelColor_BeyondTheEdgeStaysPure()
        {
            Color c = GameMath.WheelColor(new Vector2(0f, -2f));
            Assert.That(Mathf.Min(c.r, Mathf.Min(c.g, c.b)), Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void SpawnPoint_LookingStraightUp_StillGivesAValidPoint()
        {
            Vector3 p = GameMath.SpawnPoint(Vector3.zero, Vector3.up, 55f, 2f, 2f, 0f, 0f, 0.5f, 0f, 0f);
            Assert.That(p.magnitude, Is.EqualTo(2f).Within(0.001f));
        }
    }
}
