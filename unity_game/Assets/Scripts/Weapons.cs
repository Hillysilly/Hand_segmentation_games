using UnityEngine;

namespace Repulsor
{
    /// <summary>The first four are the old test game's cycle. None disarms the hands; LaserRepulsor gives both.</summary>
    public enum Weapon { Repulsor, Laser, Shockwave, Aura, None, LaserRepulsor }

    /// <summary>
    /// Thumb and index pinched with the other three fingers up, like an "OK" sign. Making a fist
    /// brings the thumb near the index too, so requiring the other fingers to be up keeps the two
    /// gestures apart. Returns true once per pinch, after it has been held briefly.
    /// </summary>
    public sealed class PinchDetector
    {
        public const float EngageDistance = 0.025f;
        public const float ReleaseDistance = 0.04f;
        public const float HoldSeconds = 0.1f;

        bool pinching;
        bool triggered;
        float heldFor;

        public bool Step(float thumbToIndex, float middle, float ring, float little, float dt)
        {
            bool othersUp = middle > GestureClassifier.OpenAbove
                && ring > GestureClassifier.OpenAbove
                && little > GestureClassifier.OpenAbove;

            if (!othersUp || thumbToIndex >= (pinching ? ReleaseDistance : EngageDistance))
            {
                pinching = false;
                triggered = false;
                heldFor = 0f;
                return false;
            }

            pinching = true;
            heldFor += dt;
            if (triggered || heldFor < HoldSeconds)
                return false;

            triggered = true;
            return true;
        }
    }

    /// <summary>
    /// How long the beam should be, given noisy surface readings. It shortens at once so it never pokes
    /// through a surface, extends smoothly, and keeps its last surface through brief gaps in the depth
    /// data so it doesn't flash out to full length.
    /// </summary>
    public sealed class BeamLength
    {
        public const float MaxLength = 6f;
        public const float HoldSeconds = 0.3f;
        public const float ExtendSpeed = 25f;

        float length = MaxLength;
        float wanted = MaxLength;
        float sinceSurface = HoldSeconds;

        /// <summary>Whether the beam currently ends on a surface.</summary>
        public bool Hitting => sinceSurface < HoldSeconds;

        public float Step(bool surface, float distance, float dt)
        {
            if (surface)
            {
                sinceSurface = 0f;
                wanted = Mathf.Min(distance, MaxLength);
            }
            else
            {
                sinceSurface += dt;
                if (!Hitting)
                    wanted = MaxLength;
            }

            length = wanted < length
                ? wanted
                : Mathf.Lerp(length, wanted, 1f - Mathf.Exp(-ExtendSpeed * dt));
            return length;
        }

        public void Reset()
        {
            length = MaxLength;
            wanted = MaxLength;
            sinceSurface = HoldSeconds;
        }
    }

    /// <summary>
    /// The prototype's beam rule (js/powers/ironman.js): holding the palm out for a moment turns the
    /// laser on. It survives brief dropouts, because the gesture flickers for a frame or two.
    /// </summary>
    public sealed class LaserTrigger
    {
        public const float SecondsToFire = 0.35f;
        public const float DropoutSeconds = 0.15f;

        float heldFor;
        float droppedFor;

        public bool Firing => heldFor >= SecondsToFire;

        public void Step(bool palmOut, float dt)
        {
            if (palmOut)
            {
                heldFor += dt;
                droppedFor = 0f;
                return;
            }

            droppedFor += dt;
            if (droppedFor >= DropoutSeconds)
                heldFor = 0f;
        }

        public void Reset()
        {
            heldFor = 0f;
            droppedFor = 0f;
        }
    }
}
