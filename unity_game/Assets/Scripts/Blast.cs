using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// A repulsor shot: a crackling ball with a comet's tail. Flies straight; a stronger charge is bigger and
    /// faster. Everything hangs from an unscaled root facing backwards, so the tail runs along its forward axis.
    /// </summary>
    public sealed class Blast : MonoBehaviour
    {
        const float MinSpeed = 7f;
        const float MaxSpeed = 12f;
        const float MinRadius = 0.07f;
        const float MaxRadius = 0.13f;
        const float LifeSeconds = 2f;
        const float HeadOverRadius = 1.4f;
        const float TailWidthOverRadius = 1.6f;
        const float MinTail = 0.7f;
        const float MaxTail = 1.6f;

        [SerializeField] Transform head;
        [SerializeField] Transform tail;

        public Transform Head => head;

        /// <summary>Stretches or shortens the tail; set after <see cref="Launch"/>.</summary>
        public float TailScale
        {
            set => tailLength *= value;
        }
        /// <summary>Radius of the explosion where it lands, 0 for none.</summary>
        public float Aoe { get; set; }
        public Color Color { get; set; }
        public float Damage { get => damage; set => damage = value; }
        /// <summary>Share of its damage the explosion does, when it has one (<see cref="Aoe"/>).</summary>
        public float Splash { get; set; } = 0.6f;
        /// <summary>Flies through enemies, hitting each once, instead of stopping at the first.</summary>
        public bool Pierce { get; set; }
        /// <summary>An enemy this shot steers toward while it lives (the dragon fist's lock-on).</summary>
        public IHittable Homing { get; set; }
        /// <summary>Called when it hits something (not for piercing shots): what, and where.</summary>
        public System.Action<IHittable, Vector3> OnHit { get; set; }
        readonly System.Collections.Generic.HashSet<IHittable> pierced = new System.Collections.Generic.HashSet<IHittable>();

        RepulsorGame game;
        Vector3 velocity;
        float radius;
        float age;
        float tailLength;
        float travelled;
        float damage;
        float wallAt = float.MaxValue;
        float flown;

        /// <summary>Where a real surface stops this shot, measured from where it was fired.</summary>
        public float WallDistance { set => wallAt = value; }

        public void Launch(RepulsorGame owner, Vector3 position, Vector3 direction, float charge, float size = 1f, float speed = 1f)
        {
            game = owner;
            damage = (owner != null ? owner.RepulsorDamage : global::Repulsor.Damage.Repulsor) * Mathf.Lerp(0.6f, 1f, charge);
            radius = Mathf.Lerp(MinRadius, MaxRadius, charge) * size;
            velocity = direction.normalized * (Mathf.Lerp(MinSpeed, MaxSpeed, charge) * speed);
            tailLength = Mathf.Lerp(MinTail, MaxTail, charge) * Mathf.Sqrt(size);
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(-velocity));
            head.localScale = Vector3.one * (radius * HeadOverRadius);
            ShowTail(0f);
        }

        /// <summary>The tail grows out of the palm as the shot leaves, so it never pokes out behind the hand.</summary>
        public void ShowTail(float distance)
        {
            travelled = distance;
            tail.localScale = new Vector3(radius * TailWidthOverRadius, 1f, Mathf.Max(Mathf.Min(travelled, tailLength), 0.001f));
        }

        void Update()
        {
            age += Time.deltaTime;
            bool homing = Hittables.IsLive(Homing);
            if (homing)
            {
                Vector3 toTarget = Homing.Center - transform.position;
                if (toTarget.sqrMagnitude > 1e-4f)
                {
                    velocity = Vector3.RotateTowards(velocity, toTarget.normalized * velocity.magnitude,
                        Aim.HomingDegrees * Mathf.Deg2Rad * Time.deltaTime, 0f);
                    transform.rotation = Quaternion.LookRotation(-velocity);
                }
            }
            Vector3 from = transform.position;
            Vector3 to = from + velocity * Time.deltaTime;
            transform.position = to;
            ShowTail(travelled + velocity.magnitude * Time.deltaTime);

            flown += velocity.magnitude * Time.deltaTime;
            // A piercing shot keeps flying after a hit; others stop at the first.
            bool hit;
            if (Pierce)
            {
                game.HitAllAlong(from, to, radius, damage, pierced);
                hit = false;
            }
            else
            {
                hit = game.TryHit(from, to, radius, damage, out IHittable struck);
                if (hit)
                    OnHit?.Invoke(struck, to);
            }
            // The wall was measured along the first heading, so a shot that has turned toward its target ignores it.
            bool wall = flown >= wallAt && !homing;
            if (hit || age > LifeSeconds || wall)
            {
                if (Aoe > 0f && (hit || wall))
                    game.Explode(to, Aoe, damage * Splash, Color);
                Destroy(gameObject);
            }
        }
    }
}
