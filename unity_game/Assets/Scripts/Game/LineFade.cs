using UnityEngine;

namespace Repulsor
{
    /// <summary>A line that fades out and goes: sword sweeps, wind streaks. Its colours' alpha runs down over its life.</summary>
    public sealed class LineFade : MonoBehaviour
    {
        LineRenderer line;
        Color start, end;
        float life = 0.25f;
        float age;

        /// <summary>A soft, see-through line with tapered ends in this colour, that fades over <paramref name="seconds"/>.</summary>
        public static LineRenderer Make(string name, Transform parent, Color color, float width, int points, float seconds)
        {
            var go = new GameObject(name, typeof(LineRenderer));
            go.transform.SetParent(parent, false);
            var line = go.GetComponent<LineRenderer>();
            line.sharedMaterial = Resources.Load<Material>("WebLine");
            line.positionCount = points;
            line.useWorldSpace = true;
            line.numCapVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.widthMultiplier = width;
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.05f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.05f));
            line.startColor = line.endColor = color;
            if (seconds > 0f)
            {
                var fade = go.AddComponent<LineFade>();
                fade.line = line;
                fade.start = fade.end = color;
                fade.life = seconds;
            }
            return line;
        }

        void Update()
        {
            age += Time.deltaTime;
            float left = 1f - Mathf.Clamp01(age / life);
            Color a = start, b = end;
            a.a *= left * left;
            b.a *= left * left;
            line.startColor = a;
            line.endColor = b;
            if (age >= life)
                Destroy(gameObject);
        }
    }
}
