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

        RepulsorGame game;
        Vector3 velocity;
        float radius;
        float age;
        float tailLength;
        float travelled;

        public void Launch(RepulsorGame owner, Vector3 position, Vector3 direction, float charge)
        {
            game = owner;
            radius = Mathf.Lerp(MinRadius, MaxRadius, charge);
            velocity = direction.normalized * Mathf.Lerp(MinSpeed, MaxSpeed, charge);
            tailLength = Mathf.Lerp(MinTail, MaxTail, charge);
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

            if (game.TryHit(from, to, radius) || age > LifeSeconds)
                Destroy(gameObject);
        }
    }
}
