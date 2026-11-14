using System.Collections.Generic;
using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// The Spider's level 2 web grab on enemies: a web that hits an enemy wraps it in a cocoon hanging off your hand.
    /// Move your arm and it swings, smashing into other enemies it touches; swing fast and it's flung, hurting itself
    /// and whatever it lands on.
    /// </summary>
    public sealed partial class Arena
    {
        /// <summary>How far the webbed enemy hangs from your hand, and how hard swings and landings hit.</summary>
        public static float WebLength = 1.1f;
        public static float WebSwingDamage = 6f;
        public static float WebFlingDamage = 14f;
        /// <summary>A fling goes this many times as fast as your hand.</summary>
        public static float WebFlingBoost = 2.5f;
        /// <summary>Webs within this many degrees of an enemy grab it.</summary>
        public static float WebGrabDegrees = 10f;
        const float WebHoldSeconds = 6f;
        const float FlingGravity = 6f;

        sealed class Webbed
        {
            public Enemy Enemy;
            public Bubble Cocoon;
            public Vector3 Velocity;
            public float Age;
            public readonly HashSet<IHittable> Hit = new HashSet<IHittable>();
        }

        Webbed swinging;
        int swingingHand;
        readonly List<Webbed> flung = new List<Webbed>();

        /// <summary>The enemy a web from here would grab, if any (level 2 and up, local enemies only).</summary>
        Enemy WebGrabTarget(Vector3 origin, Vector3 direction)
        {
            if (player.Level < 2)
                return null;
            return Aim.Target(origin, direction, WebGrabDegrees, 8f) is Enemy enemy && !enemy.IsProxy && !enemy.Spawning
                && !enemy.Webbed ? enemy : null;
        }

        void WebEnemy(Enemy enemy, int hand)
        {
            enemy.Webbed = true;
            swinging = new Webbed { Enemy = enemy, Cocoon = Bubble.Create("Web Cocoon", foes) };
            swingingHand = hand;
            PlaceCocoon(swinging);
            game.PlayAt(enemy.Center, game.WhooshSound);
            Log($"Webbed a {enemy.Kind}");
        }

        /// <summary>Holding a webbed enemy: true while one hangs from a hand (other web shots wait).</summary>
        bool TickWebSwing(float dt)
        {
            if (swinging == null)
                return false;
            Enemy enemy = swinging.Enemy;
            if (!Hittables.IsLive(enemy))
            {
                ReleaseWebbed(swinging);
                swinging = null;
                return false;
            }
            swinging.Age += dt;
            HandPose hand = swingingHand == 0 ? input.Left : input.Right;
            if (hand.Tracked)
            {
                Vector3 position = enemy.transform.position;
                Vector3 arm = body.ArmDirection(hand.IsLeft, hand.Palm);
                Vector3 aim = Vector3.Slerp(hand.Forward, arm, 0.5f).normalized;
                Vector3 next = Vector3.Lerp(position, hand.Palm + aim * WebLength, 1f - Mathf.Exp(-8f * dt));
                swinging.Velocity = dt > 0f ? (next - position) / dt : Vector3.zero;
                enemy.transform.position = next;
                ShowAim(swingingHand, hand.Wrist, next);
                // Swung into another enemy: that one gets hurt, once each.
                Smash(swinging, position, next, player.Outgoing(WebSwingDamage));

                if (swinging.Age > 0.3f && hand.Velocity.magnitude > ForceGrabDetector.ThrowSpeed
                    && Vector3.Dot(hand.Velocity.normalized, arm) > 0.3f)
                {
                    Vector3 throwVelocity = hand.Velocity * WebFlingBoost + swinging.Velocity * 0.5f;
                    if (throwVelocity.magnitude < 5f)
                        throwVelocity = throwVelocity.normalized * 5f;
                    swinging.Velocity = throwVelocity;
                    swinging.Age = 0f;
                    swinging.Hit.Clear();
                    flung.Add(swinging);
                    game.PlayAt(next, game.WhooshSound);
                    Log($"Flung a {enemy.Kind} at {throwVelocity.magnitude:0.0} m/s");
                    swinging = null;
                    return true;
                }
            }
            PlaceCocoon(swinging);
            if (swinging.Age > WebHoldSeconds)
            {
                ReleaseWebbed(swinging);
                swinging = null;
            }
            return true;
        }

        /// <summary>Flung enemies fly, fall, and burst when they land or hit a wall or another enemy.</summary>
        void TickFlung(float dt)
        {
            for (int i = flung.Count - 1; i >= 0; i--)
            {
                Webbed thrown = flung[i];
                if (!Hittables.IsLive(thrown.Enemy))
                {
                    ReleaseWebbed(thrown);
                    flung.RemoveAt(i);
                    continue;
                }
                thrown.Age += dt;
                thrown.Velocity += Vector3.down * (FlingGravity * dt);
                Vector3 from = thrown.Enemy.transform.position;
                Vector3 step = thrown.Velocity * dt;
                Vector3 to = from + step;
                bool landed = to.y < FloorY + thrown.Enemy.Radius || thrown.Age > 1.6f;
                if (!landed && step.sqrMagnitude > 1e-8f && game.Room.TryDistance(from, step.normalized, out float wall))
                    landed = wall < step.magnitude + thrown.Enemy.Radius;
                bool hitSomeone = Smash(thrown, from, to, player.Outgoing(WebFlingDamage * 0.6f));
                thrown.Enemy.transform.position = to;
                PlaceCocoon(thrown);
                if (!landed && !hitSomeone)
                    continue;
                Log($"Flung {thrown.Enemy.Kind} {(hitSomeone ? "hit another enemy" : "landed")}");
                game.Strike(thrown.Enemy, player.Outgoing(WebFlingDamage), to, "Web fling");
                game.Explode(to, 0.6f, player.Outgoing(WebFlingDamage * 0.4f), new Color(0.9f, 0.92f, 1f));
                ReleaseWebbed(thrown);
                flung.RemoveAt(i);
            }
        }

        /// <summary>Hurts every other enemy the webbed one passes through. True if it hit any.</summary>
        bool Smash(Webbed webbed, Vector3 from, Vector3 to, float damage)
        {
            bool any = false;
            var all = Hittables.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (i >= all.Count)
                    continue;
                IHittable other = all[i];
                if (ReferenceEquals(other, webbed.Enemy) || !other.Alive || other.Team != Team.Enemies || webbed.Hit.Contains(other))
                    continue;
                if (!HitTest.Segment(other, from, to, webbed.Enemy.Radius))
                    continue;
                webbed.Hit.Add(other);
                game.Strike(other, damage, to, "Webbed enemy");
                any = true;
            }
            return any;
        }

        void PlaceCocoon(Webbed webbed)
        {
            if (webbed.Cocoon == null || webbed.Enemy == null)
                return;
            webbed.Cocoon.transform.position = webbed.Enemy.Center;
            webbed.Cocoon.transform.localScale = Vector3.one * (webbed.Enemy.Radius * 2.8f);
            webbed.Cocoon.Show(Color.white, BubbleStyle.Web, 0.9f);
        }

        void ReleaseWebbed(Webbed webbed)
        {
            if (webbed.Enemy != null)
                webbed.Enemy.Webbed = false;
            if (webbed.Cocoon != null)
                Destroy(webbed.Cocoon.gameObject);
        }

        /// <summary>Lets go of everything webbed (end of a match, leaving the Spider).</summary>
        void ReleaseAllWebbed()
        {
            if (swinging != null)
                ReleaseWebbed(swinging);
            swinging = null;
            foreach (Webbed thrown in flung)
                ReleaseWebbed(thrown);
            flung.Clear();
        }
    }
}
