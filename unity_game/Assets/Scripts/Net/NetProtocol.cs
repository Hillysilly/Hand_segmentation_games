using System;
using System.IO;
using UnityEngine;

namespace Repulsor
{
    public enum NetMessage : byte
    {
        Hello = 1,      // client -> host: I want to join
        Welcome = 2,    // host -> client: you're in, here's your slot
        Player = 3,     // either way: my pose and status, about 20 times a second
        Lobby = 4,      // either way: my character, colour and ready flag
        Start = 5,      // host -> client: the match starts
        Bye = 6,        // either way: leaving
        Shot = 7,       // either way: I fired something (for the other headset to draw)
        Enemies = 8,    // host -> client: every enemy and the wave, about 10 times a second
        EnemyHit = 9,   // client -> host: I hit this enemy for this much
        EnemyBolt = 10, // host -> client: an enemy fired
        End = 11,       // host -> client: the match is over
        PlayerHit = 12, // either way: I hit you for this much (PVP or friendly fire)
        Round = 13,     // host -> client: a PVP round ended
        Effect = 14,    // either way: I made a one-off attack effect (for the other headset to draw)
        EnemyMove = 15, // host -> client: a boss began a move (the client plays it and checks its own player)
    }

    public struct EnemyNet
    {
        public ushort Id;
        public byte Kind;
        public Vector3 Position;
        public float Hp;
        public float MaxHp;
        public bool Spawning;
    }

    public struct WaveNet
    {
        public byte Wave;
        public byte WaveCount;
        public byte State;
        public float Timer;
        public ushort Kills;
    }

    /// <summary>A player's state as the other headset needs it. Positions are in the shared (calibrated) frame.</summary>
    public struct PlayerSnapshot
    {
        public byte Slot;
        public Vector3 Head;
        public Quaternion HeadRotation;
        public Vector3 LeftPalm;
        public Vector3 RightPalm;
        public bool LeftTracked;
        public bool RightTracked;
        public float Hp;
        public float MaxHp;
        public byte Level;
        public bool ShieldUp;
        public bool Dead;
        public bool Invincible;
        public bool Boosted;
        public Color32 Color;
        public byte Character;
        public byte Lives;
        /// <summary>What each hand is drawing right now (<see cref="HandBeam"/>), and from where to where.</summary>
        public byte LeftBeam;
        public Vector3 LeftBeamFrom;
        public Vector3 LeftBeamTo;
        public byte RightBeam;
        public Vector3 RightBeamFrom;
        public Vector3 RightBeamTo;
    }

    /// <summary>A held attack drawn from a hand, sent with every player update.</summary>
    public enum HandBeam : byte { None, Laser, Flames, Blade, Lightning }

    /// <summary>How a shot looks, so the other headset draws the same thing.</summary>
    public enum ShotLook : byte { Plain, KiBall, Arrow, Bolt, Rock, Web, Comet }

    /// <summary>One-off attack effects the other headset draws (no damage there).</summary>
    public enum NetEffect : byte { DragonFist, FirePillar, Gust, Tornado, RockSpikes, Explosion, Shockwave, Smoke }

    public struct EffectEvent
    {
        public NetEffect Kind;
        public Vector3 A;
        public Vector3 B;
        public float F;
        public Color32 Color;
        public byte Style;
    }

    /// <summary>A boss move starting (host to client): which enemy, which move, two points and a number.</summary>
    public struct EnemyMoveEvent
    {
        public ushort Id;
        public byte Move;
        public Vector3 A;
        public Vector3 B;
        public float F;
    }

    public struct LobbyState
    {
        public byte Slot;
        public byte Character;
        public Color32 Color;
        public bool Ready;
    }

    public struct ShotEvent
    {
        public byte Slot;
        public Vector3 Origin;
        public Vector3 Direction;
        public float Size;
        public float Speed;
        public Color32 Color;
        public ShotLook Look;
        public byte Style;
    }

    /// <summary>
    /// Packs and unpacks the few messages the game sends. Every packet starts with a magic number and protocol
    /// version, then the message type, so stray traffic on the port is ignored.
    /// </summary>
    public static class NetProtocol
    {
        public const int Port = 47777;
        const ushort Magic = 0x5257;   // "RW"
        // 2 (11/15): shot looks, hand beams, effects and boss moves.
        const byte Version = 2;

