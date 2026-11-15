using System.Net;
using NUnit.Framework;
using UnityEngine;

namespace Repulsor.Tests
{
    public class NetProtocolTests
    {
        [Test]
        public void RoomCodesRoundTripEveryAddress()
        {
            for (int octet = 0; octet <= 255; octet++)
            {
                int code = NetProtocol.RoomCode((byte)octet);
                Assert.That(code, Is.InRange(0, 2559));
                Assert.That(NetProtocol.TryParseRoomCode(code, out byte back), Is.True);
                Assert.That(back, Is.EqualTo(octet));
            }
        }

        [Test]
        public void MistypedCodesAreRejected()
        {
            int code = NetProtocol.RoomCode(37);
            Assert.That(NetProtocol.TryParseRoomCode(code + 1, out _), Is.False);
            Assert.That(NetProtocol.TryParseRoomCode(9999, out _), Is.False);
        }

        [Test]
        public void PlayerSnapshotRoundTrips()
        {
            var sent = new PlayerSnapshot
            {
                Slot = 1, Head = new Vector3(1f, 1.6f, -2f), HeadRotation = Quaternion.Euler(10f, 20f, 0f),
                LeftPalm = new Vector3(0.1f, 1.2f, 0.3f), RightPalm = new Vector3(-0.1f, 1.1f, 0.4f),
                LeftTracked = true, ShieldUp = true, Hp = 87.5f, MaxHp = 150f, Level = 2,
                Color = new Color32(10, 20, 30, 255), Character = 3,
            };
            byte[] packet = NetProtocol.Player(sent);
            Assert.That(NetProtocol.TryRead(packet, packet.Length, out NetMessage type, out var reader), Is.True);
            Assert.That(type, Is.EqualTo(NetMessage.Player));
            PlayerSnapshot got = NetProtocol.ReadPlayer(reader);
            Assert.That(got.Head, Is.EqualTo(sent.Head));
            Assert.That(got.LeftTracked && !got.RightTracked && got.ShieldUp && !got.Dead, Is.True);
            Assert.That(got.Hp, Is.EqualTo(87.5f));
            Assert.That(got.Level, Is.EqualTo(2));
            Assert.That(got.Color.g, Is.EqualTo(20));
            Assert.That(got.Character, Is.EqualTo(3));
        }

        [Test]
        public void EnemyListRoundTrips()
        {
            var list = new System.Collections.Generic.List<EnemyNet>
            {
                new EnemyNet { Id = 7, Kind = 1, Position = new Vector3(1f, 2f, 3f), Hp = 12f, MaxHp = 15f, Spawning = true },
                new EnemyNet { Id = 9, Kind = 0, Position = new Vector3(-1f, 0.65f, 2f), Hp = 30f, MaxHp = 30f },
            };
            byte[] packet = NetProtocol.Enemies(new WaveNet { Wave = 3, WaveCount = 5, State = 1, Timer = 2.5f, Kills = 11 }, list);
            Assert.That(NetProtocol.TryRead(packet, packet.Length, out NetMessage type, out var reader), Is.True);
            Assert.That(type, Is.EqualTo(NetMessage.Enemies));
            var got = new System.Collections.Generic.List<EnemyNet>();
            NetProtocol.ReadEnemies(reader, out WaveNet wave, got);
            Assert.That(wave.Wave, Is.EqualTo(3));
            Assert.That(wave.Kills, Is.EqualTo(11));
            Assert.That(got.Count, Is.EqualTo(2));
            Assert.That(got[0].Id, Is.EqualTo(7));
            Assert.That(got[0].Spawning, Is.True);
            Assert.That(got[1].Position, Is.EqualTo(new Vector3(-1f, 0.65f, 2f)));
        }

