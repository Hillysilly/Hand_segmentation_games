using System.Collections;
using System.Collections.Generic;
using System.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Repulsor.Tests
{
    /// <summary>
    /// The host side of co-op for real: the game hosts a room, and a bare NetSession in the same process joins over
    /// loopback as the second player. It checks enemies are sent, the client's hits land, and the client shows up.
    /// </summary>
    public class CoopTests
    {
        const int Port = 47901;

        [UnityTest]
        public IEnumerator HostSendsEnemiesAndTakesTheClientsHits()
        {
            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            var arena = Object.FindAnyObjectByType<Arena>();
            Assert.That(arena, Is.Not.Null);
            Assert.That(arena.TestHost(Port), Is.True, "host couldn't open its port");

            using var client = new NetSession();
            Assert.That(client.Join(0, Port, IPAddress.Loopback), Is.True);
            WaveNet lastWave = default;
            var enemies = new List<EnemyNet>();
            client.EnemiesReceived += (wave, list) =>
            {
                lastWave = wave;
                enemies.Clear();
                enemies.AddRange(list);
            };

            for (int i = 0; i < 200 && !(client.Connected && arena.TestPartnerConnected); i++)
            {
                client.Poll(Time.unscaledDeltaTime);
                yield return null;
            }
            Assert.That(client.Connected, Is.True, "client never got the welcome");

            arena.TestStartHosting();
            for (int i = 0; i < 400 && enemies.Count == 0; i++)
            {
                client.Poll(Time.unscaledDeltaTime);
                client.SendPlayer(new PlayerSnapshot { Slot = 1, Head = new Vector3(1f, 1.6f, 1f), HeadRotation = Quaternion.identity, Hp = 90f, MaxHp = 100f, Level = 1, Lives = 3 }, 1f);
                yield return null;
            }
            Assert.That(enemies.Count, Is.GreaterThan(0), "no enemies arrived at the client");
            Assert.That(lastWave.Wave, Is.EqualTo(1));
            Assert.That(arena.TestPartnerVisible, Is.True, "the client's snapshots didn't make it a visible partner");

            ushort id = enemies[0].Id;
            float before = arena.TestEnemyHp(id);
            // The enemy may still be inside its spawn bubble (0.9 s), when hits don't count; keep trying for a while.
            float until = Time.realtimeSinceStartup + 4f;
            float nextHit = 0f;
            while (Time.realtimeSinceStartup < until && arena.TestEnemyHp(id) >= before)
            {
                if (Time.realtimeSinceStartup >= nextHit)
                {
                    nextHit = Time.realtimeSinceStartup + 0.25f;
                    client.SendEnemyHit(id, 5f);
                }
                client.Poll(Time.unscaledDeltaTime);
                yield return null;
            }
            float after = arena.TestEnemyHp(id);
            Assert.That(after, Is.LessThan(before), $"hit didn't land: {before} -> {after}");
        }

        [UnityTest]
        public IEnumerator ClientCopiesTheHostsEnemiesAndSendsHits()
        {
            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            var arena = Object.FindAnyObjectByType<Arena>();
            Assert.That(arena, Is.Not.Null);

            using var host = new NetSession();
            host.Host(Port + 1);
            ushort hitId = 0;
            float hitDamage = 0f;
            host.EnemyHitReceived += (id, damage) =>
            {
                hitId = id;
                hitDamage += damage;
            };
            Assert.That(arena.TestJoin(Port + 1), Is.True);

            for (int i = 0; i < 300 && !host.Connected; i++)
            {
                host.Poll(Time.unscaledDeltaTime);
                yield return null;
            }
            Assert.That(host.Connected, Is.True, "the game's hello never reached the host");

            host.SendStart(1, 0);
            var list = new List<EnemyNet>
            {
                new EnemyNet { Id = 42, Kind = 0, Position = new Vector3(0f, 0.65f, 3f), Hp = 30f, MaxHp = 30f },
            };
            float until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until && (arena.Phase != Phase.Playing || arena.TestEnemyCount == 0))
            {
                host.SendEnemies(new WaveNet { Wave = 1, WaveCount = 5, State = (byte)WaveState.Fighting }, list);
                host.Poll(Time.unscaledDeltaTime);
                yield return null;
            }
            Assert.That(arena.Phase, Is.EqualTo(Phase.Playing), "the host's start didn't start the match");
            Assert.That(arena.TestEnemyCount, Is.EqualTo(1), "the host's enemy wasn't copied");

            // Past the copy's spawn bubble, then hit it.
            until = Time.realtimeSinceStartup + 4f;
            float nextHit = 0f;
            while (Time.realtimeSinceStartup < until && hitDamage <= 0f)
            {
                if (Time.realtimeSinceStartup >= nextHit)
                {
                    nextHit = Time.realtimeSinceStartup + 0.25f;
                    arena.TestHitFirstEnemy(5f);
                }
                host.SendEnemies(new WaveNet { Wave = 1, WaveCount = 5, State = (byte)WaveState.Fighting }, list);
                host.Poll(Time.unscaledDeltaTime);
                yield return null;
            }
            Assert.That(hitId, Is.EqualTo(42));
            Assert.That(hitDamage, Is.GreaterThan(0f), "the hit never reached the host");

            // The host drops it from its list: the copy goes too.
            list.Clear();
            for (int i = 0; i < 30; i++)
            {
                host.SendEnemies(new WaveNet { Wave = 1, WaveCount = 5, State = (byte)WaveState.Fighting }, list);
                host.Poll(Time.unscaledDeltaTime);
                yield return null;
            }
            Assert.That(arena.TestEnemyCount, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator PvpHostTakesHitsAndWinsTheRoundWhenTheClientFalls()
        {
            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            var arena = Object.FindAnyObjectByType<Arena>();
            Assert.That(arena.TestHost(Port + 2, true), Is.True);

            using var client = new NetSession();
            client.Join(0, Port + 2, IPAddress.Loopback);
            int roundsSeen = 0;
            byte winner = 255;
            client.RoundReceived += (round, hostWins, clientWins, who, over) =>
            {
                roundsSeen++;
                winner = who;
            };
            for (int i = 0; i < 300 && !(client.Connected && arena.TestPartnerConnected); i++)
            {
                client.Poll(Time.unscaledDeltaTime);
                yield return null;
            }
            Assert.That(client.Connected, Is.True);
            Assert.That(client.Mode, Is.EqualTo((byte)GameMode.Pvp), "the welcome didn't carry the mode");

            arena.TestStartHosting();
            var alive = new PlayerSnapshot { Slot = 1, Head = new Vector3(0f, 1.6f, 2f), HeadRotation = Quaternion.identity, Hp = 100f, MaxHp = 100f, Level = 1, Lives = 3 };

            // Through the 3 s round countdown, then hit the host.
            float until = Time.realtimeSinceStartup + 3.5f;
            while (Time.realtimeSinceStartup < until)
            {
                client.SendPlayer(alive, 1f);
                client.Poll(Time.unscaledDeltaTime);
                yield return null;
            }
            float before = arena.TestHp;
            client.SendPlayerHit(10f);
            until = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < until && arena.TestHp >= before)
            {
                client.SendPlayer(alive, 1f);
                client.Poll(Time.unscaledDeltaTime);
                yield return null;
            }
            Assert.That(arena.TestHp, Is.EqualTo(before - 10f).Within(0.01f), "the client's hit didn't land on the host");

            // The client falls: the host should win the round and tell the client.
            PlayerSnapshot fallen = alive;
            fallen.Dead = true;
            fallen.Hp = 0f;
            until = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < until && roundsSeen == 0)
            {
                client.SendPlayer(fallen, 1f);
                client.Poll(Time.unscaledDeltaTime);
                yield return null;
            }
            Assert.That(roundsSeen, Is.EqualTo(1), "no round result reached the client");
            Assert.That(winner, Is.EqualTo(0), "the host should have won the round");
        }

        /// <summary>
        /// The client's copy of GOLIATH plays the host's swing (11/15: before, only the host's player could be hit by its
        /// swing, slam and shockwave), and the other player's laser and real aura are drawn. Pictures:
        /// Logs/play_coop_remote.png.
        /// </summary>
        [UnityTest]
        public IEnumerator ClientPlaysTheMechsSwingAndDrawsThePartnersLooks()
        {
            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            var arena = Object.FindAnyObjectByType<Arena>();
            Assert.That(arena, Is.Not.Null);

            using var host = new NetSession();
            host.Host(Port + 3);
            Assert.That(arena.TestJoin(Port + 3), Is.True);
            for (int i = 0; i < 300 && !host.Connected; i++)
            {
                host.Poll(Time.unscaledDeltaTime);
                yield return null;
            }
            Assert.That(host.Connected, Is.True, "the game's hello never reached the host");
            host.SendStart(1, 0);

            Vector3 me = arena.LocalHead;
            Vector3 ahead = new Vector3(me.x, 0f, me.z + 1.6f);
            var list = new List<EnemyNet>
            {
                new EnemyNet { Id = 7, Kind = (byte)EnemyKind.Mech, Position = ahead + Vector3.up * 1.2f, Hp = 900f, MaxHp = 900f },
            };
            var partner = new PlayerSnapshot
            {
                Slot = 0, Head = new Vector3(me.x + 1.2f, 1.6f, me.z + 2.5f), HeadRotation = Quaternion.Euler(0f, 180f, 0f),
                LeftPalm = new Vector3(me.x + 1f, 1.3f, me.z + 2.2f), RightPalm = new Vector3(me.x + 1.4f, 1.3f, me.z + 2.2f),
                LeftTracked = true, RightTracked = true, Hp = 100f, MaxHp = 100f, Level = 3, Color = new Color(1f, 0.82f, 0.25f),
                Character = (byte)CharacterId.KiFighter, Lives = 3,
                RightBeam = (byte)HandBeam.Laser, RightBeamFrom = new Vector3(me.x + 1.4f, 1.3f, me.z + 2.2f),
                RightBeamTo = new Vector3(me.x - 1f, 1.1f, me.z + 3.5f),
            };
            float until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until && (arena.Phase != Phase.Playing || arena.TestEnemyCount == 0))
            {
                host.SendEnemies(new WaveNet { Wave = 10, WaveCount = 10, State = (byte)WaveState.Fighting }, list);
                host.SendPlayer(partner, 1f);
                host.Poll(Time.unscaledDeltaTime);
                yield return null;
            }
            Assert.That(arena.TestEnemyCount, Is.EqualTo(1), "the host's mech wasn't copied");

            // Past its spawn, keep the partner and the mech coming, then the host's swing straight at you.
            until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until)
            {
                host.SendEnemies(new WaveNet { Wave = 10, WaveCount = 10, State = (byte)WaveState.Fighting }, list);
                host.SendPlayer(partner, 1f);
                host.Poll(Time.unscaledDeltaTime);
                yield return null;
            }
            Assert.That(arena.TestRemoteBeam(1), Is.True, "the partner's laser isn't drawn");
            Assert.That(arena.TestRemoteAura, Is.True, "the partner's aura isn't drawn");
            Snap(me + new Vector3(0f, 0.1f, -0.6f), partner.Head + Vector3.down * 0.6f, "coop_remote");

            // Ryuu level 2 charging an energy ball, the Valkyr's drawn bow, Terra level 3: pictures of each.
            Vector3 chest = partner.Head + new Vector3(0f, -0.3f, -0.3f);
            partner.Character = (byte)CharacterId.KiFighter;
            partner.Level = 2;
            partner.RightBeam = 0;
            partner.LeftBeam = (byte)HandBeam.KiCharge;
            partner.LeftBeamFrom = chest;
            partner.LeftBeamTo = chest + Vector3.up * 0.8f;
            yield return Pump(host, partner, 0.6f);
            Assert.That(arena.TestRemoteCharge, Is.True, "the partner's charging ball isn't drawn");
            Assert.That(arena.TestRemoteAura, Is.True, "Ryuu's level 2 aura isn't drawn");
            Snap(me + new Vector3(0f, 0.1f, -0.6f), partner.Head + Vector3.down * 0.6f, "coop_ryu2");
            partner.Character = (byte)CharacterId.Archer;
            partner.LeftBeam = (byte)HandBeam.Bow;
            partner.LeftBeamFrom = chest + new Vector3(0.1f, 0.1f, -0.35f);
            partner.LeftBeamTo = chest + new Vector3(0.05f, 0.1f, 0.15f);
            partner.RightBeam = (byte)HandBeam.BowAim;
            partner.RightBeamFrom = partner.LeftBeamFrom;
            partner.RightBeamTo = partner.LeftBeamFrom + new Vector3(0f, 0f, -0.75f);
            yield return Pump(host, partner, 0.4f);
            Snap(me + new Vector3(0.6f, 0.1f, -0.2f), partner.Head + Vector3.down * 0.4f, "coop_bow");
            partner.Character = (byte)CharacterId.Earth;
            partner.Level = 3;
            partner.LeftBeam = partner.RightBeam = 0;
            yield return Pump(host, partner, 0.6f);
            Snap(me + new Vector3(0f, 0.3f, -0.6f), partner.Head + Vector3.down * 0.9f, "coop_terra3");

            float before = arena.TestHp;
            Vector3 facing = Vector3.ProjectOnPlane(me - ahead, Vector3.up).normalized;
            host.SendEnemyMove(new EnemyMoveEvent { Id = 7, Move = MechBrain.SwingMove, A = facing, F = 0.5f });
            until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until && arena.TestHp >= before)
            {
                host.SendEnemies(new WaveNet { Wave = 10, WaveCount = 10, State = (byte)WaveState.Fighting }, list);
                host.SendPlayer(partner, 1f);
                host.Poll(Time.unscaledDeltaTime);
                yield return null;
            }
            Debug.Log($"[Test] client HP {before} -> {arena.TestHp}");
            Assert.That(arena.TestHp, Is.LessThan(before), "the mech's swing didn't reach the client's player");
        }