        public static byte[] Hello(string name) => Write(NetMessage.Hello, w => w.Write(name ?? ""));

        public static byte[] Welcome(byte slot, int roomCode, byte mode) => Write(NetMessage.Welcome, w =>
        {
            w.Write(slot);
            w.Write(roomCode);
            w.Write(mode);
        });

        public static byte[] PlayerHit(float damage) => Write(NetMessage.PlayerHit, w => w.Write(damage));

        public static byte[] Round(byte round, byte hostWins, byte clientWins, byte winner, bool over) => Write(NetMessage.Round, w =>
        {
            w.Write(round);
            w.Write(hostWins);
            w.Write(clientWins);
            w.Write(winner);
            w.Write(over);
        });

        public static byte[] Bye() => Write(NetMessage.Bye, w => { });

        public static byte[] Start(int seed, byte character) => Write(NetMessage.Start, w =>
        {
            w.Write(seed);
            w.Write(character);
        });

        public static byte[] Player(in PlayerSnapshot p)
        {
            PlayerSnapshot s = p;
            return Write(NetMessage.Player, w =>
            {
                w.Write(s.Slot);
                WriteVector(w, s.Head);
                WriteQuaternion(w, s.HeadRotation);
                WriteVector(w, s.LeftPalm);
                WriteVector(w, s.RightPalm);
                byte flags = (byte)((s.LeftTracked ? 1 : 0) | (s.RightTracked ? 2 : 0) | (s.ShieldUp ? 4 : 0) | (s.Dead ? 8 : 0)
                    | (s.Invincible ? 16 : 0) | (s.Boosted ? 32 : 0));
                w.Write(flags);
                w.Write(s.Hp);
                w.Write(s.MaxHp);
                w.Write(s.Level);
                WriteColor(w, s.Color);
                w.Write(s.Character);
                w.Write(s.Lives);
                w.Write(s.LeftBeam);
                if (s.LeftBeam != 0)
                {
                    WriteVector(w, s.LeftBeamFrom);
                    WriteVector(w, s.LeftBeamTo);
                }
                w.Write(s.RightBeam);
                if (s.RightBeam != 0)
                {
                    WriteVector(w, s.RightBeamFrom);
                    WriteVector(w, s.RightBeamTo);
                }
            });
        }

        public static byte[] Enemies(WaveNet wave, System.Collections.Generic.List<EnemyNet> enemies) => Write(NetMessage.Enemies, w =>
        {
            w.Write(wave.Wave);
            w.Write(wave.WaveCount);
            w.Write(wave.State);
            w.Write(wave.Timer);
            w.Write(wave.Kills);
            int count = Math.Min(enemies.Count, 40);
            w.Write((byte)count);
            for (int i = 0; i < count; i++)
            {
                EnemyNet e = enemies[i];
                w.Write(e.Id);
                w.Write(e.Kind);
                WriteVector(w, e.Position);
                w.Write(e.Hp);
                w.Write(e.MaxHp);
                w.Write(e.Spawning);
            }
        });

        public static void ReadEnemies(BinaryReader r, out WaveNet wave, System.Collections.Generic.List<EnemyNet> into)
        {
            wave = new WaveNet
            {
                Wave = r.ReadByte(),
                WaveCount = r.ReadByte(),
                State = r.ReadByte(),
                Timer = r.ReadSingle(),
                Kills = r.ReadUInt16(),
            };
            into.Clear();
            int count = r.ReadByte();
            for (int i = 0; i < count; i++)
            {
                into.Add(new EnemyNet
                {
                    Id = r.ReadUInt16(),
                    Kind = r.ReadByte(),
                    Position = ReadVector(r),
                    Hp = r.ReadSingle(),
                    MaxHp = r.ReadSingle(),
                    Spawning = r.ReadBoolean(),
                });
            }
        }

        public static byte[] EnemyHit(ushort id, float damage) => Write(NetMessage.EnemyHit, w =>
        {
            w.Write(id);
            w.Write(damage);
        });

