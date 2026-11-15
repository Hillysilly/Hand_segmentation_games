using System.Collections.Generic;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Hands;

namespace Repulsor
{
    /// <summary>
    /// The test game: orbs float in your room and you shoot them with hand-tracked weapons.
    /// Lines tagged [Repulsor] appear in the headset's log (adb logcat -s Unity).
    /// </summary>
    public sealed class RepulsorGame : MonoBehaviour
    {
        const int MaxTargets = 3;
        const float RespawnSeconds = 1.2f;
        const float HudDistance = 1.6f;
        const float HudHeight = 0.28f;
        const float HudFollowSpeed = 2.5f;
        const float MinSpawnDistance = 0.7f;
        const float WallClearance = 0.35f;
        const float LaserRadius = 0.04f;
        const float MinPalmAwayFromHead = 0.2f;
        const string SelectedColor = "#78E1FF";

        const float AuraBehind = 0.12f;   // About the middle of a person, so flames surround them.
        const float AuraInFront = 0.15f;
        const float AuraSideProbe = 0.5f;
        const float AuraDepthGap = 0.2f;   // Small, so someone standing near a wall isn't taken for the wall.

        static readonly Weapon[] Weapons = { Weapon.Repulsor, Weapon.Laser, Weapon.Shockwave, Weapon.Aura };

        [SerializeField] XROrigin origin;
        [SerializeField] RoomSense room;
        [SerializeField] RoomDim roomDim;
        [SerializeField] AuraView aura;
        [SerializeField] HandRepulsor leftHand;
        [SerializeField] HandRepulsor rightHand;
        [SerializeField] Target targetPrefab;
        [SerializeField] Blast blastPrefab;
        [SerializeField] Shockwave shockwavePrefab;
        [SerializeField] ParticleSystem explosionPrefab;
        [SerializeField] Transform hud;
        [SerializeField] Text hudText;
        [SerializeField] AudioSource audioSource;

        readonly List<Target> targets = new List<Target>();
        readonly List<XRHandSubsystem> handSubsystems = new List<XRHandSubsystem>();
        readonly StringBuilder hudBuilder = new StringBuilder();
        readonly AuraTracker auraTracker = new AuraTracker();
        XRHandSubsystem hands;
        AudioClip zap;
        AudioClip boom;
        AudioClip whoosh;
        AudioClip click;
        AudioClip ignite;
        Transform head;
        PersonDetector person;
        SuitView suits;
        float nextSpawnTime;
        int score;
        int shots;
        string shownHud;
#if DEVELOPMENT_BUILD
        float nextAuraTestCheck;
        string auraTestCommand = "";
        bool AuraTestOn => auraTestCommand.Length > 0;
#else
        const bool AuraTestOn = false;
#endif

        public Weapon CurrentWeapon { get; private set; } = Weapon.Repulsor;
        public RoomSense Room => room;

        /// <summary>
        /// The old test game (orbs, OK sign cycles weapons, aura and colour wheel). Off in the real game, where
        /// <see cref="Arena"/> decides what the hands can do.
        /// </summary>
        public bool Legacy { get; set; }
        /// <summary>
        /// The real game's aiming for fist-and-release shots: (left hand?, palm, palm direction) to a direction.
        /// Unset, or in the old test game, shots follow the palm.
        /// </summary>
        public System.Func<bool, Vector3, Vector3, Vector3> HandAim { get; set; }
        /// <summary>Bends a shot (from, direction) onto a nearby enemy. Unset, or in the old test game, no assist.</summary>
        public System.Func<Vector3, Vector3, Vector3> AssistAim { get; set; }
        /// <summary>What the hands do: the cycled weapon in the old test game, otherwise what the arena allows.</summary>
        public Weapon ArmedWeapon => Legacy ? CurrentWeapon : arenaWeapon;
        /// <summary>Multiplies damage, for the 1.5x powerup.</summary>
        public float DamageScale { get; set; } = 1f;
        public float LaserDps { get; set; } = Damage.LaserPerSecond;
        /// <summary>The palm laser pours fire instead (the Mage's level 2 flamethrower).</summary>
        public bool FlameLaser { get; set; }
        public float RepulsorDamage { get; set; } = Damage.Repulsor;
        /// <summary>Repulsor shots explode over this radius where they land (Technoman level 3). 0 for none.</summary>
        public float RepulsorAoe { get; set; }
        /// <summary>The look of fist-and-release shots: 0 comet, 1 streak (small, long tail), 2 orb (big, short tail, shell).</summary>
        public int CometStyle { get; set; }
        /// <summary>Restyles each fist-then-open shot as it's fired (the Earth character's rock orbs), if set.</summary>
        public System.Action<Blast> Dress { get; set; }
        public static readonly string[] CometStyleNames = { "A  COMET", "B  STREAK", "C  ORB", "D  TECH" };
        public static readonly Color DefaultWeaponColor = new Color(0.1f, 0.4f, 1f);
        public Color WeaponColor { get; private set; } = DefaultWeaponColor;

