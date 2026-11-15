using System.Collections.Generic;
using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// Enemies in the match: which kind each wave sends (<see cref="WavePlan"/>), where they appear (<see cref="RoomMap"/>:
    /// open floor as far from the players as the room allows), the shots they fire, how those hit the player's body,
    /// and the boss's health bar.
    /// </summary>
    public sealed partial class Arena
    {
        /// <summary>Enemies appear at least this far away, then walk in.</summary>
        public static float SpawnMinDistance = 2f;
        public static float SpawnMaxDistance = 8f;
        /// <summary>Your body for enemy hits: a capsule from your head down this far, this thick.</summary>
        public static float BodyLength = 0.75f;
        public static float BodyRadius = 0.2f;

        readonly RoomMap roomMap = new RoomMap();
        readonly List<Vector3> playerSpots = new List<Vector3>();
        readonly List<Vector3> enemySpots = new List<Vector3>();
        Enemy boss;

        public RepulsorGame Game => game;
        public float FloorY => game.Origin.transform.position.y;
        public RoomMap Map => roomMap;

        /// <summary>Learns the room every frame: the depth scan, and Space Setup's floor when there is one.</summary>
        void TickRoomMap()
        {
            if (game.Room == null)
                return;
            roomMap.Scan(game.Room, head.position, head.forward, FloorY);
            roomMap.ReadPlanes(game.Room.Planes, Time.unscaledTime);
        }

        void SpawnEnemy()
        {
            EnemyKind kind = waves.BossWave
                ? (BossAlive ? WavePlan.BossHelper(Random.value) : WavePlan.FinalBoss)
                : WavePlan.KindFor(Mathf.Max(1, waves.Wave), Random.value);
            SpawnEnemy(kind);
        }

        bool BossAlive => boss != null && boss.Alive;

        Enemy SpawnEnemy(EnemyKind kind)
        {
            Vector3 point = SpawnPoint(kind, out string where);
            point.y = EnemySpec.Of(kind).Flies ? head.position.y + 0.2f : FloorY + Enemy.MiddleHeight(kind);
            Enemy enemy = Enemy.Spawn(kind, point, this, head, foes, ++nextEnemyId);
            enemies.Add(enemy);
            if (EnemySpec.IsBoss(kind))
            {
                boss = enemy;
                hud.Message("BOSS", EnemySpec.Of(kind).Name, 2.5f);
            }
            else
            {
                OverheadBar.Attach(enemy, head, foes, false);
            }
            game.PlayAt(point, spawnSound);
            Log($"{kind} spawned {Vector3.Distance(Flat(point), Flat(head.position)):0.0} m away ({where}), "
                + $"{Vector3.SignedAngle(Vector3.ProjectOnPlane(head.forward, Vector3.up), Vector3.ProjectOnPlane(point - head.position, Vector3.up), Vector3.up):0} deg from where you face");
            return enemy;
        }

        /// <summary>The boss calls in helpers: they appear near it, and count toward the wave.</summary>
        public void SpawnBossHelpers(Enemy caller, int count)
        {
            for (int i = 0; i < count; i++)
            {
                EnemyKind kind = WavePlan.BossHelper(Random.value);
                waves.AddExtra();
                Enemy helper = SpawnEnemy(kind);
                Vector3 side = caller.transform.right * (i % 2 == 0 ? -1.3f : 1.3f);
                Vector3 near = caller.transform.position + side;
                near.y = helper.transform.position.y;
                helper.transform.position = near;
            }
            hud.Message("REINFORCEMENTS", "", 1.2f);
        }

        /// <summary>
        /// Where the next enemy appears: the room map's best spot (far from everyone, open floor), or, while the map
        /// knows too little, the old way: somewhere ahead with the most room.
        /// </summary>
        Vector3 SpawnPoint(EnemyKind kind, out string where)
        {
            playerSpots.Clear();
            playerSpots.Add(head.position);
            if (Multiplayer && remote.Visible && !remote.Latest.Dead)
                playerSpots.Add(remote.Latest.Head);
            enemySpots.Clear();
            foreach (IHittable foe in enemies)
                if (Hittables.IsLive(foe))
                    enemySpots.Add(foe.Center);
            float min = EnemySpec.IsBoss(kind) ? 2.6f : SpawnMinDistance;
            float radius = EnemySpec.Of(kind).Radius;
            if (roomMap.TryPick(playerSpots, enemySpots, min, SpawnMaxDistance, InsideFurniture, radius, out Vector3 point, out where))
                return point;
            Vector3 direction = SpawnDirection(radius, out float distance, out string how);
            where = how + ", map not ready";
            return head.position + direction * distance;
        }

        bool InsideFurniture(Vector3 floorPoint) => room != null && room.Blocks(floorPoint + Vector3.up * 0.3f, 0.3f);

        /// <summary>
        /// The fallback spawn direction, while the map knows too little: within 40 degrees of where you face (the
        /// headset's depth only covers what's in view), wherever there's the most room. Each direction is measured
        /// with three depth rays as wide as the enemy, and it appears at least its own radius plus 30 cm short of
        /// whatever is there. A direction the depth can't measure counts as short, not as open, so a blind spot never
        /// wins over a measured one. Rays start 0.5 m out so your own hands aren't walls.
        /// </summary>
        Vector3 SpawnDirection(float radius, out float distance, out string how)
        {
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.forward;
            forward.Normalize();
            const int Tries = 7;
            const float Far = 4.5f;
            const float Blind = 1.9f;
            Vector3 bestDirection = forward;
            float bestScore = -1f;
            float free = Blind;
            bool measured = false;
            float offset = Random.Range(-5f, 5f);
            for (int i = 0; i < Tries; i++)
            {
                float angle = Mathf.Lerp(-40f, 40f, i / (Tries - 1f)) + offset;
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                Vector3 side = Vector3.Cross(Vector3.up, direction) * radius;
                float space = float.MaxValue;
                bool known = false;
                for (int k = -1; k <= 1; k++)
                {
                    // Aimed at where its body would be: chest height, and each edge.
                    Vector3 start = head.position + direction * 0.5f + side * k + Vector3.down * 0.4f;
                    if (!game.Room.TryDistance(start, direction, out float wall))
                        continue;
                    known = true;
                    space = Mathf.Min(space, wall + 0.5f);
                }
                if (!known)
                    space = Blind;
                float score = Mathf.Min(space, Far + 0.5f) + Random.Range(0f, 1.2f) + (known ? 1f : 0f);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestDirection = direction;
                    free = space;
                    measured = known;
                }
            }
            float margin = radius + 0.3f;
            bool atWall = measured && free <= Far + margin;
            distance = !measured ? Blind - 0.2f : atWall ? Mathf.Max(1.2f, free - margin) : Random.Range(2.5f, Far);
            how = !measured ? "close ahead, no depth there" : atWall ? "short of a wall" : "ahead";
            return bestDirection;
        }

        static bool ShotSendsDirection(EnemyShot shot) => shot != EnemyShot.Missile && shot != EnemyShot.Strike;

        /// <summary>
        /// An enemy attacks: starts it here and tells the other headset. For lasers <paramref name="b"/> is the
        /// direction; for missiles the target point; strikes use only <paramref name="a"/>.
        /// Returns the laser, so the shooter can cancel it if it dies while charging.
        /// </summary>
        public EnemyLaser EnemyFires(EnemyShot shot, Vector3 a, Vector3 b, Enemy shooter)
        {
            if (Multiplayer && net.Connected)
                net.SendEnemyBolt((byte)shot, frame.ToShared(a), ShotSendsDirection(shot) ? frame.DirectionToShared(b) : frame.ToShared(b));
            return StartShot(shot, a, b, shooter);
        }

        EnemyLaser StartShot(EnemyShot shot, Vector3 a, Vector3 b, Enemy shooter)
        {
            switch (shot)
            {
                case EnemyShot.Missile:
                    Missile.Launch(this, a, b, foes, shooter);
                    game.PlayAt(a, game.WhooshSound);
                    return null;
                case EnemyShot.Strike:
                    TrackingStrike.Drop(this, a, foes);
                    return null;
                case EnemyShot.Sniper:
                    game.PlayAt(a, enemyShotSound);
                    return EnemyLaser.Fire(this, a, b, 1.5f, 14f, 1.6f, foes, null, 0.9f);
                case EnemyShot.BossLaser:
                    return EnemyLaser.Fire(this, a, b, 1f, 8f, 1.3f, foes);
                case EnemyShot.QuickLaser:
                    game.PlayAt(a, enemyShotSound);
                    return EnemyLaser.Fire(this, a, b, 0.55f, 4f, 0.8f, foes);
                default:
                    game.PlayAt(a, enemyShotSound);
                    return EnemyLaser.Fire(this, a, b, 0.75f, 6f, 1f, foes);
            }
        }

        /// <summary>Your body, as enemies see it: a capsule from your head down to about your waist.</summary>
        Vector3 BodyTop => head.position;
        Vector3 BodyBottom => head.position + Vector3.down * BodyLength;
        bool CanBeHit => phase == Phase.Playing && !player.IsDead;

        /// <summary>A ball of this radius moving from one point to another touches your body.</summary>
        public bool SegmentHitsPlayer(Vector3 from, Vector3 to, float radius)
        {
            if (!CanBeHit)
                return false;
            GameMath.ClosestOnSegments(from, to, BodyBottom, BodyTop, out float s, out float t);
            Vector3 a = from + (to - from) * s;
            Vector3 b = BodyBottom + (BodyTop - BodyBottom) * t;
            return (a - b).magnitude <= radius + BodyRadius;
        }

        /// <summary>Your body is within this distance of a point (blasts).</summary>
        public bool SphereHitsPlayer(Vector3 centre, float radius) => SegmentHitsPlayer(centre, centre, radius);

        /// <summary>You're standing within this distance of a floor point (strikes from above).</summary>
        public bool PlayerStandsWithin(Vector3 floorPoint, float radius) =>
            CanBeHit && Vector3.Distance(Flat(head.position), Flat(floorPoint)) < radius + BodyRadius;

        /// <summary>
        /// Reads the room's light from the camera about once a second while nobody is fighting (menus, the countdown,
        /// between rounds) and lights the enemies to match. During a fight the last reading stays.
        /// </summary>
        void TickRoomLight(float dt)
        {
            roomLight.Tick(dt);
            bool fighting = phase == Phase.Playing && waves != null && waves.State == WaveState.Fighting;
            lightSampleIn -= dt;
            if (fighting || lightSampleIn > 0f || game.Person == null)
                return;
            lightSampleIn = 1f;
            if (!game.Person.TrySampleLight(out Color average))
                return;
            roomLight.Sample(average);
            if (Mathf.Abs(roomLight.Level - lightLogged) > 0.15f)
            {
                lightLogged = roomLight.Level;
                Log($"Room light: level {roomLight.Level:0.00}, tint ({roomLight.Tint.r:0.00}, {roomLight.Tint.g:0.00}, {roomLight.Tint.b:0.00})");
            }
        }

        float buriedCheck;
        int buriedNext;

        /// <summary>
        /// Looks for enemies stuck in or behind a wall, one every 0.15 s: the room map says its spot is solid (or off
        /// Space Setup's floor), or, while it's in view, the headset's depth sees a real surface well in front of both
        /// its middle and its top twice running. Those are told to come out (Enemy.Buried).
        /// </summary>
        void TickBuried(float dt)
        {
            buriedCheck -= dt;
            if (buriedCheck > 0f || enemies.Count == 0 || game.Room == null)
                return;
            buriedCheck = 0.15f;
            buriedNext = (buriedNext + 1) % enemies.Count;
            if (!(enemies[buriedNext] is Enemy enemy) || enemy == null || !enemy.Alive || enemy.Spawning || enemy.IsProxy
                || enemy.Webbed || enemy.IsBoss || enemy.Practice)
                return;
            bool was = enemy.Buried;
            bool walled = roomMap.Walled(enemy.Center, 0f);
            Vector3 toEnemy = enemy.Center - head.position;
            float distance = toEnemy.magnitude;
            if (distance > 0.8f && Vector3.Angle(head.forward, toEnemy) < 35f)
            {
                bool hidden = HiddenBehindSurface(enemy.Center, enemy.Radius) && HiddenBehindSurface(enemy.Top, enemy.Radius);
                enemy.HiddenChecks = hidden ? enemy.HiddenChecks + 1 : 0;
            }
            enemy.Buried = walled || enemy.HiddenChecks >= 2;
            if (enemy.Buried && !was)
                Log($"{enemy.Kind} {enemy.Id} is in a wall ({(walled ? "room map" : "hidden behind a surface")}); calling it out");
        }

        /// <summary>The headset's depth finds a real surface on the line to this point, well short of it.</summary>
        bool HiddenBehindSurface(Vector3 point, float radius)
        {
            Vector3 to = point - head.position;
            float distance = to.magnitude;
            return game.Room.TryDistance(head.position, to / distance, out float surface) && surface < distance - radius - 0.25f;
        }

        float standingHeadY = float.NaN;

        /// <summary>Your usual head height, learnt slowly, so a quick jump stands out above it.</summary>
        void TrackStanding(float dt)
        {
            float y = head.position.y;
            if (float.IsNaN(standingHeadY))
                standingHeadY = y;
            // Follows slowly (a jump is over long before it catches up), and drops quickly if you crouch or sit.
            float rate = y < standingHeadY ? 3f : 0.4f;
            standingHeadY = Mathf.Lerp(standingHeadY, y, 1f - Mathf.Exp(-rate * dt));
        }

        /// <summary>Your head is this much above where it usually is: you're in the air.</summary>
        public bool PlayerJumping(float clearance) => !float.IsNaN(standingHeadY) && head.position.y - standingHeadY > clearance;

        /// <summary>How far you stand from a floor point, along the floor.</summary>
        public float PlayerFloorDistance(Vector3 floorPoint) =>
            CanBeHit ? Vector3.Distance(Flat(head.position), Flat(floorPoint)) : float.MaxValue;

        /// <summary>Your shield is up and facing something coming from <paramref name="from"/>: it takes the hit (and flashes).</summary>
        public bool PlayerShieldBlocks(Vector3 from)
        {
            if (!CanBeHit || !player.Shield.Up)
                return false;
            Vector3 toSource = Vector3.ProjectOnPlane(from - head.position, Vector3.up).normalized;
            Vector3 facing = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (Vector3.Dot(toSource, facing) < 0.2f)
                return false;
            shield.Flash();
            return true;
        }

        /// <summary>You're standing where something is about to hit (the boss's strikes): the view goes slightly red.</summary>
        public void WarnPlayer(float amount)
        {
            if (CanBeHit)
                hurtFlash.Warn(amount);
        }

        /// <summary>A laser beam from one point to another: hurts you if it crosses your body.</summary>
        public void LaserHitsPlayer(Vector3 from, Vector3 to, float thickness, float damage)
        {
            if (SegmentHitsPlayer(from, to, thickness))
                HurtPlayer(damage, "enemy laser");
            // A beam that ended on your raised shield: it flashes where it took the hit.
            else if (CanBeHit && player.Shield.Up && Vector3.Distance(to, head.position + Vector3.down * 0.35f) < ShieldView.Radius + 0.05f)
                shield.Flash();
        }

        /// <summary>How far a beam goes before your raised shield stops it (or <paramref name="length"/> if it doesn't).</summary>
        public float ShieldStop(Vector3 from, Vector3 direction, float length)
        {
            if (!CanBeHit || !player.Shield.Up)
                return length;
            Vector3 centre = head.position + Vector3.down * 0.35f;
            if (GameMath.RayHitsSphere(from, direction, centre, ShieldView.Radius, out float distance) && distance < length && distance > 0f)
                return distance;
            return length;
        }

        /// <summary>The boss's bar across the top of your view, while it's alive.</summary>
        void ShowBossBar()
        {
            bool show = phase == Phase.Playing && boss != null && boss.Alive;
            hud.Boss(show ? boss.Spec.Name : null, show ? boss.Hp / boss.MaxHp : 0f);
            if (boss != null && !boss.Alive)
                boss = null;
        }

#if UNITY_EDITOR
        /// <summary>PlayMode test: spawn one enemy of a kind, the way a wave would.</summary>
        public Enemy TestSpawn(EnemyKind kind)
        {
            waves.AddExtra();
            return SpawnEnemy(kind);
        }

        /// <summary>PlayMode test: never lose HP, so a long fight can run.</summary>
        public void TestGod(bool on) => player.God = on;

        /// <summary>PlayMode test: hit an enemy as an attack would.</summary>
        public void TestHurt(IHittable target, float damage) => game.Strike(target, damage, target.Center, "test");

        public bool TestBossBarShown => hud.BossShown;
#endif
    }
}
