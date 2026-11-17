using UnityEngine;

namespace Repulsor
{
    /// <summary>How an enemy attack starts. Sent to the other headset as a byte, so only add to the end.</summary>
    public enum EnemyShot : byte { Laser, Sniper, Missile, Strike, BossLaser, QuickLaser }

    /// <summary>
    /// An enemy laser, drawn like the player's own hand laser: a thin pulsing red aim line while it charges (it
    /// shows exactly where the beam will go, so you can step out of it), then the full beam for a moment. The aim
    /// can follow you for the first part of the charge (snipers), then locks. Hits the player's body once; a raised
    /// shield stops it short and takes the hit. Walls stop it too.
    /// </summary>
    public sealed class EnemyLaser : MonoBehaviour
    {
        public const float BeamSeconds = 0.3f;
        static readonly Color Red = new Color(1f, 0.08f, 0.05f);

        Arena arena;
        Transform muzzleFollow;
        Vector3 muzzle;
        Vector3 direction;
        float charge;
        float track;
        float damage;
        float width;
        float age;
        float length;
        bool hitDone;
        LineRenderer aimLine;
        FxSpin glow;
        LaserBeamView beam;

        /// <param name="follow">Moves the start with the shooter while charging (its gun), if given.</param>
        /// <param name="track">Seconds of the charge during which the aim follows the nearest player.</param>
        public static EnemyLaser Fire(Arena arena, Vector3 muzzle, Vector3 direction, float charge, float damage, float width,
            Transform parent, Transform follow = null, float track = 0f)
        {
            var go = new GameObject("Enemy Laser");
            go.transform.SetParent(parent, false);
            var laser = go.AddComponent<EnemyLaser>();
            laser.arena = arena;
            laser.muzzle = muzzle;
            laser.muzzleFollow = follow;
            laser.direction = direction.normalized;
            // Difficulty (11/16): the aim line shows for longer on easier settings; the tracking part too.
            laser.charge = charge * Arena.TellScale;
            laser.track = track * Arena.TellScale;
            laser.damage = damage;
            laser.width = width;

            laser.aimLine = go.AddComponent<LineRenderer>();
            laser.aimLine.sharedMaterial = Resources.Load<Material>("AimLine");
            laser.aimLine.positionCount = 2;
            laser.aimLine.useWorldSpace = true;
            laser.aimLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            laser.aimLine.receiveShadows = false;
            Tint.Line(laser.aimLine, Red);
            laser.glow = BallLook.Sprite("Charge Glow", go.transform, "FxGlow", 0.05f, Red * 1.5f).Set(billboard: true, flicker: 0.25f);
            laser.Update();
            return laser;
        }

        Vector3 Muzzle => muzzleFollow != null ? muzzleFollow.position : muzzle;

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            if (age < track && arena != null)
            {
                Vector3 target = arena.NearestPlayerChest(Muzzle);
                direction = Vector3.Slerp(direction, (target - Muzzle).normalized, 1f - Mathf.Exp(-6f * dt));
            }
            // Walls stop it; so does a raised shield.
            Vector3 start = Muzzle;
            length = arena != null && arena.Game.Room.TryDistance(start, direction, out float wall) ? wall : 12f;
            if (arena != null)
                length = arena.ShieldStop(start, direction, length);
            Vector3 end = start + direction * length;

            if (age < charge)
            {
                float t = age / charge;
                aimLine.enabled = true;
                aimLine.SetPosition(0, start);
                aimLine.SetPosition(1, end);
                // Thin and flickering at first, steadier and brighter as it's about to fire.
                float pulse = 0.5f + 0.5f * Mathf.Sin(age * Mathf.Lerp(18f, 50f, t));
                aimLine.widthMultiplier = Mathf.Lerp(0.004f, 0.012f, t) * (0.7f + 0.3f * pulse) * Mathf.Max(1f, width);
                Tint.Line(aimLine, Red * Mathf.Lerp(0.6f, 1.6f, t));
                glow.transform.position = start;
                glow.transform.localScale = Vector3.one * Mathf.Lerp(0.04f, 0.16f, t) * Mathf.Max(1f, width * 0.8f);
                return;
            }

            aimLine.enabled = false;
            glow.gameObject.SetActive(false);
            if (beam == null && arena != null)
            {
                beam = arena.Game.MakeLaserBeam(transform, Red);
                beam.Width = 0.45f * width;
                arena.Game.PlayAt(start, arena.Game.ZapSound);
            }
            if (age < charge + BeamSeconds)
            {
                beam?.Aim(start, end, true, -direction);
                if (!hitDone && arena != null)
                {
                    hitDone = true;
                    // A little slimmer than the beam looks (11/14: a touch easier to dodge): 5.5 cm either side for a gunner's.
                    arena.LaserHitsPlayer(start, end, 0.035f * width + 0.02f, damage);
                }
                return;
            }
            // Let the beam power down, then go.
            if (age > charge + BeamSeconds + 0.4f)
                Destroy(gameObject);
        }

