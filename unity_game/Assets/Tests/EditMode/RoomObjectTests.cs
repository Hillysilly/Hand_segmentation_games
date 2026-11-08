using NUnit.Framework;
using UnityEngine;

namespace Repulsor.Tests
{
    public class RoomObjectTests
    {
        GameObject box;

        [SetUp]
        public void MakeBox()
        {
            box = new GameObject("Table");
            box.transform.SetPositionAndRotation(new Vector3(0f, 0.4f, 2f), Quaternion.Euler(0f, 30f, 0f));
        }

        [TearDown]
        public void RemoveBox() => Object.DestroyImmediate(box);

        static readonly Vector3 TableSize = new Vector3(1.2f, 0.8f, 0.6f);

        [Test]
        public void RayAtTheBoxHitsItsNearFace()
        {
            Vector3 eye = new Vector3(0f, 1.5f, 0f);
            Vector3 aim = (box.transform.position - eye).normalized;
            Assert.That(RoomObjects.RayHitsBox(eye, aim, box.transform, TableSize, out float distance), Is.True);
            float toCentre = Vector3.Distance(eye, box.transform.position);
            Assert.That(distance, Is.LessThan(toCentre).And.GreaterThan(toCentre - 0.8f));
        }

        [Test]
        public void RayBesideTheBoxMisses()
        {
            Vector3 eye = new Vector3(0f, 1.5f, 0f);
            Vector3 aim = (box.transform.position + Vector3.right * 2f - eye).normalized;
            Assert.That(RoomObjects.RayHitsBox(eye, aim, box.transform, TableSize, out _), Is.False);
        }

        [Test]
        public void RayPointingAwayMisses()
        {
            Vector3 eye = new Vector3(0f, 1.5f, 0f);
            Vector3 away = (eye - box.transform.position).normalized;
            Assert.That(RoomObjects.RayHitsBox(eye, away, box.transform, TableSize, out _), Is.False);
        }

        [Test]
        public void RotationCounts()
        {
            // A straight ray just past the unrotated half-width (0.6 m), which the corner of the turned table reaches (0.67 m).
            Vector3 eye = new Vector3(0.64f, 0.4f, 0f);
            Vector3 aim = Vector3.forward;
            box.transform.rotation = Quaternion.identity;
            bool straight = RoomObjects.RayHitsBox(eye, aim, box.transform, TableSize, out _);
            box.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            bool turned = RoomObjects.RayHitsBox(eye, aim, box.transform, TableSize, out _);
            Assert.That(straight, Is.False);
            Assert.That(turned, Is.True);
        }
    }
}
