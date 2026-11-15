using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// Gust of wind (Elementalist level 1): an open hand pushed forward fast, palm leading. Loose on purpose: any
    /// quick forward shove of an open hand counts.
    /// </summary>
    public sealed class PalmPushDetector
    {
        public static float MinSpeed = 1.3f;
        public static float Cooldown = 0.45f;
        float cooldown;

        public bool Step(HandPose hand, Stance body, float dt, out Vector3 origin, out Vector3 direction)
        {
            origin = direction = default;
            cooldown -= dt;
            if (!hand.Tracked || hand.Fist || cooldown > 0f)
                return false;
            float forward = Vector3.Dot(hand.Velocity, body.Forward);
            if (forward < MinSpeed || body.Local(hand.Palm).z < 0.05f)
                return false;
            Vector3 moving = hand.Velocity.normalized;
            Vector3 arm = body.ArmDirection(hand.IsLeft, hand.Palm);
            if (Vector3.Dot(moving, arm) < 0.4f)
                return false;
            origin = hand.Palm;
            direction = Vector3.Slerp(moving, arm, 0.5f).normalized;
            cooldown = Cooldown;
            return true;
        }
    }

    /// <summary>
    /// Lightning (Elementalist level 2): both hands held out in front, open and flat (palms down or forward, not
    /// up), like casting force lightning. Held, with a dropout grace. Each hand aims along its arm.
    /// </summary>
    public sealed class FlatHandsDetector
    {
        public static float MinReach = 0.25f;
        readonly HoldTimer hold = new HoldTimer(0.2f, 0.35f);

        public bool Holding => hold.On;

        public bool Step(HandPose left, HandPose right, Stance body, float dt)
        {
            hold.Step(IsPose(left, body) && IsPose(right, body), dt);
            return hold.On;
        }

        public static bool IsPose(HandPose hand, Stance body)
        {
            if (!hand.Tracked || hand.Fist || hand.Gesture == HandGesture.None && hand.Index < HandPose.OpenAbove)
                return false;
            Vector3 local = body.Local(hand.Palm);
            bool outInFront = local.z > MinReach && local.y < 0.1f && local.y > -0.8f;
            bool notFacingUp = hand.PalmNormal.y < 0.5f;
            return outInFront && notFacingUp && hand.Index > 1.3f && hand.Middle > 1.3f;
        }

        public void Reset() => hold.Reset();
    }

    /// <summary>
    /// Tornado (Elementalist level 3): both open hands swirled round in circles in front of the chest charge it;
    /// then pushing both forward together throws it. Only real circling charges: each hand's direction of travel
    /// has to keep turning the same way round (as it does going round a circle), so waving, shaking or holding
    /// still don't. About one full circle arms it and two charge it fully.
    /// </summary>
    public sealed class SwirlDetector
    {
        public static float MinSpeed = 0.3f;
        /// <summary>Circles of both hands (averaged) for a full charge.</summary>
        public static float FullTurns = 2f;
        public static float MinCharge = 0.45f;
        public static float PushSpeed = 1f;
        public static float Fade = 0.5f;
        /// <summary>A turn bigger than this in one frame is a change of direction (a wave), not circling.</summary>
        public static float MaxTurnPerFrame = 50f;

        /// <summary>One hand's circling: which way its travel is turning, and how far.</summary>
        sealed class Circling
        {
            Vector3 lastDirection;
            Vector3 axis;

            /// <summary>Degrees its direction of travel turned this frame: positive when it keeps turning the same way round.</summary>
            public float Step(Vector3 velocity)
            {
                float speed = velocity.magnitude;
                if (speed < MinSpeed)
                {
                    lastDirection = Vector3.zero;
                    return 0f;
                }
                Vector3 direction = velocity / speed;
                Vector3 previous = lastDirection;
                lastDirection = direction;
                if (previous == Vector3.zero)
                    return 0f;
                float angle = Vector3.Angle(previous, direction);
                if (angle < 0.05f || angle > MaxTurnPerFrame)
                    return 0f;
                Vector3 turnAxis = Vector3.Cross(previous, direction).normalized;
                if (axis == Vector3.zero)
                {
                    axis = turnAxis;
                    return angle;
                }
                if (Vector3.Dot(turnAxis, axis) > 0f)
                {
                    axis = Vector3.Slerp(axis, turnAxis, 0.15f).normalized;
                    return angle;
                }
                // Turning back the other way: a wiggle. A steady new circle the other way takes over after a while.
                axis = (axis + turnAxis * 0.1f).normalized;
                return -angle;
            }

            public void Reset()
            {
                lastDirection = Vector3.zero;
                axis = Vector3.zero;
            }
        }

        readonly Circling leftCircle = new Circling();
        readonly Circling rightCircle = new Circling();

        /// <summary>0 to 1.</summary>
        public float Charge { get; private set; }
        public Vector3 Center { get; private set; }
        public bool Charging => Charge > 0.05f;

        public bool Step(HandPose left, HandPose right, Stance body, float dt, out Vector3 direction, out float power)
        {
            direction = default;
            power = 0f;
            bool both = left.Tracked && right.Tracked && !left.Fist && !right.Fist;
            if (!both)
            {
                leftCircle.Reset();
                rightCircle.Reset();
                Charge = Mathf.Max(0f, Charge - Fade * dt);
                return false;
            }
            Center = (left.Palm + right.Palm) * 0.5f;
            Vector3 local = body.Local(Center);
            bool inFront = local.z > 0.1f && local.y < 0.2f && local.y > -0.8f && Vector3.Distance(left.Palm, right.Palm) < 0.8f;
            float push = Mathf.Min(Vector3.Dot(left.Velocity, body.Forward), Vector3.Dot(right.Velocity, body.Forward));
            if (Charge >= MinCharge && push > PushSpeed)
            {
                direction = Vector3.Slerp(((left.Velocity + right.Velocity) * 0.5f).normalized, body.Forward, 0.4f).normalized;
                power = Charge;
                Charge = 0f;
                leftCircle.Reset();
                rightCircle.Reset();
                return true;
            }
            float turned = (leftCircle.Step(left.Velocity) + rightCircle.Step(right.Velocity)) * 0.5f;
            if (inFront && turned > 0f)
                Charge = Mathf.Min(1f, Charge + turned / (360f * FullTurns));
            else
                Charge = Mathf.Max(0f, Charge - Fade * 0.5f * dt + Mathf.Min(0f, turned) / (360f * FullTurns));
            return false;
        }
    }

    /// <summary>
    /// Rock spikes (Earth level 2): a fist low at the waist, then raised fast to above the elbow, like an uppercut
    /// lifting the ground. The spikes run out along the way you face.
    /// </summary>
    public sealed class UppercutDetector
    {
        public static float LowBelow = -0.5f;
        public static float HighAbove = -0.3f;
        public static float MinRise = 0.9f;
        public static float Window = 0.8f;
        public static float Cooldown = 0.9f;
        float sinceLow = 99f;
        float cooldown;

        public bool Step(HandPose hand, Stance body, float dt, out Vector3 direction)
        {
            direction = body.Forward;
            cooldown -= dt;
            sinceLow += dt;
            if (!hand.Tracked || !hand.Fist)
                return false;
            float y = body.Local(hand.Wrist).y;
            if (y < LowBelow)
                sinceLow = 0f;
            if (cooldown > 0f || sinceLow > Window || y < HighAbove || hand.Velocity.y < MinRise)
                return false;
            cooldown = Cooldown;
            sinceLow = 99f;
            return true;
        }
    }

    /// <summary>
    /// A finger snap (Earth level 3 sets off stuck rocks): the thumb tip pressed to the middle (or index) fingertip,
    /// then flicked apart fast.
    /// </summary>
    public sealed class SnapDetector
    {
        public static float Touch = 0.03f;
        public static float Apart = 0.055f;
        public static float Window = 0.25f;
        public static float Cooldown = 0.6f;
        float sinceTouch = 99f;
        float cooldown;

        public bool Step(HandPose hand, float dt)
        {
            cooldown -= dt;
            sinceTouch += dt;
            if (!hand.Tracked)
                return false;
            float middle = Vector3.Distance(hand[Joint.ThumbTip], hand[Joint.MiddleTip]);
            float index = Vector3.Distance(hand[Joint.ThumbTip], hand[Joint.IndexTip]);
            float gap = Mathf.Min(middle, index);
            if (gap < Touch)
            {
                sinceTouch = 0f;
                return false;
            }
            if (cooldown > 0f || sinceTouch > Window || gap < Apart)
                return false;
            cooldown = Cooldown;
            sinceTouch = 99f;
            return true;
        }
    }
}
