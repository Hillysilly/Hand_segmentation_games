using UnityEngine;

namespace Repulsor
{
    /// <summary>Same order as XR Hands' XRHandJointID, minus Invalid, so XRHandJointID.ToIndex() maps straight in.</summary>
    public enum Joint
    {
        Wrist, Palm,
        ThumbMetacarpal, ThumbProximal, ThumbDistal, ThumbTip,
        IndexMetacarpal, IndexProximal, IndexIntermediate, IndexDistal, IndexTip,
        MiddleMetacarpal, MiddleProximal, MiddleIntermediate, MiddleDistal, MiddleTip,
        RingMetacarpal, RingProximal, RingIntermediate, RingDistal, RingTip,
        LittleMetacarpal, LittleProximal, LittleIntermediate, LittleDistal, LittleTip,
    }

    public enum Finger { Index, Middle, Ring, Little }

    /// <summary>
    /// One hand this frame, in world space, plus the shapes read from it. The same instance is reused every
    /// frame, so shapes with two thresholds (engage and release) remember whether they were on.
    /// Plain data with no XR dependency, so gestures can be tested with made-up hands.
    /// </summary>
    public sealed class HandPose
    {
        public const int JointCount = 26;

        // Finger extension: distance(tip, wrist) / distance(knuckle, wrist). About 1.8 straight, 1.0 curled.
        public static float OpenAbove => GestureClassifier.OpenAbove;
        public static float CurledBelow => GestureClassifier.CurledBelow;
        public static float PinchEngage = 0.025f;
        public static float PinchRelease = 0.04f;
        // Thumb tip's distance from the index knuckle, over the hand's length. Tucked about 0.4, out about 0.8.
        public static float ThumbOutAbove = 0.6f;
        /// <summary>Finger gun: the index is out past this, and the ring and little fingers are in below <see cref="GunOthersBelow"/>.</summary>
        public static float GunIndexAbove = 1.35f;
        public static float GunOthersBelow = 1.42f;
        public static float CupLow = 1.2f;
        public static float CupHigh = 1.65f;

        public readonly Vector3[] Joints = new Vector3[JointCount];
        public readonly bool IsLeft;

        public HandPose(bool isLeft) => IsLeft = isLeft;

        public bool Tracked;
        public float Time;
        public Vector3 PalmNormal;
        /// <summary>Smoothed palm velocity, m/s.</summary>
        public Vector3 Velocity;

        public float Index, Middle, Ring, Little;
        public float ThumbOut;
        public float Pinch;
        public HandGesture Gesture;

        public bool Fist => Tracked && Gesture == HandGesture.Fist;
        public bool Open => Tracked && Gesture == HandGesture.Open;
        public bool Ok { get; private set; }
        /// <summary>Index and middle straight, ring and little curled, thumb up.</summary>
        public bool Gun { get; private set; }
        /// <summary>Index and little straight, middle and ring curled.</summary>
        public bool SpiderSign { get; private set; }
        /// <summary>Every finger half bent, like holding a ball.</summary>
        public bool Cup { get; private set; }

        public Vector3 this[Joint joint] => Joints[(int)joint];
        public Vector3 Wrist => Joints[(int)Joint.Wrist];
        public Vector3 Palm => Joints[(int)Joint.Palm];
        /// <summary>Wrist to middle knuckle: where the hand points.</summary>
        public Vector3 Forward => (this[Joint.MiddleProximal] - Wrist).normalized;
        /// <summary>Along the index finger.</summary>
        public Vector3 IndexDirection => (this[Joint.IndexTip] - this[Joint.IndexProximal]).normalized;
        /// <summary>Little knuckle to index knuckle: the thumb side.</summary>
        public Vector3 ThumbSide => (this[Joint.IndexProximal] - this[Joint.LittleProximal]).normalized;
        public float Size => Vector3.Distance(Wrist, this[Joint.MiddleProximal]);

        public float Extension(Finger finger)
        {
            switch (finger)
            {
                case Finger.Index: return Index;
                case Finger.Middle: return Middle;
                case Finger.Ring: return Ring;
                default: return Little;
            }
        }

        /// <summary>Reads finger extensions and shapes from the joints. Call once per frame after filling them.</summary>
        public void Analyze()
        {
            if (!Tracked)
            {
                Gesture = HandGesture.None;
                Ok = Gun = SpiderSign = Cup = false;
                return;
            }

            Index = FingerExtension(Joint.IndexProximal, Joint.IndexTip);
            Middle = FingerExtension(Joint.MiddleProximal, Joint.MiddleTip);
            Ring = FingerExtension(Joint.RingProximal, Joint.RingTip);
            Little = FingerExtension(Joint.LittleProximal, Joint.LittleTip);
            float size = Mathf.Max(Size, 0.001f);
            ThumbOut = Vector3.Distance(this[Joint.ThumbTip], this[Joint.IndexProximal]) / size;
            Pinch = Vector3.Distance(this[Joint.ThumbTip], this[Joint.IndexTip]);
            Gesture = GestureClassifier.Classify(Index, Middle, Ring, Little);

            bool othersUp = Middle > OpenAbove && Ring > OpenAbove && Little > OpenAbove;
            Ok = othersUp && Pinch < (Ok ? PinchRelease : PinchEngage);

            // A little give once on, because tracking wobbles around the thresholds.
            float slack = 0.08f;
            // Finger gun, kept loose on purpose (a stray shot beats a shot that won't come): the index out and
            // the ring and little fingers in. The middle finger and thumb can be anywhere, so pointing counts.
            float gunGive = Gun ? 0.1f : 0f;
            Gun = Index > GunIndexAbove - gunGive && Ring < GunOthersBelow + gunGive && Little < GunOthersBelow + gunGive;

            float open = OpenAbove - (SpiderSign ? slack : 0f);
            float curled = CurledBelow + (SpiderSign ? slack : 0f);
            SpiderSign = Index > open && Little > open && Middle < curled && Ring < curled;

            float average = (Index + Middle + Ring + Little) * 0.25f;
            Cup = InRange(Index) && InRange(Middle) && InRange(Ring) && InRange(Little)
                && average > 1.3f && average < 1.58f && Pinch > PinchRelease && ThumbOut > 0.45f;
        }

