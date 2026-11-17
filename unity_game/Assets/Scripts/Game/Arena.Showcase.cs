using UnityEngine;
using UnityEngine.UI;

namespace Repulsor
{
    /// <summary>
    /// The character select screen's right side: the mannequin acting out the moves (CharacterShowcase), the level
    /// arrows, and the ability list that scrolls (by its arrows, and to the level being shown).
    /// </summary>
    public sealed partial class Arena
    {
        static readonly Vector2 SelectSize = new Vector2(600f, 400f);
        static readonly Vector2 WellCentre = new Vector2(150f, 98f);
        static readonly Vector2 WellSize = new Vector2(276f, 156f);
        /// <summary>The hand sign's box on the stage (right end), and where the hand floats in front of it (mm).</summary>
        /// <summary>The panel's stage area is now the hand sign's box; the mannequin stands life size beside the menu.</summary>
        static readonly Vector2 SignBoxCentre = WellCentre;
        static readonly Vector2 SignBoxSize = WellSize;
        static readonly Vector3 SignHand = new Vector3(150f, 112f, -20f);
        /// <summary>Where the life-size mannequin stands: this far beyond the menu and to its right (metres), on the floor.</summary>
        const float FigureAhead = 1.2f;
        const float FigureRight = 1.1f;
        const float AbilityLine = 15f;
        const float ManualScrollHold = 6f;

        CharacterShowcase showcase;
        RectTransform characterScreen;
        Text showcaseLevel;
        Text signCaption;
        RectTransform abilityContent;
        RectTransform abilityViewport;
        Text[] abilityBlocks;
        float[] abilityTops;
        float abilityHeight;
        float abilityScroll;
        float abilityScrollGoal;
        float lastManualScroll = float.MinValue;
        int shownShowcaseLevel = -1;

        void BuildAbilityList(RectTransform s, CharacterDef def)
        {
            Ui.Box(s, new Vector2(138f, -100f), new Vector2(256f, 92f), Ui.Well);
            abilityViewport = Ui.Rect("Abilities", s, new Vector2(138f, -100f), new Vector2(248f, 86f));
            abilityViewport.gameObject.AddComponent<RectMask2D>();
            abilityContent = Ui.Rect("Content", abilityViewport, Vector2.zero, new Vector2(248f, 10f));
            abilityContent.anchorMin = abilityContent.anchorMax = abilityContent.pivot = new Vector2(0.5f, 1f);
            abilityContent.anchoredPosition = Vector2.zero;

            int blocks = def.Controls.Length + 1;
            abilityBlocks = new Text[blocks];
            abilityTops = new float[blocks];
            float y = 0f;
            for (int i = 0; i < blocks; i++)
            {
                Text text = Ui.Label(abilityContent, "", Vector2.zero, new Vector2(244f, 10f), 11, TextAnchor.UpperLeft);
                text.lineSpacing = 1.05f;
                text.text = i < def.Controls.Length ? AbilityBlock(def, i, false)
                    : "<b>EVERYONE</b>\nCross wrists in an X: shield.\nMeter full: clench both fists at your sides to power up.\n"
                      + "Both hands OK sign for 2 s: pause.";
                var rect = (RectTransform)text.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
                float height = Mathf.Max(AbilityLine, text.preferredHeight) + 6f;
                rect.sizeDelta = new Vector2(244f, height);
                rect.anchoredPosition = new Vector2(0f, -y);
                abilityTops[i] = y;
                abilityBlocks[i] = text;
                y += height;
            }
            abilityHeight = y;
            abilityContent.sizeDelta = new Vector2(248f, y);
            abilityScroll = abilityScrollGoal = 0f;
        }

        /// <summary>
        /// The player level a move arrives at: its ability level in PVP (1, 2, 3), and 1, 3, 5 in PVE, where the levels
        /// between make the moves stronger.
        /// </summary>
        int ShownLevel(int abilityLevel) => Pvp ? abilityLevel : abilityLevel * 2 - 1;

        string AbilityBlock(CharacterDef def, int level, bool current)
        {
            string head = $"LEVEL {ShownLevel(level + 1)}  ·  {CharacterShowcase.MoveName(def.Id, level + 1).ToUpperInvariant()}";
            string colour = current ? "#" + ColorUtility.ToHtmlStringRGB(PlayerColor) : "#9AAABB";
            string body = "• " + string.Join("\n• ", def.Controls[level]);
            return current
                ? $"<b><color={colour}>{head}</color></b>\n{body}"
                : $"<b><color={colour}>{head}</color></b>\n<color=#9AAABB>{body}</color>";
        }

