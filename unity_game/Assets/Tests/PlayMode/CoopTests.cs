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
    }
}