        static bool InRange(float extension) => extension > CupLow && extension < CupHigh;

        float FingerExtension(Joint knuckle, Joint tip)
        {
            float knuckleDistance = Vector3.Distance(this[knuckle], Wrist);
            return knuckleDistance < 0.001f ? 0f : Vector3.Distance(this[tip], Wrist) / knuckleDistance;
        }

        public void CopyFrom(HandPose other)
        {
            System.Array.Copy(other.Joints, Joints, JointCount);
            Tracked = other.Tracked;
            Time = other.Time;
            PalmNormal = other.PalmNormal;
            Velocity = other.Velocity;
        }

        /// <summary>
        /// A made-up hand for tests and the PC preview. Curl is 0 for a straight finger and 1 for one folded
        /// into the palm. With the identity rotation the fingers point along +Z and the palm faces -Y.
        /// </summary>
        public void Pose(Vector3 wrist, Quaternion rotation, float index, float middle, float ring, float little,
            ThumbPose thumb)
        {
            Tracked = true;
            Vector3 forward = rotation * Vector3.forward;
            Vector3 palmNormal = rotation * Vector3.down;
            Vector3 thumbSide = rotation * (IsLeft ? Vector3.right : Vector3.left);
            PalmNormal = palmNormal;

            Set(Joint.Wrist, wrist);
            Set(Joint.Palm, wrist + forward * 0.05f);
            PoseFinger(Joint.IndexMetacarpal, wrist, forward, palmNormal, thumbSide * 0.025f, index);
            PoseFinger(Joint.MiddleMetacarpal, wrist, forward, palmNormal, Vector3.zero, middle);
            PoseFinger(Joint.RingMetacarpal, wrist, forward, palmNormal, -thumbSide * 0.02f, ring);
            PoseFinger(Joint.LittleMetacarpal, wrist, forward, palmNormal, -thumbSide * 0.04f, little);

            Vector3 thumbTip;
            switch (thumb)
            {
                case ThumbPose.Out:
                    thumbTip = wrist + forward * 0.06f + thumbSide * 0.085f - palmNormal * 0.02f;
                    break;
                case ThumbPose.PinchIndex:
                    thumbTip = this[Joint.IndexTip];
                    break;
                default:
                    thumbTip = wrist + forward * 0.1f + palmNormal * 0.035f;
                    break;
            }
            Set(Joint.ThumbMetacarpal, wrist + thumbSide * 0.02f + forward * 0.02f);
            Set(Joint.ThumbProximal, Vector3.Lerp(this[Joint.ThumbMetacarpal], thumbTip, 0.4f));
            Set(Joint.ThumbDistal, Vector3.Lerp(this[Joint.ThumbMetacarpal], thumbTip, 0.75f));
            Set(Joint.ThumbTip, thumbTip);
        }

        void PoseFinger(Joint metacarpal, Vector3 wrist, Vector3 forward, Vector3 palmNormal, Vector3 sideways, float curl)
        {
            Vector3 knuckle = wrist + forward * 0.09f + sideways;
            Vector3 straight = knuckle + forward * 0.075f;
            Vector3 folded = wrist + forward * 0.085f + palmNormal * 0.03f + sideways;
            Vector3 tip = Vector3.Lerp(straight, folded, curl);

            int m = (int)metacarpal;
            Joints[m] = wrist + forward * 0.03f + sideways;
            Joints[m + 1] = knuckle;
            Joints[m + 2] = Vector3.Lerp(knuckle, tip, 0.45f);
            Joints[m + 3] = Vector3.Lerp(knuckle, tip, 0.75f);
            Joints[m + 4] = tip;
        }

        void Set(Joint joint, Vector3 position) => Joints[(int)joint] = position;
    }

    public enum ThumbPose { Tucked, Out, PinchIndex }

    /// <summary>Where the player is: head position and the way the body faces, for gestures that depend on posture.</summary>
    public struct Stance
    {
        public Vector3 Head;
        /// <summary>Horizontal facing direction.</summary>
        public Vector3 Forward;
        public Vector3 Right;
        public float FloorY;

        public static Stance From(Vector3 head, Vector3 lookForward, float floorY = 0f)
        {
            Vector3 flat = Vector3.ProjectOnPlane(lookForward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f)
                flat = Vector3.forward;
            flat.Normalize();
            return new Stance
            {
                Head = head,
                Forward = flat,
                Right = Vector3.Cross(Vector3.up, flat),
                FloorY = floorY,
            };
        }

        /// <summary>A point relative to the head: x right, y up, z forward.</summary>
        public Vector3 Local(Vector3 world)
        {
            Vector3 d = world - Head;
            return new Vector3(Vector3.Dot(d, Right), d.y, Vector3.Dot(d, Forward));
        }

        public Vector3 Shoulder(bool left) => Head + Vector3.down * 0.22f + Right * (left ? -0.18f : 0.18f);

        /// <summary>Where an arm points: from the shoulder through the hand.</summary>
        public Vector3 ArmDirection(bool left, Vector3 hand) => (hand - Shoulder(left)).normalized;
    }
}
