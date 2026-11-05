using UnityEngine;

namespace Repulsor
{
    /// <summary>A repulsor shot. Flies straight; a stronger charge is bigger and faster.</summary>
    public sealed class Blast : MonoBehaviour
    {
        const float MinSpeed = 7f;
        const float MaxSpeed = 12f;
        const float MinRadius = 0.07f;
        const float MaxRadius = 0.13f;
        const float LifeSeconds = 2f;

        RepulsorGame game;
        Vector3 velocity;
        float radius;
        float age;

        public void Launch(RepulsorGame owner, Vector3 position, Vector3 direction, float charge)
        {
            game = owner;
            radius = Mathf.Lerp(MinRadius, MaxRadius, charge);
            velocity = direction.normalized * Mathf.Lerp(MinSpeed, MaxSpeed, charge);
            transform.position = position;
            transform.localScale = Vector3.one * (radius * 2f);
        }

        void Update()
        {
            age += Time.deltaTime;
            Vector3 from = transform.position;
            Vector3 to = from + velocity * Time.deltaTime;
            transform.position = to;

            if (game.TryHit(from, to, radius) || age > LifeSeconds)
                Destroy(gameObject);
        }
    }
}