        public static void ReadEnemyHit(BinaryReader r, out ushort id, out float damage)
        {
            id = r.ReadUInt16();
            damage = r.ReadSingle();
        }

        /// <summary>An enemy attack starting: which kind of shot, and two points or a point and a direction.</summary>
        public static byte[] EnemyBolt(byte shot, Vector3 origin, Vector3 direction) => Write(NetMessage.EnemyBolt, w =>
        {
            w.Write(shot);
            WriteVector(w, origin);
            WriteVector(w, direction);
        });

        public static void ReadEnemyBolt(BinaryReader r, out byte shot, out Vector3 origin, out Vector3 direction)
        {
            shot = r.ReadByte();
            origin = ReadVector(r);
            direction = ReadVector(r);
        }

        public static byte[] End(bool won) => Write(NetMessage.End, w => w.Write(won));

        public static bool ReadEnd(BinaryReader r) => r.ReadBoolean();

        public static byte[] Lobby(in LobbyState l)
        {
            LobbyState s = l;
            return Write(NetMessage.Lobby, w =>
            {
                w.Write(s.Slot);
                w.Write(s.Character);
                WriteColor(w, s.Color);
                w.Write(s.Ready);
            });
        }

        public static byte[] Shot(in ShotEvent e)
        {
            ShotEvent s = e;
            return Write(NetMessage.Shot, w =>
            {
                w.Write(s.Slot);
                WriteVector(w, s.Origin);
                WriteVector(w, s.Direction);
                w.Write(s.Size);
                w.Write(s.Speed);
                WriteColor(w, s.Color);
                w.Write((byte)s.Look);
                w.Write(s.Style);
            });
        }

        public static byte[] Effect(in EffectEvent e)
        {
            EffectEvent s = e;
            return Write(NetMessage.Effect, w =>
            {
                w.Write((byte)s.Kind);
                WriteVector(w, s.A);
                WriteVector(w, s.B);
                w.Write(s.F);
                WriteColor(w, s.Color);
                w.Write(s.Style);
            });
        }

        public static EffectEvent ReadEffect(BinaryReader r) => new EffectEvent
        {
            Kind = (NetEffect)r.ReadByte(),
            A = ReadVector(r),
            B = ReadVector(r),
            F = r.ReadSingle(),
            Color = ReadColor(r),
            Style = r.ReadByte(),
        };

        public static byte[] EnemyMove(in EnemyMoveEvent e)
        {
            EnemyMoveEvent s = e;
            return Write(NetMessage.EnemyMove, w =>
            {
                w.Write(s.Id);
                w.Write(s.Move);
                WriteVector(w, s.A);
                WriteVector(w, s.B);
                w.Write(s.F);
            });
        }

        public static EnemyMoveEvent ReadEnemyMove(BinaryReader r) => new EnemyMoveEvent
        {
            Id = r.ReadUInt16(),
            Move = r.ReadByte(),
            A = ReadVector(r),
            B = ReadVector(r),
            F = r.ReadSingle(),
        };

        /// <summary>Reads the header. False for anything that isn't ours.</summary>
        public static bool TryRead(byte[] data, int length, out NetMessage type, out BinaryReader reader)
        {
            type = 0;
            reader = null;
            if (data == null || length < 4)
                return false;
            reader = new BinaryReader(new MemoryStream(data, 0, length));
            if (reader.ReadUInt16() != Magic || reader.ReadByte() != Version)
                return false;
            type = (NetMessage)reader.ReadByte();
            return true;
        }

        public static string ReadHello(BinaryReader r) => r.ReadString();

        public static void ReadWelcome(BinaryReader r, out byte slot, out int roomCode, out byte mode)
        {
            slot = r.ReadByte();
            roomCode = r.ReadInt32();
            mode = r.ReadByte();
        }

        public static void ReadStart(BinaryReader r, out int seed, out byte hostCharacter)
        {
            seed = r.ReadInt32();
            hostCharacter = r.ReadByte();
        }

