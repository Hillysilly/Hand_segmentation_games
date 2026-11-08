using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// A floating orb that bobs gently where it spawned. It heats up and shrinks as it takes damage. In the old
    /// test mode one hit destroys it; in the game it has HP and stands in for enemies until robots exist.
    /// </summary>
    public sealed class Target : MonoBehaviour, IHittable
    {
        const float BobHeight = 0.06f;
        const float BobSpeed = 1.6f;
        const float GrowSeconds = 0.3f;
        const float BurnSeconds = 0.4f;
        const float BurntScale = 0.4f;
        const float FlashSeconds = 0.12f;

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
        float flash;

        public float Radius => radius;
        public Vector3 Center => transform.position;
        public bool Alive => Hp > 0f;
        public Team Team => Team.Enemies;
        public float Hp { get; private set; } = 1f;
        public float MaxHp { get; private set; } = 1f;
        public Vector3 BarPoint => transform.position + Vector3.up * (radius + 0.12f);

        void OnEnable() => Hittables.Add(this);
        void OnDisable() => Hittables.Remove(this);

        public void Place(Vector3 position, float hp = 1f)
        {
            home = position;
            transform.position = position;
            transform.localScale = Vector3.zero;
            age = 0f;
            phase = Random.value * Mathf.PI * 2f;
            Hp = MaxHp = hp;
        }

        public float TakeHit(float damage, Vector3 from)
        {
            if (!Alive)
                return 0f;
            float taken = Mathf.Min(damage, Hp);
            Hp -= taken;
            flash = FlashSeconds;
            ShowHeat();
            return taken;
        }

        /// <summary>Adds laser time. Returns true when this target has burnt through.</summary>
        public bool Burn(float dt)
        {
            burnt += dt;
            ShowHeat();
            return burnt >= BurnSeconds;
        }

        float Damage => Mathf.Max(burnt / BurnSeconds, 1f - Hp / MaxHp);

        void ShowHeat()
        {
            if (heat == null)
            {
                heat = new MaterialPropertyBlock();
                sphere = GetComponent<Renderer>();
                coolColor = sphere.sharedMaterial.GetColor(BaseColor);
            }
            float glow = Mathf.Clamp01(Damage + (flash > 0f ? 0.5f : 0f));
            heat.SetColor(BaseColor, Color.Lerp(coolColor, HotColor, glow));
            sphere.SetPropertyBlock(heat);
        }

        void Update()
        {
            age += Time.deltaTime;
            transform.position = home + Vector3.up * (Mathf.Sin(age * BobSpeed + phase) * BobHeight);
            if (flash > 0f)
            {
                flash -= Time.deltaTime;
                ShowHeat();
            }

            float grown = Mathf.Clamp01(age / GrowSeconds);
            float burning = Mathf.Lerp(1f, BurntScale, Mathf.Clamp01(Damage));
            transform.localScale = Vector3.one * (radius * 2f * grown * burning);
        }
    }
}
