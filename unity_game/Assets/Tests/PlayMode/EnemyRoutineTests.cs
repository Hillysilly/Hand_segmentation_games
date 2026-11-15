using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Repulsor.Tests
{
    /// <summary>
    /// The Dasher's routine (tell, dash, plant, shoot), and grabbing enemies: the force grabs and flings like the
    /// Spider's web, and nothing grabs the boss. Pictures go to Logs/play_dasher_*.png.
    /// </summary>
    public class EnemyRoutineTests
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
            File.WriteAllBytes($"Logs/play_dasher_{name}.png", image.EncodeToPNG());
        }

        static IEnumerator WaitFor(Enemy dasher, string state, float seconds)
        {
            float waited = 0f;
            while (dasher.DashState != state && waited < seconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator TheDasherShowsWhereItDashesThenDashesAndShoots()
        {
            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene("Main");
            for (int i = 0; i < 30; i++)
                yield return null;
            var arena = Object.FindAnyObjectByType<Arena>();
            Transform head = Camera.main.transform;
            arena.TestStart(CharacterId.Technoman);
            arena.TestGod(true);
            yield return null;
            arena.TestSkipTime(WaveDirector.CountdownSeconds + 0.1f);
            Enemy dasher = arena.TestSpawn(EnemyKind.Dasher);
            dasher.transform.position = head.position + new Vector3(0f, -0.6f, 2.6f);
            yield return new WaitForSecondsRealtime(Enemy.SpawnSeconds + 0.3f);

            Vector3 eye = head.position + new Vector3(0.8f, 0.6f, -0.6f);
            yield return WaitFor(dasher, "Tell", 6f);
            Assert.That(dasher.DashState, Is.EqualTo("Tell"), "it gets ready to dash");
            Vector3 before = dasher.transform.position;
            yield return new WaitForSecondsRealtime(Enemy.DashTellSeconds * 0.7f);
            Snap(eye, dasher.transform.position, "tell");
            yield return WaitFor(dasher, "Dash", 2f);
            yield return new WaitForSecondsRealtime(Enemy.DashSeconds * 0.6f);
            Snap(eye, dasher.transform.position, "dash");
            yield return WaitFor(dasher, "Shoot", 2f);
            float moved = Vector3.Distance(Vector3.ProjectOnPlane(before, Vector3.up), Vector3.ProjectOnPlane(dasher.transform.position, Vector3.up));
            Debug.Log($"[Test] dasher moved {moved:0.00} m, state {dasher.DashState}");
            Assert.That(dasher.DashState, Is.EqualTo("Shoot"), "it plants and shoots");
            Assert.That(moved, Is.GreaterThan(Enemy.DashLength * 0.6f), "it dashed");
        }

        [UnityTest]
        public IEnumerator TheForceGrabsAndFlingsEnemiesButNothingGrabsTheBoss()
        {
            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene("Main");
            for (int i = 0; i < 30; i++)
                yield return null;
            var arena = Object.FindAnyObjectByType<Arena>();
            Transform head = Camera.main.transform;
            arena.TestStart(CharacterId.Saber);
            arena.TestGod(true);
            yield return null;
            arena.TestSkipTime(WaveDirector.CountdownSeconds + 0.1f);
            Enemy boss = arena.TestSpawn(EnemyKind.Boss);
            boss.transform.position = head.position + new Vector3(-2f, 0f, 4f);
            Enemy dasher = arena.TestSpawn(EnemyKind.Dasher);
            dasher.transform.position = head.position + new Vector3(1.5f, -0.6f, 3f);
            yield return new WaitForSecondsRealtime(Enemy.SpawnSeconds + 0.3f);

            Assert.That(arena.TestCanGrab(boss), Is.False, "the boss is too big to web or grab");
            Assert.That(arena.TestCanGrab(dasher), Is.True, "a dasher can be grabbed");
            (ushort id, float hp) = arena.TestWebAndFling(true);
            Assert.That(id, Is.Not.EqualTo(0));
            yield return new WaitForSecondsRealtime(2f);
            Assert.That(arena.TestAnyWebbed, Is.False, "the flung enemy landed");
            Assert.That(arena.TestEnemyHp(id) < hp, Is.True, "landing hurt it");
        }
    }
}
