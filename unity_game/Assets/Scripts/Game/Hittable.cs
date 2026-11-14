using System.Collections.Generic;
using UnityEngine;

namespace Repulsor
{
    public enum Team { Players, Enemies }

    /// <summary>Anything attacks can hurt: enemies, practice orbs, later room objects and other players.</summary>
    public interface IHittable
    {
        Vector3 Center { get; }
        float Radius { get; }
        bool Alive { get; }
        Team Team { get; }
        float Hp { get; }
        float MaxHp { get; }
        /// <summary>Applies damage. Returns the HP actually taken off.</summary>
        float TakeHit(float damage, Vector3 from);
        /// <summary>Where an overhead health bar goes.</summary>
        Vector3 BarPoint { get; }
    }

    /// <summary>
    /// A hittable shaped like a standing capsule (robots, the boss) rather than a ball: <see cref="IHittable.Radius"/> is
    /// the capsule's radius and these are the ends of its axis. Hit tests go through <see cref="HitTest"/>.
    /// </summary>
    public interface ITallHittable : IHittable
    {
        Vector3 Bottom { get; }
        Vector3 Top { get; }
    }

    /// <summary>Hit tests against a hittable's real shape: a ball, or a capsule for tall ones.</summary>
    public static class HitTest
    {
        /// <summary>A sphere of radius <paramref name="extra"/> moving from one point to another touches it.</summary>
        public static bool Segment(IHittable target, Vector3 from, Vector3 to, float extra) =>
            target is ITallHittable tall
                ? GameMath.SegmentHitsCapsule(from, to, tall.Bottom, tall.Top, target.Radius + extra)
                : GameMath.SegmentHitsSphere(from, to, target.Center, target.Radius + extra);

        /// <summary>Where a ray (normalized direction) of thickness <paramref name="extra"/> first touches it.</summary>
        public static bool Ray(IHittable target, Vector3 start, Vector3 direction, float reach, float extra, out float distance) =>
            target is ITallHittable tall
                ? GameMath.RayHitsCapsule(start, direction, reach, tall.Bottom, tall.Top, target.Radius + extra, out distance)
                : GameMath.RayHitsSphere(start, direction, target.Center, target.Radius + extra, out distance);

        /// <summary>The point of its body (centre line) nearest to a point: for blasts, aim and cones.</summary>
        public static Vector3 Nearest(IHittable target, Vector3 point)
        {
            if (!(target is ITallHittable tall))
                return target.Center;
            Vector3 axis = tall.Top - tall.Bottom;
            float lengthSquared = axis.sqrMagnitude;
            float t = lengthSquared > 1e-6f ? Mathf.Clamp01(Vector3.Dot(point - tall.Bottom, axis) / lengthSquared) : 0f;
            return tall.Bottom + axis * t;
        }

        /// <summary>Its body comes within <paramref name="radius"/> of a point.</summary>
        public static bool Within(IHittable target, Vector3 point, float radius) =>
            Vector3.Distance(Nearest(target, point), point) < radius + target.Radius;
    }

    /// <summary>Everything that can currently be hit. Hittables add themselves while enabled.</summary>
    public static class Hittables
    {
        public static readonly List<IHittable> All = new List<IHittable>();

        public static void Add(IHittable hittable)
        {
            if (!All.Contains(hittable))
                All.Add(hittable);
        }

        public static void Remove(IHittable hittable) => All.Remove(hittable);

        /// <summary>Still there and alive: false for one whose object has been destroyed (say, a cleared wave).</summary>
        public static bool IsLive(IHittable hittable) =>
            hittable != null && !(hittable is UnityEngine.Object gone && gone == null) && hittable.Alive;
    }
}

namespace Repulsor
{
    /// <summary>
    /// Base damage per hit. Players have 100 HP and hits do a few points, so a fight lasts minutes, not seconds.
    /// These are starting values for playtesting.
    /// </summary>
    public static class Damage
    {
        public const float Repulsor = 5f;
        public const float LaserPerSecond = 10f;
        public const float Shockwave = 6f;
        public const float OrbHp = 20f;
    }
}
