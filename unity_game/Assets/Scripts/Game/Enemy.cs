using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// A PVE enemy. Each kind (<see cref="EnemySpec"/>) has its own CC0 model and way of fighting:
    /// - Gunner: walks in, fires lasers.
    /// - Drone: weaves at head height, quick weaker lasers.
    /// - Brawler: runs at you and punches after a wind-up; step back out of reach.
    /// - Rocketeer: hangs back and fires missiles that blow up robots too, and can be shot down.
    /// - Dasher: dashes side to side between double shots.
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
        EnemyLook look;
        BossBrain boss;
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
        float dashFor;
        Vector3 dashVelocity;
        float dashTimer;
        int queuedShots;
        float queuedIn;
        float moving;
        bool running;
        EnemyLaser charging;
        FxSpin fistGlow;

        public EnemyKind Kind { get; private set; }
        public EnemySpec Spec => spec;
        /// <summary>Shared by both headsets in co-op.</summary>
        public ushort Id { get; private set; }
        /// <summary>A copy of an enemy the host runs: no thinking or shooting of its own, it follows the host.</summary>
        public bool IsProxy { get; private set; }
        /// <summary>The Variant Lab's practice robot: stands still and never shoots.</summary>
        public bool Practice { get; set; }
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
        public bool IsBoss => Kind == EnemyKind.Boss;
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

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            TickSpawnBubble();
            flash = Mathf.Max(0f, flash - dt);
            Vector3 step = transform.position - lastPosition;
            moving = Mathf.Lerp(moving, dt > 0f ? step.magnitude / dt : 0f, 1f - Mathf.Exp(-8f * dt));
            look?.Tick(dt, moving > 0.06f, running || moving > spec.Speed * 1.8f, flash);
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
            if (IsProxy)
            {
                transform.position = Vector3.Lerp(transform.position, netPosition, 1f - Mathf.Exp(-12f * dt));
                Face(head.position, dt);
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
            // Smoke blinds them: no shooting from inside it, or through it.
            if (attackTimer <= 0f && distance < 7f && !SmokeCloud.Blocks(transform.position, target))
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

        /// <summary>Dasher: walks in, and every couple of seconds dashes sideways fast.</summary>
        void Dash(float dt, Vector3 toward, float distance)
        {
            if (dashFor > 0f)
            {
                dashFor -= dt;
                running = true;
                transform.position += arena.Steer(transform.position, dashVelocity.normalized, dashVelocity.magnitude * dt);
                return;
            }
            Walk(dt, distance > spec.Range ? toward : Vector3.zero, spec.Speed);
            dashTimer -= dt;
            if (dashTimer > 0f)
                return;
            dashTimer = Random.Range(1.6f, 2.6f);
            dashFor = 0.35f;
            Vector3 side = Vector3.Cross(Vector3.up, toward) * (Random.value < 0.5f ? -1f : 1f);
            dashVelocity = (side + toward * (distance > spec.Range ? 0.4f : -0.2f)).normalized * 4f;
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
            boss?.Stop();
        }
    }
}
