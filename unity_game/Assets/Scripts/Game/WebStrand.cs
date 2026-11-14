using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// A web shot's look: the shot flies as a real web (Quaternius cobweb, CC0) that opens out and spins as it goes,
    /// trailing a thin strand back to where it was fired. Where it stops, the web sticks (to the robot it hit, if it
    /// hit one) facing the shooter, and fades after a few seconds.
    /// </summary>
    public sealed class WebStrand : MonoBehaviour
    {
        const int Points = 12;
        const float SplatSeconds = 3f;
        const float SplatSize = 0.75f;
        const float FlyingSize = 0.5f;
        const float OpenSeconds = 0.22f;
        /// <summary>The model is a corner cobweb; squashed this much it reads as a flat net.</summary>
        const float Depth = 0.25f;

        Blast shot;
        LineRenderer line;
        Vector3 start;
        Vector3 end;
        Color color;
        Transform flying;
        Transform splat;
        Renderer[] flyingRenderers;
        Renderer[] splatRenderers;
        Transform stuckTo;
        float age;
        float sinceHit = -1f;
        float roll;
        static MaterialPropertyBlock block;

        public static void Attach(Blast shot, Vector3 start, Color color, Transform parent)
        {
            var go = new GameObject("Web Strand", typeof(LineRenderer));
            go.transform.SetParent(parent, false);
            var strand = go.AddComponent<WebStrand>();
            strand.shot = shot;
            strand.start = start;
            strand.end = shot.transform.position;
            strand.color = color;
            strand.roll = Random.Range(0f, 360f);
            strand.line = go.GetComponent<LineRenderer>();
            strand.line.sharedMaterial = Resources.Load<Material>("WebLine");
            strand.line.positionCount = Points;
            strand.line.widthMultiplier = 0.006f;
            strand.line.useWorldSpace = true;
            strand.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            strand.line.startColor = strand.line.endColor = color;

            // The shot is a web, not an energy ball: hide the ball and fly the web on it. The shot's root faces
            // back toward the shooter, so the web's front (+Z) faces you as it flies off.
            BallLook.HideCrackle(shot.Head, false);
            foreach (Renderer renderer in shot.Head.GetComponentsInChildren<Renderer>())
                renderer.enabled = false;
            shot.TailScale = 0.12f;
            strand.flying = Web(shot.transform, color);
            strand.flyingRenderers = strand.flying.GetComponentsInChildren<Renderer>();
            // Remember what it hits, so the web sticks to it.
            System.Action<IHittable, Vector3> before = shot.OnHit;
            shot.OnHit = (hit, point) =>
            {
                before?.Invoke(hit, point);
                if (strand != null && hit is Component body && body != null)
                    strand.stuckTo = body.transform;
            };
            strand.Update();
        }

        /// <summary>The web model, tinted; null-safe if the prop is missing.</summary>
        public static Transform Web(Transform parent, Color color)
        {
            var prefab = Resources.Load<GameObject>("Props/Web");
            Transform web = prefab != null ? Instantiate(prefab, parent, false).transform : new GameObject("Web").transform;
            web.SetParent(parent, false);
            web.name = "Web";
            Tint(web.GetComponentsInChildren<Renderer>(), color, 1f);
            return web;
        }

        public static void Tint(Renderer[] renderers, Color color, float alpha)
        {
            block ??= new MaterialPropertyBlock();
            color.a = alpha;
            foreach (Renderer renderer in renderers)
            {
                renderer.GetPropertyBlock(block);
                block.SetColor("_Color", color);
                renderer.SetPropertyBlock(block);
            }
        }

        void Update()
        {
            age += Time.deltaTime;
            if (shot != null)
            {
                end = shot.transform.position;
                if (flying != null)
                {
                    float open = Mathf.SmoothStep(0.15f, 1f, Mathf.Clamp01(age / OpenSeconds));
                    flying.localPosition = Vector3.zero;
                    flying.localRotation = Quaternion.Euler(0f, 0f, roll + age * 420f);
                    float size = FlyingSize * open;
                    flying.localScale = new Vector3(size, size, size * Depth);
                }
            }
            else if (sinceHit < 0f)
            {
                // The shot just ended: the web sticks where it stopped, facing back the way it came.
                sinceHit = 0f;
                Vector3 along = (end - start).normalized;
                splat = Web(transform, color);
                splat.position = end;
                splat.rotation = (along.sqrMagnitude > 0.5f ? Quaternion.LookRotation(-along) : Quaternion.identity)
                                 * Quaternion.Euler(0f, 0f, roll + age * 420f);
                splat.localScale = new Vector3(FlyingSize, FlyingSize, FlyingSize * Depth);
                if (stuckTo != null)
                    splat.SetParent(stuckTo, true);
                splatRenderers = splat.GetComponentsInChildren<Renderer>();
            }

            float fade = 1f;
            if (sinceHit >= 0f)
            {
                sinceHit += Time.deltaTime;
                fade = Mathf.Clamp01((SplatSeconds - sinceHit) / 0.8f);
                if (splat != null)
                {
                    // It slaps open a little wider on impact, then holds.
                    float spread = Mathf.Lerp(FlyingSize, SplatSize, Mathf.Clamp01(sinceHit * 10f));
                    if (splat.parent == transform)
                        splat.localScale = new Vector3(spread, spread, spread * Depth);
                    Tint(splatRenderers, color, fade);
                }
                if (sinceHit >= SplatSeconds)
                {
                    if (splat != null)
                        Destroy(splat.gameObject);
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

        void OnDestroy()
        {
            // A web stuck on a robot belongs to this strand; take it away too.
            if (splat != null && splat.parent != transform)
                Destroy(splat.gameObject);
        }
    }
}
