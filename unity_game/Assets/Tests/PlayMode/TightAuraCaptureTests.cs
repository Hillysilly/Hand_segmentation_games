using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Repulsor.Tests
{
    /// <summary>
    /// The tight aura on the PC: the pose model's body mask on saved headset camera pictures (the user in a mirror),
    /// drawn over the picture from where the camera stood, with an afterimage; and the glow on a made-up hand.
    /// Pictures go to Logs/play_tight_*.png. Needs the saved pictures in Logs/frames.
    /// </summary>
    public class TightAuraCaptureTests
    {
        static readonly Vector3 CameraAt = new Vector3(0f, 60f, 0f);

        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
                yield return null;
        }

        static Texture2D LoadPicture(string name)
        {
            string path = Path.Combine("Logs", "frames", name);
            if (!File.Exists(path))
                return null;
            var picture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            picture.LoadImage(File.ReadAllBytes(path));
            return picture;
        }

        /// <summary>The picture on a far wall, filling a camera with the replay's field of view.</summary>
        static GameObject Backdrop(Texture2D picture, Quaternion facing)
        {
            const float Distance = 30f;
            float height = 2f * Distance * Mathf.Tan(PersonDetector.TestFieldOfView * 0.5f * Mathf.Deg2Rad);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(quad.GetComponent<Collider>());
            quad.transform.SetPositionAndRotation(CameraAt + facing * Vector3.forward * Distance, facing);
            quad.transform.localScale = new Vector3(height * picture.width / picture.height, height, 1f);
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetTexture("_BaseMap", picture);
            quad.GetComponent<Renderer>().sharedMaterial = material;
            return quad;
        }

        static void Snap(Vector3 position, Quaternion rotation, float fov, int width, int height, Color background, string name)
        {
            var cameraObject = new GameObject("Capture Camera", typeof(Camera));
            cameraObject.transform.SetPositionAndRotation(position, rotation);
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 100f;
            camera.fieldOfView = fov;
            var texture = new RenderTexture(width, height, 24) { antiAliasing = 4 };
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;
            Object.Destroy(cameraObject);
            Directory.CreateDirectory("Logs");
            File.WriteAllBytes($"Logs/play_tight_{name}.png", image.EncodeToPNG());
        }

        [UnityTest]
        public IEnumerator TheBodyMaskGlowsOverThePerson()
        {
            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene("Main");
            yield return Frames(20);
            var detector = Object.FindAnyObjectByType<PersonDetector>();
            Assert.That(detector, Is.Not.Null);
            var holder = new GameObject("Glow Test");
            var glow = new BodyGlow(holder.transform);
            Quaternion facing = Quaternion.identity;
            Color color = new Color(0.25f, 0.6f, 1f);

            glow.Tick(null, 1f, color, 1f);
            int pictures = 0;
            GameObject backdrop = null;
            foreach (string name in new[] { "f0040.jpg", "f0046.jpg", "f0060.jpg", "f0066.jpg", "f0080.jpg", "f0086.jpg" })
            {
                Texture2D picture = LoadPicture(name);
                if (picture == null)
                {
                    Assert.Ignore("No saved camera pictures in Logs/frames");
                    yield break;
                }
                detector.WantMask = true;
                bool found = detector.TestTrack(picture, CameraAt, facing);
                float most = 0f, inside = 0f;
                foreach (float value in detector.Mask)
                {
                    most = Mathf.Max(most, value);
                    if (value > 0.5f)
                        inside++;
                }
                Debug.Log($"[Test] {name}: found {found}, mask version {detector.MaskVersion}, most {most:0.00}, "
                    + $"inside {inside / detector.Mask.Length:P1}, corners {detector.MaskCorners[0]} {detector.MaskCorners[2]}");
                if (!found)
                    continue;
                Assert.That(inside, Is.GreaterThan(0f), "the mask has a body in it");

                // Every second picture comes just after the one before, so the first leaves an afterimage.
                glow.Tick(detector, 1f, color, 0.02f);
                if (backdrop != null)
                    Object.Destroy(backdrop);
                backdrop = Backdrop(picture, facing);
                yield return Frames(2);
                if (pictures % 2 == 1)
                {
                    glow.Tick(detector, 1f, color, 0.15f);
                    Snap(CameraAt, facing, PersonDetector.TestFieldOfView, picture.width, picture.height, Color.black, $"body_{pictures / 2}");
                }
                pictures++;
            }
            Assert.That(pictures, Is.GreaterThan(0), "found the person in at least one picture");
            Object.Destroy(holder);
        }

        [UnityTest]
        public IEnumerator TheHandGlowHugsAHand()
        {
            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene("Main");
            yield return Frames(10);
            var glow = new HandGlow();
            Vector3 wrist = CameraAt + new Vector3(-0.05f, -0.1f, 0.35f);
            HandPose left = MadeUpHand(true, wrist, Quaternion.Euler(-60f, 10f, 0f));
            HandPose right = MadeUpHand(false, wrist + new Vector3(0.2f, 0f, 0f), Quaternion.Euler(-70f, -15f, 0f));
            Color color = new Color(1f, 0.85f, 0.3f);
            for (int i = 0; i < 12; i++)
            {
                // Sweep the right hand so it leaves afterimages.
                MoveHand(right, new Vector3(0.012f, 0.004f, 0f));
                glow.Tick(left, right, 1f, color, 0.03f);
                yield return null;
            }
            glow.Tick(left, right, 1f, color, 0.03f);
            Snap(CameraAt, Quaternion.Euler(10f, 0f, 0f), 70f, 1280, 800, new Color(0.3f, 0.28f, 0.26f), "hands");
        }

        static void MoveHand(HandPose hand, Vector3 by)
        {
            for (int i = 0; i < HandPose.JointCount; i++)
                hand.Joints[i] += by;
        }

        /// <summary>An open hand, fingers along its forward, palm facing its down.</summary>
        static HandPose MadeUpHand(bool isLeft, Vector3 wrist, Quaternion turn)
        {
            var hand = new HandPose(isLeft) { Tracked = true };
            float side = isLeft ? 1f : -1f;
            void Set(Joint joint, float x, float z) => hand.Joints[(int)joint] = wrist + turn * new Vector3(x * side, 0f, z);
            Set(Joint.Wrist, 0f, 0f);
            Set(Joint.Palm, 0f, 0.045f);
            Set(Joint.ThumbMetacarpal, 0.025f, 0.02f);
            Set(Joint.ThumbProximal, 0.045f, 0.045f);
            Set(Joint.ThumbDistal, 0.06f, 0.07f);
            Set(Joint.ThumbTip, 0.07f, 0.095f);
            float[] across = { 0.022f, 0.004f, -0.014f, -0.03f };
            float[] lengths = { 1f, 1.08f, 1f, 0.8f };
            for (int f = 0; f < 4; f++)
            {
                Joint m = Joint.IndexMetacarpal + f * 5;
                float x = across[f], l = lengths[f];
                Set(m, x * 0.5f, 0.02f);
                Set(m + 1, x, 0.085f);
                Set(m + 2, x * 1.1f, 0.085f + 0.042f * l);
                Set(m + 3, x * 1.15f, 0.085f + 0.068f * l);
                Set(m + 4, x * 1.2f, 0.085f + 0.088f * l);
            }
            return hand;
        }
    }
}
