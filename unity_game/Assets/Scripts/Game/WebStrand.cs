using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// A web shot's look: a thin strand from where it was fired to the shot, and a web splat where it stops.
    /// The strand sags a little and fades once the shot is gone.
    /// </summary>
    public sealed class WebStrand : MonoBehaviour
    {
        const int Points = 12;
        const float SplatSeconds = 3f;
        const float SplatSize = 0.45f;

        Blast shot;
        LineRenderer line;
        Vector3 start;
        Vector3 end;
        Color color;
        Bubble splat;
        float sinceHit = -1f;

        public static void Attach(Blast shot, Vector3 start, Color color, Transform parent)
        {
            var go = new GameObject("Web Strand", typeof(LineRenderer));
            go.transform.SetParent(parent, false);
            var strand = go.AddComponent<WebStrand>();
            strand.shot = shot;
            strand.start = start;
            strand.end = shot.transform.position;
            strand.color = color;
            strand.line = go.GetComponent<LineRenderer>();
            strand.line.sharedMaterial = Resources.Load<Material>("WebLine");
            strand.line.positionCount = Points;
            strand.line.widthMultiplier = 0.006f;
            strand.line.useWorldSpace = true;
            strand.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            strand.line.startColor = strand.line.endColor = color;
            strand.Update();
        }

        void Update()
        {
            if (shot != null)
            {
                end = shot.transform.position;
            }
            else if (sinceHit < 0f)
            {
                // The shot just ended: leave a web where it stopped.
                sinceHit = 0f;
                splat = Bubble.Create("Web Splat", transform);
                splat.transform.position = end;
                Vector3 along = (end - start).normalized;
                if (along.sqrMagnitude > 0.5f)
                    splat.transform.rotation = Quaternion.LookRotation(along);
            }

            float fade = 1f;
            if (sinceHit >= 0f)
            {
                sinceHit += Time.deltaTime;
                fade = Mathf.Clamp01((SplatSeconds - sinceHit) / 0.8f);
                float grow = Mathf.Clamp01(sinceHit * 8f);
                splat.transform.localScale = new Vector3(SplatSize * grow, SplatSize * grow, 0.03f);
                splat.Show(color, BubbleStyle.Web, fade);
                if (sinceHit >= SplatSeconds)
                {
                    Destroy(gameObject);
                    return;
                }
            }

            float length = Vector3.Distance(start, end);
            for (int i = 0; i < Points; i++)
            {
                float t = i / (Points - 1f);
                float sag = Mathf.Sin(t * Mathf.PI) * length * 0.03f * (sinceHit >= 0f ? 1f + sinceHit : 1f);
                line.SetPosition(i, Vector3.Lerp(start, end, t) + Vector3.down * sag);
            }
            Color shown = color;
            shown.a = fade;
            line.startColor = line.endColor = shown;
        }
    }
}
