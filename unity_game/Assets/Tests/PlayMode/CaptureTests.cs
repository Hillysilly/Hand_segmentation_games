using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Repulsor.Tests
{
    /// <summary>
    /// Not a check: renders pictures of the real game on the PC for review, to Logs/play_*.png. The roster and the
    /// boss attacking, and the elemental and archer effects. Runs with the PlayMode tests (needs graphics).
    /// </summary>
    public class CaptureTests
    {
        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
                yield return null;
        }

        static void Snap(Vector3 position, Vector3 target, string name)
        {
            var cameraObject = new GameObject("Capture Camera", typeof(Camera));
            cameraObject.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.28f, 0.27f, 0.26f);
            camera.nearClipPlane = 0.05f;
            camera.fieldOfView = 70f;
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
            File.WriteAllBytes($"Logs/play_{name}.png", image.EncodeToPNG());
        }

        [UnityTest]
        public IEnumerator CaptureTheNewEnemiesAndEffects()
        {
            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene("Main");
            yield return Frames(30);
            var arena = Object.FindAnyObjectByType<Arena>();
            Transform head = Camera.main != null ? Camera.main.transform : arena.transform;
            Vector3 eye = head.position + Vector3.up * 1.6f;

            // The roster in the Variant Lab's gallery.
            arena.TestOpenLab(5);
            yield return new WaitForSecondsRealtime(Enemy.SpawnSeconds + 0.6f);
            Snap(eye + Vector3.back * 1.2f + Vector3.up * 0.3f, head.position + Vector3.forward * 2.6f + Vector3.up * 0.8f, "roster");
            arena.TestLeaveLab();
            yield return Frames(5);

            // A boss fight: pictures as it goes through its routine.
            arena.TestStart(CharacterId.Archer);
            arena.TestGod(true);
            yield return Frames(5);
            arena.TestSkipTime(WaveDirector.CountdownSeconds + 0.1f);
            Enemy boss = arena.TestSpawn(EnemyKind.Boss);
            boss.transform.position = head.position + Vector3.forward * 3.5f + Vector3.up * Enemy.MiddleHeight(EnemyKind.Boss);
            arena.TestSpawn(EnemyKind.Rocketeer).transform.position = head.position + new Vector3(-1.5f, 0.72f, 2.5f);
            arena.TestSpawn(EnemyKind.Robot).transform.position = head.position + new Vector3(1.4f, 0.65f, 2.2f);
            for (int i = 0; i < 10; i++)
            {
                yield return new WaitForSecondsRealtime(1.3f);
                Snap(eye + Vector3.back * 2.2f + Vector3.up * 0.4f, head.position + Vector3.forward * 2.5f + Vector3.up * 0.9f, $"boss_{i}");
            }

            // Elemental and archer effects aimed at a tank.
            arena.TestStart(CharacterId.Earth);
            yield return Frames(5);
            arena.TestSkipTime(WaveDirector.CountdownSeconds + 0.1f);
            Enemy tank = arena.TestSpawn(EnemyKind.Tank);
            tank.transform.position = head.position + Vector3.forward * 2.5f + Vector3.up * 0.4f;
            yield return new WaitForSecondsRealtime(Enemy.SpawnSeconds + 0.4f);
            arena.TestElementEffects();
            for (int i = 0; i < 4; i++)
            {
                yield return new WaitForSecondsRealtime(0.15f);
                Snap(eye + new Vector3(1.2f, 0.2f, -0.8f), head.position + Vector3.forward * 1.5f + Vector3.up * 0.6f, $"effects_{i}");
            }
            Assert.Pass("pictures in Logs/play_*.png");
        }
    }
}
