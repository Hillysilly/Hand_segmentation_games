using UnityEngine;

namespace Repulsor
{
    /// <summary>A floating orb that bobs gently where it spawned and heats up and shrinks as the laser burns it.</summary>
    public sealed class Target : MonoBehaviour
    {
        const float BobHeight = 0.06f;
        const float BobSpeed = 1.6f;
        const float GrowSeconds = 0.3f;
        const float BurnSeconds = 0.4f;
        const float BurntScale = 0.4f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly Color HotColor = new Color(1f, 0.95f, 0.75f);

        [SerializeField] float radius = 0.16f;

        MaterialPropertyBlock heat;
        Renderer sphere;
        Color coolColor;
        Vector3 home;
        float age;
        float phase;
        float burnt;

        public float Radius => radius;

        public void Place(Vector3 position)
        {
            home = position;
            transform.position = position;
            transform.localScale = Vector3.zero;
            age = 0f;
            phase = Random.value * Mathf.PI * 2f;
        }

        /// <summary>Adds laser time. Returns true when this target has burnt through.</summary>
        public bool Burn(float dt)
        {
            burnt += dt;
            ShowHeat();
            return burnt >= BurnSeconds;
        }

        void ShowHeat()
        {
            if (heat == null)
            {
                heat = new MaterialPropertyBlock();
                sphere = GetComponent<Renderer>();
                coolColor = sphere.sharedMaterial.GetColor(BaseColor);
            }
            heat.SetColor(BaseColor, Color.Lerp(coolColor, HotColor, burnt / BurnSeconds));
            sphere.SetPropertyBlock(heat);
        }

        void Update()
        {
            age += Time.deltaTime;
            transform.position = home + Vector3.up * (Mathf.Sin(age * BobSpeed + phase) * BobHeight);

            float grown = Mathf.Clamp01(age / GrowSeconds);
            float burning = Mathf.Lerp(1f, BurntScale, burnt / BurnSeconds);
            transform.localScale = Vector3.one * (radius * 2f * grown * burning);
        }
    }
}
