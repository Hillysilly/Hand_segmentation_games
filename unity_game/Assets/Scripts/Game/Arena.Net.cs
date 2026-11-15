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
        bool applyingRemoteHit;
        ushort nextEnemyId;
        float enemySendTimer;
        readonly System.Collections.Generic.Dictionary<ushort, Enemy> proxies = new System.Collections.Generic.Dictionary<ushort, Enemy>();
        readonly System.Collections.Generic.Dictionary<ushort, float> killedAt = new System.Collections.Generic.Dictionary<ushort, float>();
        readonly System.Collections.Generic.List<EnemyNet> enemyStates = new System.Collections.Generic.List<EnemyNet>();
        readonly System.Collections.Generic.List<ushort> gone = new System.Collections.Generic.List<ushort>();

        /// <summary>The other player has no lives left (or there is no other player).</summary>
        bool RemoteOut => !Multiplayer || !net.Connected || !remote.Visible || (remote.Latest.Dead && remote.Latest.Lives == 0);

        /// <summary>Whichever player's head is nearest, for enemies to go after.</summary>
        /// <summary>The nearest player's chest, for aiming at bodies rather than heads.</summary>
        public Vector3 NearestPlayerChest(Vector3 from) => NearestPlayerHead(from) + Vector3.down * 0.3f;

        bool pauseReady;
        bool sawRemotePause;
        float sinceResume = 99f;
        Text pauseStatus;

        /// <summary>For tests: paused, say ready on the pause screen, and the character select readiness.</summary>
        public bool TestPaused => paused;
        public void TestPauseReady(bool ready) => pauseReady = ready;
        public bool TestReady => localReady || remoteReady;

        /// <summary>A match with another player in it (pausing then pauses both).</summary>
        bool MultiplayerMatch => Multiplayer && net.Connected && phase == Phase.Playing;

        /// <summary>
        /// Multiplayer pause (11/15): when one player pauses, both do; the match goes on when both say they're ready
        /// (on the pause screen, or the pause gesture again). Character select from the pause takes both there.
        /// The state rides on the player updates (20 a second), so a lost packet can't leave one headset stuck.
        /// </summary>
        void TickMultiplayerPause(float dt)
        {
            sinceResume += dt;
            if (!MultiplayerMatch)
                return;
            PlayerSnapshot other = remote.Latest;
            bool there = remote.Visible;
            // A fresh pause (not ready yet) from the other player pauses you too. Just after resuming, their last
            // updates can still say paused-and-ready; those don't count.
            if (!paused && there && other.Paused && !other.PauseReady && sinceResume > 0.5f)
            {
                Log("The other player paused");
                Pause();
                return;
            }
            if (!paused)
                return;
            if (there && other.Paused)
                sawRemotePause = true;
            if (there && other.Choosing && sawRemotePause)
            {
                Log("The other player went to character select");
                ShowCharacters();
                return;
            }
            bool theyreReady = !there || other.PauseReady || (sawRemotePause && !other.Paused);
            if (pauseReady && theyreReady)
            {
                Log("Both ready: resuming");
                Resume();
                return;
            }
            if (pauseStatus != null)
                pauseStatus.text = !there ? "The other player isn't connected"
                    : other.PauseReady ? "The other player is ready" : pauseReady ? "Waiting for the other player to be ready" : "Both players must press READY to go on";
        }

        /// <summary>Your own head (for things each headset checks against its own player, like a boss's sweep).</summary>
        public Vector3 LocalHead => head.position;

        /// <summary>A boss began a move (host): the client plays the same move on its copy.</summary>
        public void SendEnemyMove(Enemy enemy, byte move, Vector3 a, Vector3 b, float f, bool aIsDirection)
        {
            if (!Multiplayer || !net.Connected || net.Role != NetRole.Host || enemy == null)
                return;
            net.SendEnemyMove(new EnemyMoveEvent
            {
                Id = enemy.Id,
                Move = move,
                A = aIsDirection ? frame.DirectionToShared(a) : frame.ToShared(a),
                B = frame.ToShared(b),
                F = f,
            });
        }

        void OnEnemyMove(EnemyMoveEvent e)
        {
            if (phase != Phase.Playing || !proxies.TryGetValue(e.Id, out Enemy proxy) || proxy == null || proxy.Mech == null)
                return;
            bool aIsDirection = e.Move == MechBrain.SwingMove;
            proxy.Mech.Replay(e.Move, aIsDirection ? frame.DirectionToWorld(e.A) : frame.ToWorld(e.A), frame.ToWorld(e.B), e.F);
        }

        public Vector3 NearestPlayerHead(Vector3 from)
        {
            Vector3 mine = head.position;
            if (!Multiplayer || !remote.Visible || remote.Latest.Dead)
                return mine;
            Vector3 theirs = remote.Latest.Head;
            return (theirs - from).sqrMagnitude < (mine - from).sqrMagnitude ? theirs : mine;
        }

        /// <summary>Client: a hit on a copied enemy goes to the host, which owns its HP.</summary>
        public void ReportEnemyHit(ushort id, float damage)
        {
            if (Multiplayer && net.Role == NetRole.Client)
                net.SendEnemyHit(id, damage);
        }

        void ForgetProxy(ushort id)
        {
            proxies.Remove(id);
            killedAt[id] = Time.unscaledTime;
        }

        /// <summary>Host: every enemy's id, kind, place and HP, plus the wave.</summary>
        void SendEnemies(float dt)
        {
            enemySendTimer -= dt;
            if (enemySendTimer > 0f)
                return;
            enemySendTimer = 0.1f;
            enemyStates.Clear();
            foreach (IHittable foe in enemies)
            {
                if (foe is Enemy enemy && enemy != null && enemy.Alive)
                {
                    enemyStates.Add(new EnemyNet
                    {
                        Id = enemy.Id,
                        Kind = (byte)enemy.Kind,
                        Position = frame.ToShared(enemy.transform.position),
                        Hp = enemy.Hp,
                        MaxHp = enemy.MaxHp,
                        Spawning = enemy.Spawning,
                    });
                }
            }
            net.SendEnemies(new WaveNet
            {
                Wave = (byte)waves.Wave,
                WaveCount = (byte)waves.WaveCount,
                State = (byte)waves.State,
                Timer = waves.Timer,
                Kills = (ushort)waves.Kills,
            }, enemyStates);
        }

        /// <summary>Client: make the copies match the host's list.</summary>
        void OnEnemies(WaveNet wave, System.Collections.Generic.List<EnemyNet> states)
        {
            if (phase != Phase.Playing || waves == null)
                return;
            WaveState state = (WaveState)wave.State;
            // The match end comes as its own message; don't end on a copied state.
            if (state != WaveState.Won && state != WaveState.Lost)
                waves.Mirror(wave.Wave, state, wave.Timer, wave.Kills);

            gone.Clear();
            foreach (ushort id in proxies.Keys)
                gone.Add(id);
            foreach (EnemyNet e in states)
            {
                gone.Remove(e.Id);
                Vector3 position = frame.ToWorld(e.Position);
                if (proxies.TryGetValue(e.Id, out Enemy proxy) && proxy != null)
                {
                    proxy.ApplyState(position, e.Hp);
                    continue;
                }
                if (killedAt.TryGetValue(e.Id, out float when) && Time.unscaledTime - when < 3f)
                    continue;
                proxy = Enemy.SpawnProxy(e.Id, (EnemyKind)e.Kind, position, e.Hp, e.MaxHp, this, head, foes);
                proxies[e.Id] = proxy;
                enemies.Add(proxy);
                // The boss's health shows across the top of the view instead of over it.
                if (proxy.IsBoss)
                    boss = proxy;
                else
                    OverheadBar.Attach(proxy, head, foes, false);
            }
            // Ones the host no longer has were destroyed there.
            foreach (ushort id in gone)
            {
                if (proxies.TryGetValue(id, out Enemy proxy) && proxy != null)
                {
                    game.Explode(proxy.Center, 0.01f, 0f, new Color(1f, 0.5f, 0.2f));
                    enemies.Remove(proxy);
                    Destroy(proxy.gameObject);
                }
                proxies.Remove(id);
            }
        }

        /// <summary>Host: the client hit one of our enemies.</summary>
        void OnEnemyHit(ushort id, float damage)
        {
            foreach (IHittable foe in enemies)
            {
                if (foe is Enemy enemy && enemy != null && enemy.Id == id && enemy.Alive)
                {
                    applyingRemoteHit = true;
                    game.Strike(enemy, damage, enemy.Center, "partner", false);
                    applyingRemoteHit = false;
                    return;
                }
            }
        }

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
                if (net.Role == NetRole.Client)
                    mode = (GameMode)net.Mode;
                Log($"Network: {net.Status}, mode {mode}");
                if (phase == Phase.Lobby)
                    ShowLobby();
            };
            net.PeerLeft += () =>
            {
                Log($"Network: {net.Status}");
                remoteReady = false;
                hud.Message("PLAYER LEFT", net.Status, 2f);
                // Mid-match: the client can't go on without the host's enemies; in PVP the one left standing wins.
                if (phase == Phase.Playing && (net.Role == NetRole.Client || Pvp))
                    EndMatch(Pvp);
                if (phase == Phase.Lobby)
                    ShowLobby();
            };
            net.PlayerReceived += snapshot =>
            {
                snapshot.Head = frame.ToWorld(snapshot.Head);
                snapshot.HeadRotation = frame.ToWorld(snapshot.HeadRotation);
                snapshot.LeftPalm = frame.ToWorld(snapshot.LeftPalm);
                snapshot.RightPalm = frame.ToWorld(snapshot.RightPalm);
                snapshot.LeftBeamFrom = frame.ToWorld(snapshot.LeftBeamFrom);
                snapshot.LeftBeamTo = frame.ToWorld(snapshot.LeftBeamTo);
                snapshot.RightBeamFrom = frame.ToWorld(snapshot.RightBeamFrom);
                snapshot.RightBeamTo = frame.ToWorld(snapshot.RightBeamTo);
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
            net.EnemiesReceived += OnEnemies;
            net.EnemyHitReceived += OnEnemyHit;
            net.EnemyBoltReceived += (shot, origin, other) =>
            {
                if (phase != Phase.Playing)
                    return;
                var kind = (EnemyShot)shot;
                Vector3 a = frame.ToWorld(origin);
                // Lasers send a direction; missiles a target point; strikes only their own point.
                Vector3 b = ShotSendsDirection(kind) ? frame.DirectionToWorld(other) : frame.ToWorld(other);
                StartShot(kind, a, b, null);
            };
            net.EndReceived += won =>
            {
                if (phase == Phase.Playing)
                    EndMatch(won);
            };
            net.ShotReceived += shot =>
            {
                replayingShot = true;
                Vector3 origin = frame.ToWorld(shot.Origin);
                Blast blast = game.FireBlast(origin, frame.DirectionToWorld(shot.Direction), 1f, shot.Size, shot.Speed,
                    0f, 0f, shot.Color);
                DressReplayedShot(blast, origin, shot.Look, shot.Style, shot.Color);
                replayingShot = false;
            };
            net.EffectReceived += ReplayEffect;
            net.EnemyMoveReceived += OnEnemyMove;
            game.BlastFired += (origin, direction, size, speed, color) =>
            {
                (ShotLook look, byte style) = TakeShotLook();
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
                    Look = look,
                    Style = style,
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
                HandBeam leftBeam = BeamOf(0, out Vector3 leftFrom, out Vector3 leftTo);
                HandBeam rightBeam = BeamOf(1, out Vector3 rightFrom, out Vector3 rightTo);
                net.SendPlayer(new PlayerSnapshot
                {
                    Paused = phase == Phase.Playing && paused,
                    PauseReady = phase == Phase.Playing && paused && pauseReady,
                    Choosing = phase == Phase.Characters,
                    LeftBeam = (byte)leftBeam,
                    LeftBeamFrom = frame.ToShared(leftFrom),
                    LeftBeamTo = frame.ToShared(leftTo),
                    RightBeam = (byte)rightBeam,
                    RightBeamFrom = frame.ToShared(rightFrom),
                    RightBeamTo = frame.ToShared(rightTo),
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
                    Lives = (byte)Mathf.Max(0, player.Lives),
                    Invincible = phase == Phase.Playing && player.InvincibleFor > 0f,
                    Boosted = phase == Phase.Playing && player.BoostFor > 0f,
                }, dt);
                if (net.Role == NetRole.Host && phase == Phase.Playing && waves != null)
                    SendEnemies(dt);

                lobbyTimer -= dt;
                if (phase == Phase.Characters && lobbyTimer <= 0f)
                {
                    lobbyTimer = LobbyResend;
                    SendLobby();
                }
            }
            FlushPvpHits(dt);
            remote.FloorY = FloorY;
            remote.Tick(dt);
            TickRemoteLooks(dt);
            TickMultiplayerPause(dt);
            if (phase == Phase.Playing && Multiplayer && remote.Visible)
            {
                PlayerSnapshot partner = remote.Latest;
                string name = CharacterDef.Get((CharacterId)Mathf.Clamp(partner.Character, 0, CharacterDef.All.Length - 1)).Name;
                hud.Partner(partner.Dead ? $"P2 {name}: OUT" : $"P2 {name}", partner.MaxHp > 0f ? partner.Hp / partner.MaxHp : 0f);
            }
            else
            {
                hud.Partner(null, 0f);
            }
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

        GameMode roomMode = GameMode.PveCoop;

        void ShowCoop() => ShowRooms(GameMode.PveCoop);

        void ShowRooms(GameMode wanted)
        {
            roomMode = wanted;
            phase = Phase.Modes;
            RectTransform s = OpenScreen(new Vector2(380f, 280f), wanted == GameMode.Pvp ? "PVP" : "CO-OP");
            Ui.Label(s, "Both headsets on the same Wi-Fi, in the same room.", new Vector2(0f, 82f), new Vector2(360f, 20f), 13).color = Ui.Muted;
            Ui.Button(s, "CREATE ROOM", new Vector2(0f, 35f), new Vector2(260f, 48f), () =>
            {
                mode = roomMode;
                bool ok = net.Host();
                net.Mode = (byte)mode;
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

        /// <summary>The last host address typed here, offered again next time.</summary>
        const string LastHostKey = "LastHostAddress";

        /// <summary>
        /// The join keypad: the host's IP address (shown on its room screen) or its 4-digit room code. The code only
        /// works when both headsets' addresses share their first three numbers; the address always works on one network.
        /// </summary>
        void ShowJoin()
        {
            phase = Phase.Modes;
            RectTransform s = OpenScreen(new Vector2(330f, 440f), "JOIN A ROOM");
            Ui.Label(s, "Type the host's IP address (on its screen), or its room code.", new Vector2(0f, 168f), new Vector2(310f, 30f), 12,
                TextAnchor.MiddleCenter).color = Ui.Muted;
            Text display = Ui.Label(s, joinCode.Length > 0 ? joinCode : "_ _ _ _", new Vector2(0f, 128f), new Vector2(310f, 40f),
                joinCode.Length > 8 ? 26 : 34, TextAnchor.MiddleCenter, FontStyle.Bold);
            display.color = Ui.Accent;
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", ".", "0", "DEL" };
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                Vector2 at = new Vector2(-90f + (i % 3) * 90f, 78f - (i / 3) * 52f);
                Ui.Button(s, key, at, new Vector2(80f, 46f), () =>
                {
                    if (key == "DEL")
                        joinCode = joinCode.Length > 0 ? joinCode.Substring(0, joinCode.Length - 1) : "";
                    else if (joinCode.Length < 15)
                        joinCode += key;
                    ShowJoin();
                }, key.Length > 1 ? 15 : 24);
            }
            Ui.Button(s, "JOIN", new Vector2(0f, -132f), new Vector2(260f, 46f), TryJoin, 20);
            string last = PlayerPrefs.GetString(LastHostKey, "");
            if (last.Length > 0 && last != joinCode)
                Ui.Button(s, "LAST: " + last, new Vector2(-62f, -186f), new Vector2(190f, 36f), () =>
                {
                    joinCode = last;
                    ShowJoin();
                }, 13);
            Ui.Button(s, "BACK", new Vector2(last.Length > 0 && last != joinCode ? 100f : 0f, -186f), new Vector2(110f, 36f), () => ShowRooms(roomMode), 15);
        }

        void TryJoin()
        {
            bool joined;
            if (joinCode.Contains("."))
            {
                if (!NetProtocol.TryParseAddress(joinCode, out System.Net.IPAddress host))
                {
                    hud.Message("CAN'T JOIN", "That address isn't right (four numbers with dots)", 2.5f);
                    ShowJoin();
                    return;
                }
                joined = net.Join(-1, NetProtocol.Port, host);
                if (joined)
                    PlayerPrefs.SetString(LastHostKey, joinCode);
            }
            else
                joined = int.TryParse(joinCode, out int code) && net.Join(code);
            if (!joined)
                hud.Message("CAN'T JOIN", net.Status ?? "That code isn't right", 2.5f);
            if (!joined)
            {
                ShowJoin();
                return;
            }
            mode = roomMode;
            phase = Phase.Lobby;
            Log($"Joining {joinCode}: {net.Status}");
            ShowLobby();
        }

        void ShowLobby()
        {
            phase = Phase.Lobby;
            RectTransform s = OpenScreen(new Vector2(400f, 320f), net.Role == NetRole.Host ? "YOUR ROOM" : "JOINING");
            if (net.Role == NetRole.Host)
            {
                // The address is what always works; the code is a shortcut on small (home) networks.
                System.Net.IPAddress local = NetSession.LocalAddress();
                Ui.Label(s, "YOUR IP ADDRESS", new Vector2(0f, 112f), new Vector2(360f, 18f), 13).color = Ui.Muted;
                Ui.Label(s, local != null ? local.ToString() : "no Wi-Fi", new Vector2(0f, 84f), new Vector2(380f, 40f), 34,
                    TextAnchor.MiddleCenter, FontStyle.Bold).color = Ui.Accent;
                Ui.Label(s, "or room code  " + (net.RoomCode >= 0 ? net.RoomCode.ToString("0000") : "----"), new Vector2(0f, 52f),
                    new Vector2(360f, 20f), 15, TextAnchor.MiddleCenter).color = Ui.Muted;
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
                Ui.Label(s, net.Role == NetRole.Host ? "Waiting for player 2 to type your IP address..." : "Waiting for the host...",
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