        /// <summary>The shooter died while charging: no shot.</summary>
        public void Cancel()
        {
            if (age < charge)
                Destroy(gameObject);
        }
    }

    /// <summary>
    /// A rocketeer's or the boss's missile: launched up and out, then curving onto where you were, homing a little.
    /// It blows up on you, a wall, the floor or a robot, hurting everything near it, robots included. It can be shot
    /// down (it counts as an enemy for your attacks), which sets it off where it is.
    /// </summary>
    public sealed class Missile : MonoBehaviour, IHittable
    {
        public const float BlastRadius = 1.1f;
        public const float PlayerDamage = 10f;
        public const float RobotDamage = 20f;
        const float Speed = 2.6f;
        const float TurnDegrees = 70f;
        const float Life = 7f;
        static readonly Color Orange = new Color(1f, 0.45f, 0.1f);

        Arena arena;
        Vector3 velocity;
        Vector3 target;
        float age;
        bool exploded;
        IHittable launcher;

        public Vector3 Center => transform.position;
        public float Radius => 0.14f;
        public bool Alive => !exploded;
        public Team Team => Team.Enemies;
        public float Hp => exploded ? 0f : 1f;
        public float MaxHp => 1f;
        public Vector3 BarPoint => transform.position + Vector3.up * 0.2f;

        void OnEnable() => Hittables.Add(this);
        void OnDisable() => Hittables.Remove(this);

        public static Missile Launch(Arena arena, Vector3 from, Vector3 target, Transform parent, IHittable launcher = null)
        {
            var go = new GameObject("Missile", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.position = from;
            go.GetComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>("SphereMesh");
            go.GetComponent<MeshRenderer>().sharedMaterial = Resources.Load<Material>("EnemyPlaceholder");
            go.transform.localScale = new Vector3(0.07f, 0.07f, 0.22f);
            var missile = go.AddComponent<Missile>();
            missile.arena = arena;
            missile.target = target;
            missile.launcher = launcher;
            // Up and out first, so it arcs and you can see it coming.
            Vector3 flat = Vector3.ProjectOnPlane(target - from, Vector3.up).normalized;
            missile.velocity = (flat + Vector3.up * 1.2f).normalized * Speed;
            go.transform.rotation = Quaternion.LookRotation(missile.velocity);

            FxSpin flame = BallLook.Sprite("Exhaust", go.transform, "FxGlow", 2.2f, Orange * 2f).Set(billboard: true, flicker: 0.4f);
            flame.transform.localPosition = new Vector3(0f, 0f, -0.6f);
            ParticleSystem smoke = BallLook.Particles("Smoke", go.transform, "FxGlow", new Color(1f, 0.5f, 0.2f, 0.8f),
                new Color(0.3f, 0.3f, 0.3f, 0f), 0.08f, 0.6f, 40f, 0.05f, 0.02f);
            smoke.transform.localPosition = new Vector3(0f, 0f, -0.6f);
            return missile;
        }

        public float TakeHit(float damage, Vector3 from)
        {
            if (exploded || age < 0.15f)
                return 0f;
            Explode(transform.position, "shot down");
            return 1f;
        }

        void Update()
        {
            if (exploded)
                return;
            float dt = Time.deltaTime;
            age += dt;
            // Homes in a little on whoever is nearest, after the climb.
            if (age > 0.45f && arena != null)
                target = Vector3.Lerp(target, arena.NearestPlayerChest(transform.position), 1f - Mathf.Exp(-0.6f * dt));
            Vector3 want = (target - transform.position).normalized * Speed;
            if (age > 0.35f)
                velocity = Vector3.RotateTowards(velocity, want, TurnDegrees * Mathf.Deg2Rad * dt, 0f);
            Vector3 from = transform.position;
            Vector3 to = from + velocity * dt;
            transform.position = to;
            if (velocity.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(velocity);

            if (arena == null)
                return;
            if (arena.SegmentHitsPlayer(from, to, Radius) || age > Life)
            {
                Explode(to, "reached you");
                return;
            }
            if (to.y < arena.FloorY + 0.05f)
            {
                Explode(to, "hit the floor");
                return;
            }
            if (age > 0.3f && arena.Game.Room.TryDistance(from, velocity.normalized, out float wall) && wall < (to - from).magnitude + 0.05f)
            {
                Explode(to, "hit a wall");
                return;
            }
            // Robots in its way set it off (not the one that fired it, while it's leaving).
            foreach (IHittable other in Hittables.All)
            {
                if (other == (IHittable)this || !(other is Enemy enemy) || !enemy.Alive || (other == launcher && age < 1f))
                    continue;
                if (HitTest.Segment(enemy, from, to, Radius))
                {
                    Explode(to, $"hit a {enemy.Kind}");
                    return;
                }
            }
        }

        void Explode(Vector3 at, string why)
        {
            if (exploded)
                return;
            exploded = true;
            Debug.Log($"[Enemy] Missile {why}");
            if (arena != null)
            {
                // Robots near it take the blast too.
                arena.Game.Explode(at, BlastRadius, RobotDamage, Orange);
                if (arena.SphereHitsPlayer(at, BlastRadius))
                    arena.HurtPlayer(PlayerDamage, "missile blast");
            }
            Destroy(gameObject);
        }
    }
}
