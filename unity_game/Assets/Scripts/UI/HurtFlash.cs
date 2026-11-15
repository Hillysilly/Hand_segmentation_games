using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// A red glow round the edges of your view for a moment when you take damage; stronger for bigger hits. Drawn on
    /// the inside of a sphere on your head (shader Repulsor/Hurt Vignette), so it runs past the edge of the view
    /// on every side with no edges to see.
    /// </summary>
    public sealed class HurtFlash
    {
        const float Radius = 0.3f;
        const float FadeSpeed = 2.5f;
        static readonly int AmountId = Shader.PropertyToID("_Amount");

        readonly Transform head;
        readonly Transform sphere;
        readonly Renderer renderer;
        readonly Material material;
        float strength;

        /// <summary>How strong the flash is now, 0 to 1 (for tests).</summary>
        public float Strength => strength;

        public HurtFlash(Transform parent, Transform head)
        {
            this.head = head;
            var go = new GameObject("Hurt Flash", typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>("SphereMesh");
            sphere = go.transform;
            sphere.SetParent(parent, false);
            // The sphere mesh is 1 m across.
            sphere.localScale = Vector3.one * (Radius * 2f);
            renderer = go.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var shared = Resources.Load<Material>("HurtFlash");
            material = shared != null ? Object.Instantiate(shared) : null;
            renderer.sharedMaterial = material;
            renderer.enabled = false;
        }

        /// <summary>A hit for this much damage.</summary>
        public void Hit(float damage) => strength = Mathf.Clamp01(strength + 0.35f + damage * 0.04f);

        public void Tick(float dt)
        {
            strength = Mathf.MoveTowards(strength, 0f, FadeSpeed * dt);
            bool visible = strength > 0.01f && material != null;
            renderer.enabled = visible;
            if (!visible)
                return;
            material.SetFloat(AmountId, strength * 0.7f);
            sphere.SetPositionAndRotation(head.position, head.rotation);
        }
    }
}
