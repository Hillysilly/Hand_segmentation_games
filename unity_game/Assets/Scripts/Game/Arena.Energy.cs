using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// PVP energy (11/15): the shield's gauge is also the energy for every attack that isn't a basic fist-then-open
    /// blast. Free: snap blasts (Technoman's repulsor, Ryuu's comets, the Emberwitch's fireballs, Terra's rock orbs),
    /// Tempest's level 1 gust and finger guns (the Lumen blaster, the Valkyr crossbow).
    ///
    /// A full gauge is 6 seconds of shield, or 4 seconds of Technoman's laser. Everything else is priced by the damage
    /// it can do at the laser's rate: a full gauge buys 40 damage (4 s of the level 1 laser at 10 a second), so
    /// <see cref="EnergyPerDamage"/> is a fortieth of the gauge per point. Held attacks drain per second, priced
    /// by their damage per second; one-off attacks pay up front and don't go off without the energy; utility moves
    /// (smoke, lifting furniture) have flat prices. Spending holds off the recharge for a moment.
    /// Outside PVP nothing costs anything.
    /// </summary>
    public sealed partial class Arena
    {
        /// <summary>Seconds of shield in a full gauge in PVP (elsewhere <see cref="ShieldGauge.HoldSeconds"/>).</summary>
        public const float PvpGaugeSeconds = 6f;
        /// <summary>A full gauge buys this much damage: 4 s of the level 1 laser.</summary>
        public const float GaugeDamage = 4f * Damage.LaserPerSecond;
        public const float EnergyPerDamage = 1f / GaugeDamage;

        // Held attacks, gauge per second (damage per second x EnergyPerDamage).
        public const float LaserDrain = Damage.LaserPerSecond * EnergyPerDamage;              // 4 s
        public static float FlamesDrain => Damage.LaserPerSecond * Flamethrower.DamageScale * EnergyPerDamage; // 3.3 s
        public const float LightningDrain = LightningDps * EnergyPerDamage;                    // 2.5 s
        /// <summary>The blade only costs a little while out; its slashes pay for their damage (at half: you have to get close).</summary>
        public const float BladeDrain = 0.05f;
        public const float MeleeDiscount = 0.5f;

        // Utility, flat fractions of the gauge.
        public const float SmokeCost = 0.3f;
        public const float LiftCost = 0.3f;
        /// <summary>A tornado can catch someone for about two seconds of its damage.</summary>
        public const float TornadoDamageGuess = TornadoDps * 2f;

        float noEnergyShown;

        bool UsesEnergy => Pvp && phase == Phase.Playing;

        /// <summary>Enough energy left to keep a held attack going.</summary>
        bool HeldEnergy => !UsesEnergy || (!player.Shield.Broken && player.Shield.Remaining > 0.01f);

        /// <summary>A one-off attack worth this much damage: true (and paid) if there's the energy for it.</summary>
        bool SpendFor(float damage, float scale = 1f) => SpendFlat(damage * EnergyPerDamage * scale);

        bool SpendFlat(float fraction)
        {
            if (!UsesEnergy)
                return true;
            if (player.Shield.TrySpend(fraction))
                return true;
            NoEnergy();
            return false;
        }

        void NoEnergy()
        {
            if (Time.unscaledTime < noEnergyShown + 1.5f)
                return;
            noEnergyShown = Time.unscaledTime;
            hud.Message("", "Out of energy: let it recharge", 1.2f);
            Log("Out of energy");
        }

        /// <summary>Every frame: held attacks (laser, flames, lightning, blades) drain the gauge.</summary>
        void DrainHeldAttacks(float dt)
        {
            if (!UsesEnergy)
                return;
            for (int i = 0; i < 2; i++)
            {
                float rate = BeamOf(i, out _, out _) switch
                {
                    HandBeam.Laser => LaserDrain,
                    HandBeam.Flames => FlamesDrain,
                    HandBeam.Lightning => LightningDrain,
                    HandBeam.Blade => BladeDrain,
                    _ => 0f,
                };
                if (rate > 0f && !player.Shield.Drain(rate, dt))
                    NoEnergy();
            }
        }

        /// <summary>The hand weapon, less its held laser or flames when the gauge is empty (snap blasts are free).</summary>
        Weapon WithEnergy(Weapon weapon)
        {
            if (HeldEnergy)
                return weapon;
            return weapon == Weapon.LaserRepulsor ? Weapon.Repulsor : weapon == Weapon.Laser ? Weapon.None : weapon;
        }

        /// <summary>For tests: the gauge's fraction, and PVP prices.</summary>
        public float TestEnergy => player.Shield.Fraction;
    }
}
