using System.Collections.Generic;
using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// The two elemental characters:
    /// - Tempest (Elementalist): palm push for a gust of wind (level 1); both hands out flat for chain lightning,
    ///   at most 5 s at a time before it fizzles and recharges (level 2); circle both hands to charge a tornado,
    ///   then push both forward to throw it (level 3). Wind aura from level 2.
    /// - Terra (Earth): fist then snap open for a rock orb, with stone on your hands (level 1); a fist low at your
    ///   waist raised fast makes three rock spikes burst from the floor ahead (level 2); rock orbs stick to what
    ///   they hit and a finger snap blows them all up (level 3). Crater aura at level 2, orbiting rocks at 3.
    /// </summary>
    public sealed partial class Arena
    {
        const float LightningSeconds = 5f;
        const float LightningRecharge = 3f;
        const float LightningDps = 16f;
        const float TornadoDps = 12f;

        readonly PalmPushDetector[] gust = { new PalmPushDetector(), new PalmPushDetector() };
        readonly FlatHandsDetector flatHands = new FlatHandsDetector();
        readonly SwirlDetector swirl = new SwirlDetector();
        readonly UppercutDetector[] uppercut = { new UppercutDetector(), new UppercutDetector() };
        readonly SnapDetector[] snap = { new SnapDetector(), new SnapDetector() };
        readonly List<StuckRock> stuckRocks = new List<StuckRock>();
        ChainLightning[] lightning;
        ElementAura elementAura;
        RockGauntlets gauntlets;
        Material rockMaterial;
        Material rockHot;
        GameObject tornadoCharge;
        float lightningLeft = LightningSeconds;
        float lightningCooldown;
        bool lightningOn;

        void CreateElementViews()
        {
            rockMaterial = Resources.Load<Material>("Props/Materials/Rock");
            rockHot = Resources.Load<Material>("Props/Materials/RockHot");
            Material gold = Resources.Load<Material>("Props/Materials/RockGold");
            elementAura = new ElementAura(transform, rockMaterial, gold);
            gauntlets = new RockGauntlets(transform, rockMaterial);
            // The lightning uses the energy ball's arc material.
            GameObject ball = game.MakeEnergyBall(transform);
            LineRenderer arc = ball.GetComponentInChildren<LineRenderer>(true);
            Material arcMaterial = arc != null ? arc.sharedMaterial : Resources.Load<Material>("AimLine");
            Destroy(ball);
            Color bolt = new Color(0.6f, 0.8f, 1f);
            lightning = new[] { new ChainLightning(transform, arcMaterial, bolt), new ChainLightning(transform, arcMaterial, bolt) };
        }

        /// <summary>Per frame, for every character: auras, stone hands, and the Earth shots' look.</summary>
        void TickElementLooks(bool canFight, float dt)
        {
            bool inPlay = (phase == Phase.Playing && !player.IsDead) || practicing;
            int level = inPlay ? player.Level : 1;
            elementAura.Show(inPlay ? character : CharacterId.Technoman, level, head.position, FloorY, dt);
            gauntlets.Tick(inPlay && character == CharacterId.Earth, input.Left, input.Right);
            game.Dress = character == CharacterId.Earth ? (System.Action<Blast>)DressRock : null;
            if (!canFight || character != CharacterId.Elementalist)
            {
                lightning[0].Hide();
                lightning[1].Hide();
                ShowTornadoCharge(0f);
            }
            stuckRocks.RemoveAll(rock => rock == null);
        }

        void TickElementalist(float dt)
        {
            // Level 3 tornado: circling hands charge it, a two-handed push throws it.
            bool charging = false;
            if (player.Level >= 3)
            {
                if (swirl.Step(input.Left, input.Right, body, dt, out Vector3 direction, out float power))
                {
                    Tornado.Launch(game, swirl.Center, direction, power, FloorY, player.Outgoing(TornadoDps), foes);
                    handsBusyFor = 0.6f;
                    Log($"Tornado, power {power:0.00}");
                }
                charging = swirl.Charging;
                ShowTornadoCharge(charging ? swirl.Charge : 0f);
            }

            // Level 2 lightning: both hands out flat. Five seconds at most, then it needs a rest.
            bool flat = player.Level >= 2 && !charging && flatHands.Step(input.Left, input.Right, body, dt);
            lightningCooldown = Mathf.Max(0f, lightningCooldown - dt);
            bool zap = flat && lightningCooldown <= 0f && lightningLeft > 0f;
            if (zap)
            {
                lightningLeft -= dt;
                for (int i = 0; i < 2; i++)
                {
                    HandPose hand = i == 0 ? input.Left : input.Right;
                    Vector3 aim = Vector3.Slerp(body.ArmDirection(hand.IsLeft, hand.Palm), hand.Forward, 0.4f).normalized;
                    lightning[i].Strike(game, hand.Palm + aim * 0.05f, aim, player.Outgoing(LightningDps), dt);
                }
                if (lightningLeft <= 0f)
                {
                    lightningCooldown = LightningRecharge;
                    hud.Message("", "Lightning spent: rest your hands a moment", 1.2f);
                    Log("Lightning ran out");
                }
            }
            else
            {
                lightning[0].Hide();
                lightning[1].Hide();
                if (!flat)
                    lightningLeft = Mathf.Min(LightningSeconds, lightningLeft + dt * (LightningSeconds / LightningRecharge));
                if (lightningCooldown <= 0f && lightningLeft <= 0f)
                    lightningLeft = LightningSeconds;
            }
            if (zap != lightningOn)
            {
                lightningOn = zap;
                if (zap)
                    game.PlayAt(head.position + body.Forward * 0.4f, game.ZapSound);
                Log(zap ? "Lightning on" : "Lightning off");
            }
            if (flat || charging)
            {
                handsBusyFor = Mathf.Max(handsBusyFor, 0.3f);
                return;
            }

            // Level 1 gust: a palm shoved forward.
            for (int i = 0; i < 2; i++)
            {
                HandPose hand = i == 0 ? input.Left : input.Right;
                if (gust[i].Step(hand, body, dt, out Vector3 origin, out Vector3 direction))
                {
                    direction = Aim.Assist(origin, direction);
                    WindGust.Launch(game, origin + direction * 0.1f, direction, player.Outgoing(WindGust.Damage), foes);
                }
            }
        }

        void ShowTornadoCharge(float charge)
        {
            if (charge <= 0.02f)
            {
                if (tornadoCharge != null)
                    tornadoCharge.SetActive(false);
                return;
            }
            if (tornadoCharge == null)
            {
                tornadoCharge = new GameObject("Tornado Charge");
                tornadoCharge.transform.SetParent(transform, false);
                Color pale = new Color(0.85f, 0.95f, 1f);
                for (int i = 0; i < 4; i++)
                    BallLook.Sprite("Ring " + i, tornadoCharge.transform, "FxRing", 1f - i * 0.18f, pale).Set(roll: 600f + i * 200f)
                        .transform.localPosition = Vector3.up * (i * 0.25f - 0.35f);
            }
            tornadoCharge.SetActive(true);
            tornadoCharge.transform.position = swirl.Center;
            tornadoCharge.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            tornadoCharge.transform.localScale = Vector3.one * Mathf.Lerp(0.08f, 0.3f, charge);
        }

        void TickEarth(float dt)
        {
            // Level 2 spikes: an uppercut from the waist.
            if (player.Level >= 2)
            {
                for (int i = 0; i < 2; i++)
                {
                    HandPose hand = i == 0 ? input.Left : input.Right;
                    if (uppercut[i].Step(hand, body, dt, out Vector3 direction))
                    {
                        Vector3 feet = new Vector3(head.position.x, FloorY, head.position.z);
                        // Toward whatever's ahead, if something is.
                        IHittable target = Aim.Target(head.position, direction, 25f, 4f);
                        if (target != null)
                            direction = Vector3.ProjectOnPlane(target.Center - head.position, Vector3.up).normalized;
                        RockSpikes.Erupt(game, feet, direction, player.Outgoing(RockSpikes.Damage), rockMaterial, foes);
                        handsBusyFor = 0.5f;
                        Log("Rock spikes");
                    }
                }
            }
            // Level 3: a snap sets off every rock stuck to an enemy.
            if (player.Level >= 3 && stuckRocks.Count > 0 && (snap[0].Step(input.Left, dt) | snap[1].Step(input.Right, dt)))
            {
                int blown = 0;
                foreach (StuckRock rock in stuckRocks)
                {
                    if (rock == null)
                        continue;
                    game.Explode(rock.transform.position, 0.6f, player.Outgoing(12f), new Color(1f, 0.4f, 0.1f));
                    Destroy(rock.gameObject);
                    blown++;
                }
                stuckRocks.Clear();
                Log($"Snap: {blown} rocks blew up");
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// PlayMode test: fires every elemental and archer effect at the nearest enemy, as the gestures would: a gust,
        /// lightning for a moment, a tornado, rock spikes, a rock orb that sticks, the snap, an arrow and a bolt, and
        /// shows the bow and crossbows. Returns how many enemies there were to aim at.
        /// </summary>
        public int TestElementEffects()
        {
            IHittable target = null;
            foreach (IHittable foe in enemies)
                if (foe is Enemy enemy && enemy != null && enemy.Alive && !enemy.Spawning)
                    target = foe;
            Vector3 from = head.position + body.Forward * 0.3f;
            Vector3 aim = target != null ? (target.Center - from).normalized : body.Forward;
            WindGust.Launch(game, from, aim, 6f, foes);
            lightning[0].Strike(game, from, aim, LightningDps, 0.1f);
            Tornado.Launch(game, from, aim, 1f, FloorY, TornadoDps, foes);
            RockSpikes.Erupt(game, new Vector3(head.position.x, FloorY, head.position.z), aim, RockSpikes.Damage, rockMaterial, foes);
            if (target is Enemy stuckOn)
                stuckRocks.Add(StuckRock.Attach(stuckOn, target.Center - aim * 0.3f, rockHot, foes));
            Blast orb = game.FireBlast(from, aim, 1f, 1f, 1f, 5f, 0f);
            DressRock(orb);
            ArcherGear.Dress(game.FireBlast(from, aim, 0.3f, 0.4f, 2f, 6f, 0f), "Arrow", AttackColor);
            ArcherGear.Dress(game.FireBlast(from, aim, 0.3f, 0.4f, 2f, 6f, 0f), "Bolt", AttackColor);
            archerGear.Bow(true, from, from - aim * 0.4f, aim, 0.7f, AttackColor);
            archerGear.Crossbow(0, true, from, aim, 0.5f, AttackColor);
            elementAura.Show(CharacterId.Earth, 3, head.position, FloorY, 0.1f);
            return target != null ? 1 : 0;
        }

        /// <summary>PlayMode test: the snap, setting off stuck rocks. Returns how many went off.</summary>
        public int TestSnap()
        {
            int count = stuckRocks.Count;
            foreach (StuckRock rock in stuckRocks)
                if (rock != null)
                {
                    game.Explode(rock.transform.position, 0.6f, 12f, Color.red);
                    Destroy(rock.gameObject);
                }
            stuckRocks.Clear();
            return count;
        }
#endif

        /// <summary>Rock orbs: a stone with glowing cracks instead of the energy ball; at level 3 it sticks to what it hits.</summary>
        void DressRock(Blast shot)
        {
            BallLook.HideCrackle(shot.Head, true);
            foreach (Renderer renderer in shot.Head.GetComponentsInChildren<Renderer>())
                renderer.enabled = false;
            shot.TailScale = 0.4f;
            var prefab = Resources.Load<GameObject>("Props/RockSmallA");
            if (prefab != null)
            {
                Transform rock = Instantiate(prefab, shot.Head, false).transform;
                rock.localScale = Vector3.one * 1.1f;
                rock.localRotation = Random.rotation;
                Paint.All(rock, rockHot);
                rock.gameObject.AddComponent<FxSpin>().Set(tumble: new Vector3(200f, 320f, 90f));
            }
            BallLook.Sprite("Heat", shot.Head, "FxGlow", 1.8f, new Color(1f, 0.35f, 0.08f) * 0.7f).Set(billboard: true, flicker: 0.2f);
            BallLook.Particles("Embers", shot.Head, "FxGlow", new Color(1f, 0.5f, 0.15f), new Color(0.4f, 0.1f, 0.02f, 0f), 0.4f, 0.35f, 35f, 0.2f, 0.4f);
            if (player.Level >= 3)
                shot.OnHit = (target, at) =>
                {
                    if (target is Enemy enemy && enemy != null && enemy.Alive)
                        stuckRocks.Add(StuckRock.Attach(enemy, at, rockHot, foes));
                };
        }
    }
}
