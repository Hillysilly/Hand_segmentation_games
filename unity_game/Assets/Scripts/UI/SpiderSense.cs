using UnityEngine;
using UnityEngine.UI;

namespace Repulsor
{
    /// <summary>
    /// Spider sense: the edge of your view glows red on the side an unseen enemy is coming from, brighter the
    /// closer it is. Enemies you're already looking at don't count.
    /// </summary>
    public sealed class SpiderSense
    {
        public const float Range = 4f;
        public const float SeenDegrees = 50f;
        const float Distance = 0.5f;

        readonly Transform head;
        readonly RectTransform canvas;
        readonly Image left;
        readonly Image right;
        readonly Image behind;
        float leftShown;
        float rightShown;

        public SpiderSense(Transform parent, Transform head)
        {
            this.head = head;
            canvas = Ui.Canvas("Spider Sense", parent, new Vector2(900f, 600f));
            Sprite fade = MakeFade();
            left = Edge(fade, new Vector2(-370f, 0f), new Vector2(160f, 600f), false);
            right = Edge(fade, new Vector2(370f, 0f), new Vector2(160f, 600f), true);
            behind = Edge(fade, new Vector2(0f, -250f), new Vector2(900f, 100f), false);
            behind.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            behind.rectTransform.sizeDelta = new Vector2(100f, 900f);
            canvas.gameObject.SetActive(false);
        }

        Image Edge(Sprite sprite, Vector2 position, Vector2 size, bool flip)
        {
            Image image = Ui.Box(canvas, position, size, new Color(1f, 0.05f, 0.05f, 0f), false);
            image.sprite = sprite;
            if (flip)
                image.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            return image;
        }

        public void Tick(bool active, float dt)
        {
            float wantLeft = 0f;
            float wantRight = 0f;
            if (active)
            {
                Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                foreach (IHittable foe in Hittables.All)
                {
                    if (!foe.Alive || foe.Team != Team.Enemies)
                        continue;
                    Vector3 to = foe.Center - head.position;
                    float distance = to.magnitude;
                    if (distance > Range || Vector3.Angle(head.forward, to) < SeenDegrees)
                        continue;
                    float strength = 1f - distance / Range;
                    float side = Vector3.SignedAngle(forward, Vector3.ProjectOnPlane(to, Vector3.up), Vector3.up);
                    if (side < 0f)
                        wantLeft = Mathf.Max(wantLeft, strength);
                    else
                        wantRight = Mathf.Max(wantRight, strength);
                }
            }
            leftShown = Mathf.MoveTowards(leftShown, wantLeft, dt * 3f);
            rightShown = Mathf.MoveTowards(rightShown, wantRight, dt * 3f);
            bool visible = leftShown > 0.01f || rightShown > 0.01f;
            canvas.gameObject.SetActive(visible);
            if (!visible)
                return;

            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 9f);
            Set(left, leftShown * pulse);
            Set(right, rightShown * pulse);
            Set(behind, Mathf.Min(leftShown, rightShown) * pulse);
            canvas.position = head.position + head.forward * Distance;
            canvas.rotation = Quaternion.LookRotation(head.forward, head.up);
        }

        static void Set(Image image, float alpha)
        {
            Color color = image.color;
            color.a = Mathf.Clamp01(alpha) * 0.7f;
            image.color = color;
        }

        static Sprite MakeFade()
        {
            const int width = 64;
            var texture = new Texture2D(width, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < width; x++)
            {
                float a = Mathf.Pow(1f - x / (width - 1f), 2f);
                for (int y = 0; y < 4; y++)
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, width, 4), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
