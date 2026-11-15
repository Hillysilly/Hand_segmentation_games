using System.Collections.Generic;
using UnityEngine;

namespace Repulsor
{
    /// <summary>A hand shape the select screen shows: finger bends and where the thumb goes.</summary>
    public enum SignShape { Relaxed, Open, Fist, Gun, Spider, Cup, Snap, Snapped, Triangle }

    /// <summary>
    /// Poses a rigged hand into a <see cref="SignShape"/> and points it: fingers are straightened flat once, then
    /// bent toward the palm by the shape's angles; the thumb is reached to a point (where the model has a thumb
    /// tip) or bent. Works on the select screen's mannequin (Rigify DEF- bones) and on the XR Hands sample hand
    /// (OpenXR joint names, the same as the game's <see cref="Joint"/>s).
    /// </summary>
    public sealed class HandRig
    {
        enum ThumbAim { Rest, Out, Up, Tucked, Cup, PinchMiddle }

        struct ShapeDef
        {
            public Vector3[] Fingers;   // index, middle, ring, little: degrees at the three joints
            public ThumbAim Thumb;
        }

        static readonly Vector3 Straight = new Vector3(2f, 3f, 2f);
        static readonly Vector3 Loose = new Vector3(15f, 25f, 15f);
        static readonly Vector3 Curled = new Vector3(82f, 100f, 62f);
        static readonly Vector3 Half = new Vector3(42f, 40f, 30f);

        static ShapeDef Def(SignShape shape)
        {
            switch (shape)
            {
                case SignShape.Open:
                case SignShape.Triangle:
                    return new ShapeDef { Fingers = new[] { Straight, Straight, Straight, Straight }, Thumb = ThumbAim.Out };
                case SignShape.Fist:
                    return new ShapeDef { Fingers = new[] { Curled, Curled, Curled, Curled }, Thumb = ThumbAim.Tucked };
                case SignShape.Gun:
                    return new ShapeDef { Fingers = new[] { Straight, Curled, Curled, Curled }, Thumb = ThumbAim.Up };
                case SignShape.Spider:
                    return new ShapeDef { Fingers = new[] { Straight, Curled, Curled, Straight }, Thumb = ThumbAim.Out };
                case SignShape.Cup:
                    return new ShapeDef { Fingers = new[] { Half, Half, Half, Half }, Thumb = ThumbAim.Cup };
                case SignShape.Snap:
                    return new ShapeDef
                    {
                        Fingers = new[] { new Vector3(28f, 40f, 28f), new Vector3(42f, 52f, 36f), Curled, Curled },
                        Thumb = ThumbAim.PinchMiddle,
                    };
                case SignShape.Snapped:
                    return new ShapeDef { Fingers = new[] { new Vector3(18f, 25f, 15f), Curled, Curled, Curled }, Thumb = ThumbAim.Up };
                default:
                    return new ShapeDef { Fingers = new[] { Loose, Loose, Loose, Loose }, Thumb = ThumbAim.Rest };
            }
        }

        /// <summary>One finger bone: its straightened rotation and the axis (its own space) it bends about.</summary>
        sealed class Bone
        {
            public Transform T;
            public Quaternion Straight;
            public Vector3 Axis;
        }

        readonly bool right;
        readonly Transform wrist;
        readonly Bone[][] fingers = new Bone[4][];
        readonly Transform[] thumb;
        readonly Quaternion[] thumbRest;
        readonly Vector3[] thumbAxis;
        readonly Transform thumbTip;
        readonly Transform[] tips = new Transform[4];
        readonly Quaternion restFrame;
        readonly float length;
        /// <summary>The bone each <see cref="Joint"/> sits on, where the model has it (the XR hand has them all).</summary>
        readonly Dictionary<Joint, Transform> joints = new Dictionary<Joint, Transform>();

        // Where the shape is heading and how far along (for blending between shapes).
        readonly Vector3[] bends = new Vector3[4];
        Vector3 thumbBend;

