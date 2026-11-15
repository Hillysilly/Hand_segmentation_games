using UnityEngine;
using UnityEngine.UI;

namespace Repulsor
{
    /// <summary>
    /// Builds world-space UI in code. One canvas unit is one millimetre, so sizes read as real sizes:
    /// a 120 x 45 button is 12 cm wide, about right for poking with a finger.
    /// </summary>
    public static class Ui
    {
        public const float MetresPerUnit = 0.001f;

        public static readonly Color Panel = new Color(0.03f, 0.06f, 0.12f, 0.9f);
        public static readonly Color PanelEdge = new Color(0.2f, 0.45f, 0.7f, 0.9f);
        public static readonly Color Accent = new Color(0.47f, 0.88f, 1f, 1f);
        public static readonly Color ButtonColor = new Color(0.11f, 0.22f, 0.38f, 1f);
        public static readonly Color ButtonSelected = new Color(0.2f, 0.55f, 0.85f, 1f);
        public static readonly Color ButtonDisabled = new Color(0.16f, 0.17f, 0.2f, 1f);
        public static readonly Color Well = new Color(0.01f, 0.02f, 0.04f, 0.95f);
        public static readonly Color Ink = new Color(0.92f, 0.96f, 1f, 1f);
        public static readonly Color Muted = new Color(0.6f, 0.68f, 0.78f, 1f);
        public static readonly Color Warning = new Color(1f, 0.75f, 0.2f, 1f);

        static Font font;
        static Sprite rounded;

        public static Font Font => font != null ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        public static Sprite Rounded => rounded != null ? rounded : rounded = MakeRounded();

        /// <summary>A world-space canvas. Its front faces -Z, so point its forward away from the viewer.</summary>
        public static RectTransform Canvas(string name, Transform parent, Vector2 size)
        {
            var canvasObject = new GameObject(name, typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(parent, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObject.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 8f;
            var rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.localScale = Vector3.one * MetresPerUnit;
            return rect;
        }

        public static RectTransform Rect(string name, RectTransform parent, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        public static Image Box(RectTransform parent, Vector2 position, Vector2 size, Color color, bool round = true)
        {
            RectTransform rect = Rect("Box", parent, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            if (round)
            {
                image.sprite = Rounded;
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 2.5f;
            }
            return image;
        }

        public static Text Label(RectTransform parent, string text, Vector2 position, Vector2 size, int fontSize,
            TextAnchor anchor = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            RectTransform rect = Rect("Label", parent, position, size);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = Font;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.alignment = anchor;
            label.color = Ink;
            label.supportRichText = true;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.text = text;
            return label;
        }

        public static PokeButton Button(RectTransform parent, string label, Vector2 position, Vector2 size,
            System.Action onClick, int fontSize = 16)
        {
            RectTransform rect = Rect("Button " + label, parent, position, size);
            var button = rect.gameObject.AddComponent<PokeButton>();
            button.Build(label, fontSize, onClick);
            return button;
        }

        /// <summary>A horizontal bar. Set the fill with <see cref="Bar.Fraction"/>.</summary>
        public static Bar Bar(RectTransform parent, Vector2 position, Vector2 size, Color back, Color fill)
        {
            Image frame = Box(parent, position, size, back, false);
            var bar = frame.gameObject.AddComponent<Bar>();
            bar.Build(fill);
            return bar;
        }

        public static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        public static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                child.gameObject.SetActive(false);
                Object.Destroy(child.gameObject);
            }
        }

        static Sprite MakeRounded()
        {
            const int size = 64;
            const float radius = 24f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(radius - (x + 0.5f), (x + 0.5f) - (size - radius), 0f);
                float dy = Mathf.Max(radius - (y + 0.5f), (y + 0.5f) - (size - radius), 0f);
                float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }
    }

    /// <summary>
    /// A fill bar with a trailing "damage" bar behind it, like a fighting game's health bar: what the current attack
    /// took shows orange, and holds while hits keep coming (a held laser, a flamethrower), then drains away.
    /// </summary>
    public sealed class Bar : MonoBehaviour
    {
        public static readonly Color DamageColor = new Color(1f, 0.5f, 0.05f);
        const float TrailDelay = 0.6f;
        const float TrailSpeed = 0.8f;

        RectTransform fill;
        RectTransform trail;
        Image fillImage;
        Image trailImage;
        float shown = 1f;
        float trailShown = 1f;
        float sinceDrop;

        public Image FillImage => fillImage;

        public void Build(Color color)
        {
            var rect = (RectTransform)transform;
            trailImage = Ui.Box(rect, Vector2.zero, Vector2.zero, DamageColor, false);
            trail = trailImage.rectTransform;
            fillImage = Ui.Box(rect, Vector2.zero, Vector2.zero, color, false);
            fill = fillImage.rectTransform;
            Anchor(trail, 1f);
            Anchor(fill, 1f);
            trailImage.enabled = false;
        }

        /// <summary>Shows the orange trail of the latest damage, which catches up after the attack ends. Off by default.</summary>
        public bool Trail
        {
            set => trailImage.enabled = value;
        }

        public float Fraction
        {
            set
            {
                value = Mathf.Clamp01(value);
                if (value < shown)
                    sinceDrop = 0f;
                shown = value;
                if (trailShown < shown)
                    trailShown = shown;
                Anchor(fill, shown);
            }
        }

        public void Tick(float dt)
        {
            sinceDrop += dt;
            if (sinceDrop > TrailDelay)
                trailShown = Mathf.MoveTowards(trailShown, shown, TrailSpeed * dt);
            Anchor(trail, trailShown);
        }

        static void Anchor(RectTransform rect, float fraction)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(fraction, 1f);
            rect.offsetMin = new Vector2(2f, 2f);
            rect.offsetMax = new Vector2(fraction > 0f ? -2f : 0f, -2f);
        }
    }
}
