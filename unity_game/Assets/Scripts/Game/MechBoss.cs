using System.Collections.Generic;
using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// GOLIATH, the story's round 10 boss: a big mech with a readable routine.
    /// - SWING (when you're close): it winds up for most of a second while the reach of the swing shows red on the
    ///   floor, then sweeps it. Step back out of the red, or hold up your shield. Two swings, three when enraged.
    /// - ARM LASERS: volleys from each arm in turn, with the usual aim lines to step out of.
    /// - SHOULDER ROCKETS: a salvo of missiles that arc in (shoot them down; they also hurt its friends).
    /// - JUMP SLAM: it crouches, leaps at where you're standing (a red ring marks the landing; your view tints red
    ///   while you're in it), lands hard there, and a shockwave races out across the floor. Get out of the ring
    ///   before it lands; jump (for real) as the wave passes, or block it with your shield.
    /// Below 40% health it's enraged: shorter rests, more swings and rockets. Below 60% it calls in two helpers once.
    /// The model ("The Morser 16" by anonDutch, CC BY 4.0) brings its walk; the swing of its blade arm (right) and the
    /// aim and kick of its gun arm (left) are animated here, on its bones, after the walk each frame.
    /// Lines tagged [Mech] go to the headset log.
    /// Co-op (11/15): the host runs it and tells the client each move as it begins (<see cref="Replay"/>); the client
    /// plays the same move on its copy (wind-up, floor marks, sweep, jump arc, shockwave) and checks the hits on its
    /// own player. Each headset only ever hurts its own player.
    /// </summary>
    public sealed class MechBrain : IBossBrain
    {
        enum Move { Approach, Swing, Lasers, Rockets, Jump, Summon, Rest }

        public const float SwingReach = 2.6f;
        public const float SwingDegrees = 130f;
        // 11/16 playtest: 1.0 (was 0.8), so 1.5 s on normal and 2 s on easy; it felt like half a second.
        public const float SwingWindUpBase = 1.0f;
        /// <summary>The swing's wind-up, longer on easier settings (Arena.TellScale).</summary>
        public static float SwingWindUp => SwingWindUpBase * Arena.TellScale;
        public const float SwingDamage = 16f;
        public const float SlamRadius = 1.3f;
        public const float SlamDamage = 20f;
        public const float WaveRange = 4.5f;
        public const float WaveSpeed = 5f;
        public const float WaveDamage = 9f;
        /// <summary>Your head this much above where it usually is counts as jumping over the shockwave.</summary>
        public const float JumpClearance = 0.1f;
        // The jump (11/16: higher and slower, it was too hard to get away from): a crouch that tells it's coming,
        // then the time in the air, when you can move out of the landing ring.
        static float CrouchSeconds => 0.55f * Arena.TellScale;
        const float AirSeconds = 1.6f;
        const float JumpHeight = 2.6f;
        static readonly Color Red = new Color(1f, 0.15f, 0.08f);

        readonly Enemy body;
        readonly Arena arena;
        Move move = Move.Approach;
        Move next = Move.Approach;
        float timer = 2.5f;
        int count;
        int rotation;
        bool summoned;
        bool justSwung;
        Vector3 jumpFrom, jumpTo;
        FloorMark marker;
        readonly List<Shock> waves = new List<Shock>();

        sealed class Shock
        {
            public Vector3 Centre;
            public float Radius;
            public bool Done;
            public LineRenderer Line;
            public Bubble Disc;
        }

        /// <summary>For tests: what it's doing.</summary>
        public string MoveName => move.ToString();

        /// <summary>The move number of a swing, whose first point is a direction (its facing) rather than a place.</summary>
        public const byte SwingMove = (byte)Move.Swing;

        /// <summary>Host: tells the client's copy that a move began.</summary>
        void Tell(Move what, Vector3 a = default, Vector3 b = default, float f = 0f)
        {
            if (!body.IsProxy)
                arena.SendEnemyMove(body, (byte)what, a, b, f, what == Move.Swing);
        }

        /// <summary>Client: the host's copy began this move; play it here.</summary>
        public void Replay(byte what, Vector3 a, Vector3 b, float f)
        {
            var m = (Move)what;
            ShowMarker(false);
            count = 0;
            switch (m)
            {
                case Move.Swing:
                    move = Move.Swing;
                    swingFacing = a.sqrMagnitude > 0.5f ? a.normalized : body.transform.forward;
                    timer = windUp = Mathf.Max(0.1f, f);
                    body.Look?.PlayOnceOver("attack", windUp + 0.2f);
                    arena.Game.PlayAt(body.Center, arena.Game.WhooshSound);
                    break;
                case Move.Jump:
                    move = Move.Jump;
                    timer = CrouchSeconds;
                    jumpFrom = a;
                    jumpTo = b;
                    body.Look?.PlayOnce("hit", 0.7f);
                    break;
                case Move.Rockets:
                    move = m;
                    body.Look?.PlayOnce("shoot");
                    break;
                case Move.Summon:
                    move = m;
                    body.Look?.PlayOnce("hit");
                    break;
                default:
                    move = m;
                    break;
            }
        }

        /// <summary>Client, every frame: the copy plays its current move (it moves only for the jump).</summary>
        public void TickProxy(float dt)
        {
            timer -= dt;
            TickWaves(dt);
            Vector3 me = arena.LocalHead;
            if (move == Move.Swing)
                TickSwing(dt, me, true);
            else if (move == Move.Jump)
                TickJump(dt, me, true);
        }

        public MechBrain(Enemy body)
        {
            this.body = body;
            arena = body.Arena;
            body.gameObject.AddComponent<ShowcasePoser>().Late = Pose;
        }

        // ------------------------------------------------------------------ the model's arms, posed over its walk

        Transform bladeArm, gunArm, gunMuzzle;
        bool bonesFound;
        float aim;
        float recoil;
        float sweepAge = -1f;

        void FindBones()
        {
            bonesFound = true;
            if (body.Look == null)
                return;
            foreach (Transform t in body.Look.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Upperarm_Right_Mech_Rig")
                    bladeArm = t;
                else if (t.name == "Upperarm_Left_Mech_Rig")
                    gunArm = t;
                else if (t.name == "Gun_FX_Mech_Rig")
                    gunMuzzle = t;
            }
        }

        /// <summary>Where the gun arm's shots come from: its barrel, or beside the chest if the model has none.</summary>
        Vector3 GunMuzzle => gunMuzzle != null ? gunMuzzle.position : Arm(-1f);

        void Pose()
        {
            if (!bonesFound)
                FindBones();
            if (!body.Alive)
                return;
            float dt = Time.deltaTime;
            Vector3 up = body.transform.up, right = body.transform.right;
            Vector3 target = arena.NearestPlayerHead(body.Center);

            // Blade arm: drawn back and up through the wind-up, swept across at the strike, then back to the walk.
            if (bladeArm != null)
            {
                float yaw = 0f, lift = 0f;
                if (move == Move.Swing && timer > 0f)
                {
                    float t = Mathf.SmoothStep(0f, 1f, 1f - timer / (SwingWindUp * (Enraged ? 0.8f : 1f)));
                    yaw = 75f * t;
                    lift = 35f * t;
                    sweepAge = -1f;
                }
                else if (sweepAge >= 0f)
                {
                    sweepAge += dt;
                    float strike = Mathf.Clamp01(sweepAge / 0.18f);
                    float back = Mathf.Clamp01((sweepAge - 0.18f) / 0.45f);
                    yaw = Mathf.Lerp(75f, -80f, strike) * (1f - back);
                    lift = 35f * (1f - strike) * (1f - back) + 10f * strike * (1f - back);
                    if (back >= 1f)
                        sweepAge = -1f;
                }
                if (yaw != 0f || lift != 0f)
                    bladeArm.rotation = Quaternion.AngleAxis(yaw, up) * Quaternion.AngleAxis(-lift, right) * bladeArm.rotation;
            }

            // Gun arm: raised onto you while it fires, kicking back with each shot.
            bool firing = move == Move.Lasers;
            aim = Mathf.MoveTowards(aim, firing ? 1f : 0f, dt * 3f);
            recoil = Mathf.MoveTowards(recoil, 0f, dt * 6f);
            if (gunArm != null && gunMuzzle != null && aim > 0.001f)
            {
                Vector3 now = gunMuzzle.position - gunArm.position;
                Vector3 want = target + Vector3.down * 0.3f - gunArm.position;
                Quaternion turn = Quaternion.FromToRotation(now, want);
                gunArm.rotation = Quaternion.Slerp(Quaternion.identity, turn, aim) * Quaternion.AngleAxis(-25f * recoil, right) * gunArm.rotation;
            }
        }

        bool Enraged => body.Hp < body.MaxHp * 0.4f;
        float RestSeconds => Enraged ? 0.7f : 1.3f;
        float FloorY => arena.FloorY;
        Vector3 Floor(Vector3 p) => new Vector3(p.x, FloorY + 0.02f, p.z);

        /// <summary>Its arm cannons (side -1 left, +1 right) and shoulder pods.</summary>
        Vector3 Arm(float side) => body.Center + Vector3.up * (body.Spec.Height * 0.08f) + body.transform.right * (side * 0.62f) + body.transform.forward * 0.45f;
        Vector3 Shoulder(float side) => body.Center + Vector3.up * (body.Spec.Height * 0.36f) + body.transform.right * (side * 0.4f) - body.transform.forward * 0.1f;

        public void Tick(float dt, Vector3 target, Vector3 toward, float distance)
        {
            timer -= dt;
            TickWaves(dt);
            switch (move)
            {
                case Move.Approach:
                    body.Walk(dt, distance > body.Spec.Range ? toward : Vector3.zero, body.Spec.Speed * (Enraged ? 1.3f : 1f));
                    body.Face(target, dt, 2.5f);
                    if (timer <= 0f || distance < SwingReach * 0.85f)
                        Choose(distance);
                    break;
                case Move.Rest:
                    body.Walk(dt, distance > body.Spec.Range + 0.6f ? toward : Vector3.zero, body.Spec.Speed * 0.6f);
                    body.Face(target, dt, 2.5f);
                    if (timer <= 0f)
                    {
                        if (next == Move.Approach)
                            Choose(distance);
                        else
                            Begin(next);
                    }
                    break;
                case Move.Swing:
                    TickSwing(dt, target, false);
                    break;
                case Move.Lasers:
                    body.Walk(dt, Vector3.zero, 0f);
                    body.Face(target, dt, 3f);
                    TickLasers(target);
                    break;
                case Move.Rockets:
                    body.Walk(dt, Vector3.zero, 0f);
                    body.Face(target, dt, 3f);
                    TickRockets(target);
                    break;
                case Move.Jump:
                    TickJump(dt, target, false);
                    break;
                case Move.Summon:
                    body.Walk(dt, Vector3.zero, 0f);
                    if (timer <= 0f)
                    {
                        arena.SpawnBossHelpers(body, 2);
                        RestThen(Move.Approach);
                    }
                    break;
            }
        }

        /// <summary>Close: swing (then something else). Otherwise lasers, a jump and rockets in turn.</summary>
        void Choose(float distance)
        {
            if (!summoned && body.Hp < body.MaxHp * 0.6f)
            {
                summoned = true;
                Begin(Move.Summon);
                return;
            }
            // Swings when you're close, but never twice in a row: standing your ground still sees everything.
            if (distance < SwingReach && !justSwung)
            {
                justSwung = true;
                Begin(Move.Swing);
                return;
            }
            justSwung = false;
            Move[] cycle = { Move.Lasers, Move.Jump, Move.Rockets };
            Begin(cycle[rotation++ % cycle.Length]);
        }

        void Begin(Move what)
        {
            move = what;
            count = 0;
            switch (what)
            {
                case Move.Swing:
                    StartSwing(SwingWindUp * (Enraged ? 0.8f : 1f));
                    break;
                case Move.Lasers:
                    timer = 0.3f;
                    break;
                case Move.Rockets:
                    timer = 0.5f;
                    body.Look?.PlayOnce("shoot");
                    break;
                case Move.Jump:
                    timer = CrouchSeconds;
                    jumpFrom = body.transform.position;
                    jumpTo = LandingSpot();
                    body.Look?.PlayOnce("hit", 0.7f);
                    Tell(Move.Jump, jumpFrom, jumpTo);
                    break;
                case Move.Summon:
                    timer = 1.2f;
                    body.Look?.PlayOnce("hit");
                    break;
                case Move.Approach:
                    timer = 2.5f;
                    break;
            }
            if (what != Move.Swing && what != Move.Jump)
                Tell(what);
            Debug.Log($"[Mech] {what}, HP {body.Hp:0}/{body.MaxHp:0}{(Enraged ? " (enraged)" : "")}");
        }

        void RestThen(Move after)
        {
            move = Move.Rest;
            timer = RestSeconds;
            next = after;
            ShowMarker(false);
            Tell(Move.Rest);
        }

        // ------------------------------------------------------------------ swing

        Vector3 swingFacing;
        float windUp = SwingWindUp;

        void StartSwing(float windup)
        {
            timer = windUp = windup;
            Vector3 target = arena.NearestPlayerHead(body.Center);
            swingFacing = Vector3.ProjectOnPlane(target - body.Center, Vector3.up).normalized;
            if (swingFacing.sqrMagnitude < 0.5f)
                swingFacing = body.transform.forward;
            // The body's attack clip runs over the whole wind-up and the strike, so it doesn't swing early.
            body.Look?.PlayOnceOver("attack", windup + 0.2f);
            arena.Game.PlayAt(body.Center, arena.Game.WhooshSound);
            Tell(Move.Swing, swingFacing, default, windup);
        }

        /// <summary>
        /// The swing at <paramref name="target"/> (the nearest player). Its warning and hit are for your own player:
        /// in co-op the other headset plays the same swing and checks its own. A copy (<paramref name="puppet"/>)
        /// keeps the facing it was told and ends after one sweep (the host tells it of the next).
        /// </summary>
        void TickSwing(float dt, Vector3 target, bool puppet)
        {
            if (!puppet)
                body.Walk(dt, Vector3.zero, 0f);
            // It turns with you a little during the wind-up, then commits.
            Vector3 toYou = Vector3.ProjectOnPlane(target - body.Center, Vector3.up).normalized;
            if (!puppet && timer > 0.25f && toYou.sqrMagnitude > 0.5f)
                swingFacing = Vector3.Slerp(swingFacing, toYou, 2f * dt).normalized;
            body.Face(body.Center + swingFacing, dt, 6f);
            Vector3 mine = arena.LocalHead;
            if (timer > 0f)
            {
                float t = Mathf.Clamp01(1f - timer / windUp);
                Mark(Floor(body.Center), swingFacing, SwingReach, SwingDegrees * 0.5f, t);
                if (InSwing(mine))
                    arena.WarnPlayer(Mathf.Lerp(0.25f, 0.5f, t));
                return;
            }
            // The sweep.
            ShowMarker(false);
            sweepAge = 0f;
            SweepTrail(swingFacing);
            if (InSwing(mine))
            {
                if (arena.PlayerShieldBlocks(body.Center))
                    Debug.Log("[Mech] swing blocked by the shield");
                else
                    arena.HurtPlayer(SwingDamage, "mech swing");
            }
            if (puppet)
            {
                move = Move.Rest;
                timer = 99f;
                return;
            }
            count++;
            int swings = Enraged ? 3 : 2;
            float distance = Vector3.ProjectOnPlane(target - body.Center, Vector3.up).magnitude;
            if (count < swings && distance < SwingReach + 0.5f)
            {
                // Follow-up swings wind up a little quicker, but never under 85% of the first (they used to shrink
                // by a quarter each time, down to about half a second).
                StartSwing(SwingWindUp * (Enraged ? 0.8f : 1f) * 0.85f);
                return;
            }
            RestThen(Move.Approach);
        }

        bool InSwing(Vector3 head)
        {
            Vector3 flat = Vector3.ProjectOnPlane(head - body.Center, Vector3.up);
            return flat.magnitude < SwingReach + 0.2f && Vector3.Angle(flat, swingFacing) < SwingDegrees * 0.5f + 8f;
        }

        /// <summary>The blade's sweep: two wide soft arcs at chest and waist height that fade out behind it.</summary>
        void SweepTrail(Vector3 facing)
        {
            for (int layer = 0; layer < 2; layer++)
            {
                LineRenderer line = LineFade.Make("Mech Swing", body.transform.parent,
                    layer == 0 ? new Color(1f, 0.75f, 0.45f, 0.85f) : new Color(1f, 0.4f, 0.15f, 0.55f), layer == 0 ? 0.3f : 0.5f, 28, 0.3f);
                Vector3 centre = body.Center + Vector3.up * (layer == 0 ? 0.1f : -0.25f);
                for (int i = 0; i < 28; i++)
                {
                    float a = Mathf.Lerp(-SwingDegrees * 0.5f, SwingDegrees * 0.5f, i / 27f);
                    line.SetPosition(i, centre + Quaternion.AngleAxis(a, Vector3.up) * facing * (SwingReach * (layer == 0 ? 0.9f : 0.75f)));
                }
            }
        }

        // ------------------------------------------------------------------ arm lasers

        void TickLasers(Vector3 target)
        {
            if (timer > 0f)
                return;
            int volleys = Enraged ? 5 : 4;
            if (count >= volleys)
            {
                RestThen(Move.Approach);
                return;
            }
            Vector3 muzzle = GunMuzzle;
            Vector3 aimAt = (target + Vector3.down * 0.3f - muzzle).normalized;
            // Two beams a little apart, so a small step clears both.
            arena.EnemyFires(EnemyShot.Laser, muzzle, Quaternion.AngleAxis(-5f, Vector3.up) * aimAt, body);
            arena.EnemyFires(EnemyShot.Laser, muzzle, Quaternion.AngleAxis(5f, Vector3.up) * aimAt, body);
            recoil = 1f;
            count++;
            timer = Enraged ? 0.55f : 0.75f;
        }

        // ------------------------------------------------------------------ rockets

        void TickRockets(Vector3 target)
        {
            if (timer > 0f)
                return;
            int rockets = Enraged ? 8 : 6;
            if (count >= rockets)
            {
                RestThen(Move.Approach);
                return;
            }
            float side = count % 2 == 0 ? -1f : 1f;
            Vector3 spread = new Vector3(Random.Range(-0.9f, 0.9f), 0f, Random.Range(-0.9f, 0.9f));
            arena.EnemyFires(EnemyShot.Missile, Shoulder(side) + Vector3.up * 0.15f, target + Vector3.down * 0.4f + spread, body);
            count++;
            timer = 0.28f;
        }

        // ------------------------------------------------------------------ jump slam

        /// <summary>Where it lands: where you are now, but no more than a long leap from where it stands.</summary>
        Vector3 LandingSpot()
        {
            Vector3 you = Floor(arena.NearestPlayerHead(body.Center));
            Vector3 from = Floor(body.transform.position);
            Vector3 leap = Vector3.ClampMagnitude(you - from, 5f);
            Vector3 spot = from + leap;
            Vector3 step = arena.Steer(from, leap.normalized, leap.magnitude);
            if (step.sqrMagnitude < leap.sqrMagnitude * 0.5f)
                spot = from + step;
            return spot;
        }

        void TickJump(float dt, Vector3 target, bool puppet)
        {
            float middle = Enemy.MiddleHeight(body.Kind);
            Vector3 land = Floor(jumpTo);
            if (timer > 0f && count == 0)
            {
                // Crouching: it sinks a little and the landing ring appears.
                body.Face(jumpTo, dt, 4f);
                body.transform.position = new Vector3(jumpFrom.x, FloorY + middle - 0.25f * (1f - timer / CrouchSeconds), jumpFrom.z);
                Mark(land, Vector3.forward, SlamRadius, 180f, (1f - timer / CrouchSeconds) * CrouchSeconds / (CrouchSeconds + AirSeconds));
                WarnIfInside(target, land, 0.25f);
                return;
            }
            if (count == 0)
            {
                count = 1;
                timer = AirSeconds;
                arena.Game.PlayAt(body.Center, arena.Game.WhooshSound);
            }
            if (timer > 0f)
            {
                float t = 1f - timer / AirSeconds;
                Vector3 flat = Vector3.Lerp(jumpFrom, jumpTo, t);
                float height = 4f * JumpHeight * t * (1f - t);
                body.transform.position = new Vector3(flat.x, FloorY + middle + height, flat.z);
                body.Face(jumpTo + (jumpTo - jumpFrom), dt, 4f);
                Mark(land, Vector3.forward, SlamRadius, 180f, (CrouchSeconds + t * AirSeconds) / (CrouchSeconds + AirSeconds));
                WarnIfInside(target, land, Mathf.Lerp(0.3f, 0.55f, t));
                return;
            }
            // Landing: a hard hit where it comes down, then the shockwave.
            body.transform.position = new Vector3(jumpTo.x, FloorY + middle, jumpTo.z);
            ShowMarker(false);
            arena.Game.Explode(land + Vector3.up * 0.2f, 0f, 0f, new Color(1f, 0.5f, 0.2f));
            TransformWave.SpawnRing(land + Vector3.up * 0.05f, new Color(1f, 0.6f, 0.3f), body.transform.parent);
            arena.Game.PlayAt(land, arena.Game.BoomSound);
            if (arena.PlayerStandsWithin(land, SlamRadius))
            {
                if (arena.PlayerShieldBlocks(land))
                    Debug.Log("[Mech] slam blocked by the shield");
                else
                    arena.HurtPlayer(SlamDamage, "mech slam");
            }
            StartWave(land);
            body.Look?.PlayOnce("hit", 1.4f);
            if (puppet)
            {
                move = Move.Rest;
                timer = 99f;
                return;
            }
            RestThen(Move.Approach);
        }

        void WarnIfInside(Vector3 head, Vector3 centre, float amount)
        {
            if (arena.PlayerStandsWithin(centre, SlamRadius))
                arena.WarnPlayer(amount);
        }

        void StartWave(Vector3 centre)
        {
            var shock = new Shock { Centre = centre, Radius = 0.3f };
            var go = new GameObject("Mech Shockwave", typeof(LineRenderer));
            go.transform.SetParent(body.transform.parent, false);
            shock.Line = go.GetComponent<LineRenderer>();
            shock.Line.sharedMaterial = Resources.Load<Material>("AimLine");
            shock.Line.positionCount = 49;
            shock.Line.loop = true;
            shock.Line.useWorldSpace = true;
            shock.Line.widthMultiplier = 0.2f;
            shock.Line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shock.Disc = Bubble.Create("Shockwave Disc", body.transform.parent);
            shock.Disc.transform.position = centre;
            waves.Add(shock);
        }

        /// <summary>Shockwaves race out along the floor; one that passes under you hurts unless you're in the air or shielded.</summary>
        void TickWaves(float dt)
        {
            for (int i = waves.Count - 1; i >= 0; i--)
            {
                Shock w = waves[i];
                float before = w.Radius;
                w.Radius += WaveSpeed * dt;
                float fade = Mathf.Clamp01((WaveRange - w.Radius) / 1.2f);
                Tint.Line(w.Line, new Color(1f, 0.55f, 0.2f) * (0.5f + fade));
                w.Line.widthMultiplier = Mathf.Lerp(0.06f, 0.22f, fade);
                for (int k = 0; k < 49; k++)
                {
                    float a = k / 48f * Mathf.PI * 2f;
                    w.Line.SetPosition(k, w.Centre + new Vector3(Mathf.Cos(a), 0.03f + 0.06f * Mathf.Sin(a * 6f + w.Radius * 8f), Mathf.Sin(a)) * w.Radius);
                }
                w.Disc.transform.localScale = new Vector3(w.Radius * 2f, 0.1f, w.Radius * 2f);
                w.Disc.Show(new Color(1f, 0.45f, 0.15f), BubbleStyle.Energy, 0.5f * fade);
                if (!w.Done)
                {
                    float distance = arena.PlayerFloorDistance(w.Centre);
                    if (distance >= before && distance < w.Radius)
                    {
                        w.Done = true;
                        if (arena.PlayerJumping(JumpClearance))
                            Debug.Log("[Mech] shockwave jumped over");
                        else if (arena.PlayerShieldBlocks(w.Centre))
                            Debug.Log("[Mech] shockwave blocked by the shield");
                        else
                            arena.HurtPlayer(WaveDamage, "mech shockwave");
                    }
                }
                if (w.Radius >= WaveRange)
                {
                    Object.Destroy(w.Line.gameObject);
                    Object.Destroy(w.Disc.gameObject);
                    waves.RemoveAt(i);
                }
            }
        }

        // ------------------------------------------------------------------ floor warnings

        void ShowMarker(bool on)
        {
            if (!on)
                marker?.Hide();
        }

        /// <summary>Where an attack will land: a disc or fan on the floor that fills as the attack comes.</summary>
        void Mark(Vector3 centre, Vector3 facing, float radius, float halfArc, float fill)
        {
            marker ??= new FloorMark(body.transform.parent, Red);
            marker.Show(centre, facing, radius, halfArc, fill);
        }

        public void Stop()
        {
            marker?.Destroy();
            foreach (Shock w in waves)
            {
                if (w.Line != null)
                    Object.Destroy(w.Line.gameObject);
                if (w.Disc != null)
                    Object.Destroy(w.Disc.gameObject);
            }
            waves.Clear();
        }
    }
}