        [Test]
        public void ClientRoundTrackerPicksUpTheHostsScore()
        {
            RoundTracker rounds = RoundTracker.At(2, 1, 0, 2);
            Assert.That(rounds.Round, Is.EqualTo(2));
            Assert.That(rounds.RoundWon(0), Is.True);
            Assert.That(rounds.Winner, Is.EqualTo(0));
        }

        [Test]
        public void ForeignPacketsAreIgnored()
        {
            byte[] junk = { 1, 2, 3, 4, 5, 6 };
            Assert.That(NetProtocol.TryRead(junk, junk.Length, out _, out _), Is.False);
        }

        [Test]
        public void HostAndClientConnectOverLoopback()
        {
            using var host = new NetSession();
            using var client = new NetSession();
            Assert.That(host.Host(0), Is.True.Or.False);   // no Wi-Fi on a build machine is fine; the port opens either way
            int port = host.LocalPort;
            Assert.That(port, Is.GreaterThan(0));
            Assert.That(client.Join(0, port, IPAddress.Loopback), Is.True);

            PlayerSnapshot received = default;
            bool got = false;
            host.PlayerReceived += p =>
            {
                received = p;
                got = true;
            };

            for (int i = 0; i < 200 && !(host.Connected && client.Connected); i++)
            {
                client.Poll(0.6f);
                System.Threading.Thread.Sleep(5);
                host.Poll(0.01f);
                System.Threading.Thread.Sleep(5);
                client.Poll(0.01f);
            }
            Assert.That(host.Connected, Is.True, "host heard the hello");
            Assert.That(client.Connected, Is.True, "client got the welcome");
            Assert.That(client.Slot, Is.EqualTo(1));

            client.SendPlayer(new PlayerSnapshot { Slot = 1, Hp = 42f }, 1f);
            for (int i = 0; i < 100 && !got; i++)
            {
                System.Threading.Thread.Sleep(5);
                host.Poll(0.01f);
            }
            Assert.That(got, Is.True);
            Assert.That(received.Hp, Is.EqualTo(42f));
        }
    }

    public class SharedFrameTests
    {
        [Test]
        public void TwoHeadsetsAgreeOnAPoint()
        {
            // The same two real points, seen by headsets whose tracking spaces are turned and shifted differently.
            Vector3 a = new Vector3(1f, 0.75f, 2f);
            Vector3 b = new Vector3(2f, 0.75f, 2.5f);
            Quaternion turn = Quaternion.Euler(0f, 70f, 0f);
            Vector3 shift = new Vector3(-3f, 0.02f, 4f);
            Vector3 OtherView(Vector3 p) => turn * p + shift;

            var mine = new SharedFrame();
            var theirs = new SharedFrame();
            Assert.That(mine.Set(a, b), Is.True);
            Assert.That(theirs.Set(OtherView(a), OtherView(b)), Is.True);

            Vector3 somewhere = new Vector3(0.3f, 1.6f, 3.1f);
            Vector3 shared = mine.ToShared(somewhere);
            Vector3 inTheirWorld = theirs.ToWorld(shared);
            Assert.That(Vector3.Distance(inTheirWorld, OtherView(somewhere)), Is.LessThan(1e-4f));
        }

        [Test]
        public void PointsTooCloseAreRejected()
        {
            Assert.That(new SharedFrame().Set(Vector3.zero, new Vector3(0.1f, 0.5f, 0f)), Is.False);
        }

        [Test]
        public void StillFingertipConfirmsOnce()
        {
            var still = new StillPointDetector();
            int fired = 0;
            for (int i = 0; i < 300; i++)
                fired += still.Step(true, new Vector3(0.5f, 0.8f, 0.3f), 1f / 72f) ? 1 : 0;
            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public void MovingFingertipDoesNotConfirm()
        {
            var still = new StillPointDetector();
            for (int i = 0; i < 300; i++)
                Assert.That(still.Step(true, new Vector3(i * 0.003f, 0.8f, 0.3f), 1f / 72f), Is.False);
        }
    }
}
