namespace Repulsor
{
    public enum HandGesture { None, Fist, Open }

    /// <summary>
    /// Same rules as the web prototype (js/powers/ironman.js): a fist charges, snapping the hand open
    /// blasts, and charge drains slowly otherwise so the moment between fist and open still fires.
    /// Plain C# with no XR dependency, so it runs under the EditMode tests.
    /// </summary>
    public sealed class RepulsorGesture
    {
        public const float SecondsToFullCharge = 1.1f;
        public const float MinChargeToFire = 0.2f;
        public const float DrainPerSecond = 0.7f;

        public float Charge { get; private set; }

        /// <summary>Advances one frame. Returns the charge released, or 0 if nothing fired.</summary>
        public float Step(HandGesture gesture, float dt)
        {
            if (gesture == HandGesture.Fist)
            {
                Charge = System.Math.Min(1f, Charge + dt / SecondsToFullCharge);
                return 0f;
            }

            if (gesture == HandGesture.Open && Charge > MinChargeToFire)
            {
                float released = Charge;
                Charge = 0f;
                return released;
            }

            Charge = System.Math.Max(0f, Charge - dt * DrainPerSecond);
            return 0f;
        }

        public void Reset() => Charge = 0f;
    }

    /// <summary>
    /// Classifies a hand from four finger extensions. Each extension is
    /// distance(fingertip, wrist) / distance(knuckle, wrist), which doesn't depend on hand size:
    /// about 1.8 for a straight finger and about 1.0 for one curled into the palm.
    /// The thumb is ignored because it is unreliable when hidden behind a fist.
    /// </summary>
    public static class GestureClassifier
    {
        public static float OpenAbove = 1.5f;
        public static float CurledBelow = 1.3f;

        public static HandGesture Classify(float index, float middle, float ring, float little)
        {
            int open = 0, curled = 0;
            Count(index, ref open, ref curled);
            Count(middle, ref open, ref curled);
            Count(ring, ref open, ref curled);
            Count(little, ref open, ref curled);

            if (open >= 3 && curled == 0) return HandGesture.Open;
            if (curled >= 3 && open == 0) return HandGesture.Fist;
            return HandGesture.None;
        }

        static void Count(float extension, ref int open, ref int curled)
        {
            if (extension > OpenAbove) open++;
            else if (extension < CurledBelow) curled++;
        }
    }
}
