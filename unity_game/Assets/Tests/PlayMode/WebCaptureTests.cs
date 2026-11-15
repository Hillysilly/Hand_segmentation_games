using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Repulsor.Tests
{
    /// <summary>
    /// The Spider's web shots on the PC: a web flying open toward a robot, and stuck on it after. Pictures go to
    /// Logs/play_web_*.png.
    /// </summary>
    public class WebCaptureTests
    {
        static void Snap(Vector3 position, Vector3 target, string name)
        {
            var cameraObject = new GameObject("Capture Camera", typeof(Camera));
            cameraObject.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.3f, 0.29f, 0.27f);
            camera.nearClipPlane = 0.02f;
            camera.fieldOfView = 60f;
            var texture = new RenderTexture(1280, 800, 24) { antiAliasing = 4 };
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;
            Object.Destroy(cameraObject);
            Directory.CreateDirectory("Logs");
            File.WriteAllBytes($"Logs/play_web_{name}.png", image.EncodeToPNG());
        }

        [UnityTest]
        public IEnumerator WebShotsFlyAsWebsAndStick()
        {
            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene("Main");
            for (int i = 0; i < 30; i++)
                yield return null;
            var arena = Object.FindAnyObjectByType<Arena>();
            Transform head = Camera.main.transform;
            arena.TestStart(CharacterId.Spider);
            arena.TestGod(true);
            yield return null;
            arena.TestSkipTime(WaveDirector.CountdownSeconds + 0.1f);
            Enemy robot = arena.TestSpawn(EnemyKind.Tank);
            robot.transform.position = head.position + new Vector3(0f, -0.9f, 3f);
            yield return new WaitForSecondsRealtime(Enemy.SpawnSeconds + 0.3f);

            Vector3 hand = head.position + new Vector3(0.2f, -0.3f, 0.3f);
            Vector3 eye = head.position + new Vector3(0.6f, 0.1f, -0.4f);
            arena.TestFireWeb(hand, robot.Center - hand, false);
            yield return new WaitForSecondsRealtime(0.18f);
            Snap(eye, head.position + new Vector3(0f, -0.4f, 1.5f), "flying");
            Assert.That(Object.FindAnyObjectByType<WebStrand>(), Is.Not.Null, "the web shot is out");
            yield return new WaitForSecondsRealtime(1f);
            Snap(eye, robot.Center, "stuck");

            // Black webs at level 3, at the wall beside the robot.
            arena.TestFireWeb(hand, robot.Center + new Vector3(1.2f, 0.4f, 0f) - hand, true);
            yield return new WaitForSecondsRealtime(1.2f);
            Snap(eye, robot.Center + new Vector3(0.6f, 0.2f, 0f), "black");
        }

        [UnityTest]
        public IEnumerator TempestsTornadoIsASeeThroughFunnel()
        {
            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene("Main");
            for (int i = 0; i < 30; i++)
                yield return null;
            var arena = Object.FindAnyObjectByType<Arena>();
            Transform head = Camera.main.transform;
            arena.TestStart(CharacterId.Elementalist);
            arena.TestGod(true);
            yield return null;
            var game = Object.FindAnyObjectByType<RepulsorGame>();
            float floor = head.position.y - 1.6f;
            Tornado.Launch(game, head.position + Vector3.forward * 1.5f, Vector3.forward, 1f, floor, 0f, arena.transform);
            yield return new WaitForSecondsRealtime(0.8f);
            Snap(head.position + new Vector3(0.4f, -0.3f, -0.6f), head.position + new Vector3(0f, -0.6f, 2.6f), "tornado");
            Assert.That(Object.FindAnyObjectByType<Tornado>(), Is.Not.Null);
        }
    }
}
