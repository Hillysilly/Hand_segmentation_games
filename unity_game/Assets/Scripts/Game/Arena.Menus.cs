using UnityEngine;
using UnityEngine.UI;

namespace Repulsor
{
    /// <summary>The menus: panels at arm's length that you poke. Each screen is rebuilt when it opens.</summary>
    public sealed partial class Arena
    {
        public const string GameTitle = "POWER UP";
        const float ReplaceAfterTurn = 50f;
        const float LostMenuDegrees = 75f;
        const float LostMenuSeconds = 1.5f;
        const float OutOfReachSeconds = 0.5f;
        float menuAwayFor;
        float menuOutOfReachFor;

        static readonly string[] Swatches =
        {
            "#4FC3FF", "#3F6BFF", "#9B5CFF", "#FF4FD8", "#E02030", "#FF6A20", "#FFD23F", "#4CFF7A",
        };

        RectTransform menu;
        RectTransform screen;
        bool menuPlaced;
        System.Action debugBack;

        void CreateMenu()
        {
            menu = Ui.Canvas("Menu", transform, new Vector2(400f, 300f));
        }

        void TickMenu(float dt)
        {
            if (!menu.gameObject.activeSelf)
                return;
            bool headTracked = head.localPosition != Vector3.zero;
            if (!menuPlaced && headTracked)
                PlaceMenu();

            // Turned well away from the menu for a moment: bring it back in front.
            Vector3 toMenu = Vector3.ProjectOnPlane(menu.position - head.position, Vector3.up);
            Vector3 facing = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            menuAwayFor = Vector3.Angle(toMenu, facing) > LostMenuDegrees ? menuAwayFor + dt : 0f;
            // Too far to reach (you walked off, or the headset re-found its position after the menu was placed).
            menuOutOfReachFor = MenuOutOfReach(0.35f) ? menuOutOfReachFor + dt : 0f;
            if (menuAwayFor > LostMenuSeconds || menuOutOfReachFor > OutOfReachSeconds)
            {
                menuAwayFor = 0f;
                menuOutOfReachFor = 0f;
                PlaceMenu();
            }
            if (PokeInput.Tick(input.Left, input.Right, dt, out Vector3 clickedAt))
                game.PlayAt(clickedAt, game.ClickSound);
        }

        /// <summary>Puts the menu in front of you, a little low, within reach of your finger.</summary>
        void PlaceMenu()
        {
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.forward;
            forward.Normalize();
            menu.position = head.position + forward * MenuLayout.Distance + Vector3.down * MenuLayout.Drop;
            menu.rotation = Quaternion.LookRotation(menu.position - head.position, Vector3.up);
            menuPlaced = head.localPosition != Vector3.zero;
            Debug.Log($"[Menu] Placed {MenuLayout.Distance:0.00} m ahead, head at {head.position}");
        }

        /// <summary>
        /// True when the menu is further away than where it's normally placed by more than <paramref name="slack"/>
        /// metres, sideways or in height. Being closer never counts: you lean in to poke.
        /// </summary>
        bool MenuOutOfReach(float slack)
        {
            Vector3 offset = menu.position - head.position;
            float across = Vector3.ProjectOnPlane(offset, Vector3.up).magnitude;
            return across > MenuLayout.Distance + slack || Mathf.Abs(offset.y + MenuLayout.Drop) > slack + 0.1f;
        }

        RectTransform OpenScreen(Vector2 size, string title)
        {
            menu.gameObject.SetActive(true);
            // Re-place if you've turned away since the last screen.
            Vector3 toMenu = Vector3.ProjectOnPlane(menu.position - head.position, Vector3.up);
            Vector3 facing = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (!menuPlaced || Vector3.Angle(toMenu, facing) > ReplaceAfterTurn || MenuOutOfReach(0.15f))
                PlaceMenu();

            Ui.Clear(menu);
            menu.sizeDelta = size;
            screen = Ui.Rect("Screen", menu, Vector2.zero, size);
            Ui.Box(screen, Vector2.zero, size + new Vector2(6f, 6f), Ui.PanelEdge);
            Ui.Box(screen, Vector2.zero, size, Ui.Panel);
            if (!string.IsNullOrEmpty(title))
                Ui.Label(screen, title, new Vector2(0f, size.y * 0.5f - 24f), new Vector2(size.x - 20f, 34f), 24,
                    TextAnchor.MiddleCenter, FontStyle.Bold).color = Ui.Accent;
            return screen;
        }

