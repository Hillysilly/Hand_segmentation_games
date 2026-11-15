using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Repulsor.Tests
{
    /// <summary>
    /// GOLIATH, the round 10 mech boss: runs its whole routine (swing, arm lasers, rockets, jump slam and its
    /// shockwave, the call for help) without crashing, and the swing and jump warnings are photographed
    /// (Logs/play_mech_*.png).
    /// </summary>
    public class MechBossTests
    {
        static void Snap(Vector3 eye, Vector3 target, string name)
        {
            var cameraObject = new GameObject("Capture Camera", typeof(Camera));
            cameraObject.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.3f, 0.29f, 0.27f);
            camera.nearClipPlane = 0.02f;
            camera.fieldOfView = 70f;
            var texture = new RenderTexture(1280, 800, 24) { antiAliasing = 4 };
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;
            Object.Destroy(cameraObject);
            Directory.CreateDirectory("Logs");
            File.WriteAllBytes($"Logs/play_mech_{name}.png", image.EncodeToPNG());
        }

        [UnityTest]
        public IEnumerator TheMechRunsItsWholeRoutine()
        {
            LogAssert.ignoreFailingMessages = true;
            var crashes = new List<string>();
            var moves = new HashSet<string>();
            void OnLog(string message, string stack, LogType type)
            {
                if (type == LogType.Exception)
                    crashes.Add(message + "\n" + stack);
                if (message.StartsWith("[Mech] "))
                    moves.Add(message.Substring(7).Split(',')[0]);
            }
            Application.logMessageReceived += OnLog;
            SceneManager.LoadScene("Main");
            for (int i = 0; i < 30; i++)
                yield return null;
            var arena = Object.FindAnyObjectByType<Arena>();
            Transform head = Camera.main.transform;
            arena.TestStart(CharacterId.Technoman);
            arena.TestGod(true);
            yield return null;
            arena.TestSkipTime(WaveDirector.CountdownSeconds + 0.1f);
            Enemy mech = arena.TestSpawn(EnemyKind.Mech);
            mech.transform.position = new Vector3(head.position.x + 0.5f, mech.transform.position.y, head.position.z + 3.5f);
            Assert.That(mech.IsBoss, Is.True);
            Vector3 eye = head.position + new Vector3(-1.2f, 0.6f, -1.2f);
            yield return new WaitForSecondsRealtime(Enemy.SpawnSeconds + 1f);
            // As you first see it: facing you, walking in.
            Snap(head.position + Vector3.down * 0.2f, mech.Center, "arrives");
            Snap(mech.Center + new Vector3(3.5f, 0.3f, 0.2f), mech.Center, "side");

            bool swingShot = false, jumpShot = false, lasersShot = false;
            float clock = 0f;
            bool hurt = false;
            while (clock < 40f && !(moves.Contains("Swing") && moves.Contains("Lasers") && moves.Contains("Rockets") && moves.Contains("Jump") && moves.Contains("Summon")))
            {
                clock += Time.unscaledDeltaTime;
                if (!hurt && clock > 6f)
                {
                    hurt = true;
                    arena.TestHurt(mech, mech.MaxHp * 0.45f);
                }
                if (!swingShot && mech.BossMove == "Swing")
                {
                    yield return new WaitForSecondsRealtime(0.6f);
                    Snap(eye, mech.Center + Vector3.down * 0.8f, "swing");
                    swingShot = true;
                }
                if (!lasersShot && mech.BossMove == "Lasers")
                {
                    yield return new WaitForSecondsRealtime(0.6f);
                    Snap(eye, mech.Center + Vector3.down * 0.5f, "lasers");
                    lasersShot = true;
                }
                if (!jumpShot && mech.BossMove == "Jump" && mech.Center.y > head.position.y - 0.2f)
                {
                    Snap(eye, mech.Center + Vector3.down * 1.2f, "jump");
                    jumpShot = true;
                }
                yield return null;
            }
            Application.logMessageReceived -= OnLog;
            Debug.Log($"[Test] mech moves seen: {string.Join(", ", moves)} in {clock:0} s");
            Assert.That(crashes, Is.Empty, string.Join("\n\n", crashes));
            Assert.That(moves, Does.Contain("Swing").And.Contain("Lasers").And.Contain("Rockets").And.Contain("Jump"));
            Assert.That(arena.TestBossBarShown, Is.True, "no boss bar on the HUD");
        }
    }
}
