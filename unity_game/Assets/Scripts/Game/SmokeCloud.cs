using System.Collections.Generic;
using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// The Archer's smoke bomb: thrown underhand, it bursts where it lands into a grey cloud about 2.5 m across
    /// that lasts 8 seconds. Enemies can't shoot from inside it or through it. That's PVP's; against the robots
    /// (11/16) it's a poison bomb instead: a green cloud that poisons each enemy that's in it, once
    /// (<see cref="Poison"/>), and doesn't stop shots.
    /// </summary>
    public sealed class SmokeCloud : MonoBehaviour
    {
        const float Radius = 1.25f;
        const float LifeSeconds = 8f;
        const float Gravity = 9f;
        const int Puffs = 7;

        static readonly List<SmokeCloud> active = new List<SmokeCloud>();
        static readonly Color Grey = new Color(0.55f, 0.56f, 0.6f);
        static readonly Color Green = new Color(0.35f, 0.95f, 0.25f);

        RepulsorGame game;
        float poisonPerSecond;
        readonly HashSet<IHittable> poisoned = new HashSet<IHittable>();
        bool Poisonous => poisonLook;
        bool poisonLook;
        Color Tone => poisonLook ? Green : Grey;

        readonly Bubble[] puffs = new Bubble[Puffs];
        readonly Vector3[] offsets = new Vector3[Puffs];
        Bubble shell;
        Vector3 velocity;
        float age;
        bool burst;

        /// <summary>
        /// A poison bomb (<paramref name="poisonLook"/>): green; and with a <paramref name="game"/> and damage, it
        /// poisons enemies (the other player's replayed one only looks it).
        /// </summary>
        public static SmokeCloud Throw(Vector3 from, Vector3 handVelocity, Transform parent, bool poisonLook,
            RepulsorGame game = null, float poisonPerSecond = 0f)
        {
            SmokeCloud cloud = Throw(from, handVelocity, parent);
            cloud.poisonLook = poisonLook;
            cloud.game = game;
            cloud.poisonPerSecond = poisonPerSecond;
            return cloud;
        }

        public static SmokeCloud Throw(Vector3 from, Vector3 handVelocity, Transform parent)
        {
            var go = new GameObject("Smoke Bomb");
            go.transform.SetParent(parent, false);
            go.transform.position = from;
            var cloud = go.AddComponent<SmokeCloud>();
            cloud.velocity = handVelocity * 1.6f + Vector3.up * 1.5f;
            cloud.shell = Bubble.Create("Bomb", go.transform);
            cloud.shell.transform.localScale = Vector3.one * 0.08f;
            return cloud;
        }

        /// <summary>True if smoke sits on the line between two points, or around the first.</summary>
        public static bool Blocks(Vector3 from, Vector3 to)
        {
            foreach (SmokeCloud cloud in active)
            {
                if (!cloud.burst || cloud.Poisonous)
                    continue;
                Vector3 centre = cloud.transform.position;
                if (GameMath.SegmentHitsSphere(from, to, centre, Radius))
                    return true;
            }
            return false;
        }

        void OnEnable() => active.Add(this);
        void OnDisable() => active.Remove(this);

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            if (!burst)
            {
                velocity += Vector3.down * (Gravity * dt);
                transform.position += velocity * dt;
                shell.Show(Tone, BubbleStyle.Hex, 1f);
                if (transform.position.y <= 0.1f || age > 2f)
                    Burst();
                return;
            }

            float t = age / LifeSeconds;
            float grow = Mathf.Clamp01(age * 2.5f);
            float fade = Mathf.Clamp01((1f - t) * 4f);
            for (int i = 0; i < Puffs; i++)
            {
                Vector3 drift = new Vector3(Mathf.Sin(age * 0.4f + i), Mathf.Sin(age * 0.3f + i * 2f) * 0.3f, Mathf.Cos(age * 0.35f + i)) * 0.1f;
                puffs[i].transform.localPosition = offsets[i] * grow + drift;
                puffs[i].transform.localScale = Vector3.one * (Radius * (0.9f + 0.25f * Mathf.Sin(i * 1.7f)) * grow);
                puffs[i].Show(Tone, BubbleStyle.Smoke, 0.75f * fade);
            }
            if (game != null && poisonPerSecond > 0f)
                PoisonWhoIsIn();
            if (t >= 1f)
                Destroy(gameObject);
        }

        /// <summary>Every enemy in the cloud gets poisoned, once per cloud (and never more than one poison at a time).</summary>
        void PoisonWhoIsIn()
        {
            Vector3 centre = transform.position;
            var all = Hittables.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (i >= all.Count)
                    continue;
                IHittable target = all[i];
                if (!target.Alive || target.Team != Team.Enemies || poisoned.Contains(target))
                    continue;
                if ((target.Center - centre).sqrMagnitude > (Radius + target.Radius) * (Radius + target.Radius))
                    continue;
                if (!(target is Component body) || body == null)
                    continue;
                poisoned.Add(target);
                Poison.Apply(game, target, body.gameObject, poisonPerSecond, Balance.PoisonSeconds);
            }
        }

        void Burst()
        {
            burst = true;
            // The bomb popping open (11/17).
            var puff = new GameObject("Puff Sound").AddComponent<AudioSource>();
            puff.transform.position = transform.position;
            puff.spatialBlend = 1f;
            puff.minDistance = 0.6f;
            puff.dopplerLevel = 0f;
            puff.PlayOneShot(Arena.PuffSfx);
            Destroy(puff.gameObject, 1f);
            age = 0f;
            Destroy(shell.gameObject);
            transform.position = new Vector3(transform.position.x, Radius * 0.7f, transform.position.z);
            for (int i = 0; i < Puffs; i++)
            {
                puffs[i] = Bubble.Create("Puff", transform);
                offsets[i] = i == 0 ? Vector3.zero : Random.insideUnitSphere * Radius * 0.6f;
            }
        }
    }
}
