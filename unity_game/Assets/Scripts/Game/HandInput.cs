using UnityEngine;
using UnityEngine.XR.Hands;

namespace Repulsor
{
    /// <summary>Copies both tracked hands into <see cref="HandPose"/>s in world space each frame, for the gesture library.</summary>
    public sealed class HandInput
    {
        const float VelocitySmoothing = 25f;

        public readonly HandPose Left = new HandPose(true);
        public readonly HandPose Right = new HandPose(false);
        float clock;

        public HandPose Get(bool left) => left ? Left : Right;

        public void Tick(XRHandSubsystem hands, Transform trackingSpace, float dt)
        {
            clock += dt;
            if (hands == null)
            {
                Left.Tracked = Right.Tracked = false;
                Left.Analyze();
                Right.Analyze();
                return;
            }
            Read(hands.leftHand, Left, trackingSpace, dt);
            Read(hands.rightHand, Right, trackingSpace, dt);
        }

        /// <summary>
        /// A hand that drops out of tracking is kept where it was for this long, as if still tracked, so a short
        /// dropout doesn't reset whatever gesture was under way. On the headset (log of 11/14) hands dropped out about
        /// ten times a minute, six in ten of those for under 0.3 s.
        /// </summary>
        public const float CoastSeconds = 0.3f;

        readonly float[] lastSeen = { -10f, -10f };
        readonly Vector3[] lastPalm = new Vector3[2];

        void Read(XRHand hand, HandPose pose, Transform space, float dt)
        {
            int side = pose.IsLeft ? 0 : 1;
            bool was = pose.Tracked;
            bool seen = hand.isTracked
                && hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out _) && hand.GetJoint(XRHandJointID.Palm).TryGetPose(out _);
            pose.Time = clock;
            if (seen)
            {
                for (int i = 0; i < HandPose.JointCount; i++)
                {
                    if (!hand.GetJoint(XRHandJointIDUtility.FromIndex(i)).TryGetPose(out Pose joint))
                        continue;
                    pose.Joints[i] = space.TransformPoint(joint.position);
                    if (i == (int)Joint.Palm)
                        pose.PalmNormal = space.TransformDirection(joint.rotation * Vector3.down);
                }
                // Speed over the time since it was last really seen, so coming back from a dropout isn't a spike.
                float gap = clock - lastSeen[side];
                pose.Velocity = was && gap > 0f && gap < CoastSeconds + 0.1f
                    ? Vector3.Lerp(pose.Velocity, (pose.Palm - lastPalm[side]) / gap, 1f - Mathf.Exp(-VelocitySmoothing * gap))
                    : Vector3.zero;
                lastSeen[side] = clock;
                lastPalm[side] = pose.Palm;
                pose.Tracked = true;
            }
            else if (was && clock - lastSeen[side] < CoastSeconds)
            {
                // Coasting: the pose stays as it was last seen, slowing to a stop.
                pose.Velocity *= Mathf.Exp(-10f * dt);
            }
            else
            {
                pose.Tracked = false;
                pose.Velocity = Vector3.zero;
            }
            pose.Analyze();
        }
    }
}
