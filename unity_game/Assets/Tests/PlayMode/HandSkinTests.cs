using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Repulsor.Tests
{
    /// <summary>
    /// Hand models follow the tracked hand: driven from a made-up tracked hand, the model's fingertips end up where
    /// the tracked ones point. Uses the XR sample hand (the skeleton every hand model is fitted to) as the model.
    /// </summary>
    public class HandSkinTests
    {
        static Transform Bone(HandSkin skin, string name)
        {
            foreach (Transform t in skin.Root.GetComponentsInChildren<Transform>())
                if (t.name == name)
                    return t;
            return null;
        }

        [UnityTest]
        public IEnumerator AHandModelBendsWithTheTrackedFingers()
        {
            var parent = new GameObject("Game").transform;
            HandSkin skin = HandSkin.Create("Showcase/RightHand", parent, false);
            Assert.That(skin, Is.Not.Null);
            var hand = new HandPose(false);
            Vector3 wrist = new Vector3(0.2f, 1.2f, 0.4f);
            Quaternion facing = Quaternion.Euler(-20f, 30f, 10f);

            foreach (var (curl, what) in new[] { (0f, "open"), (1f, "fist"), (0.5f, "half") })
            {
                hand.Pose(wrist, facing, curl, curl, curl, curl, curl > 0.9f ? ThumbPose.Tucked : ThumbPose.Out);
                hand.Analyze();
                skin.Follow(hand, hand.Wrist + new Vector3(0f, -0.1f, -0.25f));
                yield return null;
                // Each finger's model bones point the way the tracked finger does.
                foreach (string finger in new[] { "Index", "Middle", "Ring", "Little" })
                {
                    Transform knuckle = Bone(skin, "R_" + finger + "Proximal");
                    Transform tip = Bone(skin, "R_" + finger + "Tip");
                    Joint k = (Joint)System.Enum.Parse(typeof(Joint), finger + "Proximal");
                    Joint t = (Joint)System.Enum.Parse(typeof(Joint), finger + "Tip");
                    Joint mid = (Joint)System.Enum.Parse(typeof(Joint), finger + "Intermediate");
                    Vector3 tracked = (hand[mid] - hand[k]).normalized;
                    Vector3 model = (Bone(skin, "R_" + finger + "Intermediate").position - knuckle.position).normalized;
                    Assert.That(Vector3.Angle(tracked, model), Is.LessThan(3f), $"{what}: {finger} first bone points with the tracked one");
                    Assert.That(tip, Is.Not.Null);
                }
                float wristGap = Vector3.Distance(Bone(skin, "R_Wrist").position, hand.Wrist);
                Assert.That(wristGap, Is.LessThan(0.001f), "the wrist sits on yours");
                float size = Vector3.Distance(Bone(skin, "R_Wrist").position, Bone(skin, "R_MiddleProximal").position);
                Assert.That(size, Is.EqualTo(hand.Size).Within(0.01f), "scaled to your hand");
            }
            Object.Destroy(parent.gameObject);
        }
    }
}
