using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// A ring perpendicular to the palm that travels along it and widens, as in the web prototype.
    /// Destroys every target the front reaches. A stronger charge travels further.
    /// </summary>
    public sealed class Shockwave : MonoBehaviour
    {
        public const float StartRadius = 0.12f;
        public const float HalfAngleDegrees = 28f;

        const float Speed = 5.5f;
        const float MinRange = 2f;
        const float MaxRange = 3.5f;
        const float StartWidth = 0.06f;
        const float EndWidth = 0.008f;

        [SerializeField] LineRenderer ring;

        RepulsorGame game;
        Vector3 origin;
        Vector3 direction;
        float range;
        float travelled;

        void Awake()
        {
            for (int i = 0; i < ring.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2f / ring.positionCount;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f));
            }
        }

        public void Launch(RepulsorGame owner, Vector3 position, Vector3 aim, float charge)
        {
            game = owner;
            origin = position;
            direction = aim.normalized;
            range = Mathf.Lerp(MinRange, MaxRange, charge);
            transform.rotation = Quaternion.LookRotation(direction);
            Place();
        }

        void Update()
        {
            travelled += Speed * Time.deltaTime;
            if (travelled >= range)
            {
                Destroy(gameObject);
                return;
            }

            Place();
            game.HitWithShockwave(origin, direction, travelled);
        }

        void Place()
        {
            float radius = StartRadius + travelled * Mathf.Tan(HalfAngleDegrees * Mathf.Deg2Rad);
            transform.position = origin + direction * travelled;
            transform.localScale = new Vector3(radius, radius, 1f);
            ring.widthMultiplier = Mathf.Lerp(StartWidth, EndWidth, travelled / range);
        }
    }
}
