using System.Collections.Generic;
using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// Character attacks beyond the hand weapons in <see cref="HandRepulsor"/>:
    /// - Ki fighter: energy ball (level 2) and dragon fist (level 3)
    /// - Saber: finger-gun blaster (level 1) and laser sword (level 2)
    /// - Archer: finger-gun crossbow that reloads (level 1) and bow (level 2)
    /// - Spider: webs with an aim line (level 1); from level 2 webs grab furniture or enemies to swing and fling
    ///   (Arena.WebSwing)
    /// - Mage: flamethrower (level 2, the palm laser drawn as fire in HandRepulsor) and triangle fire pillar (level 3)
    /// Each runs only while the player can fight. Looks marked placeholder get their own variants later.
    /// </summary>
    public sealed partial class Arena
    {
        const float BallMinSize = 1.2f;
        const float BallMaxSize = 4f;
        const float BladeLength = 1f;
        const float BladeRadius = 0.06f;
        /// <summary>
        /// The laser sword deals most of its damage the moment it cuts into an enemy: a slash. Fast swings hit for the
        /// full amount, a slow push for less; the same enemy can't be slashed again for <see cref="SlashCooldown"/>.
        /// Holding the blade inside an enemy only adds a little more.
        /// </summary>
        public static float SlashDamage = 20f;
        public static float SlashMinDamage = 9f;
        /// <summary>Tip speed (m/s) at which a slash does full damage.</summary>
        public static float SlashFullSpeed = 2.5f;
        public static float SlashCooldown = 0.35f;
        const float BladeHoldDps = 6f;
        readonly Vector3[] lastHilt = new Vector3[2];
        readonly Vector3[] lastTip = new Vector3[2];
        readonly bool[] bladeWasOut = new bool[2];
        readonly Dictionary<IHittable, float> slashedAt = new Dictionary<IHittable, float>();
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
        /// <summary>Seconds the hand weapons stay off: during a two-hand ability and just after, so its fists don't fire.</summary>
        float handsBusyFor;
        float busyFor;
        public static string[] BallStyleNames => BallLook.Names;
        // A new key, so the Star (the user's favourite) replaces picks saved before it existed.
        const string BallPickKey = "variant.ball.v2";
        int PickedBall => PlayerPrefs.GetInt(BallPickKey, BallLook.Star);

        /// <summary>Gives a ball the chosen energy ball look (see <see cref="BallLook"/>).</summary>
        static void DecorateBall(Transform ball, int style, Color color) => BallLook.Apply(ball, style, color);
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
            archerGear = new ArcherGear(transform);
        }

        ArcherGear archerGear;

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
                        break;
                    case CharacterId.Spider:
                        TickSpider(dt);
                        break;
                    case CharacterId.Elementalist:
                        TickElementalist(dt);
                        break;
                    case CharacterId.Earth:
                        TickEarth(dt);
                        break;
                }
            }
            TickElementLooks(canFight, dt);
            spiderSense.Tick(canFight && character == CharacterId.Spider, dt);
            if (!canFight || character != CharacterId.Archer)
                archerGear.Hide();
            if (swinging != null && (!canFight || character != (swingingByForce ? CharacterId.Saber : CharacterId.Spider)))
            {
                ReleaseWebbed(swinging);
                swinging = null;
            }
            TickFlung(dt);

            handsBusyFor = Mathf.Max(0f, handsBusyFor - dt);
            // Each detector only updates for its own character, so only that character's counts (a stale one can't lock others out).
            bool busy = canFight && ((character == CharacterId.Mage && triangle.Aiming)
                || (character == CharacterId.KiFighter && kiCharge.Charging)
                || (character == CharacterId.Archer && bow.Formed));
            if (busy)
                handsBusyFor = 0.6f;
            // Diagnostic: a two-hand gesture that keeps the hand weapons off for long may be a false detection.
            if (busy)
                busyFor += dt;
            else if (busyFor > 0f)
            {
                if (busyFor > 1f)
                    Log($"Hand weapons were held off {busyFor:0.0} s by the {(character == CharacterId.Mage ? "triangle" : character == CharacterId.KiFighter ? "two-hand charge" : "bow")}");
                busyFor = 0f;
            }

            if (charging)
            {
                if (chargeBall == null)
                {
                    chargeBall = game.MakeEnergyBall(transform);
                    DecorateBall(chargeBall.transform, PickedBall, AttackColor);
                }
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
                    direction = Aim.Assist(kiCharge.Center, direction);
                    Vector3 from = kiCharge.Center + direction * 0.15f;
                    float size = Mathf.Lerp(BallMinSize, BallMaxSize, power);
                    NextShot(ShotLook.KiBall, PickedBall);
                    Blast ball = game.FireBlast(from, direction, 1f, size, Mathf.Lerp(0.8f, 0.55f, power),
                        player.Outgoing(12f + 18f * power), 0.6f + 0.6f * power);
                    DecorateBall(ball.Head, PickedBall, AttackColor);
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
                        // Its comets are sent as the one effect below, not as shots of their own.
                        replayingShot = true;
                        IHittable locked = DragonFist.Launch(game, PickedDragon, origin + punch * 0.1f, punch, player.Outgoing(10f));
                        replayingShot = false;
                        SendEffect(NetEffect.DragonFist, origin + punch * 0.1f, punch, 0f, AttackColor, PickedDragon);
                        Log($"Dragon fist at {hand.Velocity.magnitude:0.0} m/s, {(locked != null ? $"locked on {Vector3.Angle(punch, locked.Center - origin):0} deg off" : "nothing to lock on")}");
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
                    CutWithBlade(i, hilt, tip, dt);
                    continue;
                }
                bladeWasOut[i] = false;
                if (TickGun(i, hand, dt, out Vector3 origin, out Vector3 direction))
                    game.FireBlast(origin, direction, 0.2f, 0.45f, 1.7f, player.Outgoing(4f), 0f, BlasterRed);
            }
        }

        void CutWithBlade(int hand, Vector3 hilt, Vector3 tip, float dt)
        {
            bool was = bladeWasOut[hand];
            Vector3 oldHilt = was ? lastHilt[hand] : hilt, oldTip = was ? lastTip[hand] : tip;
            float tipSpeed = was && dt > 0f ? Vector3.Distance(tip, oldTip) / dt : 0f;
            lastHilt[hand] = hilt;
            lastTip[hand] = tip;
            bladeWasOut[hand] = true;
            var all = Hittables.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (i >= all.Count)
                    continue;
                IHittable target = all[i];
                if (!target.Alive || target.Team != Team.Enemies)
                    continue;
                bool inside = HitTest.Segment(target, hilt, tip, BladeRadius);
                // A fast swing can pass right through between frames: check where the blade swept as well, along
                // the tip's path and the middle of the blade's.
                bool swept = !inside && was && (HitTest.Segment(target, oldTip, tip, BladeRadius)
                    || HitTest.Segment(target, (oldHilt + oldTip) * 0.5f, (hilt + tip) * 0.5f, BladeRadius));
                if (!inside && !swept)
                    continue;
                if (!slashedAt.TryGetValue(target, out float last) || Time.time - last >= SlashCooldown)
                {
                    slashedAt[target] = Time.time;
                    float hit = Mathf.Lerp(SlashMinDamage, SlashDamage, Mathf.Clamp01(tipSpeed / SlashFullSpeed));
                    game.Strike(target, player.Outgoing(hit), hilt, "Sword slash");
                }
                else if (inside)
                {
                    game.Strike(target, player.Outgoing(BladeHoldDps * dt), hilt, "Sword");
                }
            }
            if (slashedAt.Count > 32)
                slashedAt.Clear();
        }

        /// <summary>For tests: one frame of a blade held from hilt to tip in this hand (0 left, 1 right).</summary>
        public void TestBlade(int hand, Vector3 hilt, Vector3 tip, float dt) => CutWithBlade(hand, hilt, tip, dt);

        void TickArcher(float dt)
        {
            if (player.Level >= 2 && bow.Step(input.Left, input.Right, body, dt, out Vector3 from, out Vector3 aim, out float draw))
            {
                NextShot(ShotLook.Arrow);
                Blast arrow = game.FireBlast(from + aim * 0.1f, aim, 0.3f, 0.4f, Mathf.Lerp(1.2f, 2.2f, draw), player.Outgoing(6f + 10f * draw), 0f);
                arrow.Pierce = player.Level >= 3;
                ArcherGear.Dress(arrow, "Arrow", AttackColor);
                Log($"Arrow, draw {draw:0.00}{(arrow.Pierce ? ", piercing" : "")}");
            }
            // The longbow in the front fist, its string to the drawing hand.
            archerGear.Bow(player.Level >= 2 && bow.Formed, bow.BowPosition, bow.StringPosition, bow.Direction, bow.Draw, AttackColor);
            if (bow.Formed)
            {
                // Where the arrow will go.
                ShowAim(2, bow.BowPosition, bow.BowPosition + bow.Direction * (1.5f + 3f * bow.Draw));
                archerGear.Crossbow(0, false, default, Vector3.forward, 0f, AttackColor);
                archerGear.Crossbow(1, false, default, Vector3.forward, 0f, AttackColor);
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
                        SendEffect(NetEffect.Smoke, from2, velocity.normalized, velocity.magnitude);
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
                    NextShot(ShotLook.Bolt);
                    Blast bolt = game.FireBlast(origin, direction, 0.4f, 0.5f, 2f, player.Outgoing(7f), 0f);
                    ArcherGear.Dress(bolt, "Bolt", AttackColor);
                }
                // A crossbow in the finger-gun hand, reloading as it charges.
                archerGear.Crossbow(i, gun[i].Aiming, hand.Palm, gun[i].Direction, 1f - reload[i] / CrossbowReload, AttackColor);
            }
        }

        void TickMage(float dt)
        {
            if (player.Level < 3)
                return;
            if (triangle.Step(input.Left, input.Right, body, dt, out Vector3 target))
            {
                handsBusyFor = 0.8f;
                game.Explode(target + Vector3.up * 0.6f, 1.1f, player.Outgoing(25f), AttackColor);
                FirePillar.Spawn(target, AttackColor, PickedPillar, foes);
                SendEffect(NetEffect.FirePillar, target, Vector3.up, 0f, AttackColor, PickedPillar);
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

        /// <summary>
        /// Force powers: a cupped hand at an enemy grabs it (it hangs off your hand like the Spider's webbed ones, swing
        /// to smash, swing fast to fling; the boss is too big); at a piece of furniture it lifts a copy of it, and a
        /// fast swing throws it.
        /// </summary>
        void TickForce(float dt)
        {
            if (swinging != null && swingingByForce)
            {
                HandPose holding = swingingHand == 0 ? input.Left : input.Right;
                // Close the hand into a fist round it: it's crushed.
                crushFist = holding.Fist ? crushFist + dt : 0f;
                if (crushFist >= CrushHoldSeconds)
                {
                    CrushHeld();
                    return;
                }
                GrabEvent kept = force[swingingHand].Step(holding, body, dt, out _);
                if (TickWebSwing(dt) && kept == GrabEvent.Drop && swinging != null && !holding.Fist)
                {
                    // Let go of the cup: it drops where it is.
                    ReleaseWebbed(swinging);
                    swinging = null;
                }
                return;
            }
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
                Enemy foe = Grabbable(force[i].AimOrigin, force[i].AimDirection);
                if (foe != null)
                {
                    ShowAim(i, force[i].AimOrigin, foe.Center);
                    if (grab == GrabEvent.Grab)
                    {
                        WebEnemy(foe, i, true);
                        return;
                    }
                    continue;
                }
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

        /// <summary>Lumen's crush: damage from closing a fist on an enemy held by the force (most die; a tank survives).</summary>
        public static float CrushDamage = 50f;
        /// <summary>The fist has to stay closed this long, so a hand passing through a fist on its way to a throw doesn't crush.</summary>
        const float CrushHoldSeconds = 0.12f;
        float crushFist;

        void CrushHeld()
        {
            Webbed crushed = swinging;
            swinging = null;
            crushFist = 0f;
            Enemy enemy = crushed.Enemy;
            ReleaseWebbed(crushed);
            if (enemy == null || !enemy.Alive)
                return;
            Vector3 at = enemy.Center;
            game.Strike(enemy, player.Outgoing(CrushDamage), at, "Force crush");
            game.Explode(at, 0.35f, 0f, AttackColor);
            game.PlayAt(at, game.BoomSound);
            Log($"Crushed a {enemy.Kind} with the force");
        }

        /// <summary>For tests: crush what the force holds, as closing the fist does.</summary>
        public void TestCrushHeld()
        {
            if (swinging != null && swingingByForce)
                CrushHeld();
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
            float wall = game.WallAhead(held.transform.position, aim, out float d) ? d : float.MaxValue;
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

        /// <summary>How much of the room the game knows: furniture for force powers comes from Space Setup.</summary>
        string RoomStatus() => room == null || game == null || game.Room.Boxes == null
            ? "Room: waiting for the scene permission"
            : room.Count > 0 ? $"Room: {room.Count} pieces of furniture found"
            : "Room: no furniture found. Run Space Setup for force powers.";

        /// <summary>
        /// A robot's step toward its target, slid round Space Setup furniture and walls: straight if clear, otherwise
        /// the nearest clear direction up to 90 degrees off, otherwise no move. Walks, dashes, knockback, the tornado's
        /// pull and the mech's leap all go through here, so none of them can put a robot in a wall the game knows of.
        /// </summary>
        public Vector3 Steer(Vector3 from, Vector3 direction, float step)
        {
            const float margin = 0.3f;
            // Already inside a box or a wall (spawned there, or the map is wrong): walk out rather than freeze.
            if (room.Blocks(from, 0f) || roomMap.Walled(from, 0f))
                return direction * step;
            for (int i = 0; i <= 12; i++)
            {
                float angle = (i + 1) / 2 * 15f * (i % 2 == 0 ? 1f : -1f);
                Vector3 tryDirection = Quaternion.AngleAxis(angle, Vector3.up) * direction;
                Vector3 ahead = from + tryDirection * (step + margin);
                // Walls too: the cells the depth scan saw solid, and the edge of Space Setup's floor.
                if (!room.Blocks(ahead, margin * 0.5f) && !roomMap.Walled(ahead))
                    return tryDirection * step;
            }
            return Vector3.zero;
        }

        /// <summary>A thrown object's path touches an enemy.</summary>
        public bool ThrownHits(Vector3 from, Vector3 to, float radius)
        {
            foreach (IHittable foe in Hittables.All)
                if (foe.Alive && foe.Team == Team.Enemies && HitTest.Segment(foe, from, to, radius))
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

            if (TickWebSwing(dt))
                return;

            // Webs: a strand to a small pale shot and a splat where it lands; darker and stronger at level 3.
            // From level 2 a web grabs the enemy or furniture it's aimed at; what it would grab is shown first.
            bool black = player.Level >= 3;
            Color color = black ? new Color(0.35f, 0.1f, 0.45f) : new Color(0.9f, 0.92f, 1f);
            for (int i = 0; i < 2; i++)
            {
                HandPose hand = i == 0 ? input.Left : input.Right;
                bool fired = web[i].Step(hand, body, dt, out Vector3 origin, out Vector3 direction);
                if (!web[i].Aiming)
                    continue;
                Enemy grab = WebGrabTarget(origin, direction);
                UnityEngine.XR.ARFoundation.ARBoundingBox box = null;
                bool onBox = grab == null && player.Level >= 2 && room.TryAim(origin, direction, out box);
                Vector3 aimed = Aim.Assist(origin, direction);
                if (grab != null)
                    ShowAim(i, origin, grab.Center);
                else if (onBox)
                {
                    room.Highlight(box, color);
                    ShowAim(i, origin, origin + direction * Mathf.Max(0.3f, Vector3.Distance(origin, box.transform.position)));
                }
                else
                    ShowAim(i, origin, origin + aimed * 4f);
                if (!fired)
                    continue;
                if (grab != null)
                {
                    WebEnemy(grab, i);
                    return;
                }
                if (onBox)
                {
                    LiftBox(box, i, color);
                    return;
                }
                NextShot(ShotLook.Web, black ? 1 : 0);
                Blast shot = game.FireBlast(origin, aimed, 0.3f, 0.35f, 1.6f, player.Outgoing(black ? 7f : 5f), 0f, color);
                WebStrand.Attach(shot, origin, black ? new Color(0.25f, 0.2f, 0.3f) : Color.white, foes);
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
