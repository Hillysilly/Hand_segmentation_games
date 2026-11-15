using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// Debug: compare looks in the headset and try them with your own hands. Outside a match, the lab makes you the
    /// character that owns the item, at the level it unlocks, with a practice robot ahead to hit, so the real gesture
    /// can be tried straight away. Poking a style picks and saves it; it's also fired once ahead of you to see.
    /// Picks are logged as [Variant] lines so they can be copied into game_schedule.md.
    /// </summary>
    public sealed partial class Arena
    {
        static readonly string[] ShieldNames = { "A  HEX", "B  FLAME", "C  GLASS", "D  THIN", "E  WEB" };
        const string ShieldPickKey = "variant.shield.";
        const string DragonPickKey = "variant.dragon";
        const string CometPickKey = "variant.comet";
        static readonly string[] LabItems = { "SHIELDS", "DRAGON FIST", "ENERGY BALL", "HUD", "COMET", "ENEMIES" };
        /// <summary>The fire pillar look is settled (C BOTH), so it's no longer in the lab.</summary>
        static int PickedPillar => FirePillar.Default;
        bool HudLabOpen => LabItems[labItem] == "HUD";
        /// <summary>The lab sits this far to your left while you practise, so your shots go past it.</summary>
        const float PracticeMenuYaw = -40f;

        int PickedDragon => PlayerPrefs.GetInt(DragonPickKey, 0);
        int PickedComet => PlayerPrefs.GetInt(CometPickKey, 0);

        Bubble labBubble;
        int labStyle = -1;
        int labItem;
        float labFlash;

        bool practicing;
        CharacterId practiceReturn;
        Enemy practiceDummy;
        float dummyGoneFor;

        void ShowVariantLab()
        {
            switch (LabItems[labItem])
            {
                case "DRAGON FIST":
                    ShowPickLab("DRAGON FIST", DragonFist.StyleNames, PickedDragon, DragonPickKey, CharacterId.KiFighter, 3, style =>
                    {
                        Vector3 forward = Ahead(head.forward);
                        DragonFist.Launch(game, style, head.position + Vector3.down * 0.3f + forward * 0.5f + head.right * 0.35f, forward, 0f);
                    });
                    return;
                case "ENERGY BALL":
                    ShowPickLab("ENERGY BALL", BallStyleNames, PickedBall, BallPickKey, CharacterId.KiFighter, 2, style =>
                    {
                        Vector3 forward = Ahead(head.forward);
                        Blast shot = game.FireBlast(head.position + Vector3.down * 0.3f + forward * 0.6f, forward, 1f, 3f, 0.6f, 0f, 0f);
                        DecorateBall(shot.Head, style, AttackColor);
                    });
                    return;
                case "HUD":
                    ShowHudLab();
                    return;
                case "ENEMIES":
                    ShowEnemyLab();
                    return;
                case "COMET":
                    ShowPickLab("COMET", RepulsorGame.CometStyleNames, PickedComet, CometPickKey, CharacterId.KiFighter, 1, style =>
                    {
                        Vector3 forward = Ahead(head.forward);
                        int before = game.CometStyle;
                        game.CometStyle = style;
                        game.Fire(head.position + Vector3.down * 0.3f + forward * 0.5f + head.right * 0.3f, forward, 1f);
                        game.CometStyle = before;
                    });
                    return;
                default:
                    ShowShieldLab();
                    return;
            }
        }

        static Vector3 Ahead(Vector3 direction)
        {
            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            return flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
        }

        /// <summary>
        /// One lab page: a button per style that picks it, saves it and shows it once; and, outside a match, live
        /// practice as <paramref name="who"/> at <paramref name="level"/> so the real gesture uses the pick.
        /// </summary>
        void ShowPickLab(string item, string[] names, int picked, string key, CharacterId who, int level, System.Action<int> demo)
        {
            Time.timeScale = 1f;
            RectTransform s = OpenScreen(new Vector2(480f, 320f), "VARIANT LAB");
            LabTabs(s);
            bool live = StartPractice(who, level);
            string how = string.Join(".  ", CharacterDef.Get(who).Controls[level - 1]);
            Ui.Label(s, live ? $"Try it now: {how}." : "Poke a style to pick it. Resume the match to try it with your hands.",
                new Vector2(0f, 72f), new Vector2(450f, 34f), 12).color = Ui.Muted;
            float width = names.Length > 3 ? 100f : 120f;
            float step = width + 10f;
            for (int i = 0; i < names.Length; i++)
            {
                int style = i;
                PokeButton b = Ui.Button(s, names[i], new Vector2((i - (names.Length - 1) * 0.5f) * step, 22f), new Vector2(width, 44f), () =>
                {
                    PlayerPrefs.SetInt(key, style);
                    PlayerPrefs.Save();
                    Debug.Log($"[Variant] {item}: {names[style]}");
                    demo(style);
                    ShowVariantLab();
                }, 13);
                b.Selected = i == picked;
            }
            Ui.Label(s, "Your pick is saved as soon as you poke it.", new Vector2(0f, -22f), new Vector2(450f, 18f), 11).color = Ui.Muted;
            Ui.Button(s, "BACK", new Vector2(0f, -70f), new Vector2(150f, 44f), () => ShowDebug(debugBack), 16);
        }

        void ShowShieldLab()
        {
            RectTransform s = OpenScreen(new Vector2(480f, 320f), "VARIANT LAB");
            LabTabs(s);
            bool live = StartPractice(practicing ? practiceReturn : character, 1);
            Ui.Label(s, live ? $"Shield for {Character.Name}. Poke a style, then cross your arms in an X to raise it."
                    : $"Shield for {Character.Name}. Poke a style to pick it; it's saved straight away.",
                new Vector2(0f, 75f), new Vector2(450f, 20f), 13).color = Ui.Muted;
            BubbleStyle current = PickedShield(character);
            for (int i = 0; i < ShieldNames.Length; i++)
            {
                int style = i;
                PokeButton b = Ui.Button(s, ShieldNames[i], new Vector2(-180f + i * 90f, 30f), new Vector2(82f, 44f), () =>
                {
                    PlayerPrefs.SetInt(ShieldPickKey + character, style);
                    PlayerPrefs.Save();
                    shieldPicks[character] = (BubbleStyle)style;
                    Debug.Log($"[Variant] Shield for {character}: {ShieldNames[style]}");
                    labStyle = style;
                    ShowLabBubble();
                    ShowVariantLab();
                }, 13);
                b.Selected = (int)current == i;
            }
            Ui.Button(s, "HIT FLASH", new Vector2(-150f, -22f), new Vector2(130f, 40f), () => labFlash = 1f, 14);
            Ui.Button(s, "TRANSFORM WAVE", new Vector2(10f, -22f), new Vector2(170f, 40f), () =>
                TransformWave.Spawn(head.position + Vector3.down * 0.4f, PlayerColor, transform), 14);
            Ui.Button(s, "BACK", new Vector2(0f, -80f), new Vector2(150f, 44f), () =>
            {
                HideLabBubble();
                ShowDebug(debugBack);
            }, 16);
            Ui.Label(s, "The bubble ahead is how others see it. Your own draws faint from inside.",
                new Vector2(0f, -132f), new Vector2(450f, 20f), 11).color = Ui.Muted;
            if (labStyle < 0)
            {
                labStyle = (int)current;
                ShowLabBubble();
            }
        }

        /// <summary>The lab's item switcher: previous, the item's name, next.</summary>
        void LabTabs(RectTransform s)
        {
            Ui.Button(s, "<", new Vector2(-170f, 105f), new Vector2(60f, 32f), () =>
            {
                labItem = (labItem + LabItems.Length - 1) % LabItems.Length;
                HideLabBubble();
                HideLabEnemies();
                ShowVariantLab();
            }, 18);
            Ui.Label(s, $"{LabItems[labItem]}  ({labItem + 1}/{LabItems.Length})", new Vector2(0f, 105f), new Vector2(260f, 30f), 16,
                TextAnchor.MiddleCenter, FontStyle.Bold).color = Ui.Ink;
            Ui.Button(s, ">", new Vector2(170f, 105f), new Vector2(60f, 32f), () =>
            {
                labItem = (labItem + 1) % LabItems.Length;
                HideLabBubble();
                HideLabEnemies();
                ShowVariantLab();
            }, 18);
        }

        readonly Enemy[] labEnemies = new Enemy[EnemySpec.All.Length];

        /// <summary>Enemy gallery: every kind standing in a row ahead of you, with what each one does.</summary>
        void ShowEnemyLab()
        {
            StopPractice();
            RectTransform s = OpenScreen(new Vector2(580f, 330f), "VARIANT LAB");
            LabTabs(s);
            Ui.Label(s, "Every enemy, left to right. Each kind has its own model now.",
                new Vector2(0f, 72f), new Vector2(540f, 20f), 12).color = Ui.Muted;
            for (int i = 0; i < EnemySpec.All.Length; i++)
            {
                EnemySpec spec = EnemySpec.All[i];
                Ui.Label(s, $"<b>{spec.Name}</b>  {spec.Blurb}", new Vector2(i < 4 ? -140f : 140f, 45f - (i % 4) * 24f),
                    new Vector2(270f, 22f), 10, TextAnchor.MiddleLeft).color = Ui.Ink;
            }
            Ui.Button(s, "BACK", new Vector2(0f, -85f), new Vector2(150f, 44f), () =>
            {
                HideLabEnemies();
                ShowDebug(debugBack);
            }, 16);
            ShowLabEnemies();
        }

        /// <summary>The whole roster standing still in an arc ahead, the boss at the back.</summary>
        void ShowLabEnemies()
        {
            HideLabEnemies();
            Vector3 forward = Ahead(head.forward);
            float distance = 2.6f;
            if (game.Room.TryDistance(head.position + forward * 0.5f, forward, out float wall))
                distance = Mathf.Clamp(wall + 0.1f, 1.6f, 2.6f);
            int count = EnemySpec.All.Length - 1;
            for (int i = 0; i < EnemySpec.All.Length; i++)
            {
                EnemySpec spec = EnemySpec.All[i];
                bool isBoss = spec.Kind == EnemyKind.Boss;
                float angle = isBoss ? 0f : Mathf.Lerp(-55f, 55f, i / (count - 1f));
                Vector3 point = head.position + Quaternion.AngleAxis(angle, Vector3.up) * forward * (isBoss ? distance + 1.8f : distance);
                point.y = spec.Flies ? head.position.y : FloorY + Enemy.MiddleHeight(spec.Kind);
                labEnemies[i] = Enemy.Spawn(spec.Kind, point, this, head, foes);
                labEnemies[i].Practice = true;
            }
        }

        void HideLabEnemies()
        {
            for (int i = 0; i < labEnemies.Length; i++)
            {
                if (labEnemies[i] != null)
                    Destroy(labEnemies[i].gameObject);
                labEnemies[i] = null;
            }
        }

        void ShowHudLab()
        {
            StopPractice();
            RectTransform s = OpenScreen(new Vector2(480f, 320f), "VARIANT LAB");
            LabTabs(s);
            Ui.Label(s, "Where your health and power bars sit. Poke one to switch now; it's saved straight away.",
                new Vector2(0f, 72f), new Vector2(450f, 20f), 12).color = Ui.Muted;
            for (int i = 0; i < PlayerHud.LayoutNames.Length; i++)
            {
                int layout = i;
                PokeButton b = Ui.Button(s, PlayerHud.LayoutNames[i], new Vector2(-145f + i * 145f, 25f), new Vector2(135f, 44f), () =>
                {
                    hud.Layout = layout;
                    PlayerPrefs.SetInt("variant.hud", layout);
                    PlayerPrefs.Save();
                    Debug.Log($"[Variant] HUD: {PlayerHud.LayoutNames[layout]}");
                    ShowVariantLab();
                }, 13);
                b.Selected = hud.Layout == i;
            }
            Ui.Button(s, "BACK", new Vector2(0f, -55f), new Vector2(150f, 44f), () =>
            {
                hud.Visible = phase == Phase.Playing;
                ShowDebug(debugBack);
            }, 16);
            // Show the HUD while choosing, even outside a match.
            hud.Visible = true;
        }

        readonly System.Collections.Generic.Dictionary<CharacterId, BubbleStyle> shieldPicks =
            new System.Collections.Generic.Dictionary<CharacterId, BubbleStyle>();

        /// <summary>The saved shield look for a character, read from storage once and then remembered.</summary>
        BubbleStyle PickedShield(CharacterId id)
        {
            if (shieldPicks.TryGetValue(id, out BubbleStyle style))
                return style;
            int saved = PlayerPrefs.GetInt(ShieldPickKey + id, -1);
            style = saved >= 0 ? (BubbleStyle)saved : ShieldStyle(id);
            shieldPicks[id] = style;
            return style;
        }

        /// <summary>
        /// Outside a match: become <paramref name="who"/> at <paramref name="level"/> with gestures live, the lab moved
        /// to your left and a practice robot ahead. Returns false in a match, where nothing changes.
        /// </summary>
        bool StartPractice(CharacterId who, int level)
        {
            if (phase == Phase.Playing)
                return false;
            if (!practicing)
            {
                practicing = true;
                practiceReturn = character;
                menuYaw = PracticeMenuYaw;
                PlaceMenu();
                Log("Variant Lab practice on");
            }
            if (character != who || player.Level != level)
            {
                character = who;
                player.SetLevel(level);
                Log($"Practising as {Character.Name}, level {level}");
            }
            hud.Visible = false;
            return true;
        }

        void StopPractice()
        {
            if (!practicing)
                return;
            practicing = false;
            character = practiceReturn;
            player.Reset();
            xArms.Reset();
            if (practiceDummy != null)
                Destroy(practiceDummy.gameObject);
            practiceDummy = null;
            menuYaw = 0f;
            PlaceMenu();
            Log("Variant Lab practice off");
        }

        /// <summary>Practice: the X shield, and a robot to hit that comes back a moment after it's knocked down.</summary>
        void TickPractice(float dt)
        {
            if (!practicing)
                return;
            xArms.Step(input.Left, input.Right, body, dt);
            player.Shield.Hold(xArms.Held);
            player.Tick(dt);
            game.DamageScale = 1f;

            if (practiceDummy != null && practiceDummy.Alive)
                return;
            dummyGoneFor += dt;
            if (practiceDummy != null && dummyGoneFor < 1.2f)
                return;
            if (practiceDummy != null)
                Destroy(practiceDummy.gameObject);
            dummyGoneFor = 0f;
            Vector3 forward = Ahead(head.forward);
            float distance = 3f;
            if (game.Room.TryDistance(head.position + forward * 0.5f, forward, out float wall))
                distance = Mathf.Clamp(wall + 0.1f, 1.5f, 3f);
            Vector3 point = head.position + forward * distance;
            point.y = FloorY + Enemy.MiddleHeight(EnemyKind.Dasher);
            practiceDummy = Enemy.Spawn(EnemyKind.Dasher, point, this, head, foes);
            practiceDummy.Practice = true;
            OverheadBar.Attach(practiceDummy, head, foes, false);
        }

        void ShowLabBubble()
        {
            if (labBubble == null)
                labBubble = Bubble.Create("Variant Bubble", transform);
            // In front of any wall, or the room's depth hides it.
            Vector3 forward = Ahead(head.forward);
            float distance = 1.6f;
            if (game.Room.TryDistance(head.position + forward * 0.3f, forward, out float wall))
                distance = Mathf.Clamp(wall + 0.3f - 0.7f, 0.9f, 1.6f);
            labBubble.transform.position = head.position + forward * distance + Vector3.down * 0.35f;
            labBubble.transform.localScale = Vector3.one * 1.24f;
        }

        void HideLabBubble()
        {
            labStyle = -1;
            if (labBubble != null)
                Destroy(labBubble.gameObject);
        }

        void TickVariantLab(float dt)
        {
            TickPractice(dt);
            if (labBubble == null)
                return;
            labFlash = Mathf.Max(0f, labFlash - 4f * dt);
            labBubble.Show(PlayerColor, (BubbleStyle)Mathf.Max(0, labStyle), 1f, labFlash);
        }
    }
}
