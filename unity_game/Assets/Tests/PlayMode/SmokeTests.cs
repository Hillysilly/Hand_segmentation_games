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

            arena.TestKnockOutAndRespawn();
            yield return Frames(30);
            arena.TestEveryScreen();
            yield return Frames(120);

            arena.TestSandbox();
            yield return Frames(60);
            Assert.That(crashes, Is.Empty, string.Join("\n\n", crashes));
        }

        [UnityTest]
        public IEnumerator EveryEnemyKindFightsWithoutCrashing()
        {
            SceneManager.LoadScene("Main");
            yield return Frames(30);
            var arena = Object.FindAnyObjectByType<Arena>();
            arena.TestStart(CharacterId.Technoman);
            arena.TestGod(true);
            yield return Frames(5);
            arena.TestSkipTime(WaveDirector.CountdownSeconds + 0.1f);
            foreach (EnemySpec spec in EnemySpec.All)
                if (spec.Kind != EnemyKind.Boss)
                    arena.TestSpawn(spec.Kind);
            // Long enough for everyone to walk in and attack a couple of times (lasers, punches, missiles, dashes).
            yield return new WaitForSecondsRealtime(9f);
            Assert.That(arena.TestEnemyCount, Is.GreaterThan(0));
            Assert.That(crashes, Is.Empty, string.Join("\n\n", crashes));
        }

        [UnityTest]
        public IEnumerator ElementalAndArcherEffectsRunWithoutCrashing()
        {
            SceneManager.LoadScene("Main");
            yield return Frames(30);
            var arena = Object.FindAnyObjectByType<Arena>();
            arena.TestStart(CharacterId.Earth);
            arena.TestGod(true);
            yield return Frames(5);
            arena.TestSkipTime(WaveDirector.CountdownSeconds + 0.1f);
            arena.TestSpawn(EnemyKind.Tank);
            arena.TestSpawn(EnemyKind.Robot);
            yield return new WaitForSecondsRealtime(Enemy.SpawnSeconds + 0.4f);
            Assert.That(arena.TestElementEffects(), Is.EqualTo(1), "no enemy to aim at");
            yield return new WaitForSecondsRealtime(1f);
            arena.TestSnap();
            yield return new WaitForSecondsRealtime(5f);
            Assert.That(crashes, Is.Empty, string.Join("\n\n", crashes));
        }

        [UnityTest]
        public IEnumerator TheBossRunsItsWholeRoutine()
        {
            SceneManager.LoadScene("Main");
            yield return Frames(30);
            var arena = Object.FindAnyObjectByType<Arena>();
            arena.TestStart(CharacterId.Technoman);
            arena.TestGod(true);
            yield return Frames(5);
            arena.TestSkipTime(WaveDirector.CountdownSeconds + 0.1f);
            Enemy boss = arena.TestSpawn(EnemyKind.Boss);
            Assert.That(boss.IsBoss, Is.True);
            // Fans, strikes, missiles and (after a hit below 60%) the call for help: about 15 s for one loop.
            yield return new WaitForSecondsRealtime(8f);
            arena.TestHurt(boss, boss.MaxHp * 0.5f);
            yield return new WaitForSecondsRealtime(12f);
            Assert.That(boss != null && boss.Alive, Is.True, "the boss died on its own");
            Assert.That(arena.TestBossBarShown, Is.True, "no boss bar on the HUD");
            Assert.That(crashes, Is.Empty, string.Join("\n\n", crashes));
        }

        [UnityTest]
        public IEnumerator SpiderWebsAnEnemyAndFlingsIt()
        {
            SceneManager.LoadScene("Main");
            yield return Frames(30);
            var arena = Object.FindAnyObjectByType<Arena>();
            arena.TestStart(CharacterId.Spider);
            yield return Frames(10);
            arena.TestSkipTime(WaveDirector.CountdownSeconds + 0.5f);
            yield return new WaitForSecondsRealtime(Enemy.SpawnSeconds + 0.5f);
            arena.TestAdvance();   // level 2

            (ushort id, float hp) = arena.TestWebAndFling();
            Assert.That(id, Is.Not.EqualTo(0), "no enemy was ready to web");
            yield return new WaitForSecondsRealtime(2f);
            Assert.That(arena.TestAnyWebbed, Is.False, "the flung enemy never landed");
            float after = arena.TestEnemyHp(id);
            Assert.That(after < hp, Is.True, $"landing didn't hurt it ({hp} then {after}; -1 means it died)");
            Assert.That(crashes, Is.Empty, string.Join("\n\n", crashes));
        }

        [UnityTest]
        public IEnumerator VariantLabLetsYouPractiseTheItem()
        {
            SceneManager.LoadScene("Main");
            yield return Frames(30);
            var arena = Object.FindAnyObjectByType<Arena>();

            arena.TestOpenLab(2);   // energy ball: the Ki fighter at level 2
            yield return new WaitForSecondsRealtime(Enemy.SpawnSeconds + 0.3f);
            Assert.That(arena.TestPracticeState(), Is.EqualTo("True KiFighter 2 Repulsor dummy"));

            arena.TestOpenLab(1);   // dragon fist: the Ki fighter at level 3
            yield return Frames(5);
            Assert.That(arena.TestPracticeState(), Does.StartWith("True KiFighter 3 Repulsor"));

            arena.TestOpenLab(5);   // enemies: the whole roster, each in its own model
            yield return new WaitForSecondsRealtime(Enemy.SpawnSeconds + 0.5f);
            Assert.That(arena.TestLabEnemyLooks(), Does.Match(@"^Look \w+(, Look \w+){" + (EnemySpec.All.Length - 1) + "}$"));

            arena.TestLeaveLab();
            yield return Frames(5);
            Assert.That(arena.TestPracticeState(), Does.StartWith("False Technoman 1 None"));
            Assert.That(crashes, Is.Empty, string.Join("\n\n", crashes));
        }
    }
}
