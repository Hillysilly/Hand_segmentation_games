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

        [Test]
        public void LightningPath_KeepsItsEnds()
        {
            var points = new Vector3[9];
            Vector3 start = new Vector3(1, 2, 3), end = new Vector3(4, 5, 6);

            GameMath.LightningPath(start, end, 0.5f, points, new System.Random(1));

            Assert.That(Vector3.Distance(points[0], start), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(points[8], end), Is.LessThan(0.0001f));
        }

        [Test]
        public void LightningPath_StraysNoFurtherThanItsJitter()
        {
            var points = new Vector3[9];

            GameMath.LightningPath(Vector3.zero, Vector3.right, 0.1f, points, new System.Random(2));

            float furthest = 0f;
            foreach (Vector3 point in points)
                furthest = Mathf.Max(furthest, Mathf.Abs(point.y), Mathf.Abs(point.z));
            Assert.That(furthest, Is.GreaterThan(0f).And.LessThanOrEqualTo(0.1f));
        }

        [Test]
        public void Lens_LooksDownFromTheHead()
        {
            Vector3 forward = PersonDetector.LensRotation * Vector3.forward;

            Assert.That(Mathf.Asin(-forward.y) * Mathf.Rad2Deg, Is.EqualTo(11.09f).Within(0.05f));
            Assert.That(forward.z, Is.GreaterThan(0.98f));
        }

        [Test]
        public void Body_InMiddleOfPicture_IsBelowTheLevelHead()
        {
            var frame = new BodyFrame();
            var centre = new Vector2(320f, 320f);
            for (int i = 0; i < BodyFrame.JointCount; i++)
                frame.Image[i] = centre;
            var joints = new Vector3[BodyFrame.JointCount];

            frame.Place(Vector3.zero, PersonDetector.LensRotation, new Vector2(434f, 434f), centre, 3f, joints);

            Assert.That(joints[0].y, Is.EqualTo(-0.577f).Within(0.01f));
            Assert.That(joints[0].z, Is.EqualTo(2.94f).Within(0.01f));
        }

        [Test]
        public void Smoother_StillNoisyWrist_ShakesLessThanItsInput()
        {
            const int Wrist = 15;
            var smoother = new JointSmoother(BodyFrame.JointCount);
            var joints = new Vector3[BodyFrame.JointCount];
            var seen = new float[BodyFrame.JointCount];
            for (int i = 0; i < seen.Length; i++)
                seen[i] = 1f;

            float furthest = 0f;
            for (int step = 0; step < 40; step++)
            {
                joints[Wrist] = new Vector3(step % 2 == 0 ? 0.03f : -0.03f, 0f, 0f);
                smoother.Step(joints, seen, 0.055f);
                if (step > 20)
                    furthest = Mathf.Max(furthest, Mathf.Abs(smoother.Joints[Wrist].x));
            }

            Assert.That(furthest, Is.LessThan(0.015f));
        }

        [Test]
        public void Smoother_MovingHand_KeepsUp()
        {
            const int Wrist = 15;
            var smoother = new JointSmoother(BodyFrame.JointCount);
            var joints = new Vector3[BodyFrame.JointCount];
            var seen = new float[BodyFrame.JointCount];
            for (int i = 0; i < seen.Length; i++)
                seen[i] = 1f;

            for (int step = 0; step < 18; step++)
            {
                joints[Wrist] = new Vector3(step * 2f * 0.055f, 0f, 0f);
                smoother.Step(joints, seen, 0.055f);
            }

            Assert.That(joints[Wrist].x - smoother.Joints[Wrist].x, Is.LessThan(0.15f));
        }

        [Test]
        public void Smoother_UnseenWrist_HoldsToTheShoulderInsteadOfTheGuess()
        {
            const int Shoulder = 11, Wrist = 15;
            var smoother = new JointSmoother(BodyFrame.JointCount);
            var joints = new Vector3[BodyFrame.JointCount];
            var seen = new float[BodyFrame.JointCount];
            for (int i = 0; i < seen.Length; i++)
                seen[i] = 1f;
            joints[Wrist] = new Vector3(0.5f, 0f, 0f);
            smoother.Step(joints, seen, 0.055f);

            seen[Wrist] = 0.1f;
            joints[Wrist] = new Vector3(5f, 5f, 5f);
            joints[Shoulder] = new Vector3(0f, 1f, 0f);
            for (int step = 0; step < 60; step++)
                smoother.Step(joints, seen, 0.055f);

            Vector3 fromShoulder = smoother.Joints[Wrist] - smoother.Joints[Shoulder];
            Assert.That(Vector3.Distance(fromShoulder, new Vector3(0.5f, 0f, 0f)), Is.LessThan(0.02f));
        }
    }
}
