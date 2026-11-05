using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// Poses a rigged humanoid model (Mixamo bone names) from BlazePose joints in world space: the torso and
    /// head as whole frames, then each arm and leg swung onto its joints and stretched to fit. Ported from the
    /// web version's suitmodel.js, without its mirroring, because here the person isn't seen in a mirror.
    /// </summary>
    public sealed class SuitRig
    {
        const float MinStretch = 0.6f;
        const float MaxStretch = 1.6f;
        const float NoseTilt = 0.15f * Mathf.Rad2Deg;

        // BlazePose joint numbers.
        const int Nose = 0, EarLeft = 7, EarRight = 8, ShoulderLeft = 11, ShoulderRight = 12;
        const int HipLeft = 23, HipRight = 24;

        static readonly string[] Spine = { "Spine", "Spine1", "Spine2", "Spine3" };
        static readonly Regex Prefix = new Regex(@"^.*?mixamorig[:_]?", RegexOptions.IgnoreCase);
        static readonly Regex Number = new Regex(@"_\d+$");

        struct Rest
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        readonly struct Limb
        {
            public readonly string Upper, Lower, End;
            public readonly int A, B, C;

            public Limb(string upper, string lower, string end, int a, int b, int c)
            {
                Upper = upper; Lower = lower; End = end; A = a; B = b; C = c;
            }
        }

        // The model's left follows the person's left.
        static readonly Limb[] Limbs =
        {
            new Limb("LeftArm", "LeftForeArm", "LeftHand", 11, 13, 15),
            new Limb("RightArm", "RightForeArm", "RightHand", 12, 14, 16),
            new Limb("LeftUpLeg", "LeftLeg", "LeftFoot", 23, 25, 27),
            new Limb("RightUpLeg", "RightLeg", "RightFoot", 24, 26, 28),
        };

        readonly Transform holder;
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        readonly Dictionary<Transform, Rest> rest = new Dictionary<Transform, Rest>();
        readonly Dictionary<string, Quaternion> offsets = new Dictionary<string, Quaternion>();
        readonly Dictionary<string, Vector3> hinges = new Dictionary<string, Vector3>();
        readonly List<string> spine = new List<string>();
        readonly float restTorso;
        readonly float restWidth;

        /// <summary>The model's scale over its original size, for telling whether fitting looks sensible.</summary>
        public float Scale { get; private set; } = 1f;

        /// <param name="holder">The object to move and scale, with the model inside it in its rest pose.</param>
        public SuitRig(Transform holder)
        {
            this.holder = holder;
            foreach (Transform t in holder.GetComponentsInChildren<Transform>(true))
            {
                string name = BoneName(t.name);
                if (!bones.ContainsKey(name) && name.Length > 0 && t != holder)
                    bones[name] = t;
            }
            foreach (Transform bone in bones.Values)
                rest[bone] = new Rest { Position = bone.localPosition, Rotation = bone.localRotation, Scale = bone.localScale };

            foreach (string name in Spine)
            {
                if (bones.ContainsKey(name))
                    spine.Add(name);
            }

            Vector3 shoulders = Mid(Pos("LeftArm"), Pos("RightArm"));
            Vector3 hips = Mid(Pos("LeftUpLeg"), Pos("RightUpLeg"));
            Vector3 up = shoulders - hips;
            restTorso = up.magnitude;
            restWidth = Vector3.Distance(Pos("LeftArm"), Pos("RightArm"));

            Quaternion body = Frame(Pos("LeftArm") - Pos("RightArm"), up);
            var driven = new List<string> { "Hips", "Neck", "Head" };
            driven.AddRange(spine);
            foreach (string name in driven)
            {
                if (bones.TryGetValue(name, out Transform bone))
                    offsets[name] = Quaternion.Inverse(body) * bone.rotation;
            }

            // Which way each joint naturally bends, in the upper bone's own space: elbows forward, knees back.
            foreach (Limb limb in Limbs)
            {
                float bend = limb.Upper.EndsWith("Arm") ? 1f : -1f;
                Vector3 along = (Pos(limb.Lower) - Pos(limb.Upper)).normalized;
                Vector3 forward = body * Vector3.forward * bend;
                hinges[limb.Upper] = Quaternion.Inverse(bones[limb.Upper].rotation) * Vector3.Cross(along, forward).normalized;
            }
        }

        public static string BoneName(string name)
        {
            string bone = Number.Replace(Prefix.Replace(name, ""), "");
            return bone == "Pelvis" ? "Hips" : bone == "Head_end" ? "HeadTop_End" : bone;
        }

        public bool HasBone(string name) => bones.ContainsKey(name);

        /// <summary>How far each limb's joints are from where they were asked to be, for checking the fit.</summary>
        public string FitReport(Vector3[] joints)
        {
            var report = new System.Text.StringBuilder();
            foreach (Limb limb in Limbs)
            {
                report.Append($"{limb.Upper}: mid {Vector3.Distance(bones[limb.Lower].position, joints[limb.B]):0.00} ")
                    .Append($"end {Vector3.Distance(bones[limb.End].position, joints[limb.C]):0.00} ")
                    .Append($"want {joints[limb.A]:F2} {joints[limb.B]:F2} {joints[limb.C]:F2} ")
                    .Append($"got {bones[limb.Upper].position:F2} {bones[limb.Lower].position:F2} {bones[limb.End].position:F2}; ");
            }
            return report.ToString();
        }

        Vector3 Pos(string name) => bones[name].position;

        static Vector3 Mid(Vector3 a, Vector3 b) => (a + b) * 0.5f;

        /// <summary>A rotation built from a sideways and an upwards direction, the same way every time.</summary>
        static Quaternion Frame(Vector3 side, Vector3 up)
        {
            Vector3 y = up.normalized;
            Vector3 x = Vector3.ProjectOnPlane(side, y).normalized;

            // Unity is left-handed, so this cross is the other way round from the web version's.
            return Quaternion.LookRotation(Vector3.Cross(y, x), y);
        }

        static void RotateWorld(Transform bone, Quaternion delta) => bone.rotation = delta * bone.rotation;

        static void StretchTo(Transform parent, Transform bone, float length, Vector3 restPosition)
        {
            float current = Vector3.Distance(parent.position, bone.position);
            if (current > 0.000001f)
                bone.localPosition = restPosition * Mathf.Clamp(length / current, MinStretch, MaxStretch);
        }

        /// <summary>Poses the model on 33 joints in world space.</summary>
        public void Pose(Vector3[] joints)
        {
            foreach (KeyValuePair<Transform, Rest> pair in rest)
                pair.Key.SetLocalPositionAndRotation(pair.Value.Position, pair.Value.Rotation);

            Vector3 hips = Mid(joints[HipLeft], joints[HipRight]);
            Vector3 shoulders = Mid(joints[ShoulderLeft], joints[ShoulderRight]);
            Vector3 up = shoulders - hips;
            float width = Vector3.Distance(joints[ShoulderLeft], joints[ShoulderRight]);

            Scale = (up.magnitude + width) / (restTorso + restWidth);
            holder.localScale = Vector3.one * Scale;
            holder.position = Vector3.zero;

            Quaternion hipFrame = Frame(joints[HipLeft] - joints[HipRight], up);
            Quaternion chestFrame = Frame(joints[ShoulderLeft] - joints[ShoulderRight], up);

            // The head: ear to ear for sideways, and the nose for which way it's facing.
            Vector3 ears = (joints[EarLeft] - joints[EarRight]).normalized;
            Vector3 facing = Vector3.ProjectOnPlane(joints[Nose] - Mid(joints[EarLeft], joints[EarRight]), ears).normalized;
            facing = Quaternion.AngleAxis(-NoseTilt, ears) * facing;
            Quaternion headFrame = Frame(ears, Vector3.Cross(ears, facing));

            SetFrame("Hips", hipFrame);
            for (int i = 0; i < spine.Count; i++)
                SetFrame(spine[i], Quaternion.Slerp(hipFrame, chestFrame, (i + 1f) / spine.Count));
            SetFrame("Neck", Quaternion.Slerp(chestFrame, headFrame, 0.5f));
            SetFrame("Head", headFrame);

            // The model's shoulders to the person's width, then its shoulders onto theirs.
            float modelWidth = Vector3.Distance(Pos("LeftArm"), Pos("RightArm"));
            foreach (string side in new[] { "Left", "Right" })
            {
                Transform arm = bones[side + "Arm"];
                float length = Vector3.Distance(arm.position, bones[side + "Shoulder"].position);
                if (length > 0.000001f)
                    arm.localPosition *= Mathf.Clamp((length + (width - modelWidth) * 0.5f) / length, 0.5f, 2f);
            }
            holder.position = shoulders - Mid(Pos("LeftArm"), Pos("RightArm"));

            foreach (Limb limb in Limbs)
                PoseLimb(limb, joints[limb.A], joints[limb.B], joints[limb.C]);
        }

        void SetFrame(string name, Quaternion frame)
        {
            if (bones.TryGetValue(name, out Transform bone) && offsets.TryGetValue(name, out Quaternion offset))
                bone.rotation = frame * offset;
        }

        /// <summary>Swings upper, lower and end onto the points, twisting so the joint bends the way they do.</summary>
        void PoseLimb(Limb limb, Vector3 a, Vector3 b, Vector3 c)
        {
            Transform upper = bones[limb.Upper], lower = bones[limb.Lower], end = bones[limb.End];
            StretchTo(upper, lower, Vector3.Distance(a, b), rest[lower].Position);
            StretchTo(lower, end, Vector3.Distance(b, c), rest[end].Position);

            Vector3 u = (b - a).normalized, f = (c - b).normalized;
            RotateWorld(upper, Quaternion.FromToRotation((lower.position - upper.position).normalized, u));

            Vector3 n = Vector3.Cross(u, f);
            float bent = n.magnitude;
            if (bent > 0.15f)
            {
                Vector3 hinge = Vector3.ProjectOnPlane(upper.rotation * hinges[limb.Upper], u);
                n /= bent;
                float angle = Mathf.Atan2(Vector3.Dot(u, Vector3.Cross(hinge, n)), Vector3.Dot(hinge, n)) * Mathf.Rad2Deg;
                RotateWorld(upper, Quaternion.AngleAxis(angle * Mathf.Min(1f, (bent - 0.15f) / 0.3f), u));
            }

            RotateWorld(lower, Quaternion.FromToRotation((end.position - lower.position).normalized, f));
        }
    }
}
