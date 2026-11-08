using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// A frame of reference both headsets agree on, from two real points both players touched: the origin is the
    /// first point and forward runs flat toward the second. Each headset turns its own positions into this frame
    /// before sending, and back again on receipt. Uncalibrated, it passes positions through unchanged.
    /// </summary>
    public sealed class SharedFrame
    {
        public const float MinSpacing = 0.3f;

        Vector3 origin;
        Quaternion rotation = Quaternion.identity;

        public bool Calibrated { get; private set; }

        /// <summary>False if the points are too close together to give a direction.</summary>
        public bool Set(Vector3 first, Vector3 second)
        {
            Vector3 flat = Vector3.ProjectOnPlane(second - first, Vector3.up);
            if (flat.magnitude < MinSpacing)
                return false;
            origin = first;
            rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            Calibrated = true;
            return true;
        }

        public void Clear()
        {
            origin = Vector3.zero;
            rotation = Quaternion.identity;
            Calibrated = false;
        }

        public Vector3 ToShared(Vector3 world) => Quaternion.Inverse(rotation) * (world - origin);
        public Vector3 ToWorld(Vector3 shared) => origin + rotation * shared;
        public Quaternion ToShared(Quaternion world) => Quaternion.Inverse(rotation) * world;
        public Quaternion ToWorld(Quaternion shared) => rotation * shared;
        public Vector3 DirectionToShared(Vector3 world) => Quaternion.Inverse(rotation) * world;
        public Vector3 DirectionToWorld(Vector3 shared) => rotation * shared;
    }

    /// <summary>Calibration touch: a fingertip held still for a moment. Fires once, then needs to move away.</summary>
    public sealed class StillPointDetector
    {
        public const float Radius = 0.015f;
        public const float Seconds = 1.5f;
        public const float MoveAway = 0.1f;

        Vector3 anchor;
        float held;
        bool latched;

        public float Progress => Mathf.Clamp01(held / Seconds);
        public Vector3 Point => anchor;

        public bool Step(bool tracked, Vector3 tip, float dt)
        {
            if (!tracked)
            {
                held = 0f;
                return false;
            }
            if (latched)
            {
                if (Vector3.Distance(tip, anchor) > MoveAway)
                    latched = false;
                return false;
            }
            if (held == 0f || Vector3.Distance(tip, anchor) > Radius)
            {
                anchor = tip;
                held = dt > 0f ? 1e-5f : 0f;
                return false;
            }
            held += dt;
            anchor = Vector3.Lerp(anchor, tip, 0.1f);
            if (held < Seconds)
                return false;
            latched = true;
            held = 0f;
            return true;
        }

        public void Reset()
        {
            held = 0f;
            latched = false;
        }
    }
}