        void HideMenu()
        {
            HideLabBubble();
            Ui.Clear(menu);
            menu.gameObject.SetActive(false);
        }

        void ShowTitle()
        {
            if (Multiplayer)
                LeaveRoom();
            phase = Phase.Title;
            hud.Visible = false;
            RectTransform s = OpenScreen(new Vector2(360f, 230f), null);
            Ui.Label(s, GameTitle, new Vector2(0f, 62f), new Vector2(340f, 60f), 46, TextAnchor.MiddleCenter, FontStyle.Bold).color = Ui.Accent;
            Ui.Label(s, "Superpowers in your own room", new Vector2(0f, 22f), new Vector2(340f, 24f), 16).color = Ui.Muted;
            Ui.Button(s, "START", new Vector2(0f, -35f), new Vector2(170f, 52f), ShowModes, 22);
            Ui.Label(s, "Poke buttons with your index finger. Put the controllers down.", new Vector2(0f, -90f),
                new Vector2(340f, 20f), 11).color = Ui.Muted;
            Text roomStatus = Ui.Label(s, RoomStatus(), new Vector2(0f, -106f), new Vector2(340f, 16f), 10);
            roomStatus.color = room != null && room.Count > 0 ? Ui.Muted : Ui.Warning;
        }

        void ShowModes()
        {
            phase = Phase.Modes;
            RectTransform s = OpenScreen(new Vector2(400f, 320f), "CHOOSE A MODE");
            Ui.Button(s, "SOLO  vs  ROBOTS", new Vector2(0f, 85f), new Vector2(300f, 48f), () =>
            {
                mode = GameMode.PveSolo;
                ShowCharacters();
            }, 20);
            Ui.Button(s, "CO-OP  (2 players)", new Vector2(0f, 30f), new Vector2(300f, 48f), ShowCoop, 20);
            Ui.Button(s, "PVP  (2 players)", new Vector2(0f, -25f), new Vector2(300f, 48f), () => ShowRooms(GameMode.Pvp), 20);
            Ui.Label(s, "Two-player modes are early tests: both headsets on the same Wi-Fi.", new Vector2(0f, -62f),
                new Vector2(360f, 20f), 12).color = Ui.Muted;
            Ui.Button(s, "DEBUG", new Vector2(-80f, -115f), new Vector2(140f, 40f), () => ShowDebug(ShowModes), 16);
            Ui.Button(s, "BACK", new Vector2(80f, -115f), new Vector2(140f, 40f), ShowTitle, 16);
        }

