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

        void Start()
        {
            head = origin.Camera.transform;
            person = FindAnyObjectByType<PersonDetector>();
            person.Init(room);
            zap = Sfx.Zap();
            boom = Sfx.Boom();
            whoosh = Sfx.Whoosh();
            click = Sfx.Click();
            ignite = Sfx.Ignite();

            AudioClip hum = Sfx.Hum();
            Transform trackingSpace = origin.CameraFloorOffsetObject.transform;
            leftHand.Init(this, trackingSpace, hum);
            rightHand.Init(this, trackingSpace, hum);
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
            if (headTracked && !AuraTestOn && targets.Count < MaxTargets && Time.time >= nextSpawnTime)
            {
                SpawnTarget();
                nextSpawnTime = Time.time + RespawnSeconds;
            }

            if (CurrentWeapon != Weapon.Aura)
                aura.Picker.Hide();
            else if (aura.Picker.Tick(leftHand, rightHand, head.position, dt, out Color picked))
                aura.SetTint(picked, true);

            person.Running = aura.IsLit;
            if (aura.IsLit)
                aura.MoveTo(auraTracker.Tick(person, dt));

            UpdateHud(dt);
#if DEVELOPMENT_BUILD
            if (headTracked)
                CheckAuraTestFile();
#endif
        }

#if DEVELOPMENT_BUILD
        // Lights the aura with no hands, so it can be captured over adb from a headset nobody is wearing.
        // The file holds a distance in metres, then optionally a style number and a colour like #2080FF.
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

        void SpawnTarget()
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
        }

        /// <summary>The OK sign: changes the aura's style while it's lit, otherwise changes weapon.</summary>
        public void OkSign()
        {
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
            blast.Launch(this, position, direction, charge);
            PlayAt(position, zap);
            shots++;
            Debug.Log($"[Repulsor] Fired shot {shots}, charge {charge:0.00}");
        }

        public void FireShockwave(Vector3 position, Vector3 direction, float charge)
        {
            Shockwave wave = Instantiate(shockwavePrefab, transform);
            wave.Launch(this, position, direction, charge);
            PlayAt(position, whoosh);
            shots++;
            Debug.Log($"[Repulsor] Shockwave {shots}, charge {charge:0.00}");
        }

        /// <summary>Destroys the first target the moving blast touches. Returns whether one was hit.</summary>
        public bool TryHit(Vector3 from, Vector3 to, float blastRadius)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                Target target = targets[i];
                if (GameMath.SegmentHitsSphere(from, to, target.transform.position, target.Radius + blastRadius))
                {
                    DestroyTarget(i, "Repulsor");
                    return true;
                }
            }
            return false;
        }

        /// <summary>Destroys every target the shockwave's front has reached.</summary>
        public void HitWithShockwave(Vector3 origin, Vector3 direction, float travelled)
        {
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                Target target = targets[i];
                if (GameMath.ConeReaches(origin, direction, Shockwave.StartRadius, Shockwave.HalfAngleDegrees,
                        travelled, target.transform.position, target.Radius))
                    DestroyTarget(i, "Shockwave");
            }
        }

        /// <summary>Burns the nearest target within reach. Returns whether one was hit and the beam's length.</summary>
        public bool BurnWithLaser(Vector3 start, Vector3 direction, float reach, float dt, out float length)
        {
            int nearest = -1;
            length = reach;
            for (int i = 0; i < targets.Count; i++)
            {
                Target target = targets[i];
                if (GameMath.RayHitsSphere(start, direction, target.transform.position,
                        target.Radius + LaserRadius, out float distance) && distance < length)
                {
                    nearest = i;
                    length = distance;
                }
            }

            if (nearest < 0)
                return false;

            if (targets[nearest].Burn(dt))
                DestroyTarget(nearest, "Laser");
            return true;
        }

        void DestroyTarget(int index, string weapon)
        {
            Target target = targets[index];
            Vector3 position = target.transform.position;

            ParticleSystem explosion = Instantiate(explosionPrefab, position, Quaternion.identity, transform);
            Destroy(explosion.gameObject, explosion.main.duration + explosion.main.startLifetime.constantMax);
            PlayAt(position, boom);

            targets.RemoveAt(index);
            Destroy(target.gameObject);
            score++;
            nextSpawnTime = Time.time + RespawnSeconds;
            Debug.Log($"[Repulsor] Hit! {weapon}, score {score}");
        }

        void PlayAt(Vector3 position, AudioClip clip)
        {
            audioSource.transform.position = position;
            audioSource.PlayOneShot(clip);
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
                        ? $"Style {aura.StyleNumber} of {aura.StyleCount}: {aura.StyleName}. OK sign for next, palm to face for colour, fist to put out"
                        : "Hold your palm at someone to ignite. Turn your palm to your face for colour";
                default:
                    return "Fist to charge, snap open to fire. OK sign to switch";
            }
        }
    }
}
