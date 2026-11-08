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

        void Read(XRHand hand, HandPose pose, Transform space, float dt)
        {
            bool wasTracked = pose.Tracked;
            Vector3 lastPalm = pose.Palm;
            pose.Tracked = hand.isTracked;
            if (pose.Tracked)
            {
                for (int i = 0; i < HandPose.JointCount && pose.Tracked; i++)
                {
                    if (hand.GetJoint(XRHandJointIDUtility.FromIndex(i)).TryGetPose(out Pose joint))
                    {
                        pose.Joints[i] = space.TransformPoint(joint.position);
                        if (i == (int)Joint.Palm)
                            pose.PalmNormal = space.TransformDirection(joint.rotation * Vector3.down);
                    }
                    else if (i <= (int)Joint.Palm)
                    {
                        pose.Tracked = false;
                    }
                }
            }

            pose.Time = clock;
            if (pose.Tracked && wasTracked && dt > 0f)
            {
                Vector3 raw = (pose.Palm - lastPalm) / dt;
                pose.Velocity = Vector3.Lerp(pose.Velocity, raw, 1f - Mathf.Exp(-VelocitySmoothing * dt));
            }
            else
            {
                pose.Velocity = Vector3.zero;
            }
            pose.Analyze();
        }
    }
}
