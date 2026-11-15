using System.Collections.Generic;
using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// The boss's fight, as a fixed routine that loops:
    /// 1. walks in for a moment;
    /// 2. LASER FAN: stops, rears up and fires a fan of seven lasers, then a second fan into the gaps of the first;
    ///    each shows its aim lines for a second, so you step into a gap;
    /// 3. TRACKING STRIKES: a red ring on the floor chases where you were a moment ago; every second or so a strike
    ///    lands on it, so you have to keep moving;
    /// 4. MISSILE SALVO: four missiles from its back (shoot them down, or let them hit its friends);
    /// 5. below 60% health it calls in two helpers between rounds.
    /// Below a third of its health it rests less and its fans come in three waves.
    /// Lines tagged [Boss] go to the headset log.
    /// </summary>
    /// <summary>A boss's own routine, run in place of the usual enemy behaviour.</summary>
    public interface IBossBrain
    {
        void Tick(float dt, Vector3 target, Vector3 toward, float distance);
        /// <summary>It died or was cleared: take away anything it left in the room.</summary>
        void Stop();
    }

    public sealed class BossBrain : IBossBrain
    {
        enum Move { Approach, Fan, Track, Missiles, Summon, Rest }

        const int FanBeams = 7;
        const float FanDegrees = 84f;
        const float StrikeEvery = 1.1f;
        const int Strikes = 5;
        const float TrailSeconds = 0.7f;
        const float RingSpeed = 1.8f;

        readonly Enemy body;
        readonly Arena arena;
        readonly Queue<Vector3> trail = new Queue<Vector3>();
        readonly Queue<float> trailTimes = new Queue<float>();
        Move move = Move.Approach;
        Move next = Move.Fan;
        float timer = 2f;
        float clock;
        int count;
        Vector3 ring;
        LineRenderer ringLine;
        bool summoned;

        public BossBrain(Enemy body)
        {
            this.body = body;
            arena = body.Arena;
        }

        bool Enraged => body.Hp < body.MaxHp / 3f;
        float Rest => Enraged ? 0.9f : 1.6f;

        /// <summary>Its cannons: high on its back, in front.</summary>
        Vector3 Cannon(float side) =>
            body.Center + Vector3.up * (body.Spec.Height * 0.25f) + body.transform.forward * (body.Radius * 0.8f) + body.transform.right * side;

        public void Tick(float dt, Vector3 target, Vector3 toward, float distance)
        {
            clock += dt;
            RememberWhereYouWere(target);
            timer -= dt;
            switch (move)
            {
                case Move.Approach:
                    body.Walk(dt, distance > body.Spec.Range ? toward : distance < 1.8f ? -toward : Vector3.zero, body.Spec.Speed);
                    body.Face(target, dt, 2f);
                    if (timer <= 0f)
                        Begin(next);
                    break;
                case Move.Rest:
                    body.Walk(dt, distance > body.Spec.Range + 0.5f ? toward : Vector3.zero, body.Spec.Speed * 0.6f);
                    body.Face(target, dt, 2f);
                    if (timer <= 0f)
                        Begin(next);
                    break;
                case Move.Fan:
                    body.Walk(dt, Vector3.zero, 0f);
                    body.Face(target, dt, 3f);
                    TickFan(target);
                    break;
                case Move.Track:
                    body.Walk(dt, distance > body.Spec.Range + 0.8f ? toward : Vector3.zero, body.Spec.Speed * 0.5f);
                    body.Face(target, dt, 2f);
                    TickTrack(dt);
                    break;
                case Move.Missiles:
                    body.Walk(dt, Vector3.zero, 0f);
                    body.Face(target, dt, 3f);
                    TickMissiles(target);
                    break;
                case Move.Summon:
                    body.Walk(dt, Vector3.zero, 0f);
                    if (timer <= 0f)
                    {
                        arena.SpawnBossHelpers(body, 2);
                        RestThen(Move.Fan);
                    }
                    break;
            }
        }

        void Begin(Move what)
        {
            move = what;
            count = 0;
            switch (what)
            {
                case Move.Fan:
                    timer = 0f;
                    body.Look?.PlayOnce("attack");
                    break;
                case Move.Track:
                    timer = StrikeEvery;
                    ring = Floor(arena.NearestPlayerHead(body.Center));
                    ShowRing(true);
                    break;
                case Move.Missiles:
                    timer = 0.4f;
                    break;
                case Move.Summon:
                    timer = 1.2f;
                    body.Look?.PlayOnce("hit");
                    break;
            }
            Debug.Log($"[Boss] {what}, HP {body.Hp:0}/{body.MaxHp:0}");
        }

        void RestThen(Move after)
        {
            move = Move.Rest;
            timer = Rest;
            next = after;
            ShowRing(false);
        }

        /// <summary>Fans of lasers: each fan's beams spread evenly; the next fan aims between them.</summary>
        void TickFan(Vector3 target)
        {
            int fans = Enraged ? 3 : 2;
            if (timer > 0f)
                return;
            if (count >= fans)
            {
                RestThen(Move.Track);
                return;
            }
            Vector3 muzzle = Cannon(0f);
            Vector3 aim = (target + Vector3.down * 0.3f - muzzle).normalized;
            float gap = FanDegrees / (FanBeams - 1);
            float offset = count % 2 == 1 ? gap * 0.5f : 0f;
            for (int i = 0; i < FanBeams; i++)
            {
                float yaw = -FanDegrees * 0.5f + i * gap + offset;
                arena.EnemyFires(EnemyShot.BossLaser, muzzle, Quaternion.AngleAxis(yaw, Vector3.up) * aim, body);
            }
            count++;
            timer = 1.25f;
        }

        void TickTrack(float dt)
        {
            // The ring chases where you were a moment ago, no faster than a brisk walk.
            Vector3 goal = trail.Count > 0 ? Floor(trail.Peek()) : ring;
            ring = Vector3.MoveTowards(ring, goal, RingSpeed * dt);
            DrawRing(ring, 0.5f + 0.08f * Mathf.Sin(clock * 12f));
            if (timer > 0f)
                return;
            arena.EnemyFires(EnemyShot.Strike, ring, Vector3.down, body);
            count++;
            timer = Enraged ? StrikeEvery * 0.8f : StrikeEvery;
            if (count >= Strikes)
                RestThen(Move.Missiles);
        }

        void TickMissiles(Vector3 target)
        {
            if (timer > 0f)
                return;
            if (count >= 4)
            {
                RestThen(body.Hp < body.MaxHp * 0.6f && !summoned ? Move.Summon : Move.Approach);
                if (next == Move.Summon)
                    summoned = true;
                else
                {
                    summoned = false;
                    next = Move.Fan;
                }
                return;
            }
            Vector3 spread = new Vector3(Random.Range(-0.7f, 0.7f), 0f, Random.Range(-0.7f, 0.7f));
            arena.EnemyFires(EnemyShot.Missile, Cannon(count % 2 == 0 ? -0.4f : 0.4f) + Vector3.up * 0.2f, target + Vector3.down * 0.4f + spread, body);
            body.Look?.PlayOnce("shoot");
            count++;
            timer = 0.35f;
        }

        void RememberWhereYouWere(Vector3 target)
        {
            trail.Enqueue(target);
            trailTimes.Enqueue(clock);
            while (trailTimes.Count > 1 && clock - trailTimes.Peek() > TrailSeconds)
            {
                trail.Dequeue();
                trailTimes.Dequeue();
            }
        }

        Vector3 Floor(Vector3 point) => new Vector3(point.x, arena.FloorY + 0.02f, point.z);

        void ShowRing(bool on)
        {
            if (ringLine == null)
            {
                if (!on)
                    return;
                var go = new GameObject("Boss Tracking Ring", typeof(LineRenderer));
                go.transform.SetParent(body.transform.parent, false);
                ringLine = go.GetComponent<LineRenderer>();
                ringLine.sharedMaterial = Resources.Load<Material>("AimLine");
                ringLine.positionCount = 33;
                ringLine.loop = true;
                ringLine.useWorldSpace = true;
                ringLine.widthMultiplier = 0.025f;
                Tint.Line(ringLine, new Color(1f, 0.15f, 0.08f));
                ringLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            ringLine.enabled = on;
        }

        void DrawRing(Vector3 centre, float radius)
        {
            if (ringLine == null)
                return;
            for (int i = 0; i < 33; i++)
            {
                float angle = i / 32f * Mathf.PI * 2f;
                ringLine.SetPosition(i, centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
            }
        }

        /// <summary>It died or was cleared: take the ring away.</summary>
        public void Stop()
        {
            if (ringLine != null)
                Object.Destroy(ringLine.gameObject);
        }
    }

    /// <summary>
    /// One of the boss's tracking strikes: a ring on the floor closes in for half a second, then a red beam slams down
    /// from above and bursts. Hurts you if you're still standing in it. While you stand inside the ring your view
    /// turns slightly red (you can't see a ring under your own feet), stronger as it's about to land.
    /// </summary>
    public sealed class TrackingStrike : MonoBehaviour
    {
        public const float Warning = 0.55f;
        public const float Radius = 0.6f;
        public const float Damage = 9f;
        static readonly Color Red = new Color(1f, 0.12f, 0.06f);

        Arena arena;
        float age;
        bool landed;
        LineRenderer ring;
        LaserBeamView beam;

        public static void Drop(Arena arena, Vector3 floorPoint, Transform parent)
        {
            var go = new GameObject("Tracking Strike", typeof(LineRenderer));
            go.transform.SetParent(parent, false);
            go.transform.position = floorPoint;
            var strike = go.AddComponent<TrackingStrike>();
            strike.arena = arena;
            strike.ring = go.GetComponent<LineRenderer>();
            strike.ring.sharedMaterial = Resources.Load<Material>("AimLine");
            strike.ring.positionCount = 33;
            strike.ring.loop = true;
            strike.ring.useWorldSpace = true;
            strike.ring.widthMultiplier = 0.03f;
            strike.ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            strike.Update();
        }

        void Update()
        {
            age += Time.deltaTime;
            Vector3 centre = transform.position;
            if (age < Warning)
            {
                float t = age / Warning;
                float radius = Mathf.Lerp(Radius * 1.6f, Radius, t);
                Tint.Line(ring, Color.Lerp(Red * 0.6f, new Color(1f, 0.6f, 0.4f), t * (0.7f + 0.3f * Mathf.Sin(age * 40f))));
                for (int i = 0; i < 33; i++)
                {
                    float angle = i / 32f * Mathf.PI * 2f;
                    ring.SetPosition(i, centre + new Vector3(Mathf.Cos(angle), 0.01f, Mathf.Sin(angle)) * radius);
                }
                if (arena != null && arena.PlayerStandsWithin(centre, radius))
                    arena.WarnPlayer(Mathf.Lerp(0.2f, 0.45f, t));
                return;
            }
            if (!landed)
            {
                landed = true;
                ring.enabled = false;
                if (arena != null)
                {
                    beam = arena.Game.MakeLaserBeam(transform, Red);
                    beam.Width = 2.2f;
                    arena.Game.Explode(centre + Vector3.up * 0.2f, 0f, 0f, Red);
                    if (arena.PlayerStandsWithin(centre, Radius))
                        arena.HurtPlayer(Damage, "boss strike");
                }
            }
            if (age < Warning + 0.25f)
                beam?.Aim(centre + Vector3.up * 3f, centre, true, Vector3.up);
            if (age > Warning + 0.7f)
                Destroy(gameObject);
        }
    }
}
