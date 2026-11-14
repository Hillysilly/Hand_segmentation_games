using System;

namespace Repulsor
{
    /// <summary>
    /// The X-arms shield, like a Smash Bros shield: holding it drains the gauge, letting go recharges it,
    /// and draining it all breaks it for a cooldown. Blocks every hit while up.
    /// </summary>
    public sealed class ShieldGauge
    {
        public const float HoldSeconds = 5f;          // a full gauge lasts this long
        public const float BreakCooldown = 4f;
        public const float RechargePerSecond = 0.8f;  // seconds of hold regained per second
        public const float MinToRaise = 0.3f;         // seconds of hold needed to put it up again

        bool wanted;

        /// <summary>Seconds of holding left.</summary>
        public float Remaining { get; private set; } = HoldSeconds;
        public float Fraction => Remaining / HoldSeconds;
        public bool Up { get; private set; }
        public bool Broken => BrokenFor > 0f;
        public float BrokenFor { get; private set; }

        public event Action Raised;
        public event Action Lowered;
        public event Action Shattered;

        /// <summary>Whether the player is making the shield gesture this frame.</summary>
        public void Hold(bool held) => wanted = held;

        public void Tick(float dt)
        {
            if (Broken)
            {
                BrokenFor = Math.Max(0f, BrokenFor - dt);
                if (!Broken)
                    Remaining = HoldSeconds;
                SetUp(false);
                return;
            }

            bool up = wanted && (Up || Remaining >= MinToRaise);
            SetUp(up);
            if (Up)
            {
                Remaining -= dt;
                if (Remaining <= 0f)
                {
                    Remaining = 0f;
                    BrokenFor = BreakCooldown;
                    SetUp(false);
                    Shattered?.Invoke();
                }
            }
            else
            {
                Remaining = Math.Min(HoldSeconds, Remaining + RechargePerSecond * dt);
            }
        }

        public void Reset()
        {
            Remaining = HoldSeconds;
            BrokenFor = 0f;
            wanted = false;
            SetUp(false);
        }

        void SetUp(bool up)
        {
            if (up == Up)
                return;
            Up = up;
            if (up)
                Raised?.Invoke();
            else
                Lowered?.Invoke();
        }
    }
}
