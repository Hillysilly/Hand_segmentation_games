using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// Character attacks beyond the hand weapons in <see cref="HandRepulsor"/>:
    /// - Ki fighter: energy ball (level 2) and dragon fist (level 3)
    /// - Saber: finger-gun blaster (level 1) and laser sword (level 2)
    /// - Archer: finger-gun crossbow that reloads (level 1) and bow (level 2)
    /// - Mage: triangle fire pillar (level 3)
    /// Each runs only while the player can fight. Looks marked placeholder get their own variants later.
    /// </summary>
    public sealed partial class Arena
    {
        const float BallMinSize = 1.2f;
        const float BallMaxSize = 4f;
        const float BladeLength = 1f;
        const float BladeRadius = 0.05f;
        const float BladeDps = 30f;
        const float CrossbowReload = 2.5f;
        const float SmokeCooldown = 15f;
        readonly UnderhandThrowDetector[] bomb = { new UnderhandThrowDetector(), new UnderhandThrowDetector() };
        float smokeReady;
        static readonly Color KiBlue = new Color(0.25f, 0.6f, 1f);
        static readonly Color BlasterRed = new Color(1f, 0.12f, 0.08f);

        readonly TwoHandChargeDetector kiCharge = new TwoHandChargeDetector();
        readonly PunchDetector[] kiPunch = { new PunchDetector(), new PunchDetector() };
        readonly FingerGunDetector[] gun = { new FingerGunDetector(), new FingerGunDetector() };
        readonly SaberGripDetector[] grip = { new SaberGripDetector(), new SaberGripDetector() };
        readonly BowDetector bow = new BowDetector();
        readonly TriangleDetector triangle = new TriangleDetector();
        readonly float[] reload = new float[2];
        readonly SpiderSignDetector[] web = { new SpiderSignDetector(), new SpiderSignDetector() };
        SpiderSense spiderSense;
        readonly ForceGrabDetector[] force = { new ForceGrabDetector(), new ForceGrabDetector() };
        RoomObjects room;
        ThrownObject held;
        int heldHand = -1;
        float heldFor;
        float nextRoomRefresh;
        float noFurnitureHint;
        GameObject chargeBall;
        LineRenderer[] playAim;
        LineRenderer playRing;

        /// <summary>The colour attacks are drawn in: yours, except the Ki fighter turns blue at level 3.</summary>
        Color AttackColor =>
            player.BoostFor > 0f ? new Color(1f, 0.15f, 0.1f)          // 1.5x damage: everything you fire turns red
            : player.InvincibleFor > 0f && phase == Phase.Playing && !player.IsDead && player.InvincibleFor > Fighter.SpawnProtectionSeconds
                ? new Color(1f, 0.85f, 0.3f)                             // invincible powerup: gold
            : character == CharacterId.KiFighter && player.Level >= 3 ? KiBlue : PlayerColor;

        void CreateAbilityViews()
        {
            Material line = Resources.Load<Material>("AimLine");
            playAim = new LineRenderer[3];
            for (int i = 0; i < playAim.Length; i++)
                playAim[i] = CreateLine("Aim " + i, line, 0.004f, 2);
            playRing = CreateLine("Pillar Ring", line, 0.015f, 33);
            playRing.loop = true;
            spiderSense = new SpiderSense(transform, head);
            room = new RoomObjects(transform);
        }

        void TickAbilities(bool canFight, float dt)
        {
            foreach (LineRenderer aim in playAim)
                aim.enabled = false;
            playRing.enabled = false;
            room.Highlight(null, Color.white);
            if (Time.unscaledTime >= nextRoomRefresh)
            {
                nextRoomRefresh = Time.unscaledTime + 2f;
                int before = room.Count;
                room.Refresh(game.Room.Boxes);
                if (room.Count != before)
                    Log($"Room objects: {room.Count}");
            }
            if (!canFight && held != null)
                DropHeld();

            bool charging = false;
            if (canFight)
            {
                switch (character)
                {
                    case CharacterId.KiFighter:
                        charging = TickKi(dt);
                        break;
                    case CharacterId.Saber:
                        TickSaber(dt);
                        break;
                    case CharacterId.Archer:
                        TickArcher(dt);
                        break;
                    case CharacterId.Mage:
                        TickMage(dt);
                        if (player.Level >= 2)
                            TickForce(dt);
                        break;
                    case CharacterId.Spider:
                        TickSpider(dt);
                        break;
                }
            }
            spiderSense.Tick(canFight && character == CharacterId.Spider, dt);

            if (charging)
            {
                if (chargeBall == null)
                    chargeBall = game.MakeEnergyBall(transform);
                chargeBall.transform.position = kiCharge.Center;
                chargeBall.transform.localScale = Vector3.one * Mathf.Lerp(0.05f, 0.28f, kiCharge.Charge);
            }
            else if (chargeBall != null)
            {
                Destroy(chargeBall);
                chargeBall = null;
            }
        }

        bool TickKi(float dt)
        {
            bool charging = false;
            if (player.Level >= 2)
            {
                if (kiCharge.Step(input.Left, input.Right, body, dt, out Vector3 direction, out float power))
                {
                    Vector3 from = kiCharge.Center + direction * 0.15f;
                    float size = Mathf.Lerp(BallMinSize, BallMaxSize, power);
                    game.FireBlast(from, direction, 1f, size, Mathf.Lerp(0.8f, 0.55f, power),
                        player.Outgoing(12f + 18f * power), 0.6f + 0.6f * power);
                    Log($"Energy ball, power {power:0.00}");
                }
                charging = kiCharge.Charging;
            }
            if (player.Level >= 3)
            {
                for (int i = 0; i < 2; i++)
                {
                    HandPose hand = i == 0 ? input.Left : input.Right;
                    if (kiPunch[i].Step(hand, body, dt, out Vector3 origin, out Vector3 punch))
                    {
                        // Placeholder look: a big fast comet. The dragon fist gets its own look in variant batch 2.
                        game.FireBlast(origin + punch * 0.1f, punch, 1f, 2.2f, 1.4f, player.Outgoing(10f), 0f);
                        Log($"Dragon fist at {hand.Velocity.magnitude:0.0} m/s");
                    }
                }
            }
            return charging;
        }

        void TickSaber(float dt)
        {
            if (player.Level >= 3)
                TickForce(dt);
            for (int i = 0; i < 2; i++)
            {
                HandPose hand = i == 0 ? input.Left : input.Right;
                bool sword = player.Level >= 2 && grip[i].Step(hand, dt);
                if (sword)
                {
                    Vector3 hilt = grip[i].Hilt;
                    Vector3 tip = hilt + grip[i].BladeDirection * BladeLength;
                    (i == 0 ? leftHand : rightHand).ShowBlade(hilt, tip);
                    CutWithBlade(hilt, tip, dt);
                    continue;
                }
                if (TickGun(i, hand, dt, out Vector3 origin, out Vector3 direction))
                    game.FireBlast(origin, direction, 0.2f, 0.45f, 1.7f, player.Outgoing(4f), 0f, BlasterRed);
            }
        }

        void CutWithBlade(Vector3 hilt, Vector3 tip, float dt)
        {
            var all = Hittables.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (i >= all.Count)
                    continue;
                IHittable target = all[i];
                if (target.Alive && target.Team == Team.Enemies
                    && GameMath.SegmentHitsSphere(hilt, tip, target.Center, target.Radius + BladeRadius))
                    game.Strike(target, player.Outgoing(BladeDps * dt), hilt, "Sword");
            }
        }

        void TickArcher(float dt)
        {
            if (player.Level >= 2 && bow.Step(input.Left, input.Right, body, dt, out Vector3 from, out Vector3 aim, out float draw))
            {
                Blast arrow = game.FireBlast(from + aim * 0.1f, aim, 0.3f, 0.4f, Mathf.Lerp(1.2f, 2.2f, draw), player.Outgoing(6f + 10f * draw), 0f);
                arrow.Pierce = player.Level >= 3;
                Log($"Arrow, draw {draw:0.00}{(arrow.Pierce ? ", piercing" : "")}");
            }
            if (bow.Formed)
            {
                // Where the arrow will go.
                ShowAim(2, bow.BowPosition, bow.BowPosition + bow.Direction * (1.5f + 3f * bow.Draw));
                return;
            }

            if (player.Level >= 3)
            {
                for (int i = 0; i < 2; i++)
                {
                    HandPose thrower = i == 0 ? input.Left : input.Right;
                    if (bomb[i].Step(thrower, body, dt, out Vector3 from2, out Vector3 velocity) && matchTime >= smokeReady)
                    {
                        smokeReady = matchTime + SmokeCooldown;
                        SmokeCloud.Throw(from2, velocity, foes);
                        Log("Smoke bomb");
                    }
                }
            }

            for (int i = 0; i < 2; i++)
            {
                HandPose hand = i == 0 ? input.Left : input.Right;
                reload[i] = Mathf.Max(0f, reload[i] - dt);
                if (TickGun(i, hand, dt, out Vector3 origin, out Vector3 direction) && reload[i] <= 0f)
                {
                    reload[i] = CrossbowReload;
                    game.FireBlast(origin, direction, 0.4f, 0.5f, 2f, player.Outgoing(7f), 0f);
                }
            }
        }

        void TickMage(float dt)
        {
            if (player.Level < 3)
                return;
            if (triangle.Step(input.Left, input.Right, body, dt, out Vector3 target))
            {
                // Placeholder look: an explosion with a column of fire rising from it.
                game.Explode(target + Vector3.up * 0.6f, 1.1f, player.Outgoing(25f), AttackColor);
                game.FireBlast(target, Vector3.up, 1f, 3f, 0.7f, 0f, 0f);
                Log($"Fire pillar {Vector3.Distance(Flat(head.position), Flat(target)):0.0} m away");
            }
            if (triangle.Aiming)
            {
                playRing.enabled = true;
                for (int i = 0; i < 33; i++)
                {
                    float angle = i / 32f * Mathf.PI * 2f;
                    playRing.SetPosition(i, triangle.Target + new Vector3(Mathf.Cos(angle), 0.03f, Mathf.Sin(angle)) * 0.6f);
                }
            }
        }

        /// <summary>Force powers: a cupped hand at a piece of furniture lifts a copy of it; a fast swing throws it.</summary>
        void TickForce(float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                HandPose hand = i == 0 ? input.Left : input.Right;
                GrabEvent grab = force[i].Step(hand, body, dt, out Vector3 throwVelocity);
                if (held != null && heldHand == i)
                {
                    held.Hold(hand.Palm, force[i].AimDirection, dt);
                    if (grab == GrabEvent.Throw)
                        ThrowHeld(throwVelocity, force[i].AimDirection);
                    else if (grab == GrabEvent.Drop)
                        DropHeld();
                    continue;
                }
                if (held != null || !(force[i].Forming || force[i].Holding))
                    continue;
                if (room.Count == 0 && matchTime > noFurnitureHint)
                {
                    noFurnitureHint = matchTime + 15f;
                    hud.Message("NO FURNITURE FOUND", "Force powers need Space Setup: headset Settings > Environment setup", 3f);
                }
                bool aimed = room.TryAim(force[i].AimOrigin, force[i].AimDirection, out var box);
                ShowAim(i, force[i].AimOrigin, force[i].AimOrigin + force[i].AimDirection * (aimed ? 1.5f : 3f));
                if (!aimed)
                    continue;
                room.Highlight(box, AttackColor);
                if (grab == GrabEvent.Grab)
                    LiftBox(box, i, AttackColor);
            }
        }

        void LiftBox(UnityEngine.XR.ARFoundation.ARBoundingBox box, int hand, Color color)
        {
            held = room.Lift(box, color, this);
            heldHand = hand;
            heldFor = 0f;
            game.PlayAt(box.transform.position, game.WhooshSound);
            Log($"Lifted {held.Label}");
        }

        void ThrowHeld(Vector3 handVelocity, Vector3 aim)
        {
            float wall = game.Room.TryDistance(held.transform.position, aim, out float d) ? d : float.MaxValue;
            held.Throw(handVelocity, aim, wall);
            game.PlayAt(held.transform.position, game.WhooshSound);
            Log($"Threw {held.Label} at {handVelocity.magnitude:0.0} m/s");
            held = null;
        }

        void DropHeld()
        {
            if (held != null)
                held.Drop();
            held = null;
        }

        /// <summary>A thrown object's path touches an enemy.</summary>
        public bool ThrownHits(Vector3 from, Vector3 to, float radius)
        {
            foreach (IHittable foe in Hittables.All)
                if (foe.Alive && foe.Team == Team.Enemies && GameMath.SegmentHitsSphere(from, to, foe.Center, foe.Radius + radius))
                    return true;
            return false;
        }

        public void ThrownBreaks(Vector3 at, float radius, float damage, Color color) =>
            game.Explode(at, radius, player.Outgoing(damage), color);

        void TickSpider(float dt)
        {
            // Level 2 web pull: a web sign aimed at furniture pulls a copy of it to you; swing your arm to fling it.
            if (held != null)
            {
                HandPose holding = heldHand == 0 ? input.Left : input.Right;
                heldFor += dt;
                Vector3 aim = Vector3.Slerp(holding.Forward, body.ArmDirection(holding.IsLeft, holding.Palm), 0.5f).normalized;
                held.Hold(holding.Palm, aim, dt);
                ShowAim(heldHand, holding.Wrist, held.transform.position);
                if (holding.Tracked && holding.Velocity.magnitude > ForceGrabDetector.ThrowSpeed
                    && Vector3.Dot(holding.Velocity.normalized, body.ArmDirection(holding.IsLeft, holding.Palm)) > 0.3f)
                    ThrowHeld(holding.Velocity, holding.Velocity.normalized);
                else if (heldFor > 5f)
                    DropHeld();
                return;
            }

            // Placeholder look: webs are pale comets, darker and stronger at level 3. Web pull waits for room objects.
            bool black = player.Level >= 3;
            Color color = black ? new Color(0.35f, 0.1f, 0.45f) : new Color(0.9f, 0.92f, 1f);
            for (int i = 0; i < 2; i++)
            {
                HandPose hand = i == 0 ? input.Left : input.Right;
                if (!web[i].Step(hand, body, dt, out Vector3 origin, out Vector3 direction))
                    continue;
                if (player.Level >= 2 && room.TryAim(origin, direction, out var box))
                {
                    LiftBox(box, i, color);
                    continue;
                }
                game.FireBlast(origin, direction, 0.3f, 0.5f, 1.6f, player.Outgoing(black ? 7f : 5f), 0f, color);
            }
        }

        /// <summary>Finger gun on one hand: shows the blue aim line while aiming, true when a flick fires.</summary>
        bool TickGun(int index, HandPose hand, float dt, out Vector3 origin, out Vector3 direction)
        {
            bool fired = gun[index].Step(hand, body, dt, out origin, out direction);
            if (gun[index].Aiming)
                ShowAim(index, gun[index].Origin, gun[index].Origin + gun[index].Direction * 4f);
            return fired;
        }

        void ShowAim(int index, Vector3 from, Vector3 to)
        {
            LineRenderer line = playAim[index];
            line.enabled = true;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
        }
    }
}
