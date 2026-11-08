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

        public void Show(Color color, BubbleStyle style, float alpha, float hit = 0f)
        {
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
        const float Radius = 0.62f;
        const float GrowSpeed = 8f;
        const float HitFade = 4f;

        readonly Bubble bubble;
        float shown;
        float hit;

        public ShieldView(Transform parent) => bubble = Bubble.Create("Shield", parent);

        public void Flash() => hit = 1f;

        public void Tick(bool up, Vector3 head, Color color, BubbleStyle style, float dt)
        {
            shown = Mathf.MoveTowards(shown, up ? 1f : 0f, GrowSpeed * dt);
            hit = Mathf.Max(0f, hit - HitFade * dt);
            float eased = 1f - (1f - shown) * (1f - shown);
            bubble.transform.position = head + Vector3.down * 0.35f;
            bubble.transform.localScale = Vector3.one * (Radius * 2f * Mathf.Lerp(0.6f, 1f, eased));
            bubble.Show(color, style, eased, hit);
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

        public static void Spawn(Vector3 center, Color color, Transform parent)
        {
            Bubble bubble = Bubble.Create("Transform Wave", parent);
            bubble.transform.position = center;
            var wave = bubble.gameObject.AddComponent<TransformWave>();
            wave.bubble = bubble;
            wave.color = color;
            wave.Update();
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / Seconds);
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);
            bubble.transform.localScale = Vector3.one * (0.3f + EndRadius * 2f * eased);
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