        void ShowCharacters()
        {
            phase = Phase.Characters;
            CleanUpMatch();
            CharacterDef current = Character;
            RectTransform s = OpenScreen(new Vector2(580f, 360f), "CHOOSE YOUR FIGHTER");

            for (int i = 0; i < CharacterDef.All.Length; i++)
            {
                CharacterDef def = CharacterDef.All[i];
                PokeButton b = Ui.Button(s, def.Name.ToUpperInvariant() + (def.Ready ? "" : " *"),
                    new Vector2(-200f, 110f - i * 42f), new Vector2(150f, 36f), () =>
                    {
                        character = def.Id;
                        customColor = false;
                        ShowCharacters();
                    }, 15);
                b.Selected = def.Id == character;
            }

            Ui.Label(s, current.Name.ToUpperInvariant(), new Vector2(80f, 118f), new Vector2(380f, 30f), 24,
                TextAnchor.MiddleLeft, FontStyle.Bold).color = PlayerColor;
            Ui.Label(s, current.Tagline + "    Shield: " + current.Shield, new Vector2(80f, 94f), new Vector2(380f, 20f), 13,
                TextAnchor.MiddleLeft).color = Ui.Muted;
            Text controls = Ui.Label(s, current.ControlsUpTo(3), new Vector2(80f, 30f), new Vector2(380f, 100f), 13,
                TextAnchor.UpperLeft);
            controls.lineSpacing = 1.1f;
            Ui.Label(s, "Everyone: cross wrists in an X for a shield. Fill the meter, then clench both fists at your sides to power up. "
                + "Both hands OK sign for 2 s to pause.", new Vector2(80f, -45f), new Vector2(380f, 34f), 11,
                TextAnchor.UpperLeft).color = Ui.Muted;
            if (!current.Ready)
                Ui.Label(s, "* Work in progress: some of this character's attacks aren't built yet.",
                    new Vector2(80f, -78f), new Vector2(380f, 18f), 11, TextAnchor.MiddleLeft).color = Ui.Warning;

            for (int i = 0; i < Swatches.Length; i++)
            {
                string html = Swatches[i];
                Color swatch = Hex(html);
                PokeButton b = Ui.Button(s, "", new Vector2(-10f + i * 38f, -108f), new Vector2(30f, 30f), () =>
                {
                    chosenColor = swatch;
                    customColor = true;
                    ShowCharacters();
                });
                Ui.Box((RectTransform)b.Label.transform.parent, Vector2.zero, new Vector2(22f, 22f), swatch);
                b.Selected = customColor && chosenColor == swatch;
            }
            Ui.Label(s, "COLOUR", new Vector2(-75f, -108f), new Vector2(100f, 20f), 13, TextAnchor.MiddleRight).color = Ui.Muted;

            Ui.Button(s, "BACK", new Vector2(-215f, -160f), new Vector2(110f, 34f), Multiplayer ? ShowLobby : (System.Action)ShowModes, 14);
            if (!Multiplayer)
            {
                Ui.Button(s, "READY", new Vector2(215f, -152f), new Vector2(130f, 44f), StartMatch, 22);
                return;
            }
            PokeButton ready = Ui.Button(s, localReady ? "WAITING" : "READY", new Vector2(215f, -152f), new Vector2(130f, 44f), () =>
            {
                localReady = !localReady;
                SendLobby();
                TryStartTogether();
                if (phase == Phase.Characters)
                    ShowCharacters();
            }, 20);
            ready.Selected = localReady;
            string partner = !net.Connected ? "Player 2: not connected"
                : $"Player 2: {CharacterDef.Get((CharacterId)Mathf.Clamp(remoteLobby.Character, 0, CharacterDef.All.Length - 1)).Name}"
                  + (remoteReady ? "  <color=#80FF80>READY</color>" : "  choosing...");
            Ui.Label(s, partner + (frame.Calibrated ? "" : "   (not lined up)"), new Vector2(60f, -150f), new Vector2(200f, 40f), 12,
                TextAnchor.MiddleLeft);
        }

