using UnityEngine;
using UnityEngine.UI;

namespace Repulsor
{
    /// <summary>
    /// Two players on one Wi-Fi network. The host creates a room and shows its code, and the other player
    /// types it in. Both then touch the same two real points to line up their headsets, pick characters and
    /// ready up; the host starts the match.
    ///
    /// So far each side sees the other's health bar, shield, head and palm markers, and the shots they fire.
    /// Enemies run only on the host until enemy sync is built.
    /// </summary>
    public sealed partial class Arena
    {
        const float LobbyResend = 1f;

        NetSession net;
        SharedFrame frame;
        RemotePlayer remote;
        readonly StillPointDetector still = new StillPointDetector();
        LineRenderer calibrationRing;
        Text lobbyStatus;
        Text partnerLine;
        string joinCode = "";
        int calibrationStep = -1;
        Vector3 calibrationFirst;
        bool localReady;
        bool remoteReady;
        LobbyState remoteLobby;
        float lobbyTimer;
        bool replayingShot;

        bool Multiplayer => net != null && net.Role != NetRole.None;

        void CreateNet()
        {
            net = new NetSession();
            frame = new SharedFrame();
            remote = new RemotePlayer(transform, head);
            calibrationRing = CreateLine("Calibration Ring", Resources.Load<Material>("AimLine"), 0.004f, 33);
            calibrationRing.loop = true;

            net.PeerJoined += () =>
            {
                Log($"Network: {net.Status}");
                if (phase == Phase.Lobby)
                    ShowLobby();
            };
            net.PeerLeft += () =>
            {
                Log($"Network: {net.Status}");
                remoteReady = false;
                hud.Message("PLAYER LEFT", net.Status, 2f);
                if (phase == Phase.Lobby)
                    ShowLobby();
            };
            net.PlayerReceived += snapshot =>
            {
                snapshot.Head = frame.ToWorld(snapshot.Head);
                snapshot.HeadRotation = frame.ToWorld(snapshot.HeadRotation);
                snapshot.LeftPalm = frame.ToWorld(snapshot.LeftPalm);
                snapshot.RightPalm = frame.ToWorld(snapshot.RightPalm);
                remote.Receive(snapshot);
            };
            net.LobbyReceived += state =>
            {
                bool changed = state.Ready != remoteLobby.Ready || state.Character != remoteLobby.Character;
                remoteLobby = state;
                remoteReady = state.Ready;
                if (changed && phase == Phase.Characters)
                    ShowCharacters();
                TryStartTogether();
            };
            net.StartReceived += (seed, hostCharacter) =>
            {
                if (net.Role == NetRole.Client && phase != Phase.Playing)
                {
                    Log("Host started the match");
                    StartMatch();
                }
            };
            net.ShotReceived += shot =>
            {
                replayingShot = true;
                game.FireBlast(frame.ToWorld(shot.Origin), frame.DirectionToWorld(shot.Direction), 1f, shot.Size, shot.Speed,
                    0f, 0f, shot.Color);
                replayingShot = false;
            };
            game.BlastFired += (origin, direction, size, speed, color) =>
            {
                if (replayingShot || !Multiplayer || !net.Connected)
                    return;
                net.SendShot(new ShotEvent
                {
                    Slot = net.Slot,
                    Origin = frame.ToShared(origin),
                    Direction = frame.DirectionToShared(direction),
                    Size = size,
                    Speed = speed,
                    Color = color,
                });
            };
        }

        void TickNet(float dt)
        {
            if (net == null)
                return;
            net.Poll(dt);
            if (net.Connected)
            {
                net.SendPlayer(new PlayerSnapshot
                {
                    Slot = net.Slot,
                    Head = frame.ToShared(head.position),
                    HeadRotation = frame.ToShared(head.rotation),
                    LeftPalm = frame.ToShared(input.Left.Palm),
                    RightPalm = frame.ToShared(input.Right.Palm),
                    LeftTracked = input.Left.Tracked,
                    RightTracked = input.Right.Tracked,
                    Hp = phase == Phase.Playing ? player.Hp : player.MaxHp,
                    MaxHp = player.MaxHp,
                    Level = (byte)player.Level,
                    ShieldUp = phase == Phase.Playing && player.Shield.Up,
                    Dead = phase == Phase.Playing && player.IsDead,
                    Color = PlayerColor,
                    Character = (byte)character,
                }, dt);

                lobbyTimer -= dt;
                if (phase == Phase.Characters && lobbyTimer <= 0f)
                {
                    lobbyTimer = LobbyResend;
                    SendLobby();
                }
            }
            remote.Tick(dt);
            if (lobbyStatus != null)
                lobbyStatus.text = net.Status;
            TickCalibration(dt);
        }

