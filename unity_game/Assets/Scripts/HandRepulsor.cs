using UnityEngine;
using UnityEngine.XR.Hands;

namespace Repulsor
{
    /// <summary>One hand's weapons: reads the tracked joints, then charges and fires or runs the laser.</summary>
    public sealed class HandRepulsor : MonoBehaviour
    {
        const float PalmOffset = 0.07f;      // charge orb sits this far off the palm
        const float MaxOrbDiameter = 0.1f;
        const float AimLength = 3f;
        const float LaserFollowSpeed = 18f;
        const float AuraIgniteSeconds = 0.8f;
        const float AuraPutOutSeconds = 0.5f;
        const float AuraMinDistance = 0.6f;   // Anything closer is your own hand or arm, not a target.

        [SerializeField] Handedness handedness = Handedness.Left;
        [SerializeField] Transform chargeOrb;
        [SerializeField] LineRenderer aimLine;
        [SerializeField] LaserBeamView laserBeam;
        [SerializeField] AudioSource laserHum;

        readonly RepulsorGesture repulsor = new RepulsorGesture();
        readonly AimLock aim = new AimLock();
        readonly LaserTrigger laser = new LaserTrigger();
        readonly BeamLength beamLength = new BeamLength();
        readonly PinchDetector pinch = new PinchDetector();
        RepulsorGame game;
        Transform trackingSpace;
        HandGesture gesture = HandGesture.None;
        readonly GestureHold gestureHold = new GestureHold();
        /// <summary>How long a hand can be lost before its weapons stop.</summary>
        public const float LostGraceSeconds = 0.3f;
        float lostFor;
        Weapon weapon;
        bool lasing;
        Vector3 laserDirection;
        Vector3 surfaceNormal;
        float auraAim;
        float auraFist;
        bool auraPlaced;
        int laserFrames;
        int surfaceFrames;
        bool pinched;
        /// <summary>Seconds the laser stays off after a blast, so the snap open that fired it doesn't also start the beam.</summary>
        float laserBlockedFor;
        const float LaserBlockSeconds = 0.4f;
        Flamethrower flames;

        /// <summary>This hand's laser, to copy for other beams (enemies use the same look in red).</summary>
        public LaserBeamView Beam => laserBeam;
        public bool IsTracked { get; private set; }
        public bool IsOpen => IsTracked && gesture == HandGesture.Open;
        public Vector3 PalmPosition { get; private set; }
        public Vector3 PalmNormal { get; private set; }
        public Vector3 IndexTip { get; private set; }
        public HandGesture Gesture => gesture;

        HandBeam shownBeam;
        int shownFrame = -10;

        /// <summary>What this hand drew this frame or the last (laser, flames or blade), for the other headset.</summary>
        public HandBeam ShownBeam => Time.frameCount - shownFrame <= 1 ? shownBeam : HandBeam.None;
        public Vector3 ShownFrom { get; private set; }
        public Vector3 ShownTo { get; private set; }

        void Shown(HandBeam kind, Vector3 from, Vector3 to)
        {
            shownBeam = kind;
            shownFrame = Time.frameCount;
            ShownFrom = from;
            ShownTo = to;
        }

        /// <summary>Draws the laser beam as a sword blade this frame (the Saber). Only while the hand has no weapon.</summary>
        public void ShowBlade(Vector3 hilt, Vector3 tip)
        {
            laserBeam.Aim(hilt, tip, false, Vector3.up);
            Shown(HandBeam.Blade, hilt, tip);
            if (!laserHum.isPlaying)
                laserHum.Play();
            bladeShown = true;
        }

        bool bladeShown;

        void LateUpdate()
        {
            if (!bladeShown && !lasing && laserHum.isPlaying)
                laserHum.Stop();
            bladeShown = false;
        }

        public void Init(RepulsorGame owner, Transform handTrackingSpace, AudioClip hum)
        {
            game = owner;
            trackingSpace = handTrackingSpace;
            laserHum.clip = hum;
            weapon = game.ArmedWeapon;
            StopWeapons();
        }