        /// <summary>Recolours the hands' charge orbs, aim lines and lasers, and every shot fired from now on.</summary>
        public void SetWeaponColor(Color color)
        {
            if (color == WeaponColor)
                return;
            WeaponColor = color;
            Tint.Apply(leftHand.gameObject, color);
            Tint.Apply(rightHand.gameObject, color);
        }

        /// <summary>A crackling energy ball, for charging effects. Its scale is its diameter.</summary>
        public GameObject MakeEnergyBall(Transform parent)
        {
            GameObject ball = Instantiate(blastPrefab.Head.gameObject, parent);
            Tint.Apply(ball, WeaponColor);
            return ball;
        }

        /// <summary>A copy of the hand laser's beam (ribbon, glow, burst), tinted, for enemy lasers and strikes.</summary>
        public LaserBeamView MakeLaserBeam(Transform parent, Color color)
        {
            LaserBeamView beam = Instantiate(leftHand.Beam, parent);
            beam.transform.localPosition = Vector3.zero;
            beam.Silence();
            Tint.Apply(beam.gameObject, color);
            return beam;
        }

        /// <summary>Any shot built on the comet: size and speed multiply the repulsor's, damage and blast radius are set.</summary>
        public Blast FireBlast(Vector3 position, Vector3 direction, float charge, float size, float speed, float damage, float aoe)
            => FireBlast(position, direction, charge, size, speed, damage, aoe, WeaponColor);

        public Blast FireBlast(Vector3 position, Vector3 direction, float charge, float size, float speed, float damage, float aoe, Color color)
        {
            Blast blast = Instantiate(blastPrefab, transform);
            blast.Launch(this, position, direction, charge, size, speed);
            blast.Damage = damage;
            blast.Aoe = aoe;
            blast.Color = color;
            Tint.Apply(blast.gameObject, color);
            if (room.TryDistance(position, direction.normalized, out float wall))
                blast.WallDistance = wall;
            PlayAt(position, size > 1.5f ? boom : zap);
            BlastFired?.Invoke(position, direction, size, speed, color);
            return blast;
        }

        /// <summary>Hits everything the moving shot touches that it hasn't hit yet. True if anything was hit.</summary>
        public bool HitAllAlong(Vector3 from, Vector3 to, float blastRadius, float damage, HashSet<IHittable> already)
        {
            bool any = false;
            List<IHittable> all = Hittables.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (i >= all.Count)
                    continue;
                IHittable target = all[i];
                if (target.Alive && target.Team == Team.Enemies && !already.Contains(target)
                    && HitTest.Segment(target, from, to, blastRadius))
                {
                    already.Add(target);
                    Strike(target, damage, from, "Arrow");
                    any = true;
                }
            }
            return any;
        }

