using UnityEngine;
using UnityEngine.UI;

namespace Repulsor
{
    /// <summary>A red glow round the edges of your view for a moment when you take damage; stronger for bigger hits.</summary>
    public sealed class HurtFlash
    {
        const float Distance = 0.45f;
        const float FadeSpeed = 2.5f;

        readonly Transform head;
        readonly RectTransform canvas;
        readonly Image[] edges = new Image[4];
        float strength;

        public HurtFlash(Transform parent, Transform head)
        {
            this.head = head;
            canvas = Ui.Canvas("Hurt Flash", parent, new Vector2(900f, 700f));
            Sprite fade = MakeFade();
            // Left, right, bottom, top: each a gradient from the edge inward.
            edges[0] = Edge(fade, new Vector2(-380f, 0f), new Vector2(140f, 700f), 0f);
            edges[1] = Edge(fade, new Vector2(380f, 0f), new Vector2(140f, 700f), 180f);
            edges[2] = Edge(fade, new Vector2(0f, -280f), new Vector2(140f, 900f), 90f);
            edges[3] = Edge(fade, new Vector2(0f, 280f), new Vector2(140f, 900f), -90f);
            canvas.gameObject.SetActive(false);
        }

        Image Edge(Sprite sprite, Vector2 position, Vector2 size, float angle)
        {
            Image image = Ui.Box(canvas, position, size, new Color(1f, 0.08f, 0.05f, 0f), false);
            image.sprite = sprite;
            image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            return image;
        }

        /// <summary>A hit for this much damage.</summary>
        public void Hit(float damage) => strength = Mathf.Clamp01(strength + 0.35f + damage * 0.04f);

        public void Tick(float dt)
        {
            strength = Mathf.MoveTowards(strength, 0f, FadeSpeed * dt);
            bool visible = strength > 0.01f;
            canvas.gameObject.SetActive(visible);
            if (!visible)
                return;
            foreach (Image edge in edges)
            {
                Color color = edge.color;
                color.a = strength * 0.65f;
                edge.color = color;
            }
            canvas.position = head.position + head.forward * Distance;
            canvas.rotation = Quaternion.LookRotation(head.forward, head.up);
        }

        static Sprite MakeFade()
        {
            const int width = 64;
            var texture = new Texture2D(width, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < width; x++)
            {
                float a = Mathf.Pow(1f - x / (width - 1f), 2.2f);
                for (int y = 0; y < 4; y++)
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, width, 4), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
