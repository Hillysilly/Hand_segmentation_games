using UnityEngine;

namespace Repulsor
{
    public static class GameMath
    {
        /// <summary>
        /// True if a sphere moving from <paramref name="from"/> to <paramref name="to"/> touches a sphere
        /// at <paramref name="center"/>. Checking the whole segment stops fast blasts skipping through.
        /// </summary>
        public static bool SegmentHitsSphere(Vector3 from, Vector3 to, Vector3 center, float radius)
        {
            Vector3 segment = to - from;
            float lengthSquared = segment.sqrMagnitude;
            float t = lengthSquared > 0f
                ? Mathf.Clamp01(Vector3.Dot(center - from, segment) / lengthSquared)
                : 0f;
            return (from + segment * t - center).sqrMagnitude <= radius * radius;
        }

        /// <summary>
        /// Distance along a ray (with a normalized direction) to where it enters a sphere.
        /// False if it misses, or the sphere is behind the start.
        /// </summary>
        public static bool RayHitsSphere(Vector3 start, Vector3 direction, Vector3 center, float radius, out float distance)
        {
            distance = 0f;
            Vector3 toCenter = center - start;
            float along = Vector3.Dot(toCenter, direction);
            float offAxisSquared = toCenter.sqrMagnitude - along * along;
            float radiusSquared = radius * radius;
            if (offAxisSquared > radiusSquared)
                return false;

            distance = along - Mathf.Sqrt(radiusSquared - offAxisSquared);
            if (distance < 0f)
            {
                // Starting inside the sphere counts as an immediate hit; starting past it doesn't.
                if (toCenter.sqrMagnitude > radiusSquared)
                    return false;
                distance = 0f;
            }
            return true;
        }

        /// <summary>
        /// True if a sphere is inside a widening cone (with a normalized direction) that has advanced
        /// <paramref name="reach"/> from its start. The cone is <paramref name="startRadius"/> wide at the start.
        /// </summary>
        public static bool ConeReaches(Vector3 start, Vector3 direction, float startRadius, float halfAngleDegrees,
            float reach, Vector3 center, float radius)
        {
            Vector3 toCenter = center - start;
            float along = Vector3.Dot(toCenter, direction);
            if (along < -radius || along > reach + radius)
                return false;

            float coneRadius = startRadius + Mathf.Max(along, 0f) * Mathf.Tan(halfAngleDegrees * Mathf.Deg2Rad);
            float offAxis = (toCenter - direction * along).magnitude;
            return offAxis <= coneRadius + radius;
        }

        /// <summary>
        /// Which of a hit pose's three axes is the surface's normal, turned to face back along the ray.
        /// The ray can't run along a surface for long, so the normal is the axis closest to the ray.
        /// </summary>
        public static Vector3 SurfaceNormal(Vector3 rayDirection, Vector3 axisA, Vector3 axisB, Vector3 axisC)
        {
            Vector3 best = axisA;
            if (Mathf.Abs(Vector3.Dot(axisB, rayDirection)) > Mathf.Abs(Vector3.Dot(best, rayDirection)))
                best = axisB;
            if (Mathf.Abs(Vector3.Dot(axisC, rayDirection)) > Mathf.Abs(Vector3.Dot(best, rayDirection)))
                best = axisC;
            return Vector3.Dot(best, rayDirection) > 0f ? -best : best;
        }

        /// <summary>
        /// The colour at a point on the colour wheel, where the wheel's edge is 1 from its middle: hue by
        /// angle and white at the middle. The result is ready for shaders, which work in linear colour.
        /// </summary>
        public static Color WheelColor(Vector2 offset)
        {
            float hue = Mathf.Repeat(Mathf.Atan2(offset.y, offset.x) / (Mathf.PI * 2f), 1f);
            return Color.HSVToRGB(hue, Mathf.Clamp01(offset.magnitude), 1f).linear;
        }

        /// <summary>
        /// How far past a point an aura should stand. Behind a free-standing thing, the thing's real depth
        /// cuts its outline out of the flames. A flat surface would hide them all, so they go in front of it.
        /// A side that found nothing counts as open space.
        /// </summary>
        public static float AuraOffset(float centre, bool leftFound, float left, bool rightFound, float right,
            float behind, float inFront, float depthGap)
        {
            bool openLeft = !leftFound || left > centre + depthGap;
            bool openRight = !rightFound || right > centre + depthGap;
            return openLeft || openRight ? behind : -inFront;
        }

        /// <summary>
        /// A spawn point in front of the player: inside +/- <paramref name="halfArcDegrees"/> of their
        /// level facing, using inputs in 0..1 so the placement can be tested with fixed values.
        /// </summary>
        public static Vector3 SpawnPoint(Vector3 head, Vector3 headForward, float halfArcDegrees,
            float minDistance, float maxDistance, float minHeight, float maxHeight,
            float arc01, float distance01, float height01)
        {
            Vector3 flat = Vector3.ProjectOnPlane(headForward, Vector3.up);
            flat = flat.sqrMagnitude > 0.001f ? flat.normalized : Vector3.forward;

            float angle = Mathf.Lerp(-halfArcDegrees, halfArcDegrees, arc01);
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * flat;
            float distance = Mathf.Lerp(minDistance, maxDistance, distance01);
            float height = Mathf.Lerp(minHeight, maxHeight, height01);
            return head + direction * distance + Vector3.up * height;
        }
    }
}
