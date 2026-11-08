using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// Debug: compare looks in the headset. Shows a shield bubble 1.5 m in front of you in each style (A-E),
    /// in the current character's colour, and saves the pick for that character. Picks are logged as
    /// [Variant] lines so they can be copied into game_schedule.md.
    /// </summary>
    public sealed partial class Arena
    {
        static readonly string[] ShieldNames = { "A  HEX", "B  FLAME", "C  GLASS", "D  THIN", "E  WEB" };
        const string ShieldPickKey = "variant.shield.";

        const string DragonPickKey = "variant.dragon";

        Bubble labBubble;
        int labStyle = -1;
        int labItem;

        int PickedDragon => PlayerPrefs.GetInt(DragonPickKey, 0);
        const string PillarPickKey = "variant.pillar";
        int PickedPillar => PlayerPrefs.GetInt(PillarPickKey, 0);
        int pillarTried = -1;

        void ShowPillarLab()
        {
            Time.timeScale = 1f;
            RectTransform s = OpenScreen(new Vector2(480f, 320f), "VARIANT LAB");
            LabTabs(s);
            Ui.Label(s, "The Mage's fire pillar. Poke a style to raise one 2 m ahead of you.",
                new Vector2(0f, 72f), new Vector2(450f, 20f), 12).color = Ui.Muted;
            for (int i = 0; i < FirePillar.StyleNames.Length; i++)
            {
                int style = i;
                PokeButton b = Ui.Button(s, FirePillar.StyleNames[i], new Vector2(-130f + i * 130f, 25f), new Vector2(120f, 44f), () =>
                {
                    Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                    Vector3 point = head.position + forward * 2f;
                    point.y = game.Origin.transform.position.y;
                    FirePillar.Spawn(point, PlayerColor, style, transform);
                    game.PlayAt(point, game.BoomSound);
                    pillarTried = style;
                    ShowVariantLab();
                }, 13);
                b.Selected = i == (pillarTried >= 0 ? pillarTried : PickedPillar);
            }
            Ui.Button(s, "SAVE PICK", new Vector2(-90f, -55f), new Vector2(150f, 44f), () =>
            {
                if (pillarTried < 0)
                    return;
                PlayerPrefs.SetInt(PillarPickKey, pillarTried);
                PlayerPrefs.Save();
                Debug.Log($"[Variant] Fire pillar: {FirePillar.StyleNames[pillarTried]}");
                hud.Message("SAVED", $"Fire pillar {FirePillar.StyleNames[pillarTried]}", 1.5f);
            }, 16);
            Ui.Button(s, "BACK", new Vector2(90f, -55f), new Vector2(150f, 44f), () => ShowDebug(debugBack), 16);
        }

        static readonly string[] LabItems = { "SHIELDS", "DRAGON FIST", "ENERGY BALL", "FIRE PILLAR", "HUD", "COMET" };
        const string CometPickKey = "variant.comet";
        int PickedComet => PlayerPrefs.GetInt(CometPickKey, 0);
        int cometTried = -1;

        void ShowCometLab()
        {
            Time.timeScale = 1f;
            RectTransform s = OpenScreen(new Vector2(480f, 320f), "VARIANT LAB");
            LabTabs(s);
            Ui.Label(s, "The Ki fighter's fist-and-release blast. Poke a style to fire one ahead of you.",
                new Vector2(0f, 72f), new Vector2(450f, 20f), 12).color = Ui.Muted;
            for (int i = 0; i < RepulsorGame.CometStyleNames.Length; i++)
            {
                int style = i;
                PokeButton b = Ui.Button(s, RepulsorGame.CometStyleNames[i], new Vector2(-130f + i * 130f, 25f), new Vector2(120f, 44f), () =>
                {
                    Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                    game.SetWeaponColor(PlayerColor);
                    int before = game.CometStyle;
                    game.CometStyle = style;
                    game.Fire(head.position + Vector3.down * 0.3f + forward * 0.5f + head.right * 0.3f, forward, 1f);
                    game.CometStyle = before;
                    cometTried = style;
                    ShowVariantLab();
                }, 13);
                b.Selected = i == (cometTried >= 0 ? cometTried : PickedComet);
            }
            Ui.Button(s, "SAVE PICK", new Vector2(-90f, -55f), new Vector2(150f, 44f), () =>
            {
                if (cometTried < 0)
                    return;
                PlayerPrefs.SetInt(CometPickKey, cometTried);
                PlayerPrefs.Save();
                Debug.Log($"[Variant] Comet: {RepulsorGame.CometStyleNames[cometTried]}");
                hud.Message("SAVED", $"Comet {RepulsorGame.CometStyleNames[cometTried]}", 1.5f);
            }, 16);
            Ui.Button(s, "BACK", new Vector2(90f, -55f), new Vector2(150f, 44f), () => ShowDebug(debugBack), 16);
        }

        /// <summary>The lab's item switcher: previous, the item's name, next.</summary>
        void LabTabs(RectTransform s)
        {
            Ui.Button(s, "<", new Vector2(-170f, 105f), new Vector2(60f, 32f), () =>
            {
                labItem = (labItem + LabItems.Length - 1) % LabItems.Length;
                HideLabBubble();
                ShowVariantLab();
            }, 18);
            Ui.Label(s, $"{LabItems[labItem]}  ({labItem + 1}/{LabItems.Length})", new Vector2(0f, 105f), new Vector2(260f, 30f), 16,
                TextAnchor.MiddleCenter, FontStyle.Bold).color = Ui.Ink;
            Ui.Button(s, ">", new Vector2(170f, 105f), new Vector2(60f, 32f), () =>
            {
                labItem = (labItem + 1) % LabItems.Length;
                HideLabBubble();
                ShowVariantLab();
            }, 18);
        }

        void ShowHudLab()
        {
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

        void ShowDragonLab()
        {
            // Shots need time running, even if the game was paused to get here.
            Time.timeScale = 1f;
            RectTransform s = OpenScreen(new Vector2(480f, 320f), "VARIANT LAB");
            LabTabs(s);
            Ui.Label(s, "Poke a style to fire it ahead of you. Ki fighter level 3 punches use the saved one.",
                new Vector2(0f, 72f), new Vector2(450f, 20f), 12).color = Ui.Muted;
            int picked = PickedDragon;
            for (int i = 0; i < DragonFist.StyleNames.Length; i++)
            {
                int style = i;
                PokeButton b = Ui.Button(s, DragonFist.StyleNames[i], new Vector2(-165f + i * 110f, 25f), new Vector2(100f, 44f), () =>
                {
                    Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                    Vector3 from = head.position + Vector3.down * 0.3f + forward * 0.5f + head.right * 0.35f;
                    game.SetWeaponColor(PlayerColor);
                    DragonFist.Launch(game, style, from, forward, 0f);
                    dragonTried = style;
                    ShowVariantLab();
                }, 13);
                b.Selected = i == (dragonTried >= 0 ? dragonTried : picked);
            }
            Ui.Button(s, "SAVE PICK", new Vector2(-90f, -55f), new Vector2(150f, 44f), () =>
            {
                if (dragonTried < 0)
                    return;
                PlayerPrefs.SetInt(DragonPickKey, dragonTried);
                PlayerPrefs.Save();
                Debug.Log($"[Variant] Dragon fist: {DragonFist.StyleNames[dragonTried]}");
                hud.Message("SAVED", $"Dragon fist {DragonFist.StyleNames[dragonTried]}", 1.5f);
            }, 16);
            Ui.Button(s, "BACK", new Vector2(90f, -55f), new Vector2(150f, 44f), () => ShowDebug(debugBack), 16);
        }

        int dragonTried = -1;
        int ballTried = -1;

        void ShowBallLab()
        {
            Time.timeScale = 1f;
            RectTransform s = OpenScreen(new Vector2(480f, 320f), "VARIANT LAB");
            LabTabs(s);
            Ui.Label(s, "The Ki fighter's charged ball. Poke a style to fire one ahead of you.",
                new Vector2(0f, 72f), new Vector2(450f, 20f), 12).color = Ui.Muted;
            for (int i = 0; i < BallStyleNames.Length; i++)
            {
                int style = i;
                PokeButton b = Ui.Button(s, BallStyleNames[i], new Vector2(-130f + i * 130f, 25f), new Vector2(120f, 44f), () =>
                {
                    Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                    Vector3 from = head.position + Vector3.down * 0.3f + forward * 0.6f;
                    game.SetWeaponColor(PlayerColor);
                    Blast shot = game.FireBlast(from, forward, 1f, 3f, 0.6f, 0f, 0f);
                    DecorateBall(shot.Head, style, PlayerColor);
                    ballTried = style;
                    ShowVariantLab();
                }, 13);
                b.Selected = i == (ballTried >= 0 ? ballTried : PickedBall);
            }
            Ui.Button(s, "SAVE PICK", new Vector2(-90f, -55f), new Vector2(150f, 44f), () =>
            {
                if (ballTried < 0)
                    return;
                PlayerPrefs.SetInt(BallPickKey, ballTried);
                PlayerPrefs.Save();
                Debug.Log($"[Variant] Energy ball: {BallStyleNames[ballTried]}");
                hud.Message("SAVED", $"Energy ball {BallStyleNames[ballTried]}", 1.5f);
            }, 16);
            Ui.Button(s, "BACK", new Vector2(90f, -55f), new Vector2(150f, 44f), () => ShowDebug(debugBack), 16);
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

        void ShowVariantLab()
        {
            if (labItem == 1)
            {
                ShowDragonLab();
                return;
            }
            if (labItem == 2)
            {
                ShowBallLab();
                return;
            }
            if (labItem == 3)
            {
                ShowPillarLab();
                return;
            }
            if (labItem == 4)
            {
                ShowHudLab();
                return;
            }
            if (labItem == 5)
            {
                ShowCometLab();
                return;
            }
            RectTransform s = OpenScreen(new Vector2(480f, 320f), "VARIANT LAB");
            LabTabs(s);
            Ui.Label(s, $"Shield for {Character.Name}. Poke a style to see it in front of you, then SAVE.",
                new Vector2(0f, 75f), new Vector2(450f, 20f), 13).color = Ui.Muted;
            BubbleStyle current = labStyle >= 0 ? (BubbleStyle)labStyle : PickedShield(character);
            for (int i = 0; i < ShieldNames.Length; i++)
            {
                int style = i;
                PokeButton b = Ui.Button(s, ShieldNames[i], new Vector2(-180f + i * 90f, 30f), new Vector2(82f, 44f), () =>
                {
                    labStyle = style;
                    ShowLabBubble();
                    ShowVariantLab();
                }, 13);
                b.Selected = (int)current == i;
            }
            Ui.Button(s, "HIT FLASH", new Vector2(-150f, -22f), new Vector2(130f, 40f), () => labFlash = 1f, 14);
            Ui.Button(s, "TRANSFORM WAVE", new Vector2(10f, -22f), new Vector2(170f, 40f), () =>
                TransformWave.Spawn(head.position + Vector3.down * 0.4f, PlayerColor, transform), 14);
            Ui.Button(s, "SAVE PICK", new Vector2(-90f, -80f), new Vector2(150f, 44f), () =>
            {
                if (labStyle < 0)
                    return;
                PlayerPrefs.SetInt(ShieldPickKey + character, labStyle);
                shieldPicks[character] = (BubbleStyle)labStyle;
                PlayerPrefs.Save();
                Debug.Log($"[Variant] Shield for {character}: {ShieldNames[labStyle]}");
                hud.Message("SAVED", $"{Character.Name}: shield {ShieldNames[labStyle]}", 1.5f);
            }, 16);
            Ui.Button(s, "BACK", new Vector2(90f, -80f), new Vector2(150f, 44f), () =>
            {
                HideLabBubble();
                ShowDebug(debugBack);
            }, 16);
            Ui.Label(s, "Your own shield draws faint from inside; this is how others see it.",
                new Vector2(0f, -132f), new Vector2(450f, 20f), 11).color = Ui.Muted;
            if (labStyle < 0)
            {
                labStyle = (int)current;
                ShowLabBubble();
            }
        }

        float labFlash;

        void ShowLabBubble()
        {
            if (labBubble == null)
                labBubble = Bubble.Create("Variant Bubble", transform);
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            labBubble.transform.position = head.position + forward * 1.6f + Vector3.down * 0.35f;
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
            if (labBubble == null)
                return;
            labFlash = Mathf.Max(0f, labFlash - 4f * dt);
            labBubble.Show(PlayerColor, (BubbleStyle)Mathf.Max(0, labStyle), 1f, labFlash);
        }
    }
}