        void ShowPause()
        {
            if (paused && (mode == GameMode.PveSolo || phase == Phase.Sandbox))
                Time.timeScale = 0f;
            RectTransform s = OpenScreen(new Vector2(480f, 330f), "PAUSED");
            bool sandbox = phase == Phase.Sandbox;
            Ui.Button(s, "RESUME", new Vector2(-115f, 95f), new Vector2(210f, 44f), Resume, 18);
            if (sandbox)
            {
                Ui.Button(s, "LEAVE OLD CONTROLS", new Vector2(-115f, 43f), new Vector2(210f, 44f), () =>
                {
                    LeaveSandbox();
                    ShowTitle();
                }, 15);
            }
            else
            {
                Ui.Button(s, "CHARACTER SELECT", new Vector2(-115f, 43f), new Vector2(210f, 44f), () =>
                {
                    Log("Left the match for character select");
                    ShowCharacters();
                }, 15);
            }
            Ui.Button(s, "QUIT TO TITLE", new Vector2(-115f, -9f), new Vector2(210f, 44f), () =>
            {
                if (sandbox)
                    LeaveSandbox();
                CleanUpMatch();
                ShowTitle();
            }, 15);
            Ui.Button(s, "DEBUG", new Vector2(-115f, -61f), new Vector2(210f, 44f), () => ShowDebug(ShowPause), 15);

            Text volume = Ui.Label(s, "", new Vector2(115f, 108f), new Vector2(210f, 22f), 15);
            System.Action showVolume = () => volume.text = $"VOLUME {Mathf.RoundToInt(AudioListener.volume * 100f)}%";
            showVolume();
            Ui.Button(s, "-", new Vector2(65f, 78f), new Vector2(70f, 36f), () =>
            {
                AudioListener.volume = Mathf.Max(0f, AudioListener.volume - 0.1f);
                showVolume();
            }, 22);
            Ui.Button(s, "+", new Vector2(165f, 78f), new Vector2(70f, 36f), () =>
            {
                AudioListener.volume = Mathf.Min(1f, AudioListener.volume + 0.1f);
                showVolume();
            }, 22);

            Toggle(s, "ENEMIES", new Vector2(115f, 30f), () => settings.EnemiesEnabled, v => settings.EnemiesEnabled = v);
            PokeButton pvp = Toggle(s, "PVP DAMAGE", new Vector2(115f, -12f), () => settings.PvpEnabled, v => settings.PvpEnabled = v);
            pvp.Interactable = mode != GameMode.PveSolo;
            PokeButton friendly = Toggle(s, "FRIENDLY FIRE", new Vector2(115f, -54f), () => settings.FriendlyFire, v => settings.FriendlyFire = v);
            friendly.Interactable = mode == GameMode.PveCoop;

            Text lives = Ui.Label(s, "", new Vector2(115f, -92f), new Vector2(210f, 20f), 14);
            System.Action showLives = () => lives.text = $"LIVES {settings.Lives}  (next match)";
            showLives();
            Ui.Button(s, "-", new Vector2(65f, -122f), new Vector2(70f, 32f), () =>
            {
                settings.Lives = Mathf.Max(1, settings.Lives - 1);
                showLives();
            }, 20);
            Ui.Button(s, "+", new Vector2(165f, -122f), new Vector2(70f, 32f), () =>
            {
                settings.Lives = Mathf.Min(9, settings.Lives + 1);
                showLives();
            }, 20);
            Ui.Label(s, "Gameplay settings: host only in multiplayer", new Vector2(-115f, -110f), new Vector2(210f, 34f), 11).color = Ui.Muted;
        }

        PokeButton Toggle(RectTransform parent, string name, Vector2 position, System.Func<bool> get, System.Action<bool> set,
            float width = 210f)
        {
            PokeButton button = null;
            button = Ui.Button(parent, "", position, new Vector2(width, 36f), () =>
            {
                set(!get());
                button.Text = $"{name}: {(get() ? "ON" : "OFF")}";
                button.Selected = get();
            }, 14);
            button.Text = $"{name}: {(get() ? "ON" : "OFF")}";
            button.Selected = get();
            return button;
        }

        void ShowResults(bool won)
        {
            RectTransform s = OpenScreen(new Vector2(400f, 300f), null);
            Text title = Ui.Label(s, won ? "VICTORY" : "DEFEATED", new Vector2(0f, 108f), new Vector2(380f, 54f), 44,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            title.color = won ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.4f, 0.35f);
            int minutes = Mathf.FloorToInt(matchTime / 60f);
            string first = Pvp && rounds != null ? $"Rounds  {rounds.Wins(net.Slot)} - {rounds.Wins(1 - net.Slot)}\n" : "";
            if (!Pvp)
            {
                // Score, and the best for this character on this headset.
                int cleared = won ? waves.Wave : Mathf.Max(0, waves.Wave - 1);
                int score = Scoring.Score(waves.Kills, cleared, player.DamageDealt, won, matchTime);
                string key = "best." + character;
                int best = PlayerPrefs.GetInt(key, 0);
                bool record = score > best;
                if (record)
                {
                    PlayerPrefs.SetInt(key, score);
                    PlayerPrefs.Save();
                }
                Ui.Label(s, record ? $"SCORE {score}   <color=#FFE060>NEW BEST!</color>" : $"SCORE {score}   best {best}",
                    new Vector2(0f, 64f), new Vector2(380f, 24f), 18, TextAnchor.MiddleCenter, FontStyle.Bold).color = Ui.Accent;
                Log($"Score {score}{(record ? " (new best)" : "")}");
            }
            Ui.Label(s, first + $"Waves cleared  {(won ? waves.Wave : Mathf.Max(0, waves.Wave - 1))} / {waves.WaveCount}\n"
                + $"Enemies destroyed  {waves.Kills}\n"
                + $"Damage dealt  {player.DamageDealt:0}     taken  {player.DamageTaken:0}\n"
                + $"Time  {minutes}:{matchTime - minutes * 60f:00}", new Vector2(0f, 8f), new Vector2(360f, 90f), 15);
            Ui.Button(s, "PLAY AGAIN", new Vector2(0f, -60f), new Vector2(200f, 46f), () =>
            {
                if (Multiplayer)
                {
                    localReady = false;
                    ShowCharacters();
                }
                else
                {
                    StartMatch();
                }
            }, 18);
            Ui.Button(s, "CHARACTERS", new Vector2(-95f, -112f), new Vector2(170f, 38f), ShowCharacters, 14);
            Ui.Button(s, "TITLE", new Vector2(95f, -112f), new Vector2(170f, 38f), ShowTitle, 14);
        }

