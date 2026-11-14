using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// Where hand shots go. A palm's own direction is a poor guide: while the hand is a fist it faces down or
    /// sideways, and it swings as the hand snaps open. So shots follow the arm, from the shoulder through the hand,
    /// and then bend onto an enemy that's close to that line. Live-tunable in development builds (see Tuning).
    /// </summary>
    public static class Aim
    {
        /// <summary>How much of the hand's own pointing (wrist to knuckles) to mix in: 0 is the arm alone.</summary>
        public static float HandShare = 0f;
        /// <summary>Shots this many degrees or less off an enemy (counting its size) go straight at it. 0 turns assist off.</summary>
        public static float AssistDegrees = 12f;
        /// <summary>Enemies further than this get no assist.</summary>
        public static float AssistRange = 12f;

        /// <summary>The way a shot from this hand goes, before assist. Falls back to the palm when the hand isn't tracked.</summary>
        public static Vector3 Arm(Stance body, HandPose hand, Vector3 palm, Vector3 palmDirection)
        {
            if (!hand.Tracked)
                return palmDirection;
            Vector3 arm = body.ArmDirection(hand.IsLeft, palm);
            if (HandShare > 0f)
                arm = Vector3.Slerp(arm, hand.Forward, HandShare);
            return arm.normalized;
        }

        /// <summary>The dragon fist locks on this far off the punch, and then homes in.</summary>
        public static float LockDegrees = 40f;
        /// <summary>How fast a locked-on dragon fist turns toward its target, in degrees per second.</summary>
        public static float HomingDegrees = 360f;

        /// <summary>Bends a shot onto the enemy closest to its line, if one is within <see cref="AssistDegrees"/>.</summary>
        public static Vector3 Assist(Vector3 from, Vector3 direction)
        {
            IHittable target = Target(from, direction, AssistDegrees, AssistRange);
            return target != null ? (HitTest.Nearest(target, from + direction * Vector3.Distance(from, target.Center)) - from).normalized : direction;
        }

        /// <summary>
        /// The enemy closest to a shot's line, if one is within <paramref name="degrees"/> of it (measured to its edge,
        /// so bigger and nearer ones are easier to catch) and <paramref name="range"/> away. Null if none.
        /// </summary>
        public static IHittable Target(Vector3 from, Vector3 direction, float degrees, float range)
        {
            if (degrees <= 0f)
                return null;
            float best = degrees;
            IHittable result = null;
            foreach (IHittable target in Hittables.All)
            {
                if (!target.Alive || target.Team != Team.Enemies)
                    continue;
                // Tall bodies: aim at the part of them nearest the line, not only their middle.
                Vector3 to = HitTest.Nearest(target, from + direction * Vector3.Distance(from, target.Center)) - from;
                float distance = to.magnitude;
                if (distance < 0.3f || distance > range)
                    continue;
                float angle = Vector3.Angle(direction, to) - Mathf.Atan2(target.Radius, distance) * Mathf.Rad2Deg;
                if (angle < best)
                {
                    best = angle;
                    result = target;
                }
            }
            return result;
        }
    }
}