        /// <summary>Damages everything within the radius, with an explosion.</summary>
        public void Explode(Vector3 center, float radius, float damage, Color color)
        {
            ParticleSystem explosion = Instantiate(explosionPrefab, center, Quaternion.identity, transform);
            explosion.transform.localScale = Vector3.one * Mathf.Max(1f, radius * 1.5f);
            Tint.Apply(explosion.gameObject, color);
            Destroy(explosion.gameObject, explosion.main.duration + explosion.main.startLifetime.constantMax);
            PlayAt(center, boom);
            List<IHittable> all = Hittables.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (i >= all.Count)
                    continue;
                IHittable target = all[i];
                if (target.Alive && target.Team == Team.Enemies
                    && HitTest.Within(target, center, radius))
                    Strike(target, damage, center, "Explosion");
            }
        }
        public XRHandSubsystem Hands => hands;
        public Transform Head => head;
        public XROrigin Origin => origin;
        public AuraView Aura => aura;
        public PersonDetector Person => person;
        public Transform TrackingSpace => origin.CameraFloorOffsetObject.transform;
        public AudioClip ClickSound => click;
        public AudioClip BoomSound => boom;
        public AudioClip WhooshSound => whoosh;
        public AudioClip ZapSound => zap;

        /// <summary>A comet-style shot left: origin, direction, size, speed, colour. For the other headset to draw.</summary>
        public event System.Action<Vector3, Vector3, float, float, Color> BlastFired;

        /// <summary>A hit landed: what was hit and the HP it lost.</summary>
        public event System.Action<IHittable, float> Dealt;
        /// <summary>Something's HP reached 0.</summary>
        public event System.Action<IHittable> Killed;

        Weapon arenaWeapon = Weapon.None;
        Arena arena;

        public void Arm(Weapon weapon) => arenaWeapon = weapon;

        void Start()
        {
            head = origin.Camera.transform;
            person = FindAnyObjectByType<PersonDetector>();
            person.Init(room);
            suits = new GameObject("Suits").AddComponent<SuitView>();
            aura.UseSuits(suits);
            zap = Sfx.Zap();
            boom = Sfx.Boom();
            whoosh = Sfx.Whoosh();
            click = Sfx.Click();
            ignite = Sfx.Ignite();

            AudioClip hum = Sfx.Hum();
            Transform trackingSpace = origin.CameraFloorOffsetObject.transform;
            leftHand.Init(this, trackingSpace, hum);
            rightHand.Init(this, trackingSpace, hum);
            arena = gameObject.AddComponent<Arena>();
            arena.Init(this, leftHand, rightHand, roomDim);
            Debug.Log("[Repulsor] Game started");
        }

        void Update()
        {
            float dt = Time.deltaTime;

            if (hands == null || !hands.running)
                FindHandSubsystem();

            if (hands != null)
            {
                leftHand.Tick(hands.leftHand, dt);
                rightHand.Tick(hands.rightHand, dt);
            }

            // The head stays at the origin until the first tracked pose arrives. Spawning then would
            // put the target on the floor.
            bool headTracked = head.localPosition != Vector3.zero;
            if (Legacy && headTracked && !AuraTestOn && targets.Count < MaxTargets && Time.time >= nextSpawnTime)
            {
                SpawnTarget();
                nextSpawnTime = Time.time + RespawnSeconds;
            }

            if (!Legacy || CurrentWeapon != Weapon.Aura)
                aura.Picker.Hide();
            else if (aura.Picker.Tick(leftHand, rightHand, head.position, dt, out Color picked))
                aura.SetTint(picked, true);

            // In the game your aura follows your own head (Arena), so the camera tracker only runs in the old mode.
            bool tracking = (Legacy || AuraTestOn) && aura.IsLit;
            person.Running = tracking;
            person.Fast = aura.IsSuit;
            if (tracking)
                aura.MoveTo(auraTracker.Tick(person, dt));
            if (tracking && aura.IsSuit)
                suits.Tick(person, dt);

            hud.gameObject.SetActive(Legacy);
            if (Legacy)
                UpdateHud(dt);
#if DEVELOPMENT_BUILD
            CheckAuraTestFile();
#endif
        }

#if DEVELOPMENT_BUILD
        // Lights the aura with no hands, so it can be captured over adb from a headset nobody is wearing.
        // The file holds a distance in metres, then optionally a style number, a colour like #2080FF, and
        // "restart" to restart the camera or "replay" to use saved pictures in place of it.
        // Remove the file to put the aura out.
        void CheckAuraTestFile()
        {
            if (Time.time < nextAuraTestCheck)
                return;
            nextAuraTestCheck = Time.time + 1f;

            string path = System.IO.Path.Combine(Application.persistentDataPath, "aura_test.txt");
            string command = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path).Trim() : "";
            if (command == auraTestCommand)
                return;
            auraTestCommand = command;

            string[] parts = command.Split(' ');
            if (command.Length == 0 || !float.TryParse(parts[0], out float distance))
            {
                aura.Extinguish();
                Debug.Log("[Repulsor] Aura test off");
                return;
            }

            // Orbs would block the view being captured.
            foreach (Target target in targets)
                Destroy(target.gameObject);
            targets.Clear();

            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            Vector3 point = head.position + forward * distance;
            aura.Ignite(point);
            auraTracker.Begin(point);
            if (parts.Length > 1 && int.TryParse(parts[1], out int style))
                aura.ShowStyle(style - 1);
            if (parts.Length > 2 && ColorUtility.TryParseHtmlString(parts[2], out Color testTint))
                aura.SetTint(testTint.linear, false);
            if (parts.Length > 3 && parts[3] == "restart")
                person.RestartCamera();
            if (parts.Length > 3 && parts[3] == "replay")
                person.StartReplay();
            room.TryDistance(head.position, forward, out float surface);
            Debug.Log($"[Repulsor] Aura test '{command}': head {head.position}, forward {forward}, surface {surface:0.00} m");
        }