        float MaxAbilityScroll => abilityViewport != null ? Mathf.Max(0f, abilityHeight - abilityViewport.sizeDelta.y) : 0f;

        void ScrollAbilities(int direction)
        {
            lastManualScroll = Time.unscaledTime;
            abilityScrollGoal = Mathf.Clamp(abilityScrollGoal + direction * AbilityLine * 3f, 0f, MaxAbilityScroll);
        }

        void StepShowcase(int direction)
        {
            if (showcase == null)
                return;
            showcase.SetLevel(showcase.Level + direction);
            lastManualScroll = float.MinValue;
        }

        bool CharacterScreenOpen =>
            phase == Phase.Characters && characterScreen != null && characterScreen.gameObject.activeInHierarchy && menu.gameObject.activeSelf;

        /// <summary>Each frame: the mannequin in front of the screen's stage, the level label and the list's scroll.</summary>
        void TickShowcase(float dt)
        {
            if (!CharacterScreenOpen)
            {
                showcase?.Hide();
                return;
            }
            showcase ??= new CharacterShowcase(game, transform);
            showcase.Show(character, PlayerColor);
            // Life size, standing on the floor beyond the menu's right edge and facing off to the right and a little
            // toward you, so you see its moves side-on and its shots fly away from the menu.
            Vector3 away = Vector3.ProjectOnPlane(characterScreen.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(characterScreen.right, Vector3.up).normalized;
            Vector3 feet = characterScreen.position + away * FigureAhead + right * FigureRight;
            feet.y = FloorY;
            Vector3 heading = right * 0.9f - away * 0.35f;
            showcase.Place(feet, Quaternion.LookRotation(heading, Vector3.up));
            showcase.PlaceSign(characterScreen.TransformPoint(SignHand), Quaternion.LookRotation(characterScreen.forward, characterScreen.up));
            showcase.Tick(dt);
            if (signCaption != null)
                signCaption.text = showcase.SignCaption;

            int level = showcase.Level;
            if (level != shownShowcaseLevel)
            {
                shownShowcaseLevel = level;
                CharacterDef def = Character;
                if (showcaseLevel != null)
                {
                    // PVE has five levels (11/16): a character's moves arrive at levels 1, 3 and 5.
                    showcaseLevel.text = $"LV {ShownLevel(level)}  ·  {CharacterShowcase.MoveName(def.Id, level).ToUpperInvariant()}";
                    showcaseLevel.color = PlayerColor;
                }
                if (abilityBlocks != null)
                {
                    for (int i = 0; i < def.Controls.Length && i < abilityBlocks.Length; i++)
                        abilityBlocks[i].text = AbilityBlock(def, i, i == level - 1);
                    if (Time.unscaledTime - lastManualScroll > ManualScrollHold)
                        abilityScrollGoal = Mathf.Clamp(abilityTops[level - 1], 0f, MaxAbilityScroll);
                }
            }
            if (abilityContent != null)
            {
                abilityScroll = Mathf.Lerp(abilityScroll, abilityScrollGoal, 1f - Mathf.Exp(-10f * dt));
                abilityContent.anchoredPosition = new Vector2(0f, abilityScroll);
            }
        }

        /// <summary>For tests: the showcase as it stands (null before the select screen first opens).</summary>
        public CharacterShowcase TestShowcase => showcase;

        public void TestOpenCharacters(CharacterId id)
        {
            character = id;
            customColor = false;
            ShowCharacters();
        }

        public void TestShowcaseLevel(int level) => showcase?.SetLevel(level);

        public Transform TestCharacterScreen => characterScreen;

        /// <summary>For tests: where the hand sign's box is on the select screen (the screen's own space, mm).</summary>
        public static Vector3 TestSignHand => SignHand;

        /// <summary>For tests: where the life-size mannequin stands (its feet), beside the open select screen.</summary>
        public Vector3 TestFigureFeet => characterScreen == null ? Vector3.zero
            : new Vector3(0f, FloorY, 0f) + Vector3.Scale(characterScreen.position
                + Vector3.ProjectOnPlane(characterScreen.forward, Vector3.up).normalized * FigureAhead
                + Vector3.ProjectOnPlane(characterScreen.right, Vector3.up).normalized * FigureRight, new Vector3(1f, 0f, 1f));

        /// <summary>For tests: fires a web shot the way the Spider's spider sign does.</summary>
        public void TestFireWeb(Vector3 origin, Vector3 direction, bool black)
        {
            Blast shot = game.FireBlast(origin, direction.normalized, 0.3f, 0.35f, 1.6f, 5f, 0f, PlayerColor);
            WebStrand.Attach(shot, origin, black ? new Color(0.25f, 0.2f, 0.3f) : Color.white, foes);
        }
    }
}