        public Transform Wrist => wrist;
        public bool IsRight => right;
        /// <summary>Wrist to middle knuckle.</summary>
        public float Length => length;

        HandRig(bool right, Transform wrist, Transform[][] fingerBones, Transform[] fingerTips, Transform[] thumbBones, Transform thumbTip,
            Transform indexKnuckle, Transform middleKnuckle, Transform littleKnuckle)
        {
            this.right = right;
            this.wrist = wrist;
            this.thumbTip = thumbTip;
            thumb = thumbBones;

            // The hand's own frame, in the wrist bone's space: fingers forward, palm normal out of the palm.
            Vector3 forward = (middleKnuckle.position - wrist.position).normalized;
            Vector3 thumbSide = (indexKnuckle.position - littleKnuckle.position).normalized;
            Vector3 palm = (right ? Vector3.Cross(forward, thumbSide) : Vector3.Cross(thumbSide, forward)).normalized;
            length = Vector3.Distance(middleKnuckle.position, wrist.position);
            restFrame = Quaternion.LookRotation(wrist.InverseTransformDirection(forward), wrist.InverseTransformDirection(-palm));

            // Straighten each finger flat with the palm, joint by joint, then remember those rotations.
            for (int f = 0; f < 4; f++)
            {
                Transform[] chain = fingerBones[f];
                tips[f] = fingerTips[f];
                var bones = new Bone[chain.Length];
                for (int i = 0; i < chain.Length; i++)
                {
                    Transform bone = chain[i];
                    Vector3 along = Along(chain, i, fingerTips[f]);
                    Vector3 axis = BendAxis(along, palm);
                    float pitch = Mathf.Asin(Mathf.Clamp(Vector3.Dot(along, palm), -1f, 1f)) * Mathf.Rad2Deg;
                    bone.rotation = Quaternion.AngleAxis(-pitch, axis) * bone.rotation;
                    bones[i] = new Bone
                    {
                        T = bone,
                        Straight = bone.localRotation,
                        Axis = bone.InverseTransformDirection(BendAxis(Along(chain, i, fingerTips[f]), palm)),
                    };
                }
                fingers[f] = bones;
            }

            thumbRest = new Quaternion[thumb.Length];
            thumbAxis = new Vector3[thumb.Length];
            for (int i = 0; i < thumb.Length; i++)
            {
                thumbRest[i] = thumb[i].localRotation;
                thumbAxis[i] = thumb[i].InverseTransformDirection(BendAxis(Along(thumb, i, thumbTip), palm));
            }
        }

        /// <summary>Direction along a chain's bone: toward the next bone, or the tip, or (last, no tip) as the one before.</summary>
        static Vector3 Along(Transform[] chain, int i, Transform tip)
        {
            Transform next = i + 1 < chain.Length ? chain[i + 1] : tip;
            if (next != null)
                return (next.position - chain[i].position).normalized;
            return i > 0 ? (chain[i].position - chain[i - 1].position).normalized : chain[i].up;
        }

        /// <summary>The axis that turns <paramref name="along"/> toward <paramref name="palm"/> for a positive angle.</summary>
        static Vector3 BendAxis(Vector3 along, Vector3 palm)
        {
            Vector3 axis = Vector3.Cross(along, palm);
            if (axis.sqrMagnitude < 1e-6f)
                axis = Vector3.Cross(along, Vector3.up);
            axis.Normalize();
            if (Vector3.Dot(Quaternion.AngleAxis(10f, axis) * along, palm) < Vector3.Dot(along, palm))
                axis = -axis;
            return axis;
        }

        static Transform Find(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t;
            return null;
        }

