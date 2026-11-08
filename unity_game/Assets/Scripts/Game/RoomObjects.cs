using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Repulsor
{
    /// <summary>
    /// Furniture from Space Setup as things you can lift and throw (force powers, web pull). The real object
    /// stays where it is; a glowing copy of its box is what flies. Copies break when they hit something, and
    /// the same object can be used again after a short wait.
    /// </summary>
    public sealed class RoomObjects
    {
        public const float MaxReach = 6f;
        const float ReuseSeconds = 8f;
        const float MaxThrownSize = 1.2f;

        readonly Transform parent;
        readonly Dictionary<TrackableId, float> usedAt = new Dictionary<TrackableId, float>();
        readonly List<ARBoundingBox> boxes = new List<ARBoundingBox>();
        ARBoundingBoxManager manager;
        Bubble highlight;

        public RoomObjects(Transform parent) => this.parent = parent;

        public int Count => boxes.Count;

        public void Refresh(ARBoundingBoxManager boxManager)
        {
            manager = boxManager;
            boxes.Clear();
            if (manager == null)
                return;
            foreach (ARBoundingBox box in manager.trackables)
                boxes.Add(box);
        }

        /// <summary>True if a point on the floor plan is inside a furniture box, grown by a margin.</summary>
        public bool Blocks(Vector3 point, float margin)
        {
            foreach (ARBoundingBox box in boxes)
            {
                if (box == null)
                    continue;
                Vector3 local = box.transform.InverseTransformPoint(point);
                Vector3 half = box.size * 0.5f;
                if (Mathf.Abs(local.x) < half.x + margin && Mathf.Abs(local.z) < half.z + margin)
                    return true;
            }
            return false;
        }

        /// <summary>The nearest box a ray points at, within reach and not recently thrown.</summary>
        public bool TryAim(Vector3 origin, Vector3 direction, out ARBoundingBox hit)
        {
            hit = null;
            float best = MaxReach;
            foreach (ARBoundingBox box in boxes)
            {
                if (box == null || (usedAt.TryGetValue(box.trackableId, out float when) && Time.time - when < ReuseSeconds))
                    continue;
                if (RayHitsBox(origin, direction, box.transform, box.size, out float distance) && distance < best)
                {
                    best = distance;
                    hit = box;
                }
            }
            return hit != null;
        }

        /// <summary>Outlines the box being aimed at, or hides the outline with null.</summary>
        public void Highlight(ARBoundingBox box, Color color)
        {
            if (box == null)
            {
                if (highlight != null)
                    highlight.Show(color, BubbleStyle.Thin, 0f);
                return;
            }
            if (highlight == null)
                highlight = Bubble.Create("Room Highlight", parent);
            highlight.GetComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>("CubeMesh");
            highlight.transform.SetPositionAndRotation(box.transform.position, box.transform.rotation);
            highlight.transform.localScale = box.size + Vector3.one * 0.04f;
            highlight.Show(color, BubbleStyle.Glass, 0.6f);
        }

        /// <summary>Makes the flying copy of a box and marks the real one as used for a while.</summary>
        public ThrownObject Lift(ARBoundingBox box, Color color, Arena arena)
        {
            usedAt[box.trackableId] = Time.time;
            Vector3 size = box.size;
            float largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (largest > MaxThrownSize)
                size *= MaxThrownSize / largest;
            string label = box.classifications.ToString();
            return ThrownObject.Create(box.transform.position, box.transform.rotation, size, color, label, arena, parent);
        }

        /// <summary>Distance along a ray to where it enters a box (centred on its transform, of this size).</summary>
        public static bool RayHitsBox(Vector3 origin, Vector3 direction, Transform box, Vector3 size, out float distance)
        {
            // Into the box's own frame, where it's axis-aligned and centred.
            Vector3 o = box.InverseTransformPoint(origin);
            Vector3 d = box.InverseTransformDirection(direction);
            Vector3 half = size * 0.5f;
            float near = 0f;
            float far = float.MaxValue;
            distance = 0f;
            for (int axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(d[axis]) < 1e-6f)
                {
                    if (Mathf.Abs(o[axis]) > half[axis])
                        return false;
                    continue;
                }
                float t1 = (-half[axis] - o[axis]) / d[axis];
                float t2 = (half[axis] - o[axis]) / d[axis];
                near = Mathf.Max(near, Mathf.Min(t1, t2));
                far = Mathf.Min(far, Mathf.Max(t1, t2));
                if (near > far)
                    return false;
            }
            distance = near;
            return true;
        }
    }

    /// <summary>
    /// The glowing copy of a piece of furniture: it floats to your hand while held, flies when thrown, and
    /// breaks on the first enemy or surface it reaches, damaging what's nearby. Bigger objects hit harder.
    /// </summary>
    public sealed class ThrownObject : MonoBehaviour
    {
        const float HoldDistance = 0.9f;
        const float FollowSpeed = 6f;
        const float MinThrowSpeed = 6f;
        const float ThrowBoost = 3f;
        const float Gravity = 4f;
        const float LifeSeconds = 3f;

        Arena arena;
        Bubble shell;
        Vector3 velocity;
        Color color;
        float age;
        float radius;
        float damage;
        bool thrown;
        float wallAt = float.MaxValue;
        float flown;

        public string Label { get; private set; }

        public static ThrownObject Create(Vector3 position, Quaternion rotation, Vector3 size, Color color, string label,
            Arena arena, Transform parent)
        {
            Bubble shell = Bubble.Create("Thrown " + label, parent);
            shell.GetComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>("CubeMesh");
            shell.transform.SetPositionAndRotation(position, rotation);
            shell.transform.localScale = size;
            var thrown = shell.gameObject.AddComponent<ThrownObject>();
            thrown.shell = shell;
            thrown.arena = arena;
            thrown.color = color;
            thrown.Label = label;
            thrown.radius = Mathf.Max(0.15f, size.magnitude * 0.35f);
            float volume = size.x * size.y * size.z;
            thrown.damage = Mathf.Clamp(10f + 40f * volume, 10f, 40f);
            return thrown;
        }

        /// <summary>While held: drift toward a point in front of the hand.</summary>
        public void Hold(Vector3 hand, Vector3 aim, float dt)
        {
            if (thrown)
                return;
            Vector3 wanted = hand + aim * HoldDistance + Vector3.up * 0.1f;
            transform.position = Vector3.Lerp(transform.position, wanted, 1f - Mathf.Exp(-FollowSpeed * dt));
            transform.Rotate(Vector3.up, 40f * dt, Space.World);
        }

        public void Throw(Vector3 handVelocity, Vector3 aim, float wallDistance)
        {
            thrown = true;
            wallAt = wallDistance;
            flown = 0f;
            age = 0f;
            Vector3 v = handVelocity * ThrowBoost;
            if (Vector3.Dot(v, aim) < MinThrowSpeed)
                v = aim * MinThrowSpeed + Vector3.ProjectOnPlane(v, aim) * 0.3f;
            velocity = v;
        }

        /// <summary>Let go without throwing: it fades away.</summary>
        public void Drop()
        {
            thrown = true;
            velocity = Vector3.down * 0.5f;
            age = LifeSeconds - 0.4f;
            damage = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            shell.Show(color, BubbleStyle.Hex, Mathf.Clamp01(age * 4f) * (thrown ? Mathf.Clamp01((LifeSeconds - age) * 2f) : 1f));
            if (!thrown)
                return;

            velocity += Vector3.down * (Gravity * dt);
            Vector3 from = transform.position;
            Vector3 to = from + velocity * dt;
            transform.position = to;
            transform.Rotate(Vector3.Cross(Vector3.up, velocity.normalized), 200f * dt, Space.World);

            bool hit = damage > 0f && arena.ThrownHits(from, to, radius);
            flown += (to - from).magnitude;
            bool landed = to.y < 0.05f || flown >= wallAt;
            if (hit || landed || age > LifeSeconds)
            {
                if (damage > 0f && (hit || landed))
                    arena.ThrownBreaks(to, radius + 0.4f, damage, color);
                Destroy(gameObject);
            }
        }
    }
}
