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

        Bubble labBubble;
        int labStyle = -1;

        BubbleStyle PickedShield(CharacterId id)
        {
            int saved = PlayerPrefs.GetInt(ShieldPickKey + id, -1);
            return saved >= 0 ? (BubbleStyle)saved : ShieldStyle(id);
        }

        void ShowVariantLab()
        {
            RectTransform s = OpenScreen(new Vector2(480f, 300f), "VARIANT LAB: SHIELDS");
            Ui.Label(s, $"For {Character.Name}. Poke a style to see it in front of you, then SAVE.",
                new Vector2(0f, 100f), new Vector2(450f, 20f), 13).color = Ui.Muted;
            BubbleStyle current = labStyle >= 0 ? (BubbleStyle)labStyle : PickedShield(character);
            for (int i = 0; i < ShieldNames.Length; i++)
            {
                int style = i;
                PokeButton b = Ui.Button(s, ShieldNames[i], new Vector2(-180f + i * 90f, 55f), new Vector2(82f, 44f), () =>
                {
                    labStyle = style;
                    ShowLabBubble();
                    ShowVariantLab();
                }, 13);
                b.Selected = (int)current == i;
            }
            Ui.Button(s, "HIT FLASH", new Vector2(-150f, -5f), new Vector2(130f, 40f), () => labFlash = 1f, 14);
            Ui.Button(s, "TRANSFORM WAVE", new Vector2(10f, -5f), new Vector2(170f, 40f), () =>
                TransformWave.Spawn(head.position + Vector3.down * 0.4f, PlayerColor, transform), 14);
            Ui.Button(s, "SAVE PICK", new Vector2(-90f, -70f), new Vector2(150f, 44f), () =>
            {
                if (labStyle < 0)
                    return;
                PlayerPrefs.SetInt(ShieldPickKey + character, labStyle);
                PlayerPrefs.Save();
                Debug.Log($"[Variant] Shield for {character}: {ShieldNames[labStyle]}");
                hud.Message("SAVED", $"{Character.Name}: shield {ShieldNames[labStyle]}", 1.5f);
            }, 16);
            Ui.Button(s, "BACK", new Vector2(90f, -70f), new Vector2(150f, 44f), () =>
            {
                HideLabBubble();
                ShowDebug(debugBack);
            }, 16);
            Ui.Label(s, "Your own shield draws faint from inside; this is how others see it.",
                new Vector2(0f, -120f), new Vector2(450f, 20f), 11).color = Ui.Muted;
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
