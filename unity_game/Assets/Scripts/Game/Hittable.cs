using System.Collections.Generic;
using UnityEngine;

namespace Repulsor
{
    public enum Team { Players, Enemies }

    /// <summary>Anything attacks can hurt: enemies, practice orbs, later room objects and other players.</summary>
    public interface IHittable
    {
        Vector3 Center { get; }
        float Radius { get; }
        bool Alive { get; }
        Team Team { get; }
        float Hp { get; }
        float MaxHp { get; }
        /// <summary>Applies damage. Returns the HP actually taken off.</summary>
        float TakeHit(float damage, Vector3 from);
        /// <summary>Where an overhead health bar goes.</summary>
        Vector3 BarPoint { get; }
    }

    /// <summary>Everything that can currently be hit. Hittables add themselves while enabled.</summary>
    public static class Hittables
    {
        public static readonly List<IHittable> All = new List<IHittable>();

        public static void Add(IHittable hittable)
        {
            if (!All.Contains(hittable))
                All.Add(hittable);
        }

        public static void Remove(IHittable hittable) => All.Remove(hittable);
    }
}

namespace Repulsor
{
    /// <summary>
    /// Base damage per hit. Players have 100 HP and hits do a few points, so a fight lasts minutes, not seconds.
    /// These are starting values for playtesting.
    /// </summary>
    public static class Damage
    {
        public const float Repulsor = 5f;
        public const float LaserPerSecond = 10f;
        public const float Shockwave = 6f;
        public const float OrbHp = 20f;
    }
}
