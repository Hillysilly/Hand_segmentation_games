using System;
using System.Collections.Generic;
using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// The character select screen's little stage: a mannequin (Quaternius, CC0) in the chosen colour acting out
    /// each level's move with its effect, life size beside the menu (11/15; it was a 12 cm model in the panel), and a big
    /// hand in the panel making the move's hand sign
    /// (<see cref="ShowcaseSign"/>). The arms and hands follow <see cref="ShowcasePoses"/>, built from what the
    /// gesture detectors look for; the idle animation (or the punch and sword clips) moves the rest. It goes level
    /// 1, 2, 3 and round again; the screen's arrows pick a level. Everything is built in "human" metres under a
    /// stage that can be scaled (life size on the select screen).
    /// </summary>
    public sealed class CharacterShowcase
    {
        public const float FigureHeight = 0.12f;
        const float Human = 1.8f;
        /// <summary>How big the stage is drawn: 1 for life size (the select screen, since 11/15), less for a model.</summary>
        readonly float Scale;
        const float Reach = 2f;
        const float Chest = 1.25f;
        const float Pause = 0.6f;

        enum Fx
        {
            Laser, Repulsor, AreaRepulsor, Comet, SpiritBall, DragonFist, Fireball, Flame, Pillar, Bolt, Saber, ForceThrow,
            Crossbow, Bow, Smoke, Web, WebFling, BlackWeb, Gust, Lightning, Tornado, Rock, Spikes, StickySnap,
        }

        sealed class Move
        {
            public readonly string Name;
            /// <summary>The hand sign's caption.</summary>
            public readonly string Sign;
            public readonly Fx Fx;
            public readonly float FireAt;
            public readonly float Hold;
            public readonly PoseKey[] Keys;
            /// <summary>An animation clip for the whole body (the punch, the sword), or null for idle plus posed arms.</summary>
            public readonly string Clip;
            /// <summary>When a charge glows on the hand (or between both) before it fires; negative for none.</summary>
            public readonly float ChargeFrom;
            public readonly bool ChargeBoth;

            public Move(string name, string sign, Fx fx, float fireAt, float hold, PoseKey[] keys, string clip = null,
                float chargeFrom = -1f, bool chargeBoth = false)
            {
                Name = name;
                Sign = sign;
                Fx = fx;
                FireAt = fireAt;
                Hold = hold;
                Keys = keys;
                Clip = clip;
                ChargeFrom = chargeFrom;
                ChargeBoth = chargeBoth;
            }
        }

        /// <summary>Each character's move at levels 1, 2 and 3 (see CharacterDef.Controls).</summary>
        static readonly Dictionary<CharacterId, Move[]> Moves = new Dictionary<CharacterId, Move[]>
        {
            [CharacterId.Technoman] = new[]
            {
                new Move("Hand laser", "PALM OUT, HOLD", Fx.Laser, 0.35f, 1.2f, ShowcasePoses.PalmOutHold(1.6f)),
                new Move("Repulsor blast", "FIST, THEN SNAP OPEN", Fx.Repulsor, 1f, 0f, ShowcasePoses.FistThenOpen(1f), chargeFrom: 0.35f),
                new Move("Area repulsor", "FIST, THEN SNAP OPEN", Fx.AreaRepulsor, 1f, 0f, ShowcasePoses.FistThenOpen(1f), chargeFrom: 0.35f),
            },
            [CharacterId.KiFighter] = new[]
            {
                new Move("Comet blast", "FIST, THEN SNAP OPEN", Fx.Comet, 1f, 0f, ShowcasePoses.FistThenOpen(1f), chargeFrom: 0.35f),
                new Move("Giant energy ball", "HANDS TOGETHER, THEN PUSH", Fx.SpiritBall, 0.4f, 1.15f, ShowcasePoses.HandsTogetherPush(1.55f)),
                new Move("Dragon fist", "PUNCH FAST", Fx.DragonFist, 0.22f, 0f,
                    ShowcasePoses.ShapeOnly(SignShape.Fist, ShowcasePoses.F, ShowcasePoses.D, 1.2f), "Punch_Cross"),
            },
            [CharacterId.Mage] = new[]
            {
                new Move("Fireball", "FIST, THEN SNAP OPEN", Fx.Fireball, 1f, 0f, ShowcasePoses.FistThenOpen(1f), chargeFrom: 0.35f),
                new Move("Flamethrower", "PALM OUT, HOLD", Fx.Flame, 0.35f, 1.4f, ShowcasePoses.PalmOutHold(1.8f)),
                new Move("Fire pillar", "TRIANGLE TO AIM, THEN BOTH FISTS", Fx.Pillar, 0.5f, 1.45f, ShowcasePoses.TriangleThenFists(1.25f)),
            },
            [CharacterId.Saber] = new[]
            {
                new Move("Blaster bolt", "FINGER GUN, FLICK UP", Fx.Bolt, 0.75f, 0f, ShowcasePoses.FingerGunFlick(0.75f)),
                new Move("Laser sword", "MAKE A FIST", Fx.Saber, 0f, 0f,
                    ShowcasePoses.ShapeOnly(SignShape.Fist, ShowcasePoses.F, ShowcasePoses.In, 2f), "Sword_Attack"),
                new Move("Force grab and throw", "CUP TO AIM, FIST TO GRAB, SWING", Fx.ForceThrow, 0.35f, 0f, ShowcasePoses.CupLiftThrow(1.25f)),
            },
            [CharacterId.Archer] = new[]
            {
                new Move("Crossbow", "FINGER GUN, FLICK UP", Fx.Crossbow, 0.75f, 0f, ShowcasePoses.FingerGunFlick(0.75f)),
                new Move("Longbow", "FISTS TOGETHER, PULL ONE BACK, OPEN IT", Fx.Bow, 1.35f, 0f, ShowcasePoses.BowDraw(1.35f)),
                new Move("Smoke bomb", "FIST, UNDERHAND THROW", Fx.Smoke, 0.55f, 0f, ShowcasePoses.UnderhandThrow(0.55f)),
            },
            [CharacterId.Spider] = new[]
            {
                new Move("Web shot", "SPIDER SIGN (HOLD TO KEEP FIRING)", Fx.Web, 0.32f, 0f, ShowcasePoses.SpiderSign(1.1f)),
                new Move("Web and fling", "WEB IT, THEN SWING YOUR ARM", Fx.WebFling, 0.25f, 0.95f, ShowcasePoses.WebAndSwing()),
                new Move("Black webs", "SPIDER SIGN (HOLD TO KEEP FIRING)", Fx.BlackWeb, 0.32f, 0f, ShowcasePoses.SpiderSign(1.1f)),
            },
            [CharacterId.Elementalist] = new[]
            {
                new Move("Gust of wind", "OPEN PALM, PUSH FORWARD", Fx.Gust, 0.45f, 0f, ShowcasePoses.PalmShove(0.45f)),
                new Move("Chain lightning", "BOTH HANDS OUT FLAT", Fx.Lightning, 0.4f, 1.4f, ShowcasePoses.FlatHands(1.85f)),
                new Move("Tornado", "SWIRL BOTH HANDS, THEN PUSH", Fx.Tornado, 1.75f, 1.6f, ShowcasePoses.SwirlThenPush(1.75f),
                    chargeFrom: 0.35f, chargeBoth: true),
            },
            [CharacterId.Earth] = new[]
            {
                new Move("Rock orb", "FIST, THEN SNAP OPEN", Fx.Rock, 1f, 0f, ShowcasePoses.FistThenOpen(1f), chargeFrom: 0.35f),
                new Move("Rock spikes", "FIST LOW, RAISE IT FAST", Fx.Spikes, 0.5f, 0f, ShowcasePoses.Uppercut(0.5f)),
                new Move("Sticky rocks, snap", "THROW A ROCK, THEN SNAP YOUR FINGERS", Fx.StickySnap, 0.85f, 0.65f,
                    ShowcasePoses.ThrowThenSnap(0.85f, 1.85f), chargeFrom: 0.35f),
            },
        };

        /// <summary>The move's name at a level (1-3), for the screen.</summary>
        public static string MoveName(CharacterId id, int level) => Moves[id][Mathf.Clamp(level, 1, 3) - 1].Name;

        /// <summary>The hand sign's caption at a level (1-3).</summary>
        public static string SignName(CharacterId id, int level) => Moves[id][Mathf.Clamp(level, 1, 3) - 1].Sign;

        /// <summary>For tests: every move's keys, by character and level (1-3).</summary>
        public static PoseKey[] KeysFor(CharacterId id, int level) => Moves[id][Mathf.Clamp(level, 1, 3) - 1].Keys;

        sealed class Live
        {
            public GameObject Go;
            public float Age;
            public float Life;
            public Action<Live, float> Step;
            public Action End;
        }

        /// <summary>One arm of the mannequin, posed over the animation.</summary>
        sealed class Arm
        {
            public Transform Upper, Fore, Hand;
            public HandRig Rig;
        }

        readonly RepulsorGame game;
        readonly Transform stage;
        readonly Transform figureRoot;
        readonly Animation animation;
        readonly Renderer[] renderers;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        readonly Transform[] hands = new Transform[2];
        readonly Transform[] indexKnuckle = new Transform[2];
        readonly Transform[] pinkyKnuckle = new Transform[2];
        readonly Arm[] arms = new Arm[2];
        readonly List<Live> live = new List<Live>();
        readonly List<Live> running = new List<Live>();
        readonly FxSpin aura;
        readonly FxSpin charge;
        readonly ShowcaseSign sign;

        CharacterId character;
        Color color = Color.white;
        int level;
        float time;
        bool fired;

        public int Level => level + 1;

        /// <summary>For tests: what the animation is doing and where the right hand is.</summary>
        public string DebugState
        {
            get
            {
                if (animation == null)
                    return "no Animation";
                var text = new System.Text.StringBuilder($"t {time:0.00}, hand {Hand(1):F2}:");
                foreach (AnimationState state in animation)
                    if (state.enabled || state.weight > 0f)
                        text.Append($" {state.name} t{state.time:0.00} w{state.weight:0.00}");
                return text.ToString();
            }
        }
        public CharacterId Character => character;
        public bool Shown => stage.gameObject.activeSelf;
        public ShowcaseSign Sign => sign;
        public string SignCaption => Current.Sign;

        public CharacterShowcase(RepulsorGame game, Transform parent, float figureHeight = Human)
        {
            this.game = game;
            Scale = figureHeight / Human;
            stage = new GameObject("Character Showcase").transform;
            stage.SetParent(parent, false);
            stage.localScale = Vector3.one * Scale;

            var prefab = Resources.Load<GameObject>("Showcase/Mannequin");
            figureRoot = prefab != null ? UnityEngine.Object.Instantiate(prefab, stage, false).transform : new GameObject("No Mannequin").transform;
            figureRoot.SetParent(stage, false);
            figureRoot.name = "Mannequin";
            animation = figureRoot.GetComponentInChildren<Animation>();
            if (animation != null)
            {
                // A saved Animation keeps its clips but not the names they were added under; give them short names
                // ("Rig|Punch_Cross" becomes "Punch_Cross").
                var clips = new List<AnimationClip>();
                foreach (AnimationState state in animation)
                    clips.Add(state.clip);
                foreach (AnimationClip clip in clips)
                {
                    string shortName = clip.name.Substring(clip.name.LastIndexOf('|') + 1);
                    if (animation[shortName] == null)
                        animation.AddClip(clip, shortName);
                }
            }
            renderers = figureRoot.GetComponentsInChildren<Renderer>();
            foreach (Transform bone in figureRoot.GetComponentsInChildren<Transform>())
            {
                for (int side = 0; side < 2; side++)
                {
                    string suffix = side == 0 ? ".L" : ".R";
                    if (bone.name == "DEF-hand" + suffix)
                        hands[side] = bone;
                    else if (bone.name == "DEF-f_index.01" + suffix)
                        indexKnuckle[side] = bone;
                    else if (bone.name == "DEF-f_pinky.01" + suffix)
                        pinkyKnuckle[side] = bone;
                }
            }
            for (int side = 0; side < 2; side++)
            {
                string suffix = side == 0 ? ".L" : ".R";
                Transform upper = null, fore = null;
                foreach (Transform bone in figureRoot.GetComponentsInChildren<Transform>())
                {
                    if (bone.name == "DEF-upper_arm" + suffix)
                        upper = bone;
                    else if (bone.name == "DEF-forearm" + suffix)
                        fore = bone;
                }
                if (upper != null && fore != null && hands[side] != null)
                    arms[side] = new Arm { Upper = upper, Fore = fore, Hand = hands[side], Rig = HandRig.ForMannequin(figureRoot, side == 1) };
            }

            aura = BallLook.Sprite("Aura", stage, "FxGlow", 1f, Color.white).Set(billboard: true, flicker: 0.12f);
            aura.Place(new Vector3(0f, 0.95f, -0.15f), new Vector3(1.6f, 2.4f, 1f));
            charge = BallLook.Sprite("Charge", stage, "FxGlow", 0.3f, Color.white).Set(billboard: true, flicker: 0.2f);
            charge.gameObject.SetActive(false);
            sign = new ShowcaseSign(parent);
            stage.gameObject.AddComponent<ShowcasePoser>().Late = Pose;
            stage.gameObject.SetActive(false);
            sign.Show(false);
        }

        /// <summary>Shows a character from level 1 (or keeps going if it's already the one shown).</summary>
        public void Show(CharacterId id, Color tint)
        {
            bool changed = id != character || !Shown;
            character = id;
            color = tint;
            TintFigure();
            sign.Tint(tint);
            if (!Shown)
            {
                stage.gameObject.SetActive(true);
                sign.Show(true);
            }
            if (changed)
            {
                level = 0;
                StartMove();
            }
        }

        public void Hide()
        {
            if (!Shown)
                return;
            ClearEffects();
            stage.gameObject.SetActive(false);
            sign.Show(false);
        }

        /// <summary>Jumps to a level (1-3), from the screen's arrows.</summary>
        public void SetLevel(int newLevel)
        {
            level = (newLevel - 1 + 3) % 3;
            StartMove();
        }

        /// <summary>Stands the stage with its feet at <paramref name="feet"/>, the moves heading along <paramref name="facing"/>.</summary>
        public void Place(Vector3 feet, Quaternion facing) => stage.SetPositionAndRotation(feet, facing);

        /// <summary>Puts the hand sign's centre at <paramref name="centre"/>, its box facing as <paramref name="facing"/> (z into the screen).</summary>
        public void PlaceSign(Vector3 centre, Quaternion facing) => sign.Place(centre, facing);

        void TintFigure()
        {
            Color body = Color.Lerp(color, Color.white, 0.25f);
            Color joints = Color.Lerp(color, Color.black, 0.7f);
            foreach (Renderer renderer in renderers)
            {
                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    renderer.GetPropertyBlock(block, i);
                    block.SetColor("_BaseColor", slots[i] != null && slots[i].name.Contains("Joints") ? joints : body);
                    renderer.SetPropertyBlock(block, i);
                }
            }
        }

        Move Current => Moves[character][level];

        void StartMove()
        {
            ClearEffects();
            time = 0f;
            fired = false;
            Move move = Current;
            if (animation != null)
            {
                string clip = move.Clip != null && animation[move.Clip] != null ? move.Clip : "Idle_Loop";
                AnimationState state = animation[clip];
                if (state != null)
                {
                    state.speed = 1f;
                    state.wrapMode = clip == "Idle_Loop" ? WrapMode.Loop : WrapMode.Once;
                    state.time = 0f;
                    animation.CrossFade(clip, 0.12f);
                    if (clip != "Idle_Loop" && animation["Idle_Loop"] != null)
                    {
                        animation["Idle_Loop"].wrapMode = WrapMode.Loop;
                        animation.CrossFadeQueued("Idle_Loop", 0.2f);
                    }
                }
            }
            sign.Use(move.Keys);
            Equip(move);
            ShowAura();
        }

        float Duration(Move move)
        {
            float clip = move.Clip != null && animation != null && animation[move.Clip] != null ? animation[move.Clip].length : 0f;
            float keys = move.Keys.Length > 0 ? move.Keys[move.Keys.Length - 1].T : 0f;
            return Mathf.Max(clip, keys, move.FireAt + move.Hold + 0.6f) + Pause;
        }

        public void Tick(float dt)
        {
            if (!Shown)
                return;
            time += dt;
            Move move = Current;
            if (!fired && time >= move.FireAt)
            {
                fired = true;
                Fire(move);
            }
            ShowCharge(move);
            running.Clear();
            running.AddRange(live);
            foreach (Live effect in running)
            {
                if (!live.Contains(effect))
                    continue;
                effect.Age += dt;
                if (effect.Go == null || effect.Age >= effect.Life)
                {
                    live.Remove(effect);
                    effect.End?.Invoke();
                    if (effect.Go != null)
                        UnityEngine.Object.Destroy(effect.Go);
                    continue;
                }
                effect.Step?.Invoke(effect, dt);
            }
            if (time >= Duration(move))
            {
                level = (level + 1) % 3;
                StartMove();
            }
        }

        /// <summary>A glow gathering on the fist (or between the hands) while the move charges.</summary>
        void ShowCharge(Move move)
        {
            bool on = move.ChargeFrom >= 0f && time >= move.ChargeFrom && time < move.FireAt;
            charge.gameObject.SetActive(on);
            if (!on)
                return;
            float t = Mathf.Clamp01((time - move.ChargeFrom) / Mathf.Max(0.1f, move.FireAt - move.ChargeFrom));
            charge.transform.localPosition = move.ChargeBoth ? BothHands + Vector3.forward * 0.06f : Hand(1) + Vector3.forward * 0.05f;
            charge.Size = Vector3.one * Mathf.Lerp(0.08f, move.ChargeBoth ? 0.55f : 0.32f, t);
            Color glow = move.Fx == Fx.Tornado ? new Color(0.8f, 0.95f, 1f) : move.Fx == Fx.Fireball ? new Color(1f, 0.45f, 0.1f)
                : move.Fx == Fx.Rock || move.Fx == Fx.StickySnap ? new Color(1f, 0.55f, 0.2f) : color;
            SetColor(charge, glow * Mathf.Lerp(0.6f, 1.6f, t));
        }

        // ---------------------------------------------------------------- posing, after the animation each frame

        void Pose()
        {
            if (!Shown)
                return;
            Move move = Current;
            for (int side = 0; side < 2; side++)
            {
                Arm arm = arms[side];
                if (arm == null)
                    continue;
                HandSample s = ShowcasePoses.Sample(move.Keys, time, side == 0);
                if (s.Weight > 0.001f && move.Clip == null)
                    PoseArm(arm, side == 1, s);
                arm.Rig?.Shape(s.From, s.To, s.Blend);
            }
            sign.Pose(move.Keys, time);
        }

        static Vector3 Mirror(Vector3 v, bool right) => right ? v : new Vector3(-v.x, v.y, v.z);

        /// <summary>Two-bone reach of the arm to the key's wrist position, elbow down and out, then the hand turned.</summary>
        void PoseArm(Arm arm, bool right, HandSample s)
        {
            Vector3 shoulder = arm.Upper.position;
            Vector3 shoulderLocal = stage.InverseTransformPoint(shoulder);
            Vector3 goal = stage.TransformPoint(shoulderLocal + Mirror(s.At, right));
            Vector3 target = Vector3.Lerp(arm.Hand.position, goal, s.Weight);

            float a = Vector3.Distance(arm.Upper.position, arm.Fore.position);
            float b = Vector3.Distance(arm.Fore.position, arm.Hand.position);
            Vector3 toTarget = target - shoulder;
            float length = Mathf.Clamp(toTarget.magnitude, 0.05f * (a + b), (a + b) * 0.995f);
            Vector3 along = toTarget.normalized;
            float cos = Mathf.Clamp((a * a + length * length - b * b) / (2f * a * length), -1f, 1f);
            float sin = Mathf.Sqrt(1f - cos * cos);
            Vector3 pole = stage.TransformDirection(new Vector3(right ? 0.55f : -0.55f, -1f, -0.3f)).normalized;
            Vector3 bendDir = Vector3.ProjectOnPlane(pole, along);
            if (bendDir.sqrMagnitude < 1e-6f)
                bendDir = stage.TransformDirection(Vector3.down);
            bendDir.Normalize();
            Vector3 elbow = shoulder + (along * cos + bendDir * sin) * a;

            arm.Upper.rotation = Quaternion.FromToRotation(arm.Fore.position - shoulder, elbow - shoulder) * arm.Upper.rotation;
            Vector3 wrist = shoulder + along * length;
            arm.Fore.rotation = Quaternion.FromToRotation(arm.Hand.position - arm.Fore.position, wrist - arm.Fore.position) * arm.Fore.rotation;

            if (arm.Rig != null)
            {
                Quaternion facing = arm.Rig.Facing(stage.TransformDirection(Mirror(s.Fingers, right)), stage.TransformDirection(Mirror(s.Palm, right)));
                arm.Hand.rotation = Quaternion.Slerp(arm.Hand.rotation, facing, s.Weight);
            }
        }

        // ---------------------------------------------------------------- where things are, in the stage's space

        Vector3 Local(Transform bone) => bone != null ? stage.InverseTransformPoint(bone.position) : new Vector3(0f, Chest, 0.3f);

        /// <summary>The hand further forward (the one doing the move).</summary>
        int LeadHand => Local(hands[0]).z > Local(hands[1]).z ? 0 : 1;

        Vector3 Hand(int side) => Local(hands[side]);

        Vector3 BothHands => (Hand(0) + Hand(1)) * 0.5f;

        /// <summary>Where shots land: out in front at chest height.</summary>
        static Vector3 Target => new Vector3(0f, 1.1f, Reach);

        /// <summary>The same spot on the floor.</summary>
        static Vector3 FloorTarget => new Vector3(0f, 0.02f, Reach);

        // ---------------------------------------------------------------- building blocks

        Live Add(GameObject go, float life, Action<Live, float> step, Action end = null)
        {
            var effect = new Live { Go = go, Life = life, Step = step, End = end };
            live.Add(effect);
            step?.Invoke(effect, 0f);
            return effect;
        }

        GameObject Holder(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(stage, false);
            return go;
        }

        FxSpin Glow(Transform parent, string material, float size, Color tint, float flicker = 0.1f) =>
            BallLook.Sprite(material, parent, material, size, tint).Set(billboard: true, flicker: flicker);

        LineRenderer Line(Transform parent, float width, Color tint, int points)
        {
            var go = new GameObject("Line", typeof(LineRenderer));
            go.transform.SetParent(parent, false);
            var line = go.GetComponent<LineRenderer>();
            line.sharedMaterial = Resources.Load<Material>("AimLine");
            line.useWorldSpace = false;
            line.positionCount = points;
            line.widthMultiplier = width * Scale;
            line.numCapVertices = 3;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Tint.Line(line, tint);
            return line;
        }

        Transform Prop(string key, Transform parent)
        {
            var prefab = Resources.Load<GameObject>("Props/" + key);
            Transform made = prefab != null ? UnityEngine.Object.Instantiate(prefab, parent, false).transform : new GameObject(key).transform;
            made.SetParent(parent, false);
            foreach (Renderer renderer in made.GetComponentsInChildren<Renderer>())
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return made;
        }

        static void SetColor(Component sprite, Color tint)
        {
            var renderer = sprite.GetComponent<Renderer>();
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetColor("_Color", tint);
            renderer.SetPropertyBlock(properties);
        }

        /// <summary>A burst where something lands: a flash and a ring growing outward.</summary>
        void Burst(Vector3 at, float size, Color tint)
        {
            GameObject go = Holder("Burst");
            go.transform.localPosition = at;
            FxSpin flash = Glow(go.transform, "FxGlow", size, tint * 1.5f, 0f);
            FxSpin ring = Glow(go.transform, "FxRing", size, tint, 0f);
            Add(go, 0.45f, (e, dt) =>
            {
                float t = e.Age / e.Life;
                flash.Size = Vector3.one * size * (1f + t);
                ring.Size = Vector3.one * size * (0.6f + 2.2f * t);
                SetColor(flash, tint * (1.6f * (1f - t)));
                SetColor(ring, tint * (1.2f * (1f - t)));
            });
        }

        /// <summary>Something flying from <paramref name="from"/> to <paramref name="to"/>; lands with <paramref name="land"/>.</summary>
        void Fly(GameObject go, Vector3 from, Vector3 to, float seconds, Action land, float arc = 0f, bool trail = false, Color trailTint = default)
        {
            Vector3 last = from;
            Add(go, seconds, (e, dt) =>
            {
                float t = Mathf.Clamp01(e.Age / e.Life);
                Vector3 at = Vector3.Lerp(from, to, t) + Vector3.up * (arc * 4f * t * (1f - t));
                go.transform.localPosition = at;
                Vector3 heading = at - last;
                if (heading.sqrMagnitude > 1e-6f)
                    go.transform.localRotation = Quaternion.LookRotation(heading);
                if (trail && dt > 0f)
                {
                    GameObject puff = Holder("Trail");
                    puff.transform.localPosition = at;
                    FxSpin bit = Glow(puff.transform, "FxGlow", 0.22f, trailTint, 0f);
                    Add(puff, 0.25f, (p, d) =>
                    {
                        float k = 1f - p.Age / p.Life;
                        bit.Size = Vector3.one * 0.22f * k;
                        SetColor(bit, trailTint * k);
                    });
                }
                last = at;
            }, land);
        }

        /// <summary>A glowing ball from a hand out to the target spot.</summary>
        void Ball(Vector3 from, float size, Color tint, float seconds, float burst, bool trail = false)
        {
            GameObject go = Holder("Ball");
            Glow(go.transform, "FxGlow", size * 1.8f, tint);
            Glow(go.transform, "FxGlow", size * 0.7f, Color.white * 1.3f);
            Fly(go, from, Target, seconds, () =>
            {
                Burst(Target, burst, tint);
            }, 0f, trail, tint * 0.8f);
        }

        // ---------------------------------------------------------------- the moves

        /// <summary>Things held for the whole move: the crossbow, the bow, the laser sword.</summary>
        void Equip(Move move)
        {
            switch (move.Fx)
            {
                case Fx.Crossbow:
                {
                    GameObject go = Holder("Crossbow");
                    Transform crossbow = Prop("Crossbow", go.transform);
                    Add(go, 99f, (e, dt) =>
                    {
                        Vector3 aim = arms[1]?.Rig != null ? stage.InverseTransformDirection(arms[1].Rig.Pointing) : Vector3.forward;
                        go.transform.localPosition = Hand(1) + aim * 0.05f;
                        go.transform.localRotation = Quaternion.LookRotation(aim, Vector3.up);
                        crossbow.localRotation = Quaternion.identity;
                    });
                    break;
                }
                case Fx.Bow:
                {
                    GameObject go = Holder("Bow");
                    Transform bow = Prop("Bow", go.transform);
                    Transform arrow = Prop("Arrow", go.transform);
                    LineRenderer cord = Line(go.transform, 0.012f, new Color(0.9f, 0.9f, 0.85f), 3);
                    Add(go, Duration(move) - Pause, (e, dt) =>
                    {
                        Vector3 grip = Hand(0);
                        Vector3 draw = Hand(1);
                        bow.localPosition = grip;
                        bow.localRotation = Quaternion.identity;
                        Vector3 top = grip + new Vector3(0f, 0.56f, -0.29f);
                        Vector3 bottom = grip + new Vector3(0f, -0.56f, -0.29f);
                        bool loosed = e.Age >= move.FireAt;
                        Vector3 rest = grip + new Vector3(0f, 0f, -0.29f);
                        Vector3 nock = !loosed && draw.z < rest.z ? new Vector3(grip.x, grip.y, draw.z) : rest;
                        cord.SetPosition(0, top);
                        cord.SetPosition(1, nock);
                        cord.SetPosition(2, bottom);
                        arrow.gameObject.SetActive(!loosed);
                        arrow.localPosition = nock;
                        arrow.localRotation = Quaternion.identity;
                    });
                    break;
                }
                case Fx.Saber:
                {
                    GameObject go = Holder("Saber");
                    LineRenderer blade = Line(go.transform, 0.07f, color * 1.6f, 2);
                    LineRenderer core = Line(go.transform, 0.025f, Color.white, 2);
                    Add(go, 99f, (e, dt) =>
                    {
                        Vector3 hilt = Hand(1);
                        Vector3 along = (Local(indexKnuckle[1]) - Local(pinkyKnuckle[1])).normalized;
                        if (along.sqrMagnitude < 0.5f)
                            along = Vector3.forward;
                        Vector3 tip = hilt + along * 0.95f;
                        blade.SetPosition(0, hilt);
                        blade.SetPosition(1, tip);
                        core.SetPosition(0, hilt);
                        core.SetPosition(1, tip);
                    });
                    break;
                }
            }
        }

        void Fire(Move move)
        {
            int side = LeadHand;
            Vector3 hand = Hand(side);
            switch (move.Fx)
            {
                case Fx.Laser:
                {
                    // A beam drawn here (the game's beam is sized for the room), held on the robot.
                    GameObject go = Holder("Laser");
                    LineRenderer beam = Line(go.transform, 0.12f, color * 1.5f, 2);
                    LineRenderer core = Line(go.transform, 0.04f, Color.white, 2);
                    FxSpin palm = Glow(go.transform, "FxGlow", 0.35f, color * 1.5f);
                    FxSpin splash = Glow(go.transform, "FxGlow", 0.5f, color * 1.3f, 0.3f);
                    float nextHit = 0f;
                    Add(go, move.Hold, (e, dt) =>
                    {
                        Vector3 from = Hand(side) + Vector3.forward * 0.08f;
                        float flicker = 1f + 0.25f * Mathf.Sin(e.Age * 50f);
                        beam.widthMultiplier = 0.12f * Scale * flicker;
                        beam.SetPosition(0, from);
                        beam.SetPosition(1, Target);
                        core.SetPosition(0, from);
                        core.SetPosition(1, Target);
                        palm.transform.localPosition = from;
                        splash.transform.localPosition = Target - Vector3.forward * 0.1f;
                        if (e.Age >= nextHit)
                        {
                            nextHit = e.Age + 0.35f;
                        }
                    });
                    break;
                }
                case Fx.Repulsor:
                    Ball(hand, 0.18f, color, 0.35f, 0.5f);
                    break;
                case Fx.AreaRepulsor:
                    Ball(hand, 0.22f, color, 0.35f, 1.6f);
                    break;
                case Fx.Comet:
                    Ball(hand, 0.18f, color, 0.4f, 0.6f, true);
                    break;
                case Fx.DragonFist:
                    Ball(hand, 0.4f, new Color(1f, 0.55f, 0.15f), 0.45f, 1.4f, true);
                    break;
                case Fx.Fireball:
                    Ball(hand, 0.22f, new Color(1f, 0.45f, 0.1f), 0.45f, 0.7f, true);
                    break;
                case Fx.SpiritBall:
                {
                    // It gathers between the hands for the hold, then flies.
                    GameObject go = Holder("Spirit Ball");
                    FxSpin outer = Glow(go.transform, "FxGlow", 0.1f, color);
                    FxSpin inner = Glow(go.transform, "FxGlow", 0.05f, Color.white);
                    Add(go, move.Hold, (e, dt) =>
                    {
                        float t = e.Age / e.Life;
                        go.transform.localPosition = BothHands + Vector3.forward * 0.1f;
                        outer.Size = Vector3.one * Mathf.Lerp(0.1f, 0.7f, t);
                        inner.Size = Vector3.one * Mathf.Lerp(0.05f, 0.3f, t);
                    }, () => Ball(BothHands + Vector3.forward * 0.1f, 0.4f, color, 0.5f, 1.5f, true));
                    break;
                }
                case Fx.Flame:
                {
                    GameObject go = Holder("Flamethrower");
                    float next = 0f;
                    Color flame = new Color(1f, 0.45f, 0.1f);
                    Add(go, move.Hold, (e, dt) =>
                    {
                        while (next <= e.Age)
                        {
                            next += 0.035f;
                            Vector3 from = Hand(LeadHand) + Vector3.forward * 0.08f;
                            Vector3 to = Target + UnityEngine.Random.insideUnitSphere * 0.35f;
                            GameObject puff = Holder("Flame");
                            FxSpin bit = Glow(puff.transform, "FxFlame", 0.12f, flame * 1.4f, 0f);
                            Add(puff, 0.5f, (p, d) =>
                            {
                                float k = p.Age / p.Life;
                                puff.transform.localPosition = Vector3.Lerp(from, to, k) + Vector3.up * (0.25f * k * k);
                                bit.Size = Vector3.one * Mathf.Lerp(0.12f, 0.55f, k);
                                SetColor(bit, Color.Lerp(flame * 1.6f, new Color(0.6f, 0.1f, 0.02f), k) * (1f - k * 0.8f));
                            });
                        }
                    });
                    break;
                }
                case Fx.Pillar:
                {
                    // A rune circle under the robot, then a column of fire.
                    GameObject go = Holder("Fire Pillar");
                    go.transform.localPosition = FloorTarget;
                    FxSpin circle = BallLook.Sprite("Circle", go.transform, "FxRing", 1.2f, new Color(1f, 0.4f, 0.1f));
                    circle.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    circle.Set(tumble: new Vector3(0f, 0f, 90f));
                    LineRenderer column = Line(go.transform, 0.9f, new Color(1f, 0.5f, 0.15f), 2);
                    column.SetPosition(0, Vector3.zero);
                    column.SetPosition(1, Vector3.zero);
                    float nextFlame = 0f;
                    Add(go, move.Hold + 0.2f, (e, dt) =>
                    {
                        float rise = Mathf.Clamp01((e.Age - (1.25f - move.FireAt)) / 0.2f);
                        column.SetPosition(1, Vector3.up * 2.6f * rise);
                        column.widthMultiplier = 0.9f * Scale * (0.8f + 0.2f * Mathf.Sin(e.Age * 40f));
                        if (rise > 0f && e.Age >= nextFlame)
                        {
                            nextFlame = e.Age + 0.05f;
                            GameObject puff = Holder("Pillar Flame");
                            Vector3 start = FloorTarget + new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), 0.08f, UnityEngine.Random.Range(-0.3f, 0.3f));
                            FxSpin bit = Glow(puff.transform, "FxFlame", 0.4f, new Color(1f, 0.5f, 0.1f) * 1.5f, 0f);
                            Add(puff, 0.6f, (p, d) =>
                            {
                                float k = p.Age / p.Life;
                                puff.transform.localPosition = start + Vector3.up * 2.6f * k;
                                SetColor(bit, new Color(1f, 0.5f, 0.1f) * (1.5f * (1f - k)));
                            });
                        }
                    });
                    break;
                }
                case Fx.Bolt:
                    for (int i = 0; i < 1; i++)
                    {
                        float delay = i * 0.25f;
                        GameObject go = Holder("Bolt");
                        LineRenderer streak = Line(go.transform, 0.06f, color * 1.6f, 2);
                        Vector3 from = hand + Vector3.forward * 0.1f;
                        Add(go, 0.2f + delay, (e, dt) =>
                        {
                            float t = Mathf.Clamp01((e.Age - delay) / 0.2f);
                            streak.enabled = e.Age >= delay;
                            Vector3 head = Vector3.Lerp(from, Target, t);
                            streak.SetPosition(0, head - Vector3.forward * 0.4f);
                            streak.SetPosition(1, head);
                        }, () =>
                        {
                            Burst(Target, 0.4f, color);
                        });
                    }
                    break;
                case Fx.Saber:
                    // The blade is held all along (Equip); the swings hit the robot as they pass.
                    for (int i = 0; i < 3; i++)
                    {
                        float at = 0.35f + i * 0.45f;
                        Add(Holder("Swing"), at + 0.01f, null, () => { });
                    }
                    break;
                case Fx.ForceThrow:
                {
                    // A rock on the floor lifts as the cupped hand rises, hangs in front of it, and flies at the throw.
                    GameObject go = Holder("Lifted Rock");
                    Transform rock = Prop("RockSmallA", go.transform);
                    rock.localScale *= 1.6f;
                    Glow(go.transform, "FxGlow", 0.5f, color * 0.8f);
                    Vector3 rest = new Vector3(0.5f, 0.1f, 0.9f);
                    Vector3 held = new Vector3(0.32f, 1.42f, 0.85f);
                    const float lift = 0.55f;
                    float throwAt = 1.25f - move.FireAt;
                    go.transform.localPosition = rest;
                    Add(go, throwAt, (e, dt) =>
                    {
                        float t = Mathf.SmoothStep(0f, 1f, e.Age / lift);
                        go.transform.localPosition = Vector3.Lerp(rest, held, t) + Vector3.up * (0.04f * Mathf.Sin(e.Age * 20f));
                        rock.localRotation = Quaternion.Euler(e.Age * 90f, e.Age * 60f, 0f);
                    });
                    Add(Holder("Throw"), throwAt, null, () =>
                    {
                        GameObject thrown = Holder("Thrown Rock");
                        Transform flying = Prop("RockSmallA", thrown.transform);
                        flying.localScale *= 1.6f;
                        Fly(thrown, held, Target, 0.35f, () => Burst(Target, 0.9f, new Color(0.8f, 0.7f, 0.6f)));
                    });
                    break;
                }
                case Fx.Crossbow:
                {
                    GameObject go = Holder("Crossbow Bolt");
                    Prop("Bolt", go.transform);
                    Glow(go.transform, "FxGlow", 0.2f, color);
                    Fly(go, Hand(1) + Vector3.forward * 0.3f, Target, 0.25f, () =>
                    {
                        Burst(Target, 0.4f, color);
                    });
                    break;
                }
                case Fx.Bow:
                {
                    GameObject go = Holder("Arrow");
                    Prop("Arrow", go.transform);
                    Glow(go.transform, "FxGlow", 0.25f, color);
                    Fly(go, Hand(0) + Vector3.forward * 0.2f, Target, 0.25f, () =>
                    {
                        Burst(Target, 0.5f, color);
                    });
                    break;
                }
                case Fx.Smoke:
                {
                    GameObject go = Holder("Smoke Bomb");
                    Glow(go.transform, "FxGlow", 0.15f, new Color(0.5f, 0.5f, 0.55f));
                    Vector3 landing = new Vector3(0f, 0.1f, Reach - 0.4f);
                    Fly(go, hand, landing, 0.5f, () =>
                    {
                        for (int i = 0; i < 9; i++)
                        {
                            GameObject puff = Holder("Smoke");
                            Vector3 drift = new Vector3(UnityEngine.Random.Range(-0.6f, 0.6f), UnityEngine.Random.Range(0.1f, 1.2f), UnityEngine.Random.Range(-0.4f, 0.4f));
                            FxSpin bit = Glow(puff.transform, "FxGlow", 0.3f, new Color(0.55f, 0.55f, 0.6f) * 0.8f, 0f);
                            Add(puff, 1.3f, (p, d) =>
                            {
                                float k = p.Age / p.Life;
                                puff.transform.localPosition = landing + drift * Mathf.Sqrt(k);
                                bit.Size = Vector3.one * Mathf.Lerp(0.3f, 1.1f, k);
                                SetColor(bit, new Color(0.55f, 0.55f, 0.6f) * (0.9f * (1f - k)));
                            });
                        }
                    }, 0.6f);
                    break;
                }
                case Fx.Web:
                case Fx.BlackWeb:
                {
                    Color strand = move.Fx == Fx.BlackWeb ? new Color(0.12f, 0.12f, 0.14f) : new Color(0.92f, 0.92f, 0.95f);
                    GameObject go = Holder("Web");
                    LineRenderer line = Line(go.transform, 0.03f, strand, 2);
                    // The web itself (as in the game): flies out spinning and opening, then sticks on the robot.
                    Transform web = WebStrand.Web(go.transform, strand);
                    Renderer[] silk = web.GetComponentsInChildren<Renderer>();
                    bool stuck = false;
                    Add(go, 1.1f, (e, dt) =>
                    {
                        float t = Mathf.Clamp01(e.Age / 0.25f);
                        Vector3 from = Hand(side) + Vector3.forward * 0.05f;
                        Vector3 end = Vector3.Lerp(from, Target - Vector3.forward * 0.3f, t);
                        line.SetPosition(0, from);
                        line.SetPosition(1, end);
                        float size = t < 1f ? Mathf.Lerp(0.15f, 0.7f, t) : Mathf.Lerp(0.7f, 0.95f, Mathf.Clamp01((e.Age - 0.25f) * 10f));
                        web.localPosition = end;
                        web.localRotation = Quaternion.Euler(0f, 180f, t < 1f ? e.Age * 500f : 125f);
                        web.localScale = new Vector3(size, size, size * 0.25f);
                        WebStrand.Tint(silk, strand, Mathf.Clamp01((e.Life - e.Age) / 0.3f));
                        if (t >= 1f && !stuck)
                        {
                            stuck = true;
                        }
                    });
                    break;
                }
                case Fx.WebFling:
                {
                    // A web onto a rock down to the left; the arm swings it up overhead and slams it down in front.
                    GameObject go = Holder("Web Fling");
                    LineRenderer line = Line(go.transform, 0.03f, new Color(0.92f, 0.92f, 0.95f), 2);
                    Transform rock = Prop("RockSmallB", go.transform);
                    rock.localScale *= 1.5f;
                    Vector3 rest = new Vector3(-1.1f, 0.15f, 1.1f);
                    Vector3 overhead = new Vector3(0.3f, 2.3f, 0.5f);
                    bool hit = false;
                    Add(go, move.Hold, (e, dt) =>
                    {
                        float attach = Mathf.Clamp01(e.Age / 0.2f);
                        float up = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((e.Age - 0.3f) / 0.35f));
                        float down = Mathf.Clamp01((e.Age - 0.65f) / 0.25f);
                        Vector3 at = down > 0f ? Vector3.Lerp(overhead, FloorTarget + Vector3.up * 0.15f, down * down)
                            : Vector3.Lerp(rest, overhead, up) + Vector3.up * (Mathf.Sin(up * Mathf.PI) * 0.4f);
                        rock.localPosition = at;
                        rock.localRotation = Quaternion.Euler(e.Age * 200f, e.Age * 90f, 0f);
                        line.SetPosition(0, Hand(1));
                        line.SetPosition(1, Vector3.Lerp(Hand(1), at, attach));
                        line.enabled = down < 0.9f;
                        if (down >= 1f && !hit)
                        {
                            hit = true;
                            Burst(FloorTarget + Vector3.up * 0.15f, 1f, new Color(0.8f, 0.75f, 0.7f));
                        }
                    });
                    break;
                }
                case Fx.Gust:
                    for (int i = 0; i < 6; i++)
                    {
                        GameObject go = Holder("Gust");
                        LineRenderer streak = Line(go.transform, 0.05f, new Color(0.75f, 0.95f, 1f) * 0.9f, 2);
                        Vector3 offset = new Vector3(UnityEngine.Random.Range(-0.4f, 0.4f), UnityEngine.Random.Range(-0.3f, 0.4f), 0f);
                        float delay = i * 0.04f;
                        Vector3 from = hand + Vector3.forward * 0.1f;
                        Add(go, 0.45f + delay, (e, dt) =>
                        {
                            float t = Mathf.Clamp01((e.Age - delay) / 0.45f);
                            Vector3 head = Vector3.Lerp(from, Target + offset * 1.5f, t) + offset * t;
                            streak.SetPosition(0, Vector3.Lerp(from, head, 0.55f));
                            streak.SetPosition(1, head);
                            streak.widthMultiplier = 0.05f * Scale * (1f - t * 0.6f);
                        });
                    }
                    Add(Holder("Gust Hit"), 0.4f, null, () => { });
                    break;
                case Fx.Lightning:
                {
                    GameObject go = Holder("Lightning");
                    Color bolt = new Color(0.7f, 0.9f, 1f) * 1.6f;
                    LineRenderer line = Line(go.transform, 0.045f, bolt, 12);
                    FxSpin spark = Glow(go.transform, "FxGlow", 0.6f, bolt);
                    float reshape = 0f;
                    Add(go, move.Hold, (e, dt) =>
                    {
                        Vector3 from = BothHands + Vector3.forward * 0.12f;
                        spark.transform.localPosition = Target;
                        if (e.Age < reshape)
                            return;
                        reshape = e.Age + 0.05f;
                        for (int i = 0; i < 12; i++)
                        {
                            float k = i / 11f;
                            Vector3 jitter = i == 0 || i == 11 ? Vector3.zero : UnityEngine.Random.insideUnitSphere * 0.18f;
                            line.SetPosition(i, Vector3.Lerp(from, Target, k) + jitter);
                        }
                    });
                    break;
                }
                case Fx.Tornado:
                {
                    // The same see-through white funnel as in the game.
                    GameObject go = Holder("Tornado");
                    go.transform.localPosition = FloorTarget;
                    Transform funnel = Prop("Tornado", go.transform);
                    Renderer[] parts = funnel.GetComponentsInChildren<Renderer>();
                    WebStrand.Tint(parts, Color.white, 0.3f);
                    Add(go, move.Hold, (e, dt) =>
                    {
                        float grow = Mathf.Clamp01(e.Age / 0.4f) * Mathf.Clamp01((e.Life - e.Age) / 0.3f);
                        funnel.localScale = new Vector3(1.2f, 1.9f, 1.2f) * Mathf.Max(0.01f, grow);
                        funnel.localRotation = Quaternion.Euler(Mathf.Sin(e.Age * 2.4f) * 6f, e.Age * 540f, 0f);
                    });
                    break;
                }
                case Fx.Rock:
                {
                    GameObject go = Holder("Rock Orb");
                    Prop("RockSmallC", go.transform).localScale *= 1.2f;
                    Glow(go.transform, "FxGlow", 0.35f, new Color(1f, 0.5f, 0.15f) * 0.8f);
                    Fly(go, hand, Target, 0.4f, () =>
                    {
                        Burst(Target, 0.7f, new Color(1f, 0.55f, 0.2f));
                    });
                    break;
                }
                case Fx.Spikes:
                    for (int i = 0; i < 3; i++)
                    {
                        GameObject go = Holder("Spike");
                        int index = i;
                        Transform spike = Prop(i == 0 ? "SpikeA" : i == 1 ? "SpikeB" : "SpikeC", go.transform);
                        float delay = i * 0.1f;
                        float distance = 1.1f + i * 0.55f;
                        spike.localScale *= 0.55f;
                        Vector3 baseScale = spike.localScale;
                        go.transform.localPosition = new Vector3(0f, 0f, distance);
                        bool hit = false;
                        Add(go, 1.2f + delay, (e, dt) =>
                        {
                            float t = Mathf.Clamp01((e.Age - delay) / 0.15f) * Mathf.Clamp01((e.Life - e.Age) / 0.3f);
                            spike.localScale = new Vector3(baseScale.x, baseScale.y * t, baseScale.z);
                            if (index == 2 && t >= 1f && !hit)
                            {
                                hit = true;
                            }
                        });
                    }
                    break;
                case Fx.StickySnap:
                {
                    GameObject go = Holder("Sticky Rock");
                    Prop("RockSmallA", go.transform);
                    FxSpin glow = Glow(go.transform, "FxGlow", 0.3f, new Color(1f, 0.55f, 0.15f));
                    Fly(go, hand, FloorTarget + Vector3.up * 0.1f, 0.4f, () =>
                    {
                        GameObject stuck = Holder("Stuck");
                        Prop("RockSmallA", stuck.transform);
                        FxSpin pulse = Glow(stuck.transform, "FxGlow", 0.3f, new Color(1f, 0.55f, 0.15f));
                        Add(stuck, move.Hold, (e, dt) =>
                        {
                            stuck.transform.localPosition = FloorTarget + Vector3.up * 0.1f;
                            pulse.Size = Vector3.one * (0.3f + 0.25f * Mathf.Abs(Mathf.Sin(e.Age * (6f + e.Age * 12f))));
                        }, () =>
                        {
                            // The snap: a flash at the fingers, and the rock goes off.
                            Burst(Hand(LeadHand), 0.25f, Color.white);
                            Burst(FloorTarget + Vector3.up * 0.2f, 1.5f, new Color(1f, 0.5f, 0.15f));
                        });
                    });
                    break;
                }
            }
        }

        /// <summary>The glow behind the figure at levels with an aura (and Technoman's outline at 3).</summary>
        void ShowAura()
        {
            CharacterDef def = CharacterDef.Get(character);
            bool on = def.AuraStyle[level] > 0 || (character == CharacterId.Technoman && level >= 2) || (character == CharacterId.Elementalist && level > 0);
            aura.gameObject.SetActive(on);
            if (on)
                SetColor(aura, (character == CharacterId.KiFighter && level == 2 ? new Color(0.25f, 0.6f, 1f) : color) * 0.45f);
        }

        void ClearEffects()
        {
            foreach (Live effect in live)
                if (effect.Go != null)
                    UnityEngine.Object.Destroy(effect.Go);
            live.Clear();
            charge.gameObject.SetActive(false);
        }
    }
}
