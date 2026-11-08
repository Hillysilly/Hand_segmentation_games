using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Repulsor
{
    /// <summary>
    /// Numbers that pop up over whatever you hit, rise and fade. Hits on the same target within a third of a
    /// second are added together, so a laser shows one growing number rather than a stream.
    /// </summary>
    public sealed class DamageNumbers
    {
        const float GatherSeconds = 0.3f;
        const float LifeSeconds = 0.9f;
        const float Rise = 0.35f;

        sealed class Pending
        {
            public float Amount;
            public float Age;
            public Vector3 Point;
        }

        sealed class Shown
        {
            public RectTransform Canvas;
            public Text Text;
            public Vector3 Start;
            public float Age;
        }

        readonly Transform parent;
        readonly Transform head;
        readonly Dictionary<IHittable, Pending> pending = new Dictionary<IHittable, Pending>();
        readonly List<IHittable> ready = new List<IHittable>();
        readonly List<Shown> shown = new List<Shown>();
        readonly Stack<Shown> spare = new Stack<Shown>();

        public DamageNumbers(Transform parent, Transform head)
        {
            this.parent = parent;
            this.head = head;
        }

        public void Add(IHittable target, float amount)
        {
            if (!pending.TryGetValue(target, out Pending p))
                pending[target] = p = new Pending();
            p.Amount += amount;
            p.Point = target.BarPoint;
        }

        public void Tick(float dt)
        {
            ready.Clear();
            foreach (KeyValuePair<IHittable, Pending> entry in pending)
            {
                entry.Value.Age += dt;
                if (entry.Value.Age >= GatherSeconds)
                    ready.Add(entry.Key);
            }
            foreach (IHittable target in ready)
            {
                Pending p = pending[target];
                pending.Remove(target);
                if (p.Amount >= 0.5f)
                    Show(p.Point, Mathf.RoundToInt(p.Amount), !target.Alive);
            }

            for (int i = shown.Count - 1; i >= 0; i--)
            {
                Shown s = shown[i];
                s.Age += dt;
                float t = s.Age / LifeSeconds;
                if (t >= 1f)
                {
                    s.Canvas.gameObject.SetActive(false);
                    shown.RemoveAt(i);
                    spare.Push(s);
                    continue;
                }
                Vector3 point = s.Start + Vector3.up * (Rise * t);
                Vector3 toPoint = point - head.position;
                s.Canvas.position = point;
                s.Canvas.rotation = Quaternion.LookRotation(toPoint, Vector3.up);
                s.Canvas.localScale = Vector3.one * (Mathf.Clamp(toPoint.magnitude, 1f, 5f) * Ui.MetresPerUnit * (1f + 0.3f * (1f - t)));
                Color c = s.Text.color;
                c.a = 1f - t * t;
                s.Text.color = c;
            }
        }

        public void Clear()
        {
            pending.Clear();
            foreach (Shown s in shown)
            {
                s.Canvas.gameObject.SetActive(false);
                spare.Push(s);
            }
            shown.Clear();
        }

        void Show(Vector3 point, int amount, bool finishing)
        {
            Shown s = spare.Count > 0 ? spare.Pop() : Create();
            s.Canvas.gameObject.SetActive(true);
            s.Start = point + Random.insideUnitSphere * 0.05f;
            s.Age = 0f;
            s.Text.text = amount.ToString();
            s.Text.color = finishing ? new Color(1f, 0.85f, 0.3f) : Color.white;
            s.Text.fontSize = finishing ? 64 : 48;
            shown.Add(s);
        }

        Shown Create()
        {
            RectTransform canvas = Ui.Canvas("Damage Number", parent, new Vector2(160f, 70f));
            Text text = Ui.Label(canvas, "", Vector2.zero, new Vector2(160f, 70f), 48, TextAnchor.MiddleCenter, FontStyle.Bold);
            text.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            return new Shown { Canvas = canvas, Text = text };
        }
    }
}
