using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Repulsor
{
    /// <summary>
    /// Your own status, low and to the right of your view, drifting after your head rather than glued to it:
    /// a fighting-game health bar, the transformation meter, lives, shield and powerups. Also the big messages
    /// in the middle of your view (countdown, waves, knocked out).
    /// </summary>
    public sealed class PlayerHud
    {
        public static readonly string[] LayoutNames = { "A  LOW RIGHT", "B  LOW CENTRE", "C  TOP CENTRE" };
        static readonly Vector3[] PanelOffsets =
        {
            new Vector3(0.24f, -0.2f, 0.75f),
            new Vector3(0f, -0.26f, 0.75f),
            new Vector3(0f, 0.2f, 0.8f),
        };

        /// <summary>Where the panel sits: 0 low right, 1 low centre, 2 top centre (like a fighting game).</summary>
        public int Layout { get; set; } = Mathf.Clamp(PlayerPrefs.GetInt("variant.hud", 0), 0, 2);
        static readonly Vector3 MessageOffset = new Vector3(0f, 0.06f, 1.3f);
        const float FollowSpeed = 4f;
        const int MaxLifeIcons = 5;

        static readonly Color HpColor = new Color(1f, 0.82f, 0.15f);
        static readonly Color HpLow = new Color(1f, 0.35f, 0.15f);
        static readonly Color MeterColor = new Color(0.3f, 0.7f, 1f);
        static readonly Color MeterFull = new Color(0.55f, 1f, 1f);
        static readonly Color ShieldColor = new Color(0.4f, 0.9f, 1f);

        readonly Transform head;
        readonly RectTransform panel;
        readonly RectTransform message;
        readonly Text nameText;
        readonly Text hpText;
        readonly Text meterText;
        readonly Text statusText;
        readonly Text fpsText;
        readonly Text bigText;
        readonly Text smallText;
        readonly Bar hp;
        readonly Bar meter;
        readonly Bar shield;
        readonly Image[] lives = new Image[MaxLifeIcons];
        readonly StringBuilder builder = new StringBuilder();
        float messageFor;
        float fpsTimer;
        int fpsFrames;
        float fps;
        bool placed;

        public bool ShowFps { get; set; }

        public PlayerHud(Transform parent, Transform head)
        {
            this.head = head;
            panel = Ui.Canvas("Player HUD", parent, new Vector2(300f, 140f));
            Ui.Box(panel, Vector2.zero, new Vector2(300f, 140f), new Color(0.02f, 0.04f, 0.08f, 0.55f));

            nameText = Ui.Label(panel, "", new Vector2(-40f, 52f), new Vector2(210f, 26f), 18, TextAnchor.MiddleLeft, FontStyle.Bold);
            for (int i = 0; i < MaxLifeIcons; i++)
            {
                lives[i] = Ui.Box(panel, new Vector2(130f - i * 16f, 52f), new Vector2(11f, 11f), new Color(1f, 0.3f, 0.35f));
            }

            hp = Ui.Bar(panel, new Vector2(0f, 24f), new Vector2(286f, 24f), new Color(0.08f, 0.08f, 0.1f, 0.95f), HpColor);
            hp.Trail = true;
            hpText = Ui.Label((RectTransform)hp.transform, "", new Vector2(-6f, 0f), new Vector2(270f, 24f), 15, TextAnchor.MiddleRight, FontStyle.Bold);
            hpText.color = new Color(0.1f, 0.08f, 0.02f);

            meter = Ui.Bar(panel, new Vector2(0f, -6f), new Vector2(286f, 13f), new Color(0.08f, 0.08f, 0.1f, 0.95f), MeterColor);
            meterText = Ui.Label(panel, "", new Vector2(0f, -26f), new Vector2(286f, 20f), 13, TextAnchor.MiddleLeft);

            shield = Ui.Bar(panel, new Vector2(-93f, -48f), new Vector2(100f, 8f), new Color(0.08f, 0.08f, 0.1f, 0.95f), ShieldColor);
            statusText = Ui.Label(panel, "", new Vector2(50f, -48f), new Vector2(180f, 18f), 12, TextAnchor.MiddleRight);
            fpsText = Ui.Label(panel, "", new Vector2(110f, 76f), new Vector2(80f, 14f), 11, TextAnchor.MiddleRight);

            message = Ui.Canvas("Message", parent, new Vector2(700f, 260f));
            bigText = Ui.Label(message, "", new Vector2(0f, 30f), new Vector2(700f, 120f), 90, TextAnchor.MiddleCenter, FontStyle.Bold);
            smallText = Ui.Label(message, "", new Vector2(0f, -70f), new Vector2(700f, 80f), 30, TextAnchor.MiddleCenter);
            bigText.color = Ui.Accent;
        }

        public bool Visible
        {
            set
            {
                panel.gameObject.SetActive(value);
                if (!value)
                    placed = false;
            }
        }

        /// <summary>Shows a message in the middle of your view. Seconds 0 keeps it until replaced or cleared.</summary>
        public void Message(string big, string small = "", float seconds = 2f)
        {
            bigText.text = big;
            smallText.text = small;
            messageFor = seconds <= 0f ? float.MaxValue : seconds;
            message.gameObject.SetActive(true);
        }

        public void ClearMessage()
        {
            messageFor = 0f;
            message.gameObject.SetActive(false);
        }

        public void Tick(Fighter fighter, CharacterDef character, float dt)
        {
            Follow(dt);
            messageFor -= dt;
            if (messageFor <= 0f && message.gameObject.activeSelf)
                message.gameObject.SetActive(false);

            fpsFrames++;
            fpsTimer += dt;
            if (fpsTimer >= 0.5f)
            {
                fps = fpsFrames / fpsTimer;
                fpsFrames = 0;
                fpsTimer = 0f;
            }
            fpsText.text = ShowFps ? $"{fps:0} FPS" : "";

            if (!panel.gameObject.activeSelf || fighter == null)
                return;

            nameText.text = $"{character.Name.ToUpperInvariant()}  <color=#78E1FF>LV {fighter.Level}</color>";
            for (int i = 0; i < MaxLifeIcons; i++)
                lives[i].enabled = i < fighter.Lives;

            hp.Fraction = fighter.Hp / fighter.MaxHp;
            hp.FillImage.color = fighter.Hp / fighter.MaxHp < 0.3f ? HpLow : HpColor;
            hp.Tick(dt);
            hpText.text = $"{Mathf.CeilToInt(fighter.Hp)} / {fighter.MaxHp:0}";

            meter.Fraction = fighter.Level >= Fighter.MaxLevel ? 1f : fighter.Meter;
            bool ready = fighter.CanTransform;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f);
            meter.FillImage.color = ready ? Color.Lerp(MeterColor, MeterFull, pulse) : MeterColor;
            meter.Tick(dt);
            if (fighter.Level >= Fighter.MaxLevel)
                meterText.text = "MAX POWER";
            else if (ready)
                meterText.text = "<b><color=#B0FFFF>POWER UP READY: clench both fists at your sides</color></b>";
            else
                meterText.text = $"POWER {Mathf.FloorToInt(fighter.Meter * 100f)}%   (deal damage to charge)";

            shield.Fraction = fighter.Shield.Broken ? 0f : fighter.Shield.Fraction;
            shield.Tick(dt);

            builder.Clear();
            if (fighter.Shield.Broken)
                builder.Append("<color=#FF7050>SHIELD BROKEN ").Append(Mathf.CeilToInt(fighter.Shield.BrokenFor)).Append("s</color>  ");
            else if (fighter.Shield.Up)
                builder.Append("<color=#80F0FF>SHIELD UP</color>  ");
            if (fighter.InvincibleFor > 0f)
                builder.Append("<color=#FFF080>INVINCIBLE ").Append(Mathf.CeilToInt(fighter.InvincibleFor)).Append("s</color>  ");
            if (fighter.BoostFor > 0f)
                builder.Append("<color=#FF6060>1.5x DAMAGE ").Append(Mathf.CeilToInt(fighter.BoostFor)).Append("s</color>  ");
            if (fighter.Healing)
                builder.Append("<color=#80FF90>HEALING</color>");
            statusText.text = builder.ToString();
        }

        void Follow(float dt)
        {
            Place(panel, PanelOffsets[Mathf.Clamp(Layout, 0, PanelOffsets.Length - 1)], dt);
            Place(message, MessageOffset, dt);
            placed = true;
        }

        void Place(RectTransform rect, Vector3 offset, float dt)
        {
            Vector3 wanted = head.position + head.rotation * offset;
            float follow = placed ? 1f - Mathf.Exp(-FollowSpeed * dt) : 1f;
            rect.position = Vector3.Lerp(rect.position, wanted, follow);
            rect.rotation = Quaternion.LookRotation(rect.position - head.position, head.up);
        }
    }

    /// <summary>
    /// A small health bar over an enemy. It shows only while you look near it and it has been hurt, or for a
    /// moment after a hit, to keep the view clear.
    /// </summary>
    public sealed class OverheadBar : MonoBehaviour
    {
        public const float GazeDegrees = 25f;
        const float AfterHitSeconds = 2f;

        IHittable target;
        Transform head;
        Bar bar;
        RectTransform canvas;
        float lastHp;
        float sinceHit = AfterHitSeconds;
        bool alwaysWhenLooked;

        public static OverheadBar Attach(IHittable target, Transform head, Transform parent, bool showAtFullHp)
        {
            RectTransform canvas = Ui.Canvas("Overhead Bar", parent, new Vector2(140f, 16f));
            var overhead = canvas.gameObject.AddComponent<OverheadBar>();
            overhead.canvas = canvas;
            overhead.target = target;
            overhead.head = head;
            overhead.alwaysWhenLooked = showAtFullHp;
            overhead.lastHp = target.Hp;
            overhead.bar = Ui.Bar(canvas, Vector2.zero, new Vector2(140f, 16f), new Color(0.05f, 0.05f, 0.07f, 0.9f), new Color(1f, 0.3f, 0.25f));
            overhead.bar.Trail = true;
            overhead.LateUpdate();
            return overhead;
        }

        void LateUpdate()
        {
            if (target == null || (target is Object unityObject && unityObject == null) || !target.Alive)
            {
                Destroy(gameObject);
                return;
            }

            float dt = Time.deltaTime;
            if (target.Hp < lastHp)
                sinceHit = 0f;
            else
                sinceHit += dt;
            lastHp = target.Hp;

            Vector3 point = target.BarPoint;
            Vector3 toTarget = point - head.position;
            bool looked = Vector3.Angle(head.forward, toTarget) < GazeDegrees;
            bool hurt = target.Hp < target.MaxHp;
            bool show = sinceHit < AfterHitSeconds || (looked && (hurt || alwaysWhenLooked));

            canvas.gameObject.layer = 0;
            bar.gameObject.SetActive(show);
            if (!show)
                return;
            canvas.position = point;
            canvas.rotation = Quaternion.LookRotation(toTarget, Vector3.up);
            // Keep the same apparent size at any distance, within reason.
            float scale = Mathf.Clamp(toTarget.magnitude, 1f, 5f) * Ui.MetresPerUnit * 0.6f;
            canvas.localScale = Vector3.one * scale;
            bar.Fraction = target.Hp / target.MaxHp;
            bar.Tick(dt);
        }
    }
}