        /// <summary>The XR Hands sample hand ("R_Wrist", "R_IndexProximal", ... ). Null if the bones aren't there.</summary>
        public static HandRig ForXrHand(Transform root, bool right)
        {
            string p = right ? "R_" : "L_";
            Transform B(string name) => Find(root, p + name);
            Transform wrist = B("Wrist");
            if (wrist == null)
                return null;
            string[] names = { "Index", "Middle", "Ring", "Little" };
            var chains = new Transform[4][];
            var tips = new Transform[4];
            for (int f = 0; f < 4; f++)
            {
                chains[f] = new[] { B(names[f] + "Proximal"), B(names[f] + "Intermediate"), B(names[f] + "Distal") };
                tips[f] = B(names[f] + "Tip");
            }
            var rig = new HandRig(right, wrist, chains, tips,
                new[] { B("ThumbMetacarpal"), B("ThumbProximal"), B("ThumbDistal") }, B("ThumbTip"),
                B("IndexProximal"), B("MiddleProximal"), B("LittleProximal"));
            foreach (Joint joint in System.Enum.GetValues(typeof(Joint)))
            {
                Transform bone = B(joint.ToString());
                if (bone != null)
                    rig.joints[joint] = bone;
            }
            return rig;
        }

        /// <summary>The Quaternius mannequin's hand ("DEF-hand.R", "DEF-f_index.01.R", ...). Null if it isn't there.</summary>
        public static HandRig ForMannequin(Transform root, bool right)
        {
            string s = right ? ".R" : ".L";
            Transform B(string name) => Find(root, "DEF-" + name + s);
            Transform hand = B("hand");
            if (hand == null)
                return null;
            string[] names = { "f_index", "f_middle", "f_ring", "f_pinky" };
            var chains = new Transform[4][];
            for (int f = 0; f < 4; f++)
                chains[f] = new[] { B(names[f] + ".01"), B(names[f] + ".02"), B(names[f] + ".03") };
            return new HandRig(right, hand, chains, new Transform[4],
                new[] { B("thumb.01"), B("thumb.02"), B("thumb.03") }, null,
                chains[0][0], chains[1][0], chains[3][0]);
        }

        /// <summary>World rotation for the hand so its fingers point along <paramref name="forward"/> and its palm faces <paramref name="palm"/>.</summary>
        public Quaternion Facing(Vector3 forward, Vector3 palm)
        {
            Vector3 up = -palm;
            if (Vector3.Cross(forward, up).sqrMagnitude < 1e-6f)
                up = Vector3.up;
            return Quaternion.LookRotation(forward, up) * Quaternion.Inverse(restFrame);
        }

        /// <summary>Puts the hand in a shape now (no blending).</summary>
        public void Shape(SignShape shape) => Shape(shape, shape, 0f);

        /// <summary>A shape part way to the next (0 = <paramref name="from"/>, 1 = <paramref name="to"/>).</summary>
        public void Shape(SignShape from, SignShape to, float t)
        {
            ShapeDef a = Def(from);
            ShapeDef b = Def(to);
            for (int f = 0; f < 4; f++)
            {
                bends[f] = Vector3.Lerp(a.Fingers[f], b.Fingers[f], t);
                Bone[] chain = fingers[f];
                for (int i = 0; i < chain.Length; i++)
                    chain[i].T.localRotation = chain[i].Straight * Quaternion.AngleAxis(bends[f][Mathf.Min(i, 2)], chain[i].Axis);
            }
            ThumbAim aim = t < 0.5f ? a.Thumb : b.Thumb;
            PoseThumb(aim, from == to ? 1f : Mathf.Abs(t - 0.5f) * 2f);
        }

        Vector3 PalmNormal => wrist.rotation * restFrame * Vector3.down;
        Vector3 Forward => wrist.rotation * restFrame * Vector3.forward;
        Vector3 ThumbSide => right ? Vector3.Cross(PalmNormal, Forward) : Vector3.Cross(Forward, PalmNormal);

