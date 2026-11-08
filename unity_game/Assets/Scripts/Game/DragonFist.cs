using System.Collections.Generic;
using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// The Ki fighter's level 3 punch, in looks to choose between in the Variant Lab:
    /// A big comet, B three comets twisting round each other like a dragon (pierces), C a punch shockwave,
    /// D comet and shockwave together.
    /// </summary>
    public sealed class DragonFist : MonoBehaviour
    {
        public static readonly string[] StyleNames = { "A  COMET", "B  TWIST", "C  WAVE", "D  BOTH" };
        const float TwistSpeed = 9f;
        const float TwistRadius = 0.16f;
        const float TwistTurns = 2.2f;   // per second
        const float LifeSeconds = 1.6f;

        readonly HashSet<IHittable> hit = new HashSet<IHittable>();
        RepulsorGame game;
        Blast[] strands;
        Vector3 origin;
        Vector3 direction;
        Vector3 side;
        Vector3 up;
        float travelled;
        float wallAt = float.MaxValue;
        float damage;
        float age;

        public static void Launch(RepulsorGame game, int style, Vector3 origin, Vector3 direction, float damage)
        {
            switch (style)
            {
                case 1:
                    var go = new GameObject("Dragon Fist");
                    go.transform.SetParent(game.transform, false);
                    go.AddComponent<DragonFist>().Begin(game, origin, direction, damage);
                    break;
                case 2:
                    game.FireShockwave(origin, direction, 1f);
                    break;
                case 3:
                    game.FireBlast(origin, direction, 1f, 1.8f, 1.3f, damage * 0.6f, 0f);
                    game.FireShockwave(origin, direction, 0.7f);
                    break;
                default:
                    game.FireBlast(origin, direction, 1f, 2.2f, 1.4f, damage, 0f);
                    break;
            }
        }

        void Begin(RepulsorGame owner, Vector3 from, Vector3 aim, float hitDamage)
        {
            game = owner;
            origin = from;
            direction = aim.normalized;
            side = Vector3.Cross(Vector3.up, direction).normalized;
            if (side.sqrMagnitude < 0.01f)
                side = Vector3.right;
            up = Vector3.Cross(direction, side);
            damage = hitDamage;
            if (game.Room.TryDistance(origin, direction, out float wall))
                wallAt = wall;
            strands = new Blast[3];
            for (int i = 0; i < strands.Length; i++)
            {
                // Drawn by the comet, moved here: their own flight and hits are switched off.
                strands[i] = game.FireBlast(origin, direction, 0.8f, i == 0 ? 1.4f : 1f, 1f, 0f, 0f);
                strands[i].enabled = false;
            }
            Move(0f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            Vector3 before = origin + direction * travelled;
            Move(travelled + TwistSpeed * dt);
            Vector3 after = origin + direction * travelled;
            game.HitAllAlong(before, after, TwistRadius + 0.12f, damage, hit);
            if (age > LifeSeconds || travelled >= wallAt)
            {
                foreach (Blast strand in strands)
                    if (strand != null)
                        Destroy(strand.gameObject);
                Destroy(gameObject);
            }
        }

        void Move(float distance)
        {
            travelled = distance;
            float turn = age * TwistTurns * Mathf.PI * 2f;
            // The radius opens up as it leaves the fist.
            float radius = TwistRadius * Mathf.Clamp01(distance / 0.6f);
            for (int i = 0; i < strands.Length; i++)
            {
                if (strands[i] == null)
                    continue;
                float phase = turn + i * Mathf.PI * 2f / strands.Length;
                Vector3 offset = (side * Mathf.Cos(phase) + up * Mathf.Sin(phase)) * radius;
                Vector3 position = origin + direction * distance + offset;
                Vector3 previous = strands[i].transform.position;
                Vector3 heading = position - previous;
                strands[i].transform.position = position;
                if (heading.sqrMagnitude > 1e-6f)
                    strands[i].transform.rotation = Quaternion.LookRotation(-heading);
                strands[i].ShowTail(distance);
            }
        }
    }
}
