using UnityEngine;

namespace Repulsor
{
    public enum EnemyKind { Robot, Drone }

    /// <summary>
    /// A PVE enemy. Robots walk on the floor toward the player, stop at shooting range and fire slow bolts with
    /// poor aim. Drones fly at head height, weave sideways and fire less often; they're smaller and harder to hit.
    /// Both appear inside a spawn bubble. The look is a placeholder until a licensed model is picked.
    /// </summary>
    public sealed class Enemy : MonoBehaviour, IHittable
    {
        public const float SpawnSeconds = 0.9f;
        const float RobotHp = 30f;
        const float DroneHp = 15f;
        const float RobotSpeed = 0.55f;
        const float DroneSpeed = 0.9f;
        const float RobotRange = 2.2f;
        const float DroneRange = 1.8f;
        const float RobotAimError = 9f;    // degrees: they miss a fair amount
        const float DroneAimError = 12f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        Arena arena;
        Transform head;
        Bubble spawnBubble;
        Renderer body;
        MaterialPropertyBlock properties;
        Color color;
        float age;
        float fireTimer;
        float flash;
        float weave;
        float weaveSpeed;

        public EnemyKind Kind { get; private set; }
        public float Radius => Kind == EnemyKind.Robot ? 0.28f : 0.16f;
        public Vector3 Center => transform.position;
        public bool Alive => Hp > 0f;
        public Team Team => Team.Enemies;
        public float Hp { get; private set; }
        public float MaxHp { get; private set; }
        public Vector3 BarPoint => transform.position + Vector3.up * (Radius + 0.15f);
        public bool Spawning => age < SpawnSeconds;

        void OnEnable() => Hittables.Add(this);
        void OnDisable() => Hittables.Remove(this);