/// <summary>
        /// Multiplayer pause (11/15): the other player pausing pauses you; it goes on only when both are ready. And a
        /// match start leaves nobody ready, so character select afterwards waits for both again.
        /// </summary>
        [UnityTest]
        public IEnumerator PausingPausesBothAndBothMustBeReady()
        {
            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            var arena = Object.FindAnyObjectByType<Arena>();
            using var host = new NetSession();
            host.Host(Port + 4);
            Assert.That(arena.TestJoin(Port + 4), Is.True);
            for (int i = 0; i < 300 && !host.Connected; i++)
            {
                host.Poll(Time.unscaledDeltaTime);
                yield return null;
            }
            Assert.That(host.Connected, Is.True);
            host.SendStart(1, 0);
            var partner = new PlayerSnapshot { Head = new Vector3(1f, 1.6f, 2f), HeadRotation = Quaternion.identity, Hp = 100f, MaxHp = 100f, Level = 1, Lives = 3 };
            float until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until && arena.Phase != Phase.Playing)
            {
                host.SendPlayer(partner, 1f);
                host.Poll(Time.unscaledDeltaTime);
                yield return null;
            }
            Assert.That(arena.Phase, Is.EqualTo(Phase.Playing));
            Assert.That(arena.TestReady, Is.False, "readiness from before the match carried into it");

            // The host pauses: so do you.
            partner.Paused = true;
            yield return Pump(host, partner, 0.5f);
            Assert.That(arena.TestPaused, Is.True, "the other player's pause didn't pause this headset");
            // Only they are ready: still paused.
            partner.PauseReady = true;
            yield return Pump(host, partner, 0.5f);
            Assert.That(arena.TestPaused, Is.True, "resumed without this player being ready");
            // Both ready: on we go.
            arena.TestPauseReady(true);
            yield return Pump(host, partner, 0.5f);
            Assert.That(arena.TestPaused, Is.False, "didn't resume with both ready");
            // Their last paused-and-ready updates don't pause it again.
            yield return Pump(host, partner, 0.3f);
            Assert.That(arena.TestPaused, Is.False);
            partner.Paused = partner.PauseReady = false;
            yield return Pump(host, partner, 0.3f);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        static IEnumerator Pump(NetSession host, PlayerSnapshot partner, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                host.SendPlayer(partner, 1f);
                host.Poll(Time.unscaledDeltaTime);
                yield return null;
            }
        }

                static void Snap(Vector3 eye, Vector3 target, string name)
        {
            var cameraObject = new GameObject("Capture Camera", typeof(Camera));
            cameraObject.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.36f, 0.34f, 0.31f);
            camera.nearClipPlane = 0.05f;
            camera.fieldOfView = 70f;
            var texture = new RenderTexture(800, 600, 24) { antiAliasing = 4 };
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(800, 600, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 800, 600), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;
            Object.Destroy(cameraObject);
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllBytes($"Logs/play_{name}.png", image.EncodeToPNG());
        }
    }
}
