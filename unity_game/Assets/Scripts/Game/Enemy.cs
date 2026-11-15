using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// A PVE enemy. Each kind (<see cref="EnemySpec"/>) has its own CC0 model and way of fighting:
    /// - Gunner: walks in, fires lasers.
    /// - Drone: weaves at head height, quick weaker lasers.
    /// - Brawler: runs at you and punches after a wind-up; step back out of reach.
    /// - Rocketeer: hangs back and fires missiles that blow up robots too, and can be shot down.
    /// - Dasher: a set routine you can learn: it crouches and shows on the floor where it's about to dash, dashes
    ///   in a streak, plants, fires twice, then dashes back the other way, zig-zagging in.
    /// - Sniper: keeps its distance; a long aim that follows you, then a big laser.
    /// - Tank: slow and tough, a fan of three lasers.
    /// - Boss: see <see cref="BossBrain"/>.
    /// Lasers show their aim line before they fire (<see cref="EnemyLaser"/>), so they can be dodged or blocked.
    /// The body is a standing capsule matched to the model, so hits on its head or legs count.
    /// All appear inside a spawn bubble. The transform sits at the middle of the body.
    /// </summary>
    public sealed class Enemy : MonoBehaviour, ITallHittable
    {
        public const float SpawnSeconds = 0.9f;
        public static float SpeedScale = 1f;
        const float AimError = 7f;   // degrees: they miss a bit, on top of the dodge window

        Arena arena;
        Transform head;
        Bubble spawnBubble;
        Transform shadow;
        Renderer shadowRenderer;
        static MaterialPropertyBlock shadowBlock;
        static readonly int ShadowAlpha = Shader.PropertyToID("_Alpha");
        EnemyLook look;
        IBossBrain boss;
        EnemySpec spec;
        float age;
        float attackTimer;
        float flash;
        float weave;
        float weaveSpeed;
        Vector3 lastPosition;
        float floorY;
        // Brawler wind-up, dasher dash, double shots.
        float windUp;
        Vector3 dashVelocity;
        float dashTimer;
        DashStep dashStep;
        float dashStepFor;
        float dashSide = 1f;
        Vector3 dashFrom;
        Vector3 dashTo;
        LineRenderer dashLine;
        FxSpin dashMark;
        TrailRenderer dashTrail;

        enum DashStep { Approach, Tell, Dash, Plant, Shoot }

        /// <summary>For tests: which part of the Dasher's routine it's in.</summary>
        public string DashState => dashStep.ToString();

        // The Dasher's routine (seconds): crouch and show the way, dash, skid to a stop, fire twice, catch its breath.
        public const float DashTellSeconds = 0.5f;
        public const float DashSeconds = 0.3f;
        public const float DashLength = 1.5f;
        const float DashPlantSeconds = 0.25f;
        const float DashShootSeconds = 0.9f;
        int queuedShots;
        float queuedIn;
        float moving;
        bool running;
        EnemyLaser charging;
        FxSpin fistGlow;
        Vector3 push;

        /// <summary>
        /// Knocks it along (wind, tornado pull), sliding round furniture; the push fades over a moment. The boss
        /// barely moves.
        /// </summary>
        public void Push(Vector3 velocity)
        {
            if (IsProxy || Webbed)
                return;
            push += IsBoss ? velocity * 0.1f : velocity;
        }

        public EnemyKind Kind { get; private set; }
        public EnemySpec Spec => spec;
        /// <summary>Shared by both headsets in co-op.</summary>
        public ushort Id { get; private set; }
        /// <summary>A copy of an enemy the host runs: no thinking or shooting of its own, it follows the host.</summary>
        public bool IsProxy { get; private set; }
        /// <summary>The Variant Lab's practice robot: stands still and never shoots.</summary>
        public bool Practice { get; set; }
        /// <summary>
        /// The game thinks it's in or behind a wall (the room map, or the headset's depth seeing a surface in front of
        /// it). It drops what it's doing and makes straight for you until it's out; your attacks reach it meanwhile.
        /// </summary>
        public bool Buried { get; set; }
        /// <summary>How many depth checks in a row found it hidden behind a real surface.</summary>
        public int HiddenChecks { get; set; }
        /// <summary>Caught in the Spider's web: it stops thinking and goes where the web puts it.</summary>
        public bool Webbed { get; set; }
        Vector3 netPosition;
        public float Radius => spec.Radius;
        public Vector3 Center => transform.position;
        /// <summary>Its hit capsule: from just above its feet to just under the top of its head (a ball for drones).</summary>
        public Vector3 Bottom => spec.Flies ? Center : Feet + Vector3.up * (spec.HitHigh > 0f ? spec.HitLow : Radius);
        public Vector3 Top => spec.Flies ? Center : Feet + Vector3.up * (spec.HitHigh > 0f ? spec.HitHigh : Mathf.Max(Radius, spec.Height - Radius));
        Vector3 Feet => Center + Vector3.down * MiddleHeight(Kind);
        public bool Alive => Hp > 0f;
        public Team Team => Team.Enemies;
        public float Hp { get; private set; }
        public float MaxHp { get; private set; }
        public Vector3 BarPoint => Top + Vector3.up * (Radius + 0.15f);
        public bool Spawning => age < SpawnSeconds;
        public bool IsBoss => EnemySpec.IsBoss(Kind);
        /// <summary>For tests: the mech boss's current move (empty for anything else).</summary>
        public string BossMove => boss is MechBrain mech ? mech.MoveName : "";
        /// <summary>GOLIATH's brain, if this is GOLIATH (on the client, its copy plays the host's moves).</summary>
        public MechBrain Mech => boss as MechBrain;
        public Arena Arena => arena;
        public EnemyLook Look => look;
        /// <summary>Where its middle should be above the floor.</summary>
        public static float MiddleHeight(EnemyKind kind) => EnemySpec.Of(kind).Flies ? 0f : EnemySpec.Of(kind).Height * 0.5f;

        void OnEnable() => Hittables.Add(this);
        void OnDisable() => Hittables.Remove(this);

        public static Enemy SpawnProxy(ushort id, EnemyKind kind, Vector3 position, float hp, float maxHp, Arena owner,
            Transform head, Transform parent)
        {
            Enemy enemy = Spawn(kind, position, owner, head, parent, id);
            enemy.IsProxy = true;
            enemy.netPosition = position;
            enemy.Hp = hp;
            enemy.MaxHp = maxHp;
            return enemy;
        }

        /// <summary>The host's latest word on this enemy (client only).</summary>
        public void ApplyState(Vector3 position, float hp)
        {
            netPosition = position;
            Hp = Mathf.Min(Hp, hp);
        }

        /// <summary>Makes an enemy whose body's middle is at <paramref name="position"/> (ground kinds stand on the floor below it).</summary>
        public static Enemy Spawn(EnemyKind kind, Vector3 position, Arena owner, Transform head, Transform parent, ushort id = 0)
        {
            EnemySpec spec = EnemySpec.Of(kind);
            var go = new GameObject(spec.Name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var enemy = go.AddComponent<Enemy>();
            enemy.Kind = kind;
            enemy.spec = spec;
            enemy.Id = id;
            enemy.arena = owner;
            enemy.head = head;
            enemy.Hp = enemy.MaxHp = spec.Hp;
            enemy.attackTimer = Random.Range(1.2f, 2.4f);
            enemy.dashTimer = Random.Range(1.5f, 2.5f);
            enemy.weave = Random.value * 10f;
            enemy.weaveSpeed = Random.Range(0.8f, 1.4f);
            enemy.spawnBubble = Bubble.Create("Spawn Bubble", parent);
            enemy.MakeShadow(parent);
            enemy.lastPosition = position;
            enemy.floorY = position.y - MiddleHeight(kind);
            enemy.look = EnemyLook.Create(spec.Model, go.transform);
            if (enemy.look != null)
            {
                // The model stands on its feet (drones: centred); the root is the body's middle.
                enemy.look.transform.localPosition = Vector3.down * MiddleHeight(kind);
                enemy.look.gameObject.SetActive(false);
            }
            if (kind == EnemyKind.Boss)
                enemy.boss = new BossBrain(enemy);
            else if (kind == EnemyKind.Mech)
                enemy.boss = new MechBrain(enemy);
            return enemy;
        }

        public float TakeHit(float damage, Vector3 from)
        {
            if (!Alive || Spawning)
                return 0f;
            float taken = Mathf.Min(damage, Hp);
            Hp -= taken;
            flash = 0.12f;
            if (IsProxy)
                arena.ReportEnemyHit(Id, taken);
            if (!Alive && charging != null)
                charging.Cancel();
            return taken;
        }

        /// <summary>A soft dark patch on the floor under it, so it looks planted in the room.</summary>
        void MakeShadow(Transform parent)
        {
            var material = Resources.Load<Material>("BlobShadow");
            if (material == null)
                return;
            var go = new GameObject("Enemy Shadow", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>("QuadMesh");
            shadowRenderer = go.GetComponent<MeshRenderer>();
            shadowRenderer.sharedMaterial = material;
            shadowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shadow = go.transform;
            shadow.rotation = Quaternion.Euler(90f, 0f, 0f);
            PlaceShadow();
        }

        /// <summary>Under its feet on the floor; smaller and fainter the higher it is (drones, a flung robot, the mech's leap).</summary>
        void PlaceShadow()
        {
            if (shadow == null)
                return;
            float floor = arena != null ? arena.FloorY : floorY;
            float height = Mathf.Max(0f, transform.position.y - MiddleHeight(Kind) - floor);
            float size = Mathf.Max(0.5f, spec.Radius * 3.6f) / (1f + height * 0.5f);
            shadow.position = new Vector3(transform.position.x, floor + 0.012f, transform.position.z);
            shadow.localScale = new Vector3(size, size, 1f);
            float appear = Mathf.Clamp01((age - SpawnSeconds * 0.4f) / (SpawnSeconds * 0.6f));
            shadowBlock ??= new MaterialPropertyBlock();
            shadowBlock.SetFloat(ShadowAlpha, 0.6f * appear / (1f + height * 1.1f));
            shadowRenderer.SetPropertyBlock(shadowBlock);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            TickSpawnBubble();
            PlaceShadow();
            flash = Mathf.Max(0f, flash - dt);
            Vector3 step = transform.position - lastPosition;
            moving = Mathf.Lerp(moving, dt > 0f ? step.magnitude / dt : 0f, 1f - Mathf.Exp(-8f * dt));
            look?.Tick(dt, moving > 0.06f, running || moving > spec.Speed * 1.8f, flash);
            if (!Alive || Webbed)
            {
                // Caught mid-routine: no floor line or streak left behind, and it starts over when it's free.
                HideDashLine();
                if (dashTrail != null)
                    dashTrail.emitting = false;
                dashStep = DashStep.Approach;
            }
            if (!Alive)
                return;
            if (Webbed)
            {
                lastPosition = transform.position;
                return;
            }
            // Diagnostic: enemies only move a few centimetres a frame, so a big step is worth a log line.
            if (step.sqrMagnitude > 0.25f && !IsBoss)
                Debug.Log($"[Enemy] {Kind} {Id} jumped {step.magnitude:0.00} m, {lastPosition} to {transform.position}, age {age:0.00}{(IsProxy ? " (copy)" : "")}");
            lastPosition = transform.position;
            if (push.sqrMagnitude > 1e-4f && !IsProxy)
            {
                Vector3 flatPush = Vector3.ProjectOnPlane(push, Vector3.up);
                if (flatPush.sqrMagnitude > 1e-6f)
                    transform.position += arena.Steer(transform.position, flatPush.normalized, flatPush.magnitude * dt);
                push *= Mathf.Exp(-5f * dt);
            }
            if (IsProxy)
            {
                transform.position = Vector3.Lerp(transform.position, netPosition, 1f - Mathf.Exp(-12f * dt));
                Face(head.position, dt);
                // GOLIATH's copy plays the host's moves (and checks its swings, slam and shockwave on you).
                if (boss is MechBrain mech && Alive)
                    mech.TickProxy(dt);
                return;
            }
            if (Spawning)
                return;
            if (Practice)
            {
                Face(head.position, dt);
                return;
            }

            Vector3 target = arena.NearestPlayerHead(transform.position);
            Vector3 flat = Vector3.ProjectOnPlane(target - transform.position, Vector3.up);
            float distance = flat.magnitude;
            Vector3 toward = distance > 0.01f ? flat / distance : transform.forward;

            if (boss != null)
            {
                boss.Tick(dt, target, toward, distance);
                return;
            }

            running = false;
            if (Buried)
            {
                // Out of the wall first, by the shortest way to where you are.
                running = true;
                HideDashLine();
                dashStep = DashStep.Approach;
                if (spec.Flies)
                    transform.position += toward * (spec.Speed * 2f * SpeedScale * dt);
                else
                    Walk(dt, toward, spec.Speed * 2f);
                Face(target, dt);
                return;
            }
            switch (Kind)
            {
                case EnemyKind.Drone:
                    Fly(dt, target, toward, distance);
                    break;
                case EnemyKind.Brawler:
                    Brawl(dt, target, toward, distance);
                    return;
                case EnemyKind.Dasher:
                    Dash(dt, toward, distance);
                    break;
                case EnemyKind.Sniper:
                    // Keeps its distance: backs off if you come close.
                    Walk(dt, distance > spec.Range ? toward : distance < spec.Range - 1.1f ? -toward : Vector3.zero, spec.Speed);
                    break;
                default:
                    Walk(dt, distance > spec.Range ? toward : Vector3.zero, spec.Speed);
                    break;
            }
            Face(target, dt);

            if (queuedShots > 0)
            {
                queuedIn -= dt;
                if (queuedIn <= 0f)
                {
                    queuedShots--;
                    queuedIn = 0.35f;
                    Shoot(EnemyShot.QuickLaser, target);
                }
            }
            attackTimer -= dt;
            // Smoke blinds them: no shooting from inside it, or through it. The Dasher shoots as part of its routine.
            if (Kind != EnemyKind.Dasher && attackTimer <= 0f && distance < 7f && !SmokeCloud.Blocks(transform.position, target))
            {
                attackTimer = Random.Range(spec.AttackMin, spec.AttackMax);
                Attack(target);
            }
        }

        /// <summary>Turns the body (not up or down) to face a point.</summary>
        public void Face(Vector3 point, float dt, float speed = 8f)
        {
            Vector3 flat = Vector3.ProjectOnPlane(point - transform.position, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f)
                return;
            Quaternion want = Quaternion.LookRotation(flat);
            transform.rotation = dt <= 0f ? want : Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-speed * dt));
        }

        /// <summary>A step on the floor, slid round furniture; it settles back down after being thrown about.</summary>
        public void Walk(float dt, Vector3 direction, float speed)
        {
            if (direction.sqrMagnitude > 1e-4f)
                transform.position += arena.Steer(transform.position, direction.normalized, speed * SpeedScale * dt);
            float y = Mathf.MoveTowards(transform.position.y, floorY + MiddleHeight(Kind), 4f * dt);
            transform.position = new Vector3(transform.position.x, y, transform.position.z);
        }

        void Fly(float dt, Vector3 target, Vector3 toward, float distance)
        {
            weave += dt * weaveSpeed;
            Vector3 side = Vector3.Cross(Vector3.up, toward) * (Mathf.Sin(weave * 2f) * 0.25f);
            Vector3 move = (distance > spec.Range ? toward * spec.Speed : Vector3.zero) + side;
            transform.position += move * SpeedScale * dt;
            float height = target.y + 0.2f + Mathf.Sin(weave * 1.3f) * 0.25f;
            transform.position = new Vector3(transform.position.x,
                Mathf.Lerp(transform.position.y, height, 1f - Mathf.Exp(-2f * dt)), transform.position.z);
        }

        /// <summary>Brawler: runs in, winds up a punch (fists glow), then hits if you're still in reach.</summary>
        void Brawl(float dt, Vector3 target, Vector3 toward, float distance)
        {
            const float Reach = 1.05f;
            const float WindUpSeconds = 0.6f;
            if (windUp > 0f)
            {
                windUp -= dt;
                ShowFists(1f - windUp / WindUpSeconds);
                Face(target, dt, 3f);
                Walk(dt, Vector3.zero, 0f);
                if (windUp <= 0f)
                {
                    ShowFists(0f);
                    // Measured from its front: a step back gets you out.
                    if (distance < Reach && arena.SphereHitsPlayer(transform.position + toward * 0.45f, 0.55f))
                        arena.HurtPlayer(12f, "brawler punch");
                    attackTimer = Random.Range(spec.AttackMin, spec.AttackMax);
                }
                return;
            }
            running = distance > spec.Range + 0.4f;
            Walk(dt, distance > spec.Range ? toward : Vector3.zero, running ? spec.Speed * 1.4f : spec.Speed);
            Face(target, dt);
            attackTimer -= dt;
            if (distance < Reach && attackTimer <= 0f)
            {
                windUp = WindUpSeconds;
                look?.Shoot();
                arena.Game.PlayAt(transform.position, arena.Game.WhooshSound);
            }
        }

        void ShowFists(float amount)
        {
            if (fistGlow == null)
            {
                if (amount <= 0f)
                    return;
                fistGlow = BallLook.Sprite("Fist Glow", transform, "FxGlow", 0.3f, new Color(1f, 0.15f, 0.08f) * 2f).Set(billboard: true, flicker: 0.3f);
                fistGlow.transform.localPosition = new Vector3(0f, 0.05f, 0.35f);
            }
            fistGlow.gameObject.SetActive(amount > 0f);
            fistGlow.transform.localScale = Vector3.one * Mathf.Lerp(0.1f, 0.45f, amount);
        }

        /// <summary>
        /// Dasher: walks in until it's in range, then runs its routine. Tell: it crouches and leans, and a line on
        /// the floor shows where it's going. Dash: fast, leaving a streak. Plant: it skids to a stop. Shoot: two quick
        /// lasers. Then the same the other way, zig-zagging toward you (or back, if you're close).
        /// </summary>
        void Dash(float dt, Vector3 toward, float distance)
        {
            Vector3 target = arena.NearestPlayerHead(transform.position);
            dashStepFor -= dt;
            switch (dashStep)
            {
                case DashStep.Approach:
                    Lean(0f, 0f, dt);
                    Walk(dt, distance > spec.Range ? toward : Vector3.zero, spec.Speed);
                    dashTimer -= dt;
                    if (dashTimer <= 0f && distance < spec.Range + 2.5f)
                        StartTell(toward, distance);
                    break;
                case DashStep.Tell:
                {
                    float t = 1f - dashStepFor / DashTellSeconds;
                    Lean(dashSide, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 2f)), dt);
                    Walk(dt, Vector3.zero, 0f);
                    ShowDashLine(t);
                    if (dashStepFor <= 0f)
                    {
                        dashStep = DashStep.Dash;
                        dashStepFor = DashSeconds;
                        dashVelocity = (dashTo - dashFrom) / DashSeconds;
                        HideDashLine();
                        if (dashTrail != null)
                        {
                            dashTrail.Clear();
                            dashTrail.emitting = true;
                        }
                        arena.Game.PlayAt(transform.position, arena.Game.WhooshSound);
                    }
                    break;
                }
                case DashStep.Dash:
                    running = true;
                    Lean(dashSide, 1f, dt);
                    transform.position += arena.Steer(transform.position, dashVelocity.normalized, dashVelocity.magnitude * dt);
                    if (dashStepFor <= 0f)
                    {
                        dashStep = DashStep.Plant;
                        dashStepFor = DashPlantSeconds;
                        if (dashTrail != null)
                            dashTrail.emitting = false;
                    }
                    break;
                case DashStep.Plant:
                {
                    // A short skid, leaning back against it.
                    float skid = dashStepFor / DashPlantSeconds;
                    transform.position += arena.Steer(transform.position, dashVelocity.normalized, dashVelocity.magnitude * 0.25f * skid * dt);
                    Lean(-dashSide * 0.6f, skid, dt);
                    if (dashStepFor <= 0f)
                    {
                        dashStep = DashStep.Shoot;
                        dashStepFor = DashShootSeconds;
                        if (!SmokeCloud.Blocks(transform.position, target))
                        {
                            Shoot(EnemyShot.QuickLaser, target);
                            queuedShots = 1;
                            queuedIn = 0.35f;
                        }
                    }
                    break;
                }
                case DashStep.Shoot:
                    Lean(0f, 0f, dt);
                    Walk(dt, Vector3.zero, 0f);
                    if (dashStepFor <= 0f)
                    {
                        // Back the other way. Farther off it walks in a little between dashes.
                        dashSide = -dashSide;
                        dashStep = DashStep.Approach;
                        dashTimer = distance > spec.Range + 1f ? 0.6f : 0.25f;
                    }
                    break;
            }
        }

        /// <summary>Picks where the dash goes (the clear side, angled in or out), and starts showing it.</summary>
        void StartTell(Vector3 toward, float distance)
        {
            Vector3 side = Vector3.Cross(Vector3.up, toward);
            float inOut = distance > spec.Range + 0.5f ? 0.5f : distance < spec.Range - 0.6f ? -0.4f : 0f;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Vector3 direction = (side * dashSide + toward * inOut).normalized;
                Vector3 step = arena.Steer(transform.position, direction, DashLength);
                if (step.sqrMagnitude > DashLength * DashLength * 0.5f || attempt == 1)
                {
                    dashFrom = transform.position;
                    dashTo = transform.position + (step.sqrMagnitude > 1e-4f ? step : direction * DashLength);
                    break;
                }
                dashSide = -dashSide;
            }
            dashStep = DashStep.Tell;
            dashStepFor = DashTellSeconds;
            look?.PlayOnce("hit", 0.6f);
        }

        /// <summary>Tilts and crouches the model: toward a side while it gets ready and dashes, back against a skid.</summary>
        void Lean(float side, float amount, float dt)
        {
            if (look == null)
                return;
            Quaternion want = Quaternion.Euler(amount * 8f, 0f, -side * amount * 16f);
            Vector3 crouch = Vector3.down * (MiddleHeight(Kind) + amount * 0.08f);
            float k = 1f - Mathf.Exp(-14f * dt);
            look.transform.localRotation = Quaternion.Slerp(look.transform.localRotation, want, k);
            look.transform.localPosition = Vector3.Lerp(look.transform.localPosition, crouch, k);
        }

        static readonly Color DashColor = new Color(1f, 0.45f, 0.12f);

        /// <summary>The floor line from its feet to where it's about to land, drawing out over the tell.</summary>
        void ShowDashLine(float t)
        {
            if (dashLine == null)
            {
                var go = new GameObject("Dash Line", typeof(LineRenderer));
                go.transform.SetParent(transform.parent, false);
                dashLine = go.GetComponent<LineRenderer>();
                dashLine.sharedMaterial = Resources.Load<Material>("AimLine");
                dashLine.positionCount = 2;
                dashLine.useWorldSpace = true;
                dashLine.numCapVertices = 4;
                dashLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                dashMark = BallLook.Sprite("Dash Mark", go.transform, "FxRing", 0.5f, DashColor);
                dashMark.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                dashMark.Set(tumble: new Vector3(0f, 0f, 200f));

                dashTrail = gameObject.AddComponent<TrailRenderer>();
                dashTrail.sharedMaterial = Resources.Load<Material>("AimLine");
                dashTrail.time = 0.22f;
                dashTrail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.12f), new Keyframe(1f, 0f));
                dashTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                dashTrail.emitting = false;
                var trailBlock = new MaterialPropertyBlock();
                dashTrail.GetPropertyBlock(trailBlock);
                trailBlock.SetColor("_BaseColor", DashColor * 0.6f);
                dashTrail.SetPropertyBlock(trailBlock);
            }
            dashLine.gameObject.SetActive(true);
            float floor = floorY + 0.02f;
            Vector3 from = new Vector3(dashFrom.x, floor, dashFrom.z);
            Vector3 to = new Vector3(dashTo.x, floor, dashTo.z);
            float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 1.6f));
            dashLine.SetPosition(0, from);
            dashLine.SetPosition(1, Vector3.Lerp(from, to, grow));
            dashLine.widthMultiplier = Mathf.Lerp(0.03f, 0.09f, t);
            Tint.Line(dashLine, DashColor * (1f + 0.6f * Mathf.Sin(t * 30f) * t));
            dashMark.transform.position = to;
            dashMark.Size = Vector3.one * Mathf.Lerp(0.15f, 0.55f, grow);
        }

        void HideDashLine()
        {
            if (dashLine != null)
                dashLine.gameObject.SetActive(false);
        }

        void Attack(Vector3 target)
        {
            switch (Kind)
            {
                case EnemyKind.Drone:
                    Shoot(EnemyShot.QuickLaser, target);
                    break;
                case EnemyKind.Rocketeer:
                    look?.Shoot();
                    arena.EnemyFires(EnemyShot.Missile, Muzzle(0.25f), target + Vector3.down * 0.3f, this);
                    break;
                case EnemyKind.Dasher:
                    Shoot(EnemyShot.QuickLaser, target);
                    queuedShots = 1;
                    queuedIn = 0.35f;
                    break;
                case EnemyKind.Sniper:
                    Shoot(EnemyShot.Sniper, target);
                    break;
                case EnemyKind.Tank:
                    for (int i = -1; i <= 1; i++)
                        Shoot(EnemyShot.Laser, target, i * 16f);
                    break;
                default:
                    Shoot(EnemyShot.Laser, target);
                    break;
            }
        }

        /// <summary>Where its shots start: its chest, a little in front, optionally to one side.</summary>
        public Vector3 Muzzle(float side = 0f)
        {
            float up = spec.Flies ? 0f : spec.Height * 0.2f;
            return transform.position + transform.forward * (Radius + 0.06f) + Vector3.up * up + transform.right * side;
        }

        void Shoot(EnemyShot shot, Vector3 target, float yaw = 0f)
        {
            Vector3 muzzle = Muzzle();
            Vector3 aim = (target + Vector3.down * 0.25f - muzzle).normalized;
            aim = Quaternion.AngleAxis(yaw + Random.Range(-AimError, AimError), Vector3.up)
                * Quaternion.AngleAxis(Random.Range(-AimError, AimError) * 0.5f, Vector3.Cross(Vector3.up, aim)) * aim;
            charging = arena.EnemyFires(shot, muzzle, aim, this);
            look?.Shoot();
        }

        void TickSpawnBubble()
        {
            if (spawnBubble == null)
                return;
            float t = age / SpawnSeconds;
            if (t >= 1.2f)
            {
                look?.gameObject.SetActive(true);
                Destroy(spawnBubble.gameObject);
                return;
            }
            // Grows around the spot, the enemy fades in inside it, then the bubble pops.
            float size = Mathf.Max(0.8f, spec.Height * 1.25f);
            spawnBubble.transform.position = transform.position;
            spawnBubble.transform.localScale = Vector3.one * (size * Mathf.Clamp01(t * 2.5f) * (1f + Mathf.Max(0f, t - 1f) * 2f));
            spawnBubble.Show(new Color(0.8f, 0.3f, 1f), BubbleStyle.Energy, t < 1f ? 0.9f : Mathf.Clamp01((1.2f - t) * 5f));
            look?.gameObject.SetActive(t > 0.4f);
        }

        void OnDestroy()
        {
            if (spawnBubble != null)
                Destroy(spawnBubble.gameObject);
            if (dashLine != null)
                Destroy(dashLine.gameObject);
            if (shadow != null)
                Destroy(shadow.gameObject);
            boss?.Stop();
        }
    }
}
