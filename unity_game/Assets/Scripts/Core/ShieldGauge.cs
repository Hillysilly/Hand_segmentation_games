using System;

namespace Repulsor
{
    /// <summary>
    /// The X-arms shield, like a Smash Bros shield: holding it drains the gauge, letting go recharges it,
    /// and draining it all breaks it for a cooldown. Blocks every hit while up.
    /// In PVP it's also the energy for attacks (<see cref="TrySpend"/>, <see cref="Drain"/>); spending never breaks
    /// it, and the recharge waits a moment after each spend.
    /// </summary>
    public sealed class ShieldGauge
    {
        public const float HoldSeconds = 5f;          // a full gauge lasts this long
        public const float BreakCooldown = 4f;
        public const float RechargePerSecond = 0.8f;  // seconds of hold regained per second
        public const float MinToRaise = 0.3f;         // seconds of hold needed to put it up again
        public const float SpendRechargeDelay = 0.6f; // after spending energy on an attack

        bool wanted;
        float sinceSpent = 99f;

        /// <summary>Seconds of shield a full gauge holds (more in PVP, where it's also the attack energy).</summary>
        public float Capacity { get; set; } = HoldSeconds;

        /// <summary>Seconds of holding left.</summary>
        public float Remaining { get; private set; } = HoldSeconds;
        public float Fraction => Remaining / Capacity;

        /// <summary>Pays for an attack: this fraction of a full gauge. False, and nothing spent, if there isn't that much.</summary>
        public bool TrySpend(float fraction)
        {
            float cost = fraction * Capacity;
            if (Broken || Remaining < cost)
                return false;
            Remaining -= cost;
            sinceSpent = 0f;
            return true;
        }

        /// <summary>A held attack: drains this fraction of a full gauge per second. False once it's empty.</summary>
        public bool Drain(float fractionPerSecond, float dt)
        {
            if (Broken || Remaining <= 0f)
                return false;
            Remaining = Math.Max(0f, Remaining - fractionPerSecond * Capacity * dt);
            sinceSpent = 0f;
            return true;
        }
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
                    Remaining = Capacity;
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
                sinceSpent += dt;
                if (sinceSpent >= SpendRechargeDelay)
                    Remaining = Math.Min(Capacity, Remaining + RechargePerSecond * dt);
            }
        }

        public void Reset()
        {
            Remaining = Capacity;
            sinceSpent = 99f;
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
