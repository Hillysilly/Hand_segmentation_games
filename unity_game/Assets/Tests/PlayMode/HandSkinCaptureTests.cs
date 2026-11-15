using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Repulsor.Tests
{
    /// <summary>
    /// Each level 3 hand model worn on a made-up tracked right hand in a few shapes (open, fist, finger gun, spider
    /// sign), photographed from the wearer's view and from the side. Pictures go to Logs/play_skin_*.png.
    /// </summary>
    public class HandSkinCaptureTests
    {
        static void Snap(Vector3 eye, Vector3 target, string name)
        {
            var cameraObject = new GameObject("Capture Camera", typeof(Camera));
            cameraObject.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.3f, 0.31f, 0.34f);
            camera.nearClipPlane = 0.01f;
            camera.fieldOfView = 45f;
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
            File.WriteAllBytes($"Logs/play_skin_{name}.png", image.EncodeToPNG());
        }

        [UnityTest]
        public IEnumerator EachHandModelFollowsTheHandShapes()
        {
            var light = new GameObject("Light", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            var parent = new GameObject("Game").transform;
            var hand = new HandPose(false);
            Vector3 wrist = new Vector3(0.15f, 1.25f, 0.35f);
            // Palm down, fingers forward and a little in: a hand held out in front.
            Quaternion facing = Quaternion.Euler(10f, -10f, 0f);
            int made = 0;
            foreach (string name in new[] { "Technoman", "Emberwitch", "Lumen", "Valkyr", "Tempest" })
            {
                HandSkin skin = HandSkin.Create($"HandSkins/{name}_R", parent, false);
                if (skin == null)
                {
                    Debug.Log($"[Test] no hand model {name}");
                    continue;
                }
                made++;
                var shapes = new (string what, float i, float m, float r, float l, ThumbPose thumb)[]
                {
                    ("open", 0f, 0f, 0f, 0f, ThumbPose.Out), ("fist", 1f, 1f, 1f, 1f, ThumbPose.Tucked),
                    ("gun", 0f, 0f, 1f, 1f, ThumbPose.Out), ("spider", 0f, 1f, 1f, 0f, ThumbPose.Out),
                };
                foreach (var shape in shapes)
                {
                    hand.Pose(wrist, facing, shape.i, shape.m, shape.r, shape.l, shape.thumb);
                    hand.Analyze();
                    skin.Follow(hand, wrist + new Vector3(0.05f, -0.12f, -0.24f));
                    yield return null;
                    Vector3 centre = wrist + facing * Vector3.forward * 0.07f;
                    Snap(wrist + new Vector3(-0.04f, 0.26f, -0.2f), centre, $"{name}_{shape.what}_view");
                    Snap(centre + new Vector3(0.3f, 0.04f, 0.02f), centre, $"{name}_{shape.what}_side");
                }
                skin.Destroy();
            }
            Assert.That(made, Is.GreaterThan(0), "no hand models built");
            Object.Destroy(parent.gameObject);
            Object.Destroy(light.gameObject);
        }
    }
}