        void PoseThumb(ThumbAim aim, float weight)
        {
            for (int i = 0; i < thumb.Length; i++)
                thumb[i].localRotation = thumbRest[i];
            if (aim == ThumbAim.Rest)
                return;
            float scale = wrist.lossyScale.x;
            float h = length;
            Vector3 n = PalmNormal, f = Forward, t = ThumbSide;
            Vector3 origin = wrist.position;
            Vector3 target;
            switch (aim)
            {
                case ThumbAim.Out: target = origin + (f * 0.85f + t * 0.95f + n * 0.05f) * h; break;
                case ThumbAim.Up: target = origin + (f * 0.75f + t * 0.9f - n * 0.2f) * h; break;
                case ThumbAim.Cup: target = origin + (f * 0.85f + t * 0.6f + n * 0.55f) * h; break;
                case ThumbAim.PinchMiddle:
                    target = tips[1] != null ? tips[1].position : origin + (f * 1.4f + n * 0.4f) * h;
                    break;
                default:
                    // Tucked across the front of the curled index and middle fingers.
                    target = fingers[0].Length > 1 && fingers[1].Length > 1
                        ? (fingers[0][1].T.position + fingers[1][1].T.position) * 0.5f + n * 0.28f * h
                        : origin + (f * 1f + n * 0.5f) * h;
                    break;
            }
            if (thumbTip == null)
            {
                // No tip to reach with (the mannequin): bend it in for a fist, leave it out otherwise.
                float bend = aim == ThumbAim.Tucked ? 35f : aim == ThumbAim.Cup ? 15f : 0f;
                for (int i = 0; i < thumb.Length; i++)
                    thumb[i].localRotation = thumbRest[i] * Quaternion.AngleAxis(bend, thumbAxis[i]);
                return;
            }
            // Reach the tip to the target: a few passes of turning each joint toward it (CCD).
            for (int pass = 0; pass < 10; pass++)
            {
                for (int i = thumb.Length - 1; i >= 0; i--)
                {
                    Vector3 toTip = thumbTip.position - thumb[i].position;
                    Vector3 toTarget = target - thumb[i].position;
                    if (toTip.sqrMagnitude < 1e-10f || toTarget.sqrMagnitude < 1e-10f)
                        continue;
                    Quaternion turn = Quaternion.FromToRotation(toTip, toTarget);
                    thumb[i].rotation = Quaternion.Slerp(Quaternion.identity, turn, weight * 0.8f) * thumb[i].rotation;
                }
                if ((thumbTip.position - target).sqrMagnitude < (0.002f * scale) * (0.002f * scale))
                    break;
            }
        }

        /// <summary>Where the fingers point now, in world space.</summary>
        public Vector3 Pointing => Forward;

        /// <summary>Reaches the thumb tip to a point (CCD), from wherever it is now.</summary>
        public void ReachThumb(Vector3 target)
        {
            if (thumbTip == null)
                return;
            for (int pass = 0; pass < 12; pass++)
                for (int i = thumb.Length - 1; i >= 0; i--)
                {
                    Vector3 toTip = thumbTip.position - thumb[i].position;
                    Vector3 toTarget = target - thumb[i].position;
                    if (toTip.sqrMagnitude > 1e-10f && toTarget.sqrMagnitude > 1e-10f)
                        thumb[i].rotation = Quaternion.FromToRotation(toTip, toTarget) * thumb[i].rotation;
                }
        }

        /// <summary>Where the index (or another) fingertip is, if the model has tips.</summary>
        public Vector3 Tip(int finger) => tips[finger] != null ? tips[finger].position : wrist.position;
        public Vector3 ThumbTipPosition => thumbTip != null ? thumbTip.position : wrist.position;

        /// <summary>
        /// Copies the posed hand into a <see cref="HandPose"/> in <paramref name="space"/> (so its scale is undone), and
        /// reads its shapes. For checking that a sign really is the gesture the game looks for.
        /// </summary>
        public void ReadInto(HandPose pose, Transform space)
        {
            foreach (KeyValuePair<Joint, Transform> pair in joints)
                pose.Joints[(int)pair.Key] = space.InverseTransformPoint(pair.Value.position);
            pose.Tracked = true;
            pose.PalmNormal = space.InverseTransformDirection(PalmNormal).normalized;
            pose.Analyze();
        }
    }
}
