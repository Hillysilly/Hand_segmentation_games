using System.Collections.Generic;
using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// A blast front that travels along the palm's direction and widens, as in the web prototype, drawn on a
    /// real section of cone. Hits everything the front reaches, once each. A stronger charge travels further.
    /// </summary>
    public sealed class Shockwave : MonoBehaviour
    {
        public const float StartRadius = 0.12f;
        public const float HalfAngleDegrees = 28f;

        const float Speed = 5.5f;
        const float MinRange = 2f;
        const float MaxRange = 3.5f;

        static readonly int Life = Shader.PropertyToID("_Life");

        [SerializeField] Transform front;
        [SerializeField] Renderer frontRenderer;

        readonly HashSet<IHittable> hit = new HashSet<IHittable>();
        MaterialPropertyBlock properties;
        RepulsorGame game;
        Vector3 origin;
        Vector3 direction;
        float range;
        float travelled;

        /// <summary>A real surface ahead stops the front there, so it can't hit through walls.</summary>
        public float WallDistance
        {
            set => range = Mathf.Min(range, value + 0.15f);
        }

        /// <summary>Drawn only (the other player's, replayed here): it hits nothing.</summary>
        public bool Harmless { get; set; }

        public void Launch(RepulsorGame owner, Vector3 position, Vector3 aim, float charge)
        {
            game = owner;
            origin = position;
            direction = aim.normalized;
            range = Mathf.Lerp(MinRange, MaxRange, charge);
            transform.rotation = Quaternion.LookRotation(direction);
            ShowAt(0f);
        }

        void Update()
        {
            float distance = travelled + Speed * Time.deltaTime;
            if (distance >= range)
            {
                Destroy(gameObject);
                return;
            }

            ShowAt(distance);
            if (!Harmless)
                game.HitWithShockwave(origin, direction, travelled, hit);
        }

        /// <summary>Puts the front this far along. Public so the editor preview can pick a moment.</summary>
        public void ShowAt(float distance)
        {
            travelled = distance;
            float radius = StartRadius + travelled * Mathf.Tan(HalfAngleDegrees * Mathf.Deg2Rad);
            transform.position = origin + direction * travelled;
            front.localScale = Vector3.one * radius;

            properties ??= new MaterialPropertyBlock();
            properties.SetFloat(Life, travelled / range);
            frontRenderer.SetPropertyBlock(properties);
        }
    }
}