        public void Tick(XRHand hand, float dt)
        {
            if (hand.isTracked != IsTracked)
            {
                IsTracked = hand.isTracked;
                Debug.Log($"[Repulsor] {handedness} hand {(IsTracked ? "tracked" : "lost")}");
            }

            if (!IsTracked || !TryReadHand(hand, dt, out Vector3 palm, out Vector3 palmNormal, out HandGesture next))
            {
                // Tracking drops out for a moment all the time (on the headset: 260 times in 26 minutes, most under
                // 0.3 s). A charge, a beam or flames carry over a short one instead of being lost.
                lostFor += dt;
                if (lostFor < LostGraceSeconds)
                    return;
                gesture = HandGesture.None;
                gestureHold.Reset();
                StopWeapons();
                return;
            }
            lostFor = 0f;
            next = gestureHold.Step(next, dt);

            if (next != gesture)
            {
                gesture = next;
                Debug.Log($"[Repulsor] {handedness} gesture {gesture}");
            }

            if (weapon != game.ArmedWeapon)
            {
                weapon = game.ArmedWeapon;
                StopWeapons();
            }

            // A repulsor shot leaves the hand open, which would start the laser. Wait a moment, or for a fresh open palm.
            // (Waiting for a fresh palm alone let fist flickers block the laser all the time once blasts unlocked.)
            laserBlockedFor = gesture != HandGesture.Open ? 0f : Mathf.Max(0f, laserBlockedFor - dt);

            if (weapon == Weapon.None)
                return;
            if (weapon == Weapon.LaserRepulsor)
            {
                TickCharged(palm, palmNormal, dt);
                TickLaser(palm, palmNormal, dt);
            }
            else if (weapon == Weapon.Laser)
                TickLaser(palm, palmNormal, dt);
            else if (weapon == Weapon.Aura)
                TickAura(palm, palmNormal, dt);
            else
                TickCharged(palm, palmNormal, dt);
        }

        void TickCharged(Vector3 palm, Vector3 palmNormal, float dt)
        {
            // Track the hand live while it's a fist. Once it starts opening the pose can't be trusted,
            // so the aim line, the orb and the shot all stay on the remembered aim.
            // In the real game the aim follows the arm, not the palm (a fist's palm faces down or sideways).
            bool useArena = !game.Legacy && game.HandAim != null;
            Vector3 handAim = useArena ? game.HandAim(handedness == Handedness.Left, palm, palmNormal) : palmNormal;
            Vector3 muzzle = palm + handAim * PalmOffset;
            bool fist = gesture == HandGesture.Fist;
            if (fist)
                aim.Record(Time.time, muzzle, handAim);

            Vector3 shownPosition = fist ? muzzle : aim.Position;
            Vector3 shownDirection = fist ? handAim : aim.Direction;
            if (useArena && game.AssistAim != null)
                shownDirection = game.AssistAim(shownPosition, shownDirection);

            float fired = repulsor.Step(gesture, dt);
            if (fired > 0f)
            {
                Vector3 direction = useArena && game.AssistAim != null ? game.AssistAim(aim.Position, aim.Direction) : aim.Direction;
                Debug.Log($"[Repulsor] {handedness} firing: palm {Vector3.Angle(palmNormal, aim.Direction):0} deg off the aim, "
                    + $"assist bent it {Vector3.Angle(aim.Direction, direction):0} deg");
                if (weapon == Weapon.Shockwave)
                    game.FireShockwave(aim.Position, direction, fired);
                else
                    game.Fire(aim.Position, direction, fired);
                aim.Clear();
                laserBlockedFor = LaserBlockSeconds;
            }

            UpdateChargeVisuals(shownPosition, shownDirection);
        }

