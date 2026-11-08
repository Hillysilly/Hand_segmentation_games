using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Repulsor
{
    /// <summary>
    /// A button you press with a fingertip, like a key on the headset's keyboard: the key sinks into a
    /// dark hollow as the finger pushes, clicks part way down and springs back. A ring brightens as the
    /// finger comes close. The finger has to arrive from the front, so a panel appearing around a hand
    /// can't click itself.
    /// </summary>
    public sealed class PokeButton : MonoBehaviour
    {
        public const float Travel = 14f;         // mm the key can sink
        public const float ClickDepth = 9f;
        public const float HoverRange = 60f;     // mm in front where the ring starts to show
        public const float EdgeMargin = 6f;
        const float SpringSpeed = 140f;          // mm per second back up
        const float FlashSeconds = 0.25f;

        public static readonly List<PokeButton> Active = new List<PokeButton>();

        RectTransform face;
        Image faceImage;
        Image ring;
        Text label;
        System.Action onClick;
        bool armed;
        float press;
        float flash;
        bool interactable = true;
        bool selected;

        public Text Label => label;

        public bool Interactable
        {
            get => interactable;
            set
            {
                interactable = value;
                Recolor();
            }
        }

        public bool Selected
        {
            get => selected;
            set
            {
                selected = value;
                Recolor();
            }
        }

        public string Text
        {
            set => label.text = value;
        }

        void OnEnable() => Active.Add(this);

        void OnDisable()
        {
            Active.Remove(this);
            armed = false;
            press = 0f;
        }

        public void Build(string text, int fontSize, System.Action click)
        {
            onClick = click;
            var rect = (RectTransform)transform;
            Vector2 size = rect.sizeDelta;

            ring = Ui.Box(rect, Vector2.zero, size + new Vector2(10f, 10f), new Color(Ui.Accent.r, Ui.Accent.g, Ui.Accent.b, 0f));
            Image well = Ui.Box(rect, Vector2.zero, size + new Vector2(4f, 4f), Ui.Well);
            well.rectTransform.localPosition = new Vector3(0f, 0f, Travel);

            faceImage = Ui.Box(rect, Vector2.zero, size, Ui.ButtonColor);
            face = faceImage.rectTransform;
            label = Ui.Label(face, text, Vector2.zero, size - new Vector2(8f, 4f), fontSize);
            label.fontStyle = FontStyle.Bold;
            Recolor();
        }

        /// <summary>Where a fingertip is relative to this key: inside its outline, and how far pushed in (mm).</summary>
        public bool Probe(Vector3 fingertip, out float depth)
        {
            var rect = (RectTransform)transform;
            Vector3 local = rect.InverseTransformPoint(fingertip);
            Vector2 half = rect.rect.size * 0.5f;
            depth = local.z;
            return Mathf.Abs(local.x) < half.x + EdgeMargin && Mathf.Abs(local.y) < half.y + EdgeMargin;
        }

        /// <summary>Advances with the nearest fingertip's reading. Returns true on the frame it clicks.</summary>
        public bool Step(bool inside, float depth, float dt)
        {
            bool clicked = false;
            if (!inside)
            {
                armed = false;
            }
            else if (depth < -2f)
            {
                armed = true;
            }
            else if (armed && interactable && depth >= ClickDepth)
            {
                armed = false;
                clicked = true;
                flash = FlashSeconds;
            }

            float target = inside && interactable ? Mathf.Clamp(depth, 0f, Travel) : 0f;
            press = target > press ? target : Mathf.Max(target, press - SpringSpeed * dt);
            face.localPosition = new Vector3(0f, 0f, press);

            float near = inside && interactable ? Mathf.Clamp01(1f + depth / HoverRange) : 0f;
            Color ringColor = Ui.Accent;
            ringColor.a = near * near * 0.9f;
            ring.color = ringColor;

            if (flash > 0f)
            {
                flash -= dt;
                Recolor();
            }

            if (clicked)
                onClick?.Invoke();
            return clicked;
        }

        void Recolor()
        {
            if (faceImage == null)
                return;
            Color color = !interactable ? Ui.ButtonDisabled : selected ? Ui.ButtonSelected : Ui.ButtonColor;
            if (flash > 0f)
                color = Color.Lerp(color, Ui.Accent, flash / FlashSeconds);
            faceImage.color = color;
            label.color = interactable ? Ui.Ink : Ui.Muted;
        }
    }

    /// <summary>Feeds both index fingertips to every visible poke button.</summary>
    public static class PokeInput
    {
        static readonly List<PokeButton> buttons = new List<PokeButton>();

        /// <summary>Returns the position of a click this frame, if any, for a sound.</summary>
        public static bool Tick(HandPose left, HandPose right, float dt, out Vector3 clickedAt)
        {
            clickedAt = default;
            bool any = false;
            buttons.Clear();
            buttons.AddRange(PokeButton.Active);
            foreach (PokeButton button in buttons)
            {
                if (button == null || !button.isActiveAndEnabled)
                    continue;
                bool inside = false;
                float depth = -1000f;
                Read(button, left, ref inside, ref depth);
                Read(button, right, ref inside, ref depth);
                if (button.Step(inside, depth, dt))
                {
                    any = true;
                    clickedAt = button.transform.position;
                }
            }
            return any;
        }

        static void Read(PokeButton button, HandPose hand, ref bool inside, ref float depth)
        {
            if (!hand.Tracked)
                return;
            // Far behind the panel is a hand reaching round it, not a press.
            if (button.Probe(hand[Joint.IndexTip], out float d) && d > depth && d > -PokeButton.HoverRange * 2f
                && d < PokeButton.Travel + 50f)
            {
                inside = true;
                depth = d;
            }
        }
    }
}