        public static Enemy Spawn(EnemyKind kind, Vector3 position, Arena owner, Transform head, Transform parent)
        {
            var go = new GameObject(kind.ToString(), typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.GetComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>("SphereMesh");
            var enemy = go.AddComponent<Enemy>();
            enemy.Kind = kind;
            enemy.arena = owner;
            enemy.head = head;
            enemy.Hp = enemy.MaxHp = kind == EnemyKind.Robot ? RobotHp : DroneHp;
            enemy.body = go.GetComponent<MeshRenderer>();
            enemy.body.sharedMaterial = Resources.Load<Material>("EnemyPlaceholder");
            enemy.properties = new MaterialPropertyBlock();
            enemy.color = kind == EnemyKind.Robot ? new Color(0.85f, 0.2f, 0.15f) : new Color(0.95f, 0.55f, 0.1f);
            enemy.fireTimer = Random.Range(1.5f, 3f);
            enemy.weave = Random.value * 10f;
            enemy.weaveSpeed = Random.Range(0.8f, 1.4f);
            enemy.spawnBubble = Bubble.Create("Spawn Bubble", parent);
            enemy.Shape();
            return enemy;
        }

        void Shape()
        {
            // Placeholder bodies: a tall pill for robots, a flat disc for drones.
            transform.localScale = Kind == EnemyKind.Robot
                ? new Vector3(0.5f, 1.3f, 0.4f)
                : new Vector3(0.36f, 0.14f, 0.36f);
        }

        public float TakeHit(float damage, Vector3 from)
        {
            if (!Alive || Spawning)
                return 0f;
            float taken = Mathf.Min(damage, Hp);
            Hp -= taken;
            flash = 0.12f;
            return taken;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            TickSpawnBubble();
            flash = Mathf.Max(0f, flash - dt);
            properties.SetColor(BaseColor, flash > 0f ? Color.white : color);
            body.SetPropertyBlock(properties);
            if (Spawning || !Alive)
                return;

            Vector3 target = head.position;
            Vector3 toPlayer = target - transform.position;
            Vector3 flat = Vector3.ProjectOnPlane(toPlayer, Vector3.up);
            float distance = flat.magnitude;
            Vector3 direction = distance > 0.01f ? flat / distance : Vector3.forward;
            transform.rotation = Quaternion.LookRotation(direction);

            if (Kind == EnemyKind.Robot)
            {
                if (distance > RobotRange)
                    transform.position += direction * (RobotSpeed * dt);
                transform.position = new Vector3(transform.position.x, 0.65f + Mathf.Abs(Mathf.Sin(age * 6f)) * 0.03f, transform.position.z);
            }
            else
            {
                weave += dt * weaveSpeed;
                Vector3 side = Vector3.Cross(Vector3.up, direction) * (Mathf.Sin(weave * 2f) * 0.6f);
                Vector3 move = (distance > DroneRange ? direction : Vector3.zero) + side;
                transform.position += move * (DroneSpeed * dt);
                float height = target.y + 0.2f + Mathf.Sin(weave * 1.3f) * 0.25f;
                transform.position = new Vector3(transform.position.x,
                    Mathf.Lerp(transform.position.y, height, 1f - Mathf.Exp(-2f * dt)), transform.position.z);
            }

            fireTimer -= dt;
            if (fireTimer <= 0f && distance < 6f)
            {
                fireTimer = Kind == EnemyKind.Robot ? Random.Range(2.2f, 3.2f) : Random.Range(3f, 4.5f);
                Fire(target + Vector3.down * 0.25f);
            }
        }

        void Fire(Vector3 aimPoint)
        {
            Vector3 muzzle = transform.position + transform.forward * (Radius + 0.05f)
                + (Kind == EnemyKind.Robot ? Vector3.up * 0.2f : Vector3.zero);
            Vector3 aim = (aimPoint - muzzle).normalized;
            float error = Kind == EnemyKind.Robot ? RobotAimError : DroneAimError;
            aim = Quaternion.Euler(Random.Range(-error, error), Random.Range(-error, error), 0f) * aim;
            arena.FireEnemyBolt(muzzle, aim, this);
        }

        void TickSpawnBubble()
        {
            if (spawnBubble == null)
                return;
            float t = age / SpawnSeconds;
            if (t >= 1.2f)
            {
                Destroy(spawnBubble.gameObject);
                return;
            }
            // Grows around the spot, the enemy fades in inside it, then the bubble pops.
            float size = Kind == EnemyKind.Robot ? 1.6f : 0.8f;
            spawnBubble.transform.position = transform.position;
            spawnBubble.transform.localScale = Vector3.one * (size * Mathf.Clamp01(t * 2.5f) * (1f + Mathf.Max(0f, t - 1f) * 2f));
            spawnBubble.Show(new Color(0.8f, 0.3f, 1f), BubbleStyle.Energy, t < 1f ? 0.9f : Mathf.Clamp01((1.2f - t) * 5f));
            body.enabled = t > 0.4f;
        }

        void OnDestroy()
        {
            if (spawnBubble != null)
                Destroy(spawnBubble.gameObject);
        }
    }

    /// <summary>An enemy's shot: a slow glowing bolt you can dodge or block. Stops at walls.</summary>
    public sealed class EnemyBolt : MonoBehaviour
    {
        public const float Speed = 4f;
        public const float Damage = 4f;
        const float PlayerRadius = 0.3f;

        Arena arena;
        Transform head;
        Vector3 velocity;
        float life;

        public static void Launch(Vector3 position, Vector3 direction, float range, Arena owner, Transform head, Transform parent)
        {
            var go = new GameObject("Enemy Bolt", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>("SphereMesh");
            go.GetComponent<MeshRenderer>().sharedMaterial = Resources.Load<Material>("EnemyBolt");
            go.transform.position = position;
            go.transform.rotation = Quaternion.LookRotation(direction);
            go.transform.localScale = new Vector3(0.05f, 0.05f, 0.22f);
            var bolt = go.AddComponent<EnemyBolt>();
            bolt.arena = owner;
            bolt.head = head;
            bolt.velocity = direction * Speed;
            bolt.life = Mathf.Min(range, 12f) / Speed;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector3 from = transform.position;
            Vector3 to = from + velocity * dt;
            transform.position = to;
            life -= dt;

            // The body is roughly a sphere centred below the eyes.
            Vector3 chest = head.position + Vector3.down * 0.3f;
            if (GameMath.SegmentHitsSphere(from, to, chest, PlayerRadius))
            {
                arena.HurtPlayer(Damage, "enemy bolt");
                Destroy(gameObject);
                return;
            }
            if (life <= 0f)
                Destroy(gameObject);
        }
    }
}
