using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Repulsor.Tests
{
    /// <summary>
    /// Runs the real scene on the PC, where there's no headset, no hands and no camera, and fails on any exception
    /// thrown from the game's own code. Catches start-up and per-frame crashes the headset would otherwise show
    /// only after a rebuild.
    /// </summary>
    public class SmokeTests
    {
        readonly List<string> crashes = new List<string>();

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception && stack != null && stack.Contains("Repulsor."))
                crashes.Add(message + "\n" + stack);
        }

        [SetUp]
        public void Listen()
        {
            crashes.Clear();
            LogAssert.ignoreFailingMessages = true;   // XR and camera complain on a PC; that's expected
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void StopListening()
        {
            Application.logMessageReceived -= OnLog;
            Time.timeScale = 1f;
        }

        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
                yield return null;
        }

        [UnityTest]
        public IEnumerator SceneStartsAndEveryCharacterPlaysAWave()
        {
            SceneManager.LoadScene("Main");
            yield return Frames(30);
            var arena = Object.FindAnyObjectByType<Arena>();
            Assert.That(arena, Is.Not.Null, "Arena wasn't added at start");
            Assert.That(arena.Phase, Is.EqualTo(Phase.Title));

            foreach (CharacterDef character in CharacterDef.All)
            {
                arena.TestStart(character.Id);
                // Through the countdown and into the first wave, so enemies spawn, move and shoot.
                yield return Frames(30);
                arena.TestSkipTime(WaveDirector.CountdownSeconds + 6f);
                yield return Frames(200);
                arena.TestAdvance();   // fill the meter and transform, twice
                yield return Frames(30);
                arena.TestAdvance();
                yield return Frames(120);
                Assert.That(arena.Phase, Is.EqualTo(Phase.Playing), $"{character.Name} match ended early");
            }

            arena.TestSandbox();
            yield return Frames(60);
            Assert.That(crashes, Is.Empty, string.Join("\n\n", crashes));
        }
    }
}
