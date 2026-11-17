using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// On once a condition has held for <see cref="Engage"/> seconds. Brief losses shorter than
    /// <see cref="Dropout"/> are ignored, because tracking flickers for a frame or two.
    /// </summary>
    public sealed class HoldTimer
    {
        public float Engage;
        public float Dropout;
        float lost;

        public HoldTimer(float engage, float dropout)
        {
            Engage = engage;
            Dropout = dropout;
        }

        public float Held { get; private set; }
        public bool On => Held >= Engage;
        public float Progress => Engage <= 0f ? (Held > 0f ? 1f : 0f) : Mathf.Clamp01(Held / Engage);
        /// <summary>True on the one frame it turned on.</summary>
        public bool JustOn { get; private set; }

        public bool Step(bool active, float dt)
        {
            bool was = On;
            if (active)
            {
                Held += dt;
                lost = 0f;
            }
            else
            {
                lost += dt;
                if (lost >= Dropout)
                    Held = 0f;
            }
            JustOn = On && !was;
            return On;
        }

        public void Reset()
        {
            Held = 0f;
            lost = 0f;
            JustOn = false;
        }
    }

    /// <summary>Shield: wrists crossed in an X in front of the chest.</summary>
    public sealed class XArmsDetector
    {
        public static float MaxWristGap = 0.36f;
        public static float MinCross = 0f;
        // Long dropout grace: tracking often loses one of two crossed hands for a moment.
        readonly HoldTimer hold = new HoldTimer(0.1f, 0.4f);

        public bool Held => hold.On;

        public bool Step(HandPose left, HandPose right, Stance body, float dt) =>
            hold.Step(IsCrossed(left, right, body), dt);

        public static bool IsCrossed(HandPose left, HandPose right, Stance body)
        {
            if (!left.Tracked || !right.Tracked)
                return false;
            Vector3 l = body.Local(left.Wrist);
            Vector3 r = body.Local(right.Wrist);
            bool inFront = l.z > 0f && r.z > 0f && l.z < 0.8f && r.z < 0.8f;
            bool chestHigh = l.y < 0.2f && r.y < 0.2f && l.y > -0.85f && r.y > -0.85f;
            // The left hand's knuckles end up right of the right hand's.
            float cross = body.Local(left[Joint.MiddleProximal]).x - body.Local(right[Joint.MiddleProximal]).x;
            bool close = Vector3.Distance(left.Wrist, right.Wrist) < MaxWristGap;
            bool upward = left.Forward.y > -0.45f && right.Forward.y > -0.45f;
            return inFront && chestHigh && close && upward && cross > MinCross;
        }

        public void Reset() => hold.Reset();
    }

    /// <summary>Pause: an OK sign with both hands, held for 2 seconds. Fires once per hold.</summary>
    public sealed class DoubleOkDetector
    {
        public static float Seconds = 2f;
        readonly HoldTimer hold = new HoldTimer(Seconds, 0.25f);

        public float Progress => hold.Progress;

        public bool Step(HandPose left, HandPose right, float dt)
        {
            hold.Step(left.Ok && right.Ok, dt);
            return hold.JustOn;
        }
    }

    /// <summary>Transform: both fists clenched low at your sides for a second.</summary>
    public sealed class PowerUpDetector
    {
        public static float Seconds = 1f;
        readonly HoldTimer hold = new HoldTimer(Seconds, 0.3f);

        public float Progress => hold.Progress;
        public bool Posing => hold.Held > 0f;

        public bool Step(HandPose left, HandPose right, Stance body, float dt)
        {
            hold.Step(IsPose(left, right, body), dt);
            return hold.JustOn;
        }

        public static bool IsPose(HandPose left, HandPose right, Stance body)
        {
            if (!left.Fist || !right.Fist)
                return false;
            Vector3 l = body.Local(left.Wrist);
            Vector3 r = body.Local(right.Wrist);
            bool low = l.y < -0.4f && r.y < -0.4f && l.y > -1.15f && r.y > -1.15f;
            bool atSides = l.x < -0.1f && r.x > 0.1f;
            bool nearBody = l.z > -0.3f && r.z > -0.3f && l.z < 0.5f && r.z < 0.5f;
            return low && atSides && nearBody;
        }
    }

    /// <summary>
    /// Energy ball: hands held together to charge, then pushed forward together to fire. It only counts as charging
    /// (<see cref="Holding"/>) once the hands have been together for half a second: two-handed blasts leave both hands
    /// open side by side for a moment, and that alone used to switch the hand weapons off (11/15, Ryu stopped firing
    /// after reaching level 2).
    /// </summary>
    public sealed class TwoHandChargeDetector
    {
        // Loosened 11/16 (Ryuu's ki ball was hard to fire; little else uses two hands): wider gap (was 0.12), a
        // shorter minimum charge (was 0.25), a slower push (was 1.2, and both hands had to reach it), and more time
        // to push after the hands part (was 0.35 / 0.6).
        public static float MaxGap = 0.18f;
        public static float FullSeconds = 2f;
        public static float MinCharge = 0.15f;
        /// <summary>Seconds the hands must be together before it's a charge.</summary>
        public static float HoldSeconds = 0.5f;
        public static float PushSpeed = 0.9f;
        /// <summary>The slower hand need only push this share of <see cref="PushSpeed"/>.</summary>
        public static float SlowHandShare = 0.4f;
        public static float ApartGrace = 0.5f;
        public static float ApartCancel = 0.9f;

        float apart = ApartCancel;

        /// <summary>0 to 1.</summary>
        public float Charge { get; private set; }
        public bool Charging => apart < ApartGrace && Charge > 0f;
        /// <summary>Charging for real: held together long enough (the hand weapons step aside then).</summary>
        public bool Holding => Charging && Charge * FullSeconds >= HoldSeconds;
        public Vector3 Center { get; private set; }

        public bool Step(HandPose left, HandPose right, Stance body, float dt, out Vector3 direction, out float power)
        {
            direction = default;
            power = 0f;
            // Two fists together is the dragon fist's guard, not a ki ball; one curled hand is fine.
            bool together = left.Tracked && right.Tracked && !(left.Fist && right.Fist)
                && Vector3.Distance(left.Palm, right.Palm) < MaxGap
                && body.Local((left.Palm + right.Palm) * 0.5f).z > 0.05f;

            if (together)
            {
                Center = (left.Palm + right.Palm) * 0.5f;
                Charge = Mathf.Min(1f, Charge + dt / FullSeconds);
                apart = 0f;
            }
            else
            {
                apart += dt;
            }

            if (Charge >= MinCharge && apart < ApartGrace && left.Tracked && right.Tracked)
            {
                float pushLeft = Vector3.Dot(left.Velocity, body.Forward), pushRight = Vector3.Dot(right.Velocity, body.Forward);
                if ((pushLeft + pushRight) * 0.5f > PushSpeed && Mathf.Min(pushLeft, pushRight) > PushSpeed * SlowHandShare)
                {
                    Vector3 velocity = (left.Velocity + right.Velocity) * 0.5f;
                    direction = Vector3.Slerp(velocity.normalized, body.Forward, 0.3f).normalized;
                    power = Charge;
                    Charge = 0f;
                    apart = ApartCancel;
                    return true;
                }
            }

            if (apart >= ApartCancel)
                Charge = 0f;
            return false;
        }
    }

    /// <summary>
    /// Fire pillar: thumbs and index fingers touching in a triangle aims a circle on the floor where the line
    /// from the head through the triangle lands. Closing both hands into fists soon after fires it.
    /// </summary>
    public sealed class TriangleDetector
    {
        public static float TipsTouch = 0.05f;
        public static float MinOpening = 0.04f;
        public static float ArmedSeconds = 1f;
        public static float MaxRange = 7f;
        readonly HoldTimer hold = new HoldTimer(0.25f, 0.15f);
        float sinceTriangle;

        public bool Aiming { get; private set; }
        public Vector3 Target { get; private set; }

        public bool Step(HandPose left, HandPose right, Stance body, float dt, out Vector3 target)
        {
            target = default;
            bool triangle = IsTriangle(left, right);
            if (hold.Step(triangle, dt) && triangle)
            {
                Aiming = true;
                sinceTriangle = 0f;
                Vector3 center = (left[Joint.IndexTip] + right[Joint.IndexTip] + left[Joint.ThumbTip] + right[Joint.ThumbTip]) * 0.25f;
                Target = FloorPoint(body, (center - body.Head).normalized);
                return false;
            }

            if (!Aiming)
                return false;
            sinceTriangle += dt;
            if (left.Fist && right.Fist)
            {
                Aiming = false;
                hold.Reset();
                target = Target;
                return true;
            }
            if (sinceTriangle > ArmedSeconds)
                Aiming = false;
            return false;
        }

        public static bool IsTriangle(HandPose left, HandPose right)
        {
            if (!left.Tracked || !right.Tracked)
                return false;
            float indexes = Vector3.Distance(left[Joint.IndexTip], right[Joint.IndexTip]);
            float thumbs = Vector3.Distance(left[Joint.ThumbTip], right[Joint.ThumbTip]);
            float opening = Vector3.Distance((left[Joint.IndexTip] + right[Joint.IndexTip]) * 0.5f,
                (left[Joint.ThumbTip] + right[Joint.ThumbTip]) * 0.5f);
            return indexes < TipsTouch && thumbs < TipsTouch && opening > MinOpening;
        }

        public static Vector3 FloorPoint(Stance body, Vector3 direction)
        {
            float height = body.Head.y - body.FloorY;
            if (direction.y < -0.05f)
            {
                float distance = height / -direction.y;
                Vector3 point = body.Head + direction * distance;
                Vector3 flat = Vector3.ProjectOnPlane(point - body.Head, Vector3.up);
                if (flat.magnitude <= MaxRange)
                    return new Vector3(point.x, body.FloorY, point.z);
            }
            Vector3 along = Vector3.ProjectOnPlane(direction, Vector3.up);
            along = along.sqrMagnitude > 1e-4f ? along.normalized : body.Forward;
            Vector3 far = body.Head + along * MaxRange;
            return new Vector3(far.x, body.FloorY, far.z);
        }
    }

    /// <summary>
    /// Bow: both fists touching at arm's length forms it. The hand that stays out holds the bow, the other pulls
    /// the string back, and opening the pulling hand looses the arrow.
    /// </summary>
    public sealed class BowDetector
    {
        public static float FistsTouch = 0.13f;
        public static float MinReach = 0.3f;
        public static float FullDraw = 0.5f;
        public static float MinDraw = 0.12f;
        public static float LockSeparation = 0.06f;
        readonly HoldTimer form = new HoldTimer(0.2f, 0.1f);
        readonly HoldTimer frontLost = new HoldTimer(0.25f, 0f);
        int openFrames;
        bool sidesLocked;

        public bool Formed { get; private set; }
        public bool FrontIsLeft { get; private set; }
        /// <summary>0 to 1.</summary>
        public float Draw { get; private set; }
        public Vector3 BowPosition { get; private set; }
        public Vector3 StringPosition { get; private set; }
        public Vector3 Direction { get; private set; }

        public bool Step(HandPose left, HandPose right, Stance body, float dt, out Vector3 origin, out Vector3 direction, out float power)
        {
            origin = direction = default;
            power = 0f;

            if (!Formed)
            {
                bool together = left.Fist && right.Fist && Vector3.Distance(left.Palm, right.Palm) < FistsTouch
                    && body.Local((left.Palm + right.Palm) * 0.5f).z > MinReach;
                if (form.Step(together, dt))
                {
                    Formed = true;
                    sidesLocked = false;
                    openFrames = 0;
                    frontLost.Reset();
                }
                else
                {
                    return false;
                }
            }

            if (!sidesLocked)
            {
                FrontIsLeft = body.Local(left.Palm).z >= body.Local(right.Palm).z;
                if (Vector3.Distance(left.Palm, right.Palm) > LockSeparation + FistsTouch)
                    sidesLocked = true;
            }

            HandPose front = FrontIsLeft ? left : right;
            HandPose back = FrontIsLeft ? right : left;

            if (frontLost.Step(!front.Tracked || front.Open, dt))
            {
                Cancel();
                return false;
            }

            if (front.Tracked)
                BowPosition = front.Palm;
            if (back.Tracked)
                StringPosition = back.Palm;
            float separation = Vector3.Distance(BowPosition, StringPosition);
            Draw = Mathf.Clamp01((separation - FistsTouch * 0.5f) / FullDraw);
            Direction = separation > 0.05f ? (BowPosition - StringPosition).normalized : body.Forward;

            openFrames = back.Tracked && back.Open ? openFrames + 1 : 0;
            if (openFrames >= 2)
            {
                bool fire = Draw >= MinDraw;
                origin = BowPosition;
                direction = Direction;
                power = Draw;
                Cancel();
                return fire;
            }
            return false;
        }

        void Cancel()
        {
            Formed = false;
            Draw = 0f;
            form.Reset();
        }
    }

    /// <summary>
    /// Finger gun: index and middle finger forward, thumb up. Flicking the wrist up fires along where it pointed
    /// just before the flick, blended with the line of the arm. <see cref="Direction"/> is for the aim line.
    /// </summary>
    public sealed class FingerGunDetector
    {
        // Loosened 11/16 (Lumen's shots sometimes didn't fire): 10 degrees in 0.3 s (was 12 in 0.25), so a flick
        // about 30% slower counts; a slow turn of the wrist still doesn't.
        public static float FlickDegrees = 10f;
        public static float FlickWindow = 0.3f;
        public static float Cooldown = 0.25f;
        public static float ArmBlend = 0.5f;
        const int HistorySize = 48;   // covers FlickWindow at 120 fps

        struct Sample
        {
            public float Time;
            public Vector3 Origin;
            public Vector3 Direction;
            public float Elevation;
        }

        // The shape blurs during a quick flick, so it holds through a longer dropout.
        readonly HoldTimer shape = new HoldTimer(0.08f, 0.35f);
        readonly Sample[] history = new Sample[HistorySize];
        int count;
        int next;
        float clock;
        float cooldown;

        public bool Aiming => shape.On;
        public Vector3 Origin { get; private set; }
        public Vector3 Direction { get; private set; }

        public bool Step(HandPose hand, Stance body, float dt, out Vector3 origin, out Vector3 direction)
        {
            origin = direction = default;
            clock += dt;
            cooldown -= dt;
            if (!shape.Step(hand.Gun, dt))
            {
                count = 0;
                return false;
            }
            if (!hand.Tracked)
                return false;

            Vector3 index = hand.IndexDirection;
            Origin = hand[Joint.IndexTip];
            Direction = Vector3.Slerp(index, body.ArmDirection(hand.IsLeft, Origin), ArmBlend).normalized;
            float elevation = Mathf.Asin(Mathf.Clamp(index.y, -1f, 1f)) * Mathf.Rad2Deg;
            history[next] = new Sample { Time = clock, Origin = Origin, Direction = Direction, Elevation = elevation };
            next = (next + 1) % HistorySize;
            count = Mathf.Min(count + 1, HistorySize);

            // The lowest point in the last moment is where the flick started.
            int lowest = -1;
            for (int i = 0; i < count; i++)
            {
                int at = (next - 1 - i + HistorySize) % HistorySize;
                if (clock - history[at].Time > FlickWindow)
                    break;
                if (lowest < 0 || history[at].Elevation < history[lowest].Elevation)
                    lowest = at;
            }

            if (cooldown > 0f || lowest < 0 || elevation - history[lowest].Elevation < FlickDegrees)
                return false;

            origin = history[lowest].Origin;
            direction = history[lowest].Direction;
            cooldown = Cooldown;
            count = 0;
            return true;
        }
    }

    /// <summary>Dragon fist: a fist moving away from the shoulder faster than <see cref="MinSpeed"/>.</summary>
    public sealed class PunchDetector
    {
        public static float MinSpeed = 2.5f;
        public static float Cooldown = 0.5f;
        float cooldown;

        public bool Step(HandPose hand, Stance body, float dt, out Vector3 origin, out Vector3 direction)
        {
            origin = direction = default;
            cooldown -= dt;
            if (!hand.Fist || cooldown > 0f)
                return false;
            float speed = hand.Velocity.magnitude;
            if (speed < MinSpeed)
                return false;
            Vector3 moving = hand.Velocity / speed;
            Vector3 arm = body.ArmDirection(hand.IsLeft, hand.Palm);
            if (Vector3.Dot(moving, arm) < 0.6f)
                return false;
            origin = hand.Palm;
            // The hand's path wobbles; half the arm's line steadies it (lock-on does the rest).
            direction = Vector3.Slerp(moving, arm, 0.5f).normalized;
            cooldown = Cooldown;
            return true;
        }
    }

    /// <summary>
    /// Web shots: one as soon as the spider sign is made, then more every <see cref="Repeat"/> seconds while it's
    /// held. Kept loose (see <see cref="HandPose.SpiderSign"/>), with a short dropout grace.
    /// </summary>
    public sealed class SpiderSignDetector
    {
        public static float Cooldown = 0.15f;
        public static float Repeat = 0.3f;
        readonly HoldTimer hold = new HoldTimer(0.04f, 0.12f);
        float cooldown;

        /// <summary>The sign is held: show where webs go.</summary>
        public bool Aiming => hold.On;

        public bool Step(HandPose hand, Stance body, float dt, out Vector3 origin, out Vector3 direction)
        {
            cooldown -= dt;
            hold.Step(hand.SpiderSign && hand.Tracked, dt);
            Aim(hand, body, out origin, out direction);
            if (!hold.On)
            {
                // A fresh sign can fire soon after letting go; holding one repeats more slowly.
                cooldown = Mathf.Min(cooldown, Cooldown);
                return false;
            }
            if (cooldown > 0f)
                return false;
            cooldown = Repeat;
            return true;
        }

        /// <summary>Where a web from this hand goes: from the wrist, half along the hand and half along the arm.</summary>
        public static void Aim(HandPose hand, Stance body, out Vector3 origin, out Vector3 direction)
        {
            origin = hand.Wrist + hand.Forward * 0.04f;
            direction = Vector3.Slerp(hand.Forward, body.ArmDirection(hand.IsLeft, hand.Palm), 0.5f).normalized;
        }
    }

    public enum GrabEvent { None, Grab, Throw, Drop }

    /// <summary>
    /// The force (11/16, replacing cup-to-grab and fist-to-crush): a cupped hand aims (the blue line), closing it
    /// into a fist grabs what it aimed at, a fast swing of the fist throws it, and opening the hand lets go.
    /// </summary>
    public sealed class ForceGrabDetector
    {
        public static float ThrowSpeed = 1.8f;
        /// <summary>After the hand stops being cupped, closing it into a fist still grabs for this long.</summary>
        public static float FistWindow = 0.7f;
        /// <summary>Open (not a fist) this long while holding: let go. Tracking dropouts don't count.</summary>
        public static float LetGoSeconds = 0.35f;
        readonly HoldTimer cup = new HoldTimer(0.1f, 0.25f);
        float sinceCup = 99f;
        float openFor;

        public bool Holding { get; private set; }
        /// <summary>Cupped, or just was: the aim line shows and a fist will grab.</summary>
        public bool Aiming => !Holding && sinceCup < FistWindow;
        public bool Forming => Aiming;
        public Vector3 AimOrigin { get; private set; }
        public Vector3 AimDirection { get; private set; }

        /// <summary>A fist for the force: the usual fist, or a loose one (as the saber's grip).</summary>
        public static bool IsFist(HandPose hand) => hand.Tracked
            && (hand.Fist || ((hand.Middle + hand.Ring + hand.Little) / 3f < 1.35f && hand.Index < 1.35f));

        /// <summary>The fist closed on nothing: not holding (so the hand can be a saber instead).</summary>
        public void Cancel()
        {
            Holding = false;
            sinceCup = 99f;
        }

        public GrabEvent Step(HandPose hand, Stance body, float dt, out Vector3 throwVelocity)
        {
            throwVelocity = default;
            bool cupped = cup.Step(hand.Cup, dt);
            bool fist = IsFist(hand);

            if (!Holding)
            {
                // Aim while cupped; the aim stays where it was as the hand closes.
                if (hand.Tracked && hand.Cup)
                {
                    AimOrigin = hand.Palm;
                    AimDirection = Vector3.Slerp(hand.PalmNormal, body.ArmDirection(hand.IsLeft, hand.Palm), 0.5f).normalized;
                }
                sinceCup = cupped ? 0f : sinceCup + dt;
                if (fist && sinceCup < FistWindow)
                {
                    Holding = true;
                    openFor = 0f;
                    cup.Reset();
                    sinceCup = 99f;
                    return GrabEvent.Grab;
                }
                return GrabEvent.None;
            }

            if (hand.Tracked)
            {
                AimOrigin = hand.Palm;
                AimDirection = Vector3.Slerp(hand.PalmNormal, body.ArmDirection(hand.IsLeft, hand.Palm), 0.5f).normalized;
            }
            if (hand.Tracked && hand.Velocity.magnitude > ThrowSpeed
                && Vector3.Dot(hand.Velocity.normalized, body.ArmDirection(hand.IsLeft, hand.Palm)) > 0.3f)
            {
                Holding = false;
                throwVelocity = hand.Velocity;
                return GrabEvent.Throw;
            }
            openFor = !hand.Tracked || fist ? 0f : openFor + dt;
            if (openFor >= LetGoSeconds)
            {
                Holding = false;
                return GrabEvent.Drop;
            }
            return GrabEvent.None;
        }
    }

    /// <summary>Laser sword: the blade comes out of the thumb side of a fist, as if holding a hilt.</summary>
    public sealed class SaberGripDetector
    {
        public static float ForwardTilt = 0.35f;
        /// <summary>A loose fist is enough: the middle, ring and little fingers average below this, the index below <see cref="IndexBelow"/>.</summary>
        public static float FingersBelow = 1.35f;
        public static float IndexBelow = 1.3f;
        /// <summary>The blade stays lit this long through a tracking dropout or a loosened grip, so fast swings don't blink it out.</summary>
        public static float KeepLitSeconds = 0.6f;
        readonly HoldTimer hold = new HoldTimer(0.1f, 0.6f);

        public bool On => hold.On;
        public Vector3 Hilt { get; private set; }
        public Vector3 BladeDirection { get; private set; }

        public bool Step(HandPose hand, float dt)
        {
            float give = On ? 0.15f : 0f;
            bool grip = hand.Tracked && (hand.Middle + hand.Ring + hand.Little) / 3f < FingersBelow + give
                && hand.Index < IndexBelow + give;
            hold.Dropout = KeepLitSeconds;
            hold.Step(grip, dt);
            if (hand.Tracked)
            {
                Hilt = (hand[Joint.IndexProximal] + hand[Joint.LittleProximal]) * 0.5f + hand.PalmNormal * 0.02f;
                BladeDirection = (hand.ThumbSide + hand.Forward * ForwardTilt).normalized;
            }
            return On;
        }
    }

    /// <summary>Smoke bomb: a fist swung up and forward from below the chest.</summary>
    public sealed class UnderhandThrowDetector
    {
        public static float MinSpeed = 1.6f;
        public static float Cooldown = 0.6f;
        float cooldown;
        float sinceFist = 1f;

        public bool Step(HandPose hand, Stance body, float dt, out Vector3 origin, out Vector3 velocity)
        {
            origin = velocity = default;
            cooldown -= dt;
            sinceFist = hand.Fist ? 0f : sinceFist + dt;
            if (!hand.Tracked || sinceFist > 0.25f || cooldown > 0f)
                return false;
            float speed = hand.Velocity.magnitude;
            if (speed < MinSpeed)
                return false;
            Vector3 moving = hand.Velocity / speed;
            bool upAndOut = moving.y > 0.3f && Vector3.Dot(moving, body.Forward) > 0.3f;
            bool low = body.Local(hand.Palm).y < -0.35f;
            if (!upAndOut || !low)
                return false;
            origin = hand.Palm;
            velocity = hand.Velocity;
            cooldown = Cooldown;
            return true;
        }
    }
}