        void TickAura(Vector3 palm, Vector3 palmNormal, float dt)
        {
            auraFist = gesture == HandGesture.Fist ? auraFist + dt : 0f;
            if (auraFist >= AuraPutOutSeconds && game.AuraLit)
                game.PutOutAura();

            laser.Step(gesture == HandGesture.Open && game.PointsAwayFromHead(palm, palmNormal), dt);
            if (!laser.Firing)
            {
                auraAim = 0f;
                auraPlaced = false;
                aimLine.enabled = false;
                chargeOrb.gameObject.SetActive(false);
                return;
            }

            Vector3 muzzle = palm + palmNormal * PalmOffset;
            bool surface = game.Room.TryDistance(muzzle, palmNormal, out float distance);
            aimLine.enabled = true;
            aimLine.SetPosition(0, muzzle);
            aimLine.SetPosition(1, muzzle + palmNormal * (surface ? distance : AimLength));

            if (!surface || auraPlaced || distance < AuraMinDistance)
                return;

            // The orb grows as the aim is held, then the aura lights.
            auraAim += dt;
            chargeOrb.gameObject.SetActive(true);
            chargeOrb.position = muzzle;
            chargeOrb.localScale = Vector3.one * (MaxOrbDiameter * Mathf.Clamp01(auraAim / AuraIgniteSeconds));

            if (auraAim >= AuraIgniteSeconds)
            {
                auraPlaced = true;
                chargeOrb.gameObject.SetActive(false);
                game.LightAura(muzzle, palmNormal, distance);
            }
        }

        void TickLaser(Vector3 palm, Vector3 palmNormal, float dt)
        {
            // Not during an OK sign, which also has the fingers up: that's the pause gesture.
            laser.Step(gesture == HandGesture.Open && !pinched && laserBlockedFor <= 0f
                && game.PointsAwayFromHead(palm, palmNormal), dt);
            if (!laser.Firing)
            {
                StopLaser();
                return;
            }

            if (!lasing)
            {
                lasing = true;
                laserDirection = palmNormal;
                beamLength.Reset();
                laserFrames = 0;
                surfaceFrames = 0;
                if (!game.FlameLaser)
                    laserHum.Play();
                game.PlayLaserIgnite(palm);
                Debug.Log($"[Repulsor] {handedness} {(game.FlameLaser ? "flames" : "laser")} on");
            }

            if (game.FlameLaser)
            {
                // The Mage: a short cone of fire instead of a beam, aimed half by the palm and half by the arm.
                Vector3 aimed = game.HandAim != null ? game.HandAim(handedness == Handedness.Left, palm, palmNormal) : palmNormal;
                laserDirection = Vector3.Slerp(laserDirection, Vector3.Slerp(palmNormal, aimed, 0.5f).normalized,
                    1f - Mathf.Exp(-LaserFollowSpeed * dt)).normalized;
                Vector3 mouth = palm + laserDirection * PalmOffset;
                if (flames == null)
                    flames = Flamethrower.Create(game.transform);
                flames.Pour(mouth, laserDirection, game.WeaponColor);
                Shown(HandBeam.Flames, mouth, mouth + laserDirection * Flamethrower.Reach);
                Flamethrower.Burn(game, mouth, laserDirection, Flamethrower.Reach, game.LaserDps * Flamethrower.DamageScale, dt);
                laserFrames++;
                return;
            }

            // Smoothing keeps the end of a long beam from shaking with every tiny tracking wobble.
            float follow = 1f - Mathf.Exp(-LaserFollowSpeed * dt);
            laserDirection = Vector3.Slerp(laserDirection, palmNormal, follow).normalized;
            Vector3 start = palm + laserDirection * PalmOffset;

            bool surface = game.Room.TryHit(start, laserDirection, out float distance, out Vector3 normal);
            if (surface)
            {
                surfaceNormal = normal;
                // An enemy in or behind that wall can still be burnt.
                distance = RepulsorGame.ReachPast(start, laserDirection, distance);
            }

            float reach = beamLength.Step(surface, distance, dt);
            bool onTarget = game.BurnWithLaser(start, laserDirection, reach, dt, out float length, game.LaserDps);
            laserBeam.Aim(start, start + laserDirection * length, onTarget || beamLength.Hitting,
                onTarget ? -laserDirection : surfaceNormal);
            Shown(HandBeam.Laser, start, start + laserDirection * length);
            game.DimRoom();

            laserFrames++;
            if (surface)
                surfaceFrames++;
        }

