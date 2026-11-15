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
        /// <summary>
        /// How far the thresholds lean toward the shape the hand is already in. Tracking wobbles a finger across a
        /// threshold for a frame or two; on the headset (log of 11/14) four in ten gesture changes were such
        /// flickers through None, each one dropping a charge or a beam.
        /// </summary>
        public static float Give = 0.12f;

        public static HandGesture Classify(float index, float middle, float ring, float little) =>
            Classify(index, middle, ring, little, HandGesture.None);

        /// <summary>As above, but sticky: a hand that was open or a fist stays so until its fingers are clearly past the line.</summary>
        public static HandGesture Classify(float index, float middle, float ring, float little, HandGesture last)
        {
            float openAbove = OpenAbove + (last == HandGesture.Open ? -Give : last == HandGesture.Fist ? Give : 0f);
            float curledBelow = CurledBelow + (last == HandGesture.Fist ? Give : last == HandGesture.Open ? -Give : 0f);
            int open = 0, curled = 0;
            Count(index, openAbove, curledBelow, ref open, ref curled);
            Count(middle, openAbove, curledBelow, ref open, ref curled);
            Count(ring, openAbove, curledBelow, ref open, ref curled);
            Count(little, openAbove, curledBelow, ref open, ref curled);

            if (open >= 3 && curled == 0) return HandGesture.Open;
            if (curled >= 3 && open == 0) return HandGesture.Fist;
            return HandGesture.None;
        }

        static void Count(float extension, float openAbove, float curledBelow, ref int open, ref int curled)
        {
            if (extension > openAbove) open++;
            else if (extension < curledBelow) curled++;
        }
    }

    /// <summary>
    /// Holds an open hand or a fist through a moment of "neither": the shape only lets go after it has been
    /// neither for <see cref="Seconds"/>. A change straight to the other shape is immediate.
    /// </summary>
    public sealed class GestureHold
    {
        public static float Seconds = 0.08f;
        float noneFor;

        public HandGesture Shown { get; private set; }

        public HandGesture Step(HandGesture raw, float dt)
        {
            if (raw != HandGesture.None)
            {
                Shown = raw;
                noneFor = 0f;
            }
            else
            {
                noneFor += dt;
                // No time passing (a test posing a hand twice in one instant): nothing to hold through.
                if (noneFor >= Seconds || dt <= 0f)
                    Shown = HandGesture.None;
            }
            return Shown;
        }

        public void Reset()
        {
            Shown = HandGesture.None;
            noneFor = 0f;
        }
    }
}
