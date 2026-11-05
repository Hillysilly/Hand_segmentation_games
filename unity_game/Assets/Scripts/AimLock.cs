using System.Collections.Generic;
using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// Remembers where a fist was aiming a moment ago. Hand tracking jumps as the fingers fly open,
    /// often going from fist to open in a single frame, so even the last fist frame can be mid-opening.
    /// Shots use the aim from <see cref="LookbackSeconds"/> before the newest sample instead.
    /// </summary>
    public sealed class AimLock
    {
        public const float LookbackSeconds = 0.12f;

        struct Sample
        {
            public float Time;
            public Vector3 Position;
            public Vector3 Direction;
        }

        readonly Queue<Sample> recent = new Queue<Sample>();
        Sample locked;
        float lastTime;

        public bool HasAim { get; private set; }
        public Vector3 Position => locked.Position;
        public Vector3 Direction => locked.Direction;

        public void Record(float time, Vector3 position, Vector3 direction)
        {
            // A long gap means the fist was released and made again, so the old aim no longer applies.
            if (HasAim && time - lastTime > LookbackSeconds)
                Clear();

            var sample = new Sample { Time = time, Position = position, Direction = direction };
            recent.Enqueue(sample);
            lastTime = time;

            if (!HasAim)
            {
                locked = sample;
                HasAim = true;
            }

            while (recent.Count > 0 && time - recent.Peek().Time >= LookbackSeconds)
                locked = recent.Dequeue();
        }

        public void Clear()
        {
            recent.Clear();
            HasAim = false;
        }
    }
}
