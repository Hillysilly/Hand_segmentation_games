using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Repulsor.Tests
{
    /// <summary>
    /// The character select screen on the PC: the whole screen as you'd see it, and close-ups of the mannequin
    /// doing a move for several characters and levels. Pictures go to Logs/play_select_*.png.
    /// </summary>
    public class ShowcaseCaptureTests
    {
        static void Snap(Vector3 position, Vector3 target, float fov, string name)
        {
            var cameraObject = new GameObject("Capture Camera", typeof(Camera));
            cameraObject.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.32f, 0.3f, 0.28f);
            camera.nearClipPlane = 0.01f;
            camera.fieldOfView = fov;
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
            File.WriteAllBytes($"Logs/play_select_{name}.png", image.EncodeToPNG());
        }

        [UnityTest]
        public IEnumerator TheSelectScreenShowsEachCharactersMoves()
        {
            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene("Main");
            for (int i = 0; i < 20; i++)
                yield return null;
            var arena = Object.FindAnyObjectByType<Arena>();
            Transform head = Camera.main.transform;

            var shots = new (CharacterId id, int level, float wait)[]
            {
                (CharacterId.Technoman, 1, 0.7f),
                (CharacterId.KiFighter, 2, 1.3f),
                (CharacterId.Mage, 2, 0.9f),
                (CharacterId.Saber, 2, 0.6f),
                (CharacterId.Archer, 2, 1.0f),
                (CharacterId.Spider, 1, 0.42f),
                (CharacterId.Elementalist, 3, 1.2f),
                (CharacterId.Earth, 2, 0.5f),
            };
            bool first = true;
            foreach (var shot in shots)
            {
                arena.TestOpenCharacters(shot.id);
                yield return null;
                arena.TestShowcaseLevel(shot.level);
                yield return new WaitForSecondsRealtime(shot.wait);
                CharacterShowcase showcase = arena.TestShowcase;
                Assert.That(showcase, Is.Not.Null);
                Assert.That(showcase.Shown, Is.True);
                Assert.That(showcase.Level, Is.EqualTo(shot.level));
                Debug.Log($"[Test] {shot.id} {shot.level}: {showcase.DebugState}");
                Transform screen = arena.TestCharacterScreen;
                Vector3 figure = screen.TransformPoint(new Vector3(160f, 95f, -40f));
                if (first)
                {
                    Snap(head.position, screen.position, 72f, "screen");
                    first = false;
                }
                Vector3 toward = (head.position - figure).normalized;
                Snap(figure + toward * 0.32f, figure, 40f, $"{shot.id}_{shot.level}");
            }

            // It cycles on its own.
            arena.TestOpenCharacters(CharacterId.Technoman);
            yield return new WaitForSecondsRealtime(4.5f);
            Assert.That(arena.TestShowcase.Level, Is.GreaterThan(1), "moved on to level 2 by itself");
        }
    }
}