        void ShowDebug(System.Action back)
        {
            debugBack = back;
            bool inMatch = phase == Phase.Playing;
            RectTransform s = OpenScreen(new Vector2(500f, 420f), "DEBUG");
            Vector2 size = new Vector2(150f, 38f);
            float[] x = { -160f, 0f, 160f };
            float Row(int r) => 140f - r * 46f;

            Ui.Button(s, "OLD CONTROLS", new Vector2(x[0], Row(0)), size, EnterSandbox, 14);
            Toggle(s, "GESTURES", new Vector2(x[1], Row(0)), () => gestureLab, v => gestureLab = v, size.x);
            Toggle(s, "FPS", new Vector2(x[2], Row(0)), () => hud.ShowFps, v => hud.ShowFps = v, size.x);

            PokeButton[] matchOnly =
            {
                Ui.Button(s, "SPAWN ENEMY", new Vector2(x[0], Row(1)), size, () =>
                {
                    SpawnEnemy(Random.value < 0.5f ? EnemyKind.Robot : EnemyKind.Drone);
                    waves.AddExtra();
                }, 14),
                Ui.Button(s, "HIT ME 10", new Vector2(x[1], Row(1)), size, () => HurtPlayer(10f, "debug"), 14),
                Ui.Button(s, "KNOCK ME OUT", new Vector2(x[2], Row(1)), size, () => HurtPlayer(10000f, "debug"), 14),
                Ui.Button(s, "FILL METER", new Vector2(x[0], Row(2)), size, () => player.FillMeter(), 14),
                Ui.Button(s, "SPAWN POWERUP", new Vector2(x[1], Row(5)), size, () => SpawnPowerup((PowerupKind)Random.Range(0, 3)), 14),
                Ui.Button(s, "SKIP WAVE", new Vector2(x[2], Row(2)), size, () =>
                {
                    waves.SkipWave();
                    foreach (IHittable foe in enemies.ToArray())
                        if (foe is Object alive && alive != null)
                            game.Strike(foe, 1e6f, foe.Center, "debug");
                }, 14),
                Ui.Button(s, "INVINCIBLE 8s", new Vector2(x[0], Row(3)), size, () => player.ApplyPowerup(PowerupKind.Invincible), 14),
                Ui.Button(s, "1.5x DAMAGE", new Vector2(x[1], Row(3)), size, () => player.ApplyPowerup(PowerupKind.DamageBoost), 14),
                Ui.Button(s, "+30% METER", new Vector2(x[2], Row(3)), size, () => player.ApplyPowerup(PowerupKind.Charge), 14),
            };
            foreach (PokeButton b in matchOnly)
                b.Interactable = inMatch;
            Toggle(s, "GOD MODE", new Vector2(x[1], Row(2)), () => player.God, v => player.God = v, size.x);

            Ui.Button(s, "NEXT AURA", new Vector2(x[0], Row(4)), size, () =>
            {
                if (!game.Aura.IsLit)
                    game.Aura.Ignite(head.position + Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized * 1.5f);
                game.Aura.NextStyle();
                Log($"Aura style {game.Aura.StyleNumber}: {game.Aura.StyleName}");
            }, 14);
            Ui.Button(s, "VARIANT LAB", new Vector2(x[1], Row(4)), size, ShowVariantLab, 14);
            Ui.Button(s, "BACK", new Vector2(x[2], Row(4)), size, () => debugBack?.Invoke(), 14);
            Ui.Label(s, inMatch ? "Match tools are live." : "Match tools work during a match (open from the pause menu).",
                new Vector2(0f, -190f), new Vector2(470f, 20f), 11).color = Ui.Muted;
        }
    }
}
