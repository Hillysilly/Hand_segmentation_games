using UnityEngine;

namespace Repulsor
{
    public enum BubbleStyle { Hex, Flame, Glass, Thin, Web, Energy, Smoke }

    /// <summary>A sphere drawn with the Bubble shader: shields and the transformation shockwave.</summary>
    public sealed class Bubble : MonoBehaviour
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int StyleId = Shader.PropertyToID("_Style");
        static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        static readonly int HitId = Shader.PropertyToID("_Hit");
        static readonly int OccludeId = Shader.PropertyToID("_Occlude");
        static Material material;

        MeshRenderer sphere;
        MaterialPropertyBlock properties;

        public static Material Material => material != null ? material : material = Resources.Load<Material>("Bubble");

        public static Bubble Create(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>("SphereMesh");
            go.transform.SetParent(parent, false);
            var bubble = go.AddComponent<Bubble>();
            bubble.sphere = go.GetComponent<MeshRenderer>();
            bubble.sphere.sharedMaterial = Material;
            bubble.sphere.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bubble.sphere.receiveShadows = false;
            bubble.properties = new MaterialPropertyBlock();
            return bubble;
        }

        /// <param name="occlude">Hide it behind real things the headset can see.</param>
        public void Show(Color color, BubbleStyle style, float alpha, float hit = 0f, bool occlude = true)
        {
            properties.SetFloat(OccludeId, occlude ? 1f : 0f);
            sphere.enabled = alpha > 0.001f;
            properties.SetColor(ColorId, color);
            properties.SetFloat(StyleId, (float)style);
            properties.SetFloat(AlphaId, alpha);
            properties.SetFloat(HitId, hit);
            sphere.SetPropertyBlock(properties);
        }
    }

    /// <summary>Your shield: a bubble around your upper body that pops up while you hold the X.</summary>
    public sealed class ShieldView
    {
        public const float Radius = 0.62f;
        const float GrowSpeed = 8f;
        const float HitFade = 4f;

        readonly Bubble bubble;
        float shown;
        float hit;

        public ShieldView(Transform parent) => bubble = Bubble.Create("Shield", parent);

        /// <summary>Whether real things hide it (true for other players' shields, false for your own).</summary>
        public bool Occlude { get; set; } = true;

        public void Flash() => hit = 1f;

        float shatter;

        /// <summary>Breaking: a bright flash as it bursts outward and vanishes.</summary>
        public void Shatter()
        {
            shatter = 1f;
            hit = 1f;
        }

        public void Tick(bool up, Vector3 head, Color color, BubbleStyle style, float dt) =>
            Tick(up, head, Vector3.zero, color, style, dt);

        /// <summary>
        /// With a facing direction, it's a flat shield held out in front (the Archer's, until a knight's shield
        /// model is picked); with none, a bubble round the upper body.
        /// </summary>
        public void Tick(bool up, Vector3 head, Vector3 facing, Color color, BubbleStyle style, float dt)
        {
            shown = Mathf.MoveTowards(shown, up ? 1f : 0f, GrowSpeed * dt);
            hit = Mathf.Max(0f, hit - HitFade * dt);
            shatter = Mathf.Max(0f, shatter - 2.5f * dt);
            float eased = 1f - (1f - shown) * (1f - shown);
            if (shatter > 0f)
            {
                // Bursts outward and fades instead of shrinking away.
                Place(bubble.transform, head, facing, 1f + (1f - shatter) * 0.6f);
                bubble.Show(Color.Lerp(color, Color.white, 0.5f), style, shatter, 1f, Occlude);
                return;
            }
            Place(bubble.transform, head, facing, Mathf.Lerp(0.6f, 1f, eased));
            bubble.Show(color, style, eased, hit, Occlude);
        }

        /// <summary>Where a shield goes for a player whose head is here.</summary>
        public static void Place(Transform shield, Vector3 head, Vector3 facing, float grow)
        {
            Vector3 flat = Vector3.ProjectOnPlane(facing, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f)
            {
                shield.position = head + Vector3.down * 0.35f;
                shield.rotation = Quaternion.identity;
                shield.localScale = Vector3.one * (Radius * 2f * grow);
                return;
            }
            flat.Normalize();
            shield.position = head + Vector3.down * 0.45f + flat * 0.45f;
            shield.rotation = Quaternion.LookRotation(flat);
            shield.localScale = new Vector3(0.75f, 1f, 0.08f) * grow;
        }
    }

    /// <summary>The burst of energy when you transform: a sphere that races outward and fades.</summary>
    public sealed class TransformWave : MonoBehaviour
    {
        const float Seconds = 0.9f;
        const float EndRadius = 4f;

        Bubble bubble;
        Color color;
        float age;
        bool ring;

        /// <summary>A flat ring racing out along the floor instead of a sphere.</summary>
        public static void SpawnRing(Vector3 center, Color color, Transform parent)
        {
            TransformWave wave = Spawn(center, color, parent);
            wave.ring = true;
            wave.Update();
        }

        public static TransformWave Spawn(Vector3 center, Color color, Transform parent)
        {
            Bubble bubble = Bubble.Create("Transform Wave", parent);
            bubble.transform.position = center;
            var wave = bubble.gameObject.AddComponent<TransformWave>();
            wave.bubble = bubble;
            wave.color = color;
            wave.Update();
            return wave;
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / Seconds);
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);
            float size = 0.3f + EndRadius * 2f * eased;
            bubble.transform.localScale = ring ? new Vector3(size, 0.08f, size) : Vector3.one * size;
            bubble.Show(color, BubbleStyle.Energy, 1f - t);
            if (t >= 1f)
                Destroy(gameObject);
        }
    }

    /// <summary>Greys and darkens the view while you're knocked out.</summary>
    public sealed class DeathView
    {
        static readonly int AmountId = Shader.PropertyToID("_Amount");
        const float Strength = 0.6f;

        readonly Renderer quad;
        readonly Material material;
        float amount;

        public DeathView(Camera camera)
        {
            var go = new GameObject("Death View", typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>("QuadMesh");
            go.transform.SetParent(camera.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, camera.nearClipPlane + 0.01f);
            go.transform.localScale = Vector3.one * 1.5f;
            quad = go.GetComponent<Renderer>();
            material = Object.Instantiate(Resources.Load<Material>("DeathView"));
            quad.sharedMaterial = material;
            quad.enabled = false;
        }

        public void Tick(bool dead, float dt)
        {
            amount = Mathf.MoveTowards(amount, dead ? Strength : 0f, dt * (dead ? 1.5f : 3f));
            quad.enabled = amount > 0.001f;
            if (quad.enabled)
                material.SetFloat(AmountId, amount);
        }
    }
}
