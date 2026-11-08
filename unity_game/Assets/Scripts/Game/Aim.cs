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

        /// <summary>Bends a shot onto the enemy closest to its line, if one is within <see cref="AssistDegrees"/>.</summary>
        public static Vector3 Assist(Vector3 from, Vector3 direction)
        {
            if (AssistDegrees <= 0f)
                return direction;
            float best = AssistDegrees;
            Vector3 result = direction;
            foreach (IHittable target in Hittables.All)
            {
                if (!target.Alive || target.Team != Team.Enemies)
                    continue;
                Vector3 to = target.Center - from;
                float distance = to.magnitude;
                if (distance < 0.3f || distance > AssistRange)
                    continue;
                // Bigger and nearer enemies are easier to catch: the angle is measured to their edge.
                float angle = Vector3.Angle(direction, to) - Mathf.Atan2(target.Radius, distance) * Mathf.Rad2Deg;
                if (angle < best)
                {
                    best = angle;
                    result = to / distance;
                }
            }
            return result;
        }
    }
}