        public static PlayerSnapshot ReadPlayer(BinaryReader r)
        {
            var s = new PlayerSnapshot
            {
                Slot = r.ReadByte(),
                Head = ReadVector(r),
                HeadRotation = ReadQuaternion(r),
                LeftPalm = ReadVector(r),
                RightPalm = ReadVector(r),
            };
            byte flags = r.ReadByte();
            s.LeftTracked = (flags & 1) != 0;
            s.RightTracked = (flags & 2) != 0;
            s.ShieldUp = (flags & 4) != 0;
            s.Dead = (flags & 8) != 0;
            s.Invincible = (flags & 16) != 0;
            s.Boosted = (flags & 32) != 0;
            s.Hp = r.ReadSingle();
            s.MaxHp = r.ReadSingle();
            s.Level = r.ReadByte();
            s.Color = ReadColor(r);
            s.Character = r.ReadByte();
            s.Lives = r.ReadByte();
            s.LeftBeam = r.ReadByte();
            if (s.LeftBeam != 0)
            {
                s.LeftBeamFrom = ReadVector(r);
                s.LeftBeamTo = ReadVector(r);
            }
            s.RightBeam = r.ReadByte();
            if (s.RightBeam != 0)
            {
                s.RightBeamFrom = ReadVector(r);
                s.RightBeamTo = ReadVector(r);
            }
            return s;
        }

        public static LobbyState ReadLobby(BinaryReader r) => new LobbyState
        {
            Slot = r.ReadByte(),
            Character = r.ReadByte(),
            Color = ReadColor(r),
            Ready = r.ReadBoolean(),
        };

        public static ShotEvent ReadShot(BinaryReader r) => new ShotEvent
        {
            Slot = r.ReadByte(),
            Origin = ReadVector(r),
            Direction = ReadVector(r),
            Size = r.ReadSingle(),
            Speed = r.ReadSingle(),
            Color = ReadColor(r),
            Look = (ShotLook)r.ReadByte(),
            Style = r.ReadByte(),
        };

        static byte[] Write(NetMessage type, Action<BinaryWriter> body)
        {
            using var stream = new MemoryStream(64);
            using var writer = new BinaryWriter(stream);
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write((byte)type);
            body(writer);
            writer.Flush();
            return stream.ToArray();
        }

        static void WriteVector(BinaryWriter w, Vector3 v)
        {
            w.Write(v.x);
            w.Write(v.y);
            w.Write(v.z);
        }

        static Vector3 ReadVector(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        static void WriteQuaternion(BinaryWriter w, Quaternion q)
        {
            w.Write(q.x);
            w.Write(q.y);
            w.Write(q.z);
            w.Write(q.w);
        }

        static Quaternion ReadQuaternion(BinaryReader r) => new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        static void WriteColor(BinaryWriter w, Color32 c)
        {
            w.Write(c.r);
            w.Write(c.g);
            w.Write(c.b);
        }

        static Color32 ReadColor(BinaryReader r) => new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), 255);

        /// <summary>
        /// Room codes carry the host's address: the last part of its IPv4 address (0-255) and a check digit,
        /// so a joiner on the same home network (same first three parts) can find it without broadcasts.
        /// </summary>
        public static int RoomCode(byte lastOctet) => lastOctet * 10 + CheckDigit(lastOctet);

        /// <summary>
        /// A host's address typed on the join keypad: four numbers 0-255 with dots between (11/15: networks bigger
        /// than one "x.y.z.*" block, where the room code's guess at the first three numbers is wrong).
        /// </summary>
        public static bool TryParseAddress(string typed, out System.Net.IPAddress address)
        {
            address = null;
            if (string.IsNullOrEmpty(typed))
                return false;
            string[] parts = typed.Split('.');
            if (parts.Length != 4)
                return false;
            var bytes = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                if (parts[i].Length == 0 || parts[i].Length > 3 || !int.TryParse(parts[i], out int value) || value > 255)
                    return false;
                bytes[i] = (byte)value;
            }
            address = new System.Net.IPAddress(bytes);
            return true;
        }

        public static bool TryParseRoomCode(int code, out byte lastOctet)
        {
            lastOctet = 0;
            if (code < 0 || code > 2559)
                return false;
            int octet = code / 10;
            if (octet > 255 || code % 10 != CheckDigit((byte)octet))
                return false;
            lastOctet = (byte)octet;
            return true;
        }

        static int CheckDigit(byte octet) => (octet * 7 + 3) % 10;
    }
}
