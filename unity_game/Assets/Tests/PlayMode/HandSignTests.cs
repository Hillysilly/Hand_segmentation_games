using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Repulsor.Tests
{
    /// <summary>
    /// The select screen's hand signs are the gestures the game reads: each shape is posed on the sign hand, its
    /// joints read back into a HandPose, and the game's own classifier has to recognise it. Pictures of every
    /// shape go to Logs/play_sign_*.png.
    /// </summary>
    public class HandSignTests
    {
        static HandRig MakeHand(bool right, out Transform root)
        {
            root = new GameObject("Sign Test").transform;
            GameObject model = Object.Instantiate(Resources.Load<GameObject>(right ? "Showcase/RightHand" : "Showcase/LeftHand"), root, false);
            HandRig rig = HandRig.ForXrHand(model.transform, right);
            Assert.That(rig, Is.Not.Null, "the hand's bones are there");
            return rig;
        }

        static HandPose Read(HandRig rig, Transform space, bool right, SignShape shape)
        {
            var pose = new HandPose(!right);
            rig.Shape(shape);
            rig.ReadInto(pose, space);
            Debug.Log($"[Test] {shape}: index {pose.Index:0.00} middle {pose.Middle:0.00} ring {pose.Ring:0.00} little {pose.Little:0.00} "
                      + $"thumb out {pose.ThumbOut:0.00} pinch {pose.Pinch:0.000} thumb-middle {Vector3.Distance(pose[Joint.ThumbTip], pose[Joint.MiddleTip]):0.000} gesture {pose.Gesture} gun {pose.Gun} spider {pose.SpiderSign} cup {pose.Cup}");
            return pose;
        }

        static void Snap(Transform root, string name)
        {
            var cameraObject = new GameObject("Capture Camera", typeof(Camera));
            Vector3 eye = root.position + new Vector3(0.12f, 0.2f, -0.3f);
            cameraObject.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(root.position + Vector3.forward * 0.06f - eye));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.25f, 0.26f, 0.3f);
            camera.nearClipPlane = 0.01f;
            camera.fieldOfView = 40f;
            var texture = new RenderTexture(480, 480, 24) { antiAliasing = 4 };
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(480, 480, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 480, 480), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;
            Object.Destroy(cameraObject);
            Directory.CreateDirectory("Logs");
            File.WriteAllBytes($"Logs/play_sign_{name}.png", image.EncodeToPNG());
        }

        [UnityTest]
        public IEnumerator EachSignIsTheGestureTheGameReads()
        {
            var light = new GameObject("Light", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            HandRig rig = MakeHand(true, out Transform root);
            yield return null;

            foreach (SignShape shape in new[] { SignShape.Open, SignShape.Fist, SignShape.Gun, SignShape.Spider, SignShape.Cup, SignShape.Snap, SignShape.Snapped })
            {
                Read(rig, root, true, shape);
                yield return null;
                Snap(root, shape.ToString());
            }

            Assert.That(Read(rig, root, true, SignShape.Open).Open, Is.True, "open hand");
            Assert.That(Read(rig, root, true, SignShape.Fist).Fist, Is.True, "fist");
            HandPose gun = Read(rig, root, true, SignShape.Gun);
            Assert.That(gun.Gun, Is.True, "finger gun");
            Assert.That(gun.ThumbOut, Is.GreaterThan(HandPose.ThumbOutAbove), "finger gun's thumb is up");
            HandPose spider = Read(rig, root, true, SignShape.Spider);
            Assert.That(spider.SpiderSign, Is.True, "spider sign");
            Assert.That(Read(rig, root, true, SignShape.Cup).Cup, Is.True, "cupped hand");
            HandPose snap = Read(rig, root, true, SignShape.Snap);
            Assert.That(Vector3.Distance(snap[Joint.ThumbTip], snap[Joint.MiddleTip]), Is.LessThan(SnapDetector.Touch), "thumb pressed to the middle finger");
            HandPose snapped = Read(rig, root, true, SignShape.Snapped);
            float gap = Mathf.Min(Vector3.Distance(snapped[Joint.ThumbTip], snapped[Joint.MiddleTip]), Vector3.Distance(snapped[Joint.ThumbTip], snapped[Joint.IndexTip]));
            Assert.That(gap, Is.GreaterThan(SnapDetector.Apart), "snapped apart");
            Object.Destroy(root.gameObject);
            Object.Destroy(light.gameObject);
        }

        [UnityTest]
        public IEnumerator TheFirePillarSignIsATriangle()
        {
            var parent = new GameObject("Screen").transform;
            var sign = new ShowcaseSign(parent);
            PoseKey[] keys = CharacterShowcase.KeysFor(CharacterId.Mage, 3);
            sign.Use(keys);
            sign.Show(true);
            sign.Pose(keys, 0.8f);
            yield return null;
            var left = new HandPose(true);
            var right = new HandPose(false);
            sign.Rig(0).ReadInto(left, sign.Root);
            sign.Rig(1).ReadInto(right, sign.Root);
            Debug.Log($"[Test] triangle: index tips {Vector3.Distance(left[Joint.IndexTip], right[Joint.IndexTip]):0.000}, "
                      + $"thumb tips {Vector3.Distance(left[Joint.ThumbTip], right[Joint.ThumbTip]):0.000}");
            Assert.That(TriangleDetector.IsTriangle(left, right), Is.True);

            // Then both fists close.
            sign.Pose(keys, 1.6f);
            sign.Rig(0).ReadInto(left, sign.Root);
            sign.Rig(1).ReadInto(right, sign.Root);
            Assert.That(left.Fist && right.Fist, Is.True, "both fists");
            Object.Destroy(parent.gameObject);
        }

        [UnityTest]
        public IEnumerator SignsShowTheRightShapeAtTheRightTime()
        {
            // Each move's sign at the moment it fires, against the shape its gesture needs.
            var checks = new (CharacterId id, int level, float at, System.Func<HandPose, bool> ok, string what)[]
            {
                (CharacterId.Technoman, 1, 0.8f, h => h.Open, "laser: open palm"),
                (CharacterId.Technoman, 2, 0.7f, h => h.Fist, "repulsor: fist first"),
                (CharacterId.Technoman, 2, 1.2f, h => h.Open, "repulsor: then open"),
                (CharacterId.Saber, 1, 0.5f, h => h.Gun, "blaster: finger gun"),
                (CharacterId.Saber, 3, 0.7f, h => h.Cup, "force: cupped"),
                (CharacterId.Spider, 1, 0.6f, h => h.SpiderSign, "web: spider sign"),
                (CharacterId.Archer, 1, 0.5f, h => h.Gun, "crossbow: finger gun"),
                (CharacterId.Archer, 3, 0.5f, h => h.Fist, "smoke: fist"),
                (CharacterId.Elementalist, 1, 0.6f, h => h.Open, "gust: open palm"),
                (CharacterId.Earth, 2, 0.45f, h => h.Fist, "spikes: fist"),
                (CharacterId.Saber, 2, 0.5f, h => h.Fist, "sword: fist"),
                (CharacterId.KiFighter, 3, 0.5f, h => h.Fist, "dragon fist: fist"),
            };
            var parent = new GameObject("Screen").transform;
            var sign = new ShowcaseSign(parent);
            sign.Show(true);
            yield return null;
            var pose = new HandPose(false);
            foreach (var check in checks)
            {
                PoseKey[] keys = CharacterShowcase.KeysFor(check.id, check.level);
                sign.Use(keys);
                sign.Pose(keys, check.at);
                sign.Rig(1).ReadInto(pose, sign.Root);
                Assert.That(check.ok(pose), Is.True, $"{check.what} (gesture {pose.Gesture}, index {pose.Index:0.00})");
            }
            Object.Destroy(parent.gameObject);
        }

        [UnityTest]
        public IEnumerator SignPicturesAsThePlayerSeesThem()
        {
            var light = new GameObject("Light", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            var parent = new GameObject("Screen").transform;
            var sign = new ShowcaseSign(parent);
            sign.Show(true);
            sign.Tint(new Color(0.3f, 0.7f, 1f));
            var shots = new (CharacterId id, int level, float at)[]
            {
                (CharacterId.Technoman, 1, 0.8f), (CharacterId.Technoman, 2, 0.7f), (CharacterId.KiFighter, 2, 1f),
                (CharacterId.Mage, 3, 0.8f), (CharacterId.Mage, 3, 1.6f), (CharacterId.Saber, 1, 0.5f), (CharacterId.Saber, 1, 0.76f),
                (CharacterId.Saber, 3, 0.7f), (CharacterId.Archer, 2, 0.5f), (CharacterId.Archer, 2, 1.3f), (CharacterId.Spider, 1, 0.6f),
                (CharacterId.Elementalist, 2, 1f), (CharacterId.Elementalist, 3, 0.8f), (CharacterId.Earth, 2, 0.45f),
                (CharacterId.Earth, 3, 1.6f), (CharacterId.Earth, 3, 1.9f),
            };
            foreach (var shot in shots)
            {
                PoseKey[] keys = CharacterShowcase.KeysFor(shot.id, shot.level);
                sign.Use(keys);
                sign.Pose(keys, shot.at);
                yield return null;
                SnapSign(sign.Root, $"{shot.id}_{shot.level}_{shot.at:0.0}");
            }
            Object.Destroy(parent.gameObject);
            Object.Destroy(light.gameObject);
        }

        /// <summary>The sign's box as the player sees it: from in front, looking into the screen; 84 x 136 mm framed.</summary>
        static void SnapSign(Transform root, string name)
        {
            var cameraObject = new GameObject("Capture Camera", typeof(Camera));
            cameraObject.transform.SetPositionAndRotation(root.position - root.forward * 0.5f, root.rotation);
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.25f, 0.26f, 0.3f);
            camera.nearClipPlane = 0.01f;
            camera.orthographic = true;
            camera.orthographicSize = 0.068f;
            var texture = new RenderTexture(252, 408, 24) { antiAliasing = 4 };
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(252, 408, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 252, 408), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;
            Object.Destroy(cameraObject);
            Directory.CreateDirectory("Logs");
            File.WriteAllBytes($"Logs/play_signview_{name}.png", image.EncodeToPNG());
        }
    }
}