        void SendLobby() => net.SendLobby(new LobbyState
        {
            Slot = net.Slot,
            Character = (byte)character,
            Color = PlayerColor,
            Ready = localReady,
        });

        void TryStartTogether()
        {
            if (!Multiplayer || net.Role != NetRole.Host || !localReady || !remoteReady || phase != Phase.Characters)
                return;
            net.SendStart(Random.Range(0, int.MaxValue), (byte)character);
            Log("Both ready: starting");
            StartMatch();
        }

        void LeaveRoom()
        {
            if (net != null)
                net.Close();
            frame.Clear();
            calibrationStep = -1;
            localReady = remoteReady = false;
            mode = GameMode.PveSolo;
            Log("Left the room");
        }

        void ShowCoop()
        {
            phase = Phase.Modes;
            RectTransform s = OpenScreen(new Vector2(380f, 280f), "CO-OP");
            Ui.Label(s, "Both headsets on the same Wi-Fi, in the same room.", new Vector2(0f, 82f), new Vector2(360f, 20f), 13).color = Ui.Muted;
            Ui.Button(s, "CREATE ROOM", new Vector2(0f, 35f), new Vector2(260f, 48f), () =>
            {
                mode = GameMode.PveCoop;
                bool ok = net.Host();
                Log($"Hosting: {net.Status}, code {net.RoomCode}");
                phase = Phase.Lobby;
                if (!ok)
                    hud.Message("NO NETWORK", net.Status, 3f);
                ShowLobby();
            }, 20);
            Ui.Button(s, "JOIN ROOM", new Vector2(0f, -22f), new Vector2(260f, 48f), () =>
            {
                joinCode = "";
                ShowJoin();
            }, 20);
            Ui.Button(s, "BACK", new Vector2(0f, -90f), new Vector2(160f, 40f), ShowModes, 16);
        }

        void ShowJoin()
        {
            phase = Phase.Modes;
            RectTransform s = OpenScreen(new Vector2(320f, 380f), "ENTER ROOM CODE");
            Text display = Ui.Label(s, joinCode.Length > 0 ? joinCode : "_ _ _ _", new Vector2(0f, 120f), new Vector2(280f, 44f), 36,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            display.color = Ui.Accent;
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "DEL", "0", "JOIN" };
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                Vector2 at = new Vector2(-90f + (i % 3) * 90f, 65f - (i / 3) * 52f);
                Ui.Button(s, key, at, new Vector2(80f, 46f), () =>
                {
                    if (key == "DEL")
                        joinCode = joinCode.Length > 0 ? joinCode.Substring(0, joinCode.Length - 1) : "";
                    else if (key == "JOIN")
                    {
                        if (int.TryParse(joinCode, out int code) && net.Join(code))
                        {
                            mode = GameMode.PveCoop;
                            phase = Phase.Lobby;
                            Log($"Joining room {joinCode}: {net.Status}");
                            ShowLobby();
                            return;
                        }
                        hud.Message("CAN'T JOIN", net.Status, 2f);
                    }
                    else if (joinCode.Length < 4)
                        joinCode += key;
                    ShowJoin();
                }, key.Length > 1 ? 15 : 24);
            }
            Ui.Button(s, "BACK", new Vector2(0f, -160f), new Vector2(140f, 36f), ShowCoop, 15);
        }