        bool TryReadHand(XRHand hand, float dt, out Vector3 palm, out Vector3 palmNormal, out HandGesture result)
        {
            palm = default;
            palmNormal = default;
            result = HandGesture.None;

            if (!hand.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose palmPose)
                || !hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wrist)
                || !hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumbTip)
                || !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose indexTip))
                return false;

            if (!TryExtension(hand, wrist, XRHandJointID.IndexProximal, XRHandJointID.IndexTip, out float index)
                || !TryExtension(hand, wrist, XRHandJointID.MiddleProximal, XRHandJointID.MiddleTip, out float middle)
                || !TryExtension(hand, wrist, XRHandJointID.RingProximal, XRHandJointID.RingTip, out float ring)
                || !TryExtension(hand, wrist, XRHandJointID.LittleProximal, XRHandJointID.LittleTip, out float little))
                return false;

            float thumbToIndex = Vector3.Distance(thumbTip.position, indexTip.position);
            pinched = thumbToIndex < PinchDetector.ReleaseDistance;
            if (pinch.Step(thumbToIndex, middle, ring, little, dt))
                game.OkSign();

            result = GestureClassifier.Classify(index, middle, ring, little, gestureHold.Shown);
            palm = trackingSpace.TransformPoint(palmPose.position);
            // XR Hands defines a hand's palm direction as local -Y (see XRHandAxis.PalmDirection).
            palmNormal = trackingSpace.TransformDirection(palmPose.rotation * Vector3.down);
            PalmPosition = palm;
            PalmNormal = palmNormal;
            IndexTip = trackingSpace.TransformPoint(indexTip.position);
            return true;
        }

        static bool TryExtension(XRHand hand, Pose wrist, XRHandJointID knuckle, XRHandJointID tip, out float extension)
        {
            extension = 0f;
            if (!hand.GetJoint(knuckle).TryGetPose(out Pose knucklePose)
                || !hand.GetJoint(tip).TryGetPose(out Pose tipPose))
                return false;

            float knuckleDistance = Vector3.Distance(knucklePose.position, wrist.position);
            if (knuckleDistance < 0.001f)
                return false;

            extension = Vector3.Distance(tipPose.position, wrist.position) / knuckleDistance;
            return true;
        }

        LoopSound chargeHum;
        static AudioClip chargeClip;

        void UpdateChargeVisuals(Vector3 muzzle, Vector3 direction)
        {
            float charge = repulsor.Charge;
            bool charging = charge > 0.01f;
            // Energy gathering in the hand: a whine that climbs as the charge grows (11/17).
            if (chargeHum == null)
                chargeHum = LoopSound.Create("Hand Charge", chargeClip != null ? chargeClip : chargeClip = Sfx.Charge());
            chargeHum.Set(muzzle, charging ? charge : 0f, 0.8f, 1.9f, 0.55f);

            chargeOrb.gameObject.SetActive(charging);
            if (charging)
            {
                chargeOrb.position = muzzle;
                chargeOrb.localScale = Vector3.one * (MaxOrbDiameter * charge);
            }

            bool aiming = charge > RepulsorGesture.MinChargeToFire;
            aimLine.enabled = aiming;
            if (aiming)
            {
                aimLine.SetPosition(0, muzzle);
                aimLine.SetPosition(1, muzzle + direction * AimLength);
            }
        }

        void StopLaser()
        {
            if (lasing)
                Debug.Log($"[Repulsor] {handedness} laser off, surface found on {surfaceFrames}/{laserFrames} frames");
            lasing = false;
            laserHum.Stop();
            if (flames != null)
                flames.Stop();
        }

        void StopWeapons()
        {
            repulsor.Reset();
            aim.Clear();
            laser.Reset();
            auraAim = 0f;
            auraFist = 0f;
            auraPlaced = false;
            StopLaser();
            chargeOrb.gameObject.SetActive(false);
            aimLine.enabled = false;
        }
    }
}
