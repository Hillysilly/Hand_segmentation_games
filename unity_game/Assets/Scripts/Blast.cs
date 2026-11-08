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
        /// <summary>Radius of the explosion where it lands, 0 for none.</summary>
        public float Aoe { get; set; }
        public Color Color { get; set; }
        public float Damage { get => damage; set => damage = value; }

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
            Vector3 from = transform.position;
            Vector3 to = from + velocity * Time.deltaTime;
            transform.position = to;
            ShowTail(travelled + velocity.magnitude * Time.deltaTime);

            flown += velocity.magnitude * Time.deltaTime;
            bool hit = game.TryHit(from, to, radius, damage);
            if (hit || age > LifeSeconds || flown >= wallAt)
            {
                if (Aoe > 0f && (hit || flown >= wallAt))
                    game.Explode(to, Aoe, damage * 0.6f, Color);
                Destroy(gameObject);
            }
        }
    }
}
