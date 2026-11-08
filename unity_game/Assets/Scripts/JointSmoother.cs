using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// Steadies a body's noisy joints. Slow ones are smoothed hard and fast ones barely at all, so a still hand
    /// doesn't shake and a moving one doesn't drag (a One Euro filter). A joint the tracker can't see holds its
    /// place against its shoulder or hip rather than following a guess.
    /// </summary>
    public sealed class JointSmoother
    {
        const float SlowCutoff = 1.5f;
        const float CutoffPerMetrePerSecond = 4f;
        const float VelocityCutoff = 1f;
        const float MinSeen = 0.5f;
        const int ShoulderLeft = 11, ShoulderRight = 12, HipLeft = 23, HipRight = 24;

        readonly int count;
        readonly Vector3[] joints;
        readonly Vector3[] velocity;
        readonly Vector3[] heldFromAnchor;
        bool started;

        public JointSmoother(int jointCount)
        {
            count = jointCount;
            joints = new Vector3[jointCount];
            velocity = new Vector3[jointCount];
            heldFromAnchor = new Vector3[jointCount];
        }

        /// <summary>The steadied joints.</summary>
        public Vector3[] Joints => joints;

        /// <summary>How fast each steadied joint is moving, in metres a second.</summary>
        public Vector3[] Velocity => velocity;

        /// <summary>Starts again on the next body, for when someone new appears.</summary>
        public void Reset() => started = false;

        /// <summary>Takes a new body, <paramref name="dt"/> seconds after the last.</summary>
        public void Step(Vector3[] newest, float[] seen, float dt)
        {
            if (!started)
            {
                started = true;
                for (int i = 0; i < count; i++)
                {
                    joints[i] = newest[i];
                    velocity[i] = Vector3.zero;
                }
            }

            // In order of number, so that each anchor is done before the joints that rely on it.
            for (int i = 0; i < count; i++)
            {
                int anchor = AnchorOf(i);
                bool held = anchor >= 0 && seen[i] < MinSeen;
                Vector3 target = held ? joints[anchor] + heldFromAnchor[i] : newest[i];

                Vector3 moved = (target - joints[i]) / dt;
                velocity[i] = Vector3.Lerp(velocity[i], moved, Blend(VelocityCutoff, dt));
                float cutoff = SlowCutoff + CutoffPerMetrePerSecond * velocity[i].magnitude;
                joints[i] = Vector3.Lerp(joints[i], target, Blend(cutoff, dt));

                if (anchor >= 0 && !held)
                    heldFromAnchor[i] = joints[i] - joints[anchor];
            }
        }

        static float Blend(float cutoff, float dt) => 1f / (1f + 1f / (2f * Mathf.PI * cutoff * dt));

        /// <summary>The joint a limb's joint holds on to: arms to the shoulders, legs to the hips, or -1.</summary>
        static int AnchorOf(int joint)
        {
            if (joint > ShoulderRight && joint < HipLeft)
                return joint % 2 == 1 ? ShoulderLeft : ShoulderRight;
            if (joint > HipRight)
                return joint % 2 == 1 ? HipLeft : HipRight;
            return -1;
        }
    }
}