        void ShowLobby()
        {
            phase = Phase.Lobby;
            RectTransform s = OpenScreen(new Vector2(400f, 320f), net.Role == NetRole.Host ? "YOUR ROOM" : "JOINING");
            if (net.Role == NetRole.Host)
            {
                Ui.Label(s, "ROOM CODE", new Vector2(0f, 92f), new Vector2(360f, 20f), 14).color = Ui.Muted;
                Ui.Label(s, net.RoomCode >= 0 ? net.RoomCode.ToString("0000") : "----", new Vector2(0f, 58f), new Vector2(360f, 56f), 50,
                    TextAnchor.MiddleCenter, FontStyle.Bold).color = Ui.Accent;
            }
            lobbyStatus = Ui.Label(s, net.Status, new Vector2(0f, 18f), new Vector2(360f, 22f), 15);
            if (net.Connected)
            {
                Ui.Label(s, "Line up your headsets: both of you touch the same real point, then a second one.",
                    new Vector2(0f, -18f), new Vector2(360f, 36f), 13).color = Ui.Muted;
                Ui.Button(s, "CALIBRATE", new Vector2(-80f, -68f), new Vector2(150f, 46f), StartCalibration, 17);
                Ui.Button(s, "SKIP", new Vector2(85f, -68f), new Vector2(120f, 46f), () =>
                {
                    frame.Clear();
                    localReady = false;
                    ShowCharacters();
                }, 16);
            }
            else
            {
                Ui.Label(s, net.Role == NetRole.Host ? "Waiting for player 2 to enter the code..." : "Waiting for the host...",
                    new Vector2(0f, -30f), new Vector2(360f, 22f), 14).color = Ui.Muted;
            }
            Ui.Button(s, "LEAVE", new Vector2(0f, -125f), new Vector2(140f, 38f), () =>
            {
                LeaveRoom();
                ShowModes();
            }, 15);
        }

        void StartCalibration()
        {
            calibrationStep = 0;
            still.Reset();
            ShowCalibration();
        }

        void ShowCalibration()
        {
            RectTransform s = OpenScreen(new Vector2(420f, 220f), "LINE UP HEADSETS");
            string text = calibrationStep == 0
                ? "1 of 2: both players touch the SAME real point (a table corner works) with the right index finger, and hold still."
                : "2 of 2: now both touch a SECOND point, at least 30 cm from the first, and hold still.";
            Ui.Label(s, text, new Vector2(0f, 30f), new Vector2(390f, 70f), 15);
            Ui.Button(s, "START OVER", new Vector2(-80f, -60f), new Vector2(150f, 40f), StartCalibration, 15);
            Ui.Button(s, "SKIP", new Vector2(85f, -60f), new Vector2(120f, 40f), () =>
            {
                calibrationStep = -1;
                frame.Clear();
                ShowCharacters();
            }, 15);
        }

        void TickCalibration(float dt)
        {
            calibrationRing.enabled = false;
            if (calibrationStep < 0 || phase != Phase.Lobby)
                return;
            HandPose hand = input.Right;
            Vector3 tip = hand[Joint.IndexTip];
            if (!still.Step(hand.Tracked, tip, dt))
            {
                if (still.Progress > 0.05f)
                {
                    calibrationRing.enabled = true;
                    float radius = 0.03f * (1f - still.Progress) + 0.008f;
                    for (int i = 0; i < 33; i++)
                    {
                        float angle = i / 32f * Mathf.PI * 2f;
                        calibrationRing.SetPosition(i, tip + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
                    }
                }
                return;
            }

            game.PlayAt(tip, game.ClickSound);
            if (calibrationStep == 0)
            {
                calibrationFirst = still.Point;
                calibrationStep = 1;
                Log($"Calibration point 1 at {calibrationFirst}");
                ShowCalibration();
                return;
            }
            if (!frame.Set(calibrationFirst, still.Point))
            {
                hud.Message("TOO CLOSE", "Pick a second point at least 30 cm from the first", 2.5f);
                return;
            }
            Log($"Calibrated: point 2 at {still.Point}, {Vector3.Distance(calibrationFirst, still.Point):0.00} m apart");
            calibrationStep = -1;
            hud.Message("LINED UP", "The other player's markers should sit on their head and hands", 2.5f);
            localReady = false;
            ShowCharacters();
        }
    }
}