#endif

        void FindHandSubsystem()
        {
            hands = null;
            handSubsystems.Clear();
            SubsystemManager.GetSubsystems(handSubsystems);
            foreach (XRHandSubsystem subsystem in handSubsystems)
            {
                if (subsystem.running)
                {
                    hands = subsystem;
                    Debug.Log("[Repulsor] Hand subsystem running");
                    break;
                }
            }
        }

        /// <summary>Puts an orb with this much HP where the player can see and reach it.</summary>
        public Target SpawnOrb(float hp)
        {
            Target target = SpawnTarget();
            target.Place(target.transform.position, hp);
            return target;
        }

        public void ClearOrbs()
        {
            foreach (Target target in targets)
                if (target != null)
                    Destroy(target.gameObject);
            targets.Clear();
        }

        Target SpawnTarget()
        {
            Vector3 position = GameMath.SpawnPoint(head.position, head.forward, 55f, 1.2f, 2.4f, -0.5f, 0.5f,
                Random.value, Random.value, Random.value);

            // Keep orbs on this side of walls and furniture, where beams can reach them.
            Vector3 toTarget = position - head.position;
            float distance = toTarget.magnitude;
            Vector3 direction = toTarget / distance;
            if (room.TryDistance(head.position, direction, out float surface) && surface - WallClearance < distance)
                position = head.position + direction * Mathf.Max(surface - WallClearance, MinSpawnDistance);

            Target target = Instantiate(targetPrefab, transform);
            target.Place(position);
            targets.Add(target);
            Debug.Log($"[Repulsor] Target spawned at {position}, head at {head.position}");
            return target;
        }

        /// <summary>The OK sign: changes the aura's style while it's lit, otherwise changes weapon.</summary>
        public void OkSign()
        {
            if (!Legacy)
                return;
            if (CurrentWeapon == Weapon.Aura && aura.IsLit)
            {
                aura.NextStyle();
                PlayAt(head.position, click);
                Debug.Log($"[Repulsor] Aura style {aura.StyleNumber}: {aura.StyleName}");
            }
            else
            {
                NextWeapon();
            }
        }

        public void NextWeapon()
        {
            CurrentWeapon = Weapons[((int)CurrentWeapon + 1) % Weapons.Length];
            PlayAt(head.position, click);
            Debug.Log($"[Repulsor] Weapon {CurrentWeapon}");
        }

        /// <summary>False when the palm is turned toward the player, such as when looking at your own hand.</summary>
        public bool PointsAwayFromHead(Vector3 palm, Vector3 palmNormal) =>
            Vector3.Dot(palmNormal, (palm - head.position).normalized) > MinPalmAwayFromHead;

        public void DimRoom() => roomDim.Hold();

        public bool AuraLit => aura.IsLit;

        /// <summary>Lights the aura at what a ray hit, behind it or in front of it depending on what it is.</summary>
        public void LightAura(Vector3 start, Vector3 direction, float distance)
        {
            Vector3 side = Vector3.Cross(Vector3.up, direction).normalized * AuraSideProbe;
            bool leftFound = room.TryDistance(start, (direction * distance - side).normalized, out float left);
            bool rightFound = room.TryDistance(start, (direction * distance + side).normalized, out float right);
            float offset = GameMath.AuraOffset(distance, leftFound, left, rightFound, right,
                AuraBehind, AuraInFront, AuraDepthGap);

            Vector3 point = start + direction * (distance + offset);
            aura.Ignite(point);
            auraTracker.Begin(point);
            PlayAt(start, whoosh);
            Debug.Log($"[Repulsor] Aura lit at {distance:0.00} m, offset {offset:0.00}, sides {left:0.00} {right:0.00}");
        }

        public void PutOutAura()
        {
            aura.Extinguish();
            Debug.Log("[Repulsor] Aura out");
        }

        public void PlayLaserIgnite(Vector3 position) => PlayAt(position, ignite);

        public void Fire(Vector3 position, Vector3 direction, float charge)
        {
            Blast blast = Instantiate(blastPrefab, transform);
            int style = Legacy ? 0 : CometStyle;
            blast.Launch(this, position, direction, charge, style == 1 ? 0.75f : style == 2 ? 1.35f : 1f, style == 1 ? 1.2f : 1f);
            if (style == 1)
                blast.TailScale = 1.9f;
            else if (style == 2)
            {
                blast.TailScale = 0.45f;
                Bubble shell = Bubble.Create("Shell", blast.Head);
                shell.transform.localScale = Vector3.one * 1.4f;
                shell.Show(WeaponColor, BubbleStyle.Energy, 0.8f);
            }
            else if (style == 3)
            {
                blast.TailScale = 1.3f;
                BallLook.ApplyTech(blast.Head, WeaponColor);
            }
            blast.Aoe = Legacy ? 0f : RepulsorAoe;
            blast.Color = WeaponColor;
            if (WeaponColor != DefaultWeaponColor)
                Tint.Apply(blast.gameObject, WeaponColor);
            // Walls and furniture stop shots.
            if (room.TryDistance(position, direction.normalized, out float wall))
                blast.WallDistance = wall;
            if (!Legacy)
                Dress?.Invoke(blast);
            PlayAt(position, zap);
            BlastFired?.Invoke(position, direction, 1f, 1f, WeaponColor);
            shots++;
            Debug.Log($"[Repulsor] Fired shot {shots}, charge {charge:0.00}");
        }

        public void FireShockwave(Vector3 position, Vector3 direction, float charge)
        {
            Shockwave wave = Instantiate(shockwavePrefab, transform);
            wave.Launch(this, position, direction, charge);
            if (WeaponColor != DefaultWeaponColor)
                Tint.Apply(wave.gameObject, WeaponColor);
            if (room.TryDistance(position, direction.normalized, out float wall))
                wave.WallDistance = wall;
            PlayAt(position, whoosh);
            shots++;
            Debug.Log($"[Repulsor] Shockwave {shots}, charge {charge:0.00}");
        }

        /// <summary>Hits the first thing the moving blast touches. Returns whether something was hit.</summary>
        public bool TryHit(Vector3 from, Vector3 to, float blastRadius, float damage = Damage.Repulsor) =>
            TryHit(from, to, blastRadius, damage, out _);

        /// <summary>As <see cref="TryHit(Vector3, Vector3, float, float)"/>, and which thing it hit.</summary>
        public bool TryHit(Vector3 from, Vector3 to, float blastRadius, float damage, out IHittable struck)
        {
            List<IHittable> all = Hittables.All;
            for (int i = 0; i < all.Count; i++)
            {
                IHittable target = all[i];
                if (target.Alive && target.Team == Team.Enemies
                    && HitTest.Segment(target, from, to, blastRadius))
                {
                    struck = target;
                    Strike(target, damage, from, "Repulsor");
                    return true;
                }
            }
            struck = null;
            return false;
        }

        /// <summary>Hits everything the shockwave's front has reached, once per wave.</summary>
        public void HitWithShockwave(Vector3 origin, Vector3 direction, float travelled, HashSet<IHittable> alreadyHit)
        {
            List<IHittable> all = Hittables.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (i >= all.Count)
                    continue;
                IHittable target = all[i];
                if (target.Alive && target.Team == Team.Enemies && !alreadyHit.Contains(target)
                    && GameMath.ConeReaches(origin, direction, Shockwave.StartRadius, Shockwave.HalfAngleDegrees,
                        travelled, HitTest.Nearest(target, origin + direction * travelled), target.Radius))
                {
                    alreadyHit.Add(target);
                    Strike(target, Damage.Shockwave, origin, "Shockwave");
                }
            }
        }

        /// <summary>Burns the nearest thing within reach. Returns whether one was hit and the beam's length.</summary>
        public bool BurnWithLaser(Vector3 start, Vector3 direction, float reach, float dt, out float length,
            float damagePerSecond = Damage.LaserPerSecond)
        {
            IHittable nearest = null;
            length = reach;
            List<IHittable> all = Hittables.All;
            for (int i = 0; i < all.Count; i++)
            {
                IHittable target = all[i];
                if (target.Alive && target.Team == Team.Enemies
                    && HitTest.Ray(target, start, direction, reach, LaserRadius, out float distance)
                    && distance < length)
                {
                    nearest = target;
                    length = distance;
                }
            }

            if (nearest == null)
                return false;

            if (Legacy && nearest is Target orb)
            {
                if (orb.Burn(dt))
                    Strike(nearest, float.MaxValue, start, "Laser");
            }
            else
            {
                Strike(nearest, damagePerSecond * dt, start, "Laser");
            }
            return true;
        }

        /// <summary>Applies one hit: damage, then the explosion if it finished the target off.</summary>
        public void Strike(IHittable target, float damage, Vector3 from, string weapon, bool scaled = true)
        {
            if (Legacy)
                damage = float.MaxValue;
            else if (scaled)
                damage *= DamageScale;
            float taken = target.TakeHit(damage, from);
            if (taken > 0f)
                Dealt?.Invoke(target, taken);
            if (target.Alive)
                return;

            Vector3 position = target.Center;
            ParticleSystem explosion = Instantiate(explosionPrefab, position, Quaternion.identity, transform);
            Destroy(explosion.gameObject, explosion.main.duration + explosion.main.startLifetime.constantMax);
            PlayAt(position, boom);
            Killed?.Invoke(target);

            if (target is Target orb)
            {
                targets.Remove(orb);
                Destroy(orb.gameObject);
                nextSpawnTime = Time.time + RespawnSeconds;
            }
            else if (target is Component component)
            {
                Destroy(component.gameObject);
            }
            if (Legacy)
            {
                score++;
                Debug.Log($"[Repulsor] Hit! {weapon}, score {score}");
            }
        }

        AudioSource[] voices;
        int nextVoice;

        /// <summary>
        /// Plays a sound from a point in the room. Each sound is placed by moving a source there, so the sources live
        /// on their own objects at the scene root: the scene's own source sits on this object, which everything in the
        /// game hangs off, and moving it moved every enemy, menu and shot along with it. Several voices take turns, so
        /// a new sound doesn't drag one that's still playing across the room.
        /// </summary>
        public void PlayAt(Vector3 position, AudioClip clip)
        {
            if (voices == null)
                CreateVoices();
            AudioSource voice = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;
            voice.transform.position = position;
            voice.PlayOneShot(clip);
        }

        void CreateVoices()
        {
            const int Count = 8;
            voices = new AudioSource[Count];
            for (int i = 0; i < Count; i++)
            {
                var voice = new GameObject("Sound " + i).AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.outputAudioMixerGroup = audioSource.outputAudioMixerGroup;
                voice.volume = audioSource.volume;
                voice.spatialBlend = audioSource.spatialBlend;
                voice.spread = audioSource.spread;
                voice.dopplerLevel = audioSource.dopplerLevel;
                voice.rolloffMode = audioSource.rolloffMode;
                voice.minDistance = audioSource.minDistance;
                voice.maxDistance = audioSource.maxDistance;
                voice.priority = audioSource.priority;
                voices[i] = voice;
            }
        }

        void UpdateHud(float dt)
        {
            // The panel drifts to stay in view, rather than being stuck to your face.
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude > 0.001f)
            {
                Vector3 wanted = head.position + forward.normalized * HudDistance + Vector3.up * HudHeight;
                hud.position = Vector3.Lerp(hud.position, wanted, 1f - Mathf.Exp(-HudFollowSpeed * dt));
                hud.rotation = Quaternion.LookRotation(hud.position - head.position);
            }

            hudBuilder.Clear();
            hudBuilder.Append("SCORE ").Append(score).Append('\n');
            foreach (Weapon weapon in Weapons)
            {
                string name = weapon.ToString().ToUpperInvariant();
                if (weapon == CurrentWeapon)
                    hudBuilder.Append("<b><color=").Append(SelectedColor).Append(">[ ").Append(name).Append(" ]</color></b>   ");
                else
                    hudBuilder.Append(name).Append("   ");
            }
            hudBuilder.Append('\n').Append(HandHint());

            string text = hudBuilder.ToString();
            if (text != shownHud)
            {
                shownHud = text;
                hudText.text = text;
            }
        }

        string HandHint()
        {
            if (hands == null)
                return "Starting hand tracking...";
            if (!leftHand.IsTracked && !rightHand.IsTracked)
                return "Put the controllers down and show your hands";
            switch (CurrentWeapon)
            {
                case Weapon.Laser:
                    return "Hold your palm out to fire. OK sign to switch";
                case Weapon.Aura:
                    return aura.IsLit
                        ? $"Style {aura.StyleNumber} of {aura.StyleCount}: {aura.StyleName}. OK sign for next, palm to face for colour, fist to end"
                        : "Hold your palm at someone to ignite. Turn your palm to your face for colour";
                default:
                    return "Fist to charge, snap open to fire. OK sign to switch";
            }
        }
    }
}
