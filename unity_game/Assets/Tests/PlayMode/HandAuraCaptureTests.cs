using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Repulsor.Tests
{
    /// <summary>
    /// The level 3 hand aura on a made-up tracked right hand in a few shapes, over a plain grey hand standing in for
    /// your real one, from the wearer's view and the side, against a room-grey background. Logs/play_aura_*.png.
    /// </summary>
    public class HandAuraCaptureTests
    {
        static void Snap(Vector3 eye, Vector3 target, string name)
        {
            var cameraObject = new GameObject("Capture Camera", typeof(Camera));
            cameraObject.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.36f, 0.34f, 0.31f);
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
            File.WriteAllBytes($"Logs/play_aura_{name}.png", image.EncodeToPNG());
        }

        [UnityTest]
        public IEnumerator TheHandAuraFollowsTheHandShapes()
        {
            var light = new GameObject("Light", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            var parent = new GameObject("Game").transform;
            var material = Resources.Load<Material>("HandAura");
            Assert.That(material, Is.Not.Null, "Resources/HandAura.mat");
            // The stand-in real hand, and the aura over it.
            HandSkin real = HandSkin.Create("Showcase/RightHand", parent, false);
            HandSkin aura = HandSkin.Create("Showcase/RightHand", parent, false);
            Assert.That(real, Is.Not.Null);
            Assert.That(aura, Is.Not.Null);
            var block = new MaterialPropertyBlock();
            Renderer[] glow = aura.Root.GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in glow)
                renderer.sharedMaterial = material;

            var hand = new HandPose(false);
            Vector3 wrist = new Vector3(0.15f, 1.25f, 0.35f);
            Quaternion facing = Quaternion.Euler(10f, -10f, 0f);
            var colors = new[] { new Color(0.3f, 0.8f, 1f), new Color(1f, 0.45f, 0.15f), new Color(0.6f, 1f, 0.5f), new Color(0.85f, 0.5f, 1f) };
            var shapes = new (string what, float i, float m, float r, float l, ThumbPose thumb)[]
            {
                ("open", 0f, 0f, 0f, 0f, ThumbPose.Out), ("fist", 1f, 1f, 1f, 1f, ThumbPose.Tucked),
                ("gun", 0f, 0f, 1f, 1f, ThumbPose.Out), ("spider", 0f, 1f, 1f, 0f, ThumbPose.Out),
            };
            for (int s = 0; s < shapes.Length; s++)
            {
                var shape = shapes[s];
                hand.Pose(wrist, facing, shape.i, shape.m, shape.r, shape.l, shape.thumb);
                hand.Analyze();
                Vector3 elbow = wrist + new Vector3(0.05f, -0.12f, -0.24f);
                real.Follow(hand, elbow);
                aura.Follow(hand, elbow);
                foreach (Renderer renderer in glow)
                {
                    renderer.GetPropertyBlock(block);
                    block.SetColor("_Color", colors[s]);
                    block.SetVector("_WristPos", hand.Wrist);
                    block.SetVector("_WristFwd", hand[Joint.MiddleProximal] - hand.Wrist);
                    renderer.SetPropertyBlock(block);
                }
                yield return null;
                Assert.That(aura.Root.gameObject.activeSelf, Is.True);
                Vector3 centre = wrist + facing * Vector3.forward * 0.07f;
                Snap(wrist + new Vector3(-0.04f, 0.26f, -0.2f), centre, $"{shape.what}_view");
                Snap(centre + new Vector3(0.3f, 0.04f, 0.02f), centre, $"{shape.what}_side");
            }
            Object.Destroy(parent.gameObject);
            Object.Destroy(light.gameObject);
        }
    }
}
