using System;
using System.Collections.Generic;
using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// The character select screen's little stage: a mannequin (Quaternius, CC0) in the chosen colour acting out
    /// each level's move against a small robot, with a small version of the effect. It goes level 1, 2, 3 and
    /// round again; the screen's arrows pick a level. Everything is built in "human" metres under a stage that is
    /// scaled down, so a 1.8 m mannequin stands about 12 cm tall in front of the menu.
    /// </summary>
    public sealed class CharacterShowcase
    {
        public const float FigureHeight = 0.12f;
        const float Human = 1.8f;
        const float Scale = FigureHeight / Human;
        const float Reach = 2.3f;
        const float Chest = 1.25f;
        const float Pause = 0.7f;

        enum Fx
        {
            Laser, Repulsor, AreaRepulsor, Comet, SpiritBall, DragonFist, Fireball, Flame, Pillar, Bolt, Saber, ForceThrow,
            Crossbow, Bow, Smoke, Web, WebFling, BlackWeb, Gust, Lightning, Tornado, Rock, Spikes, StickySnap,
        }

        sealed class Move
        {
            public readonly string Name;
            public readonly string Clip;
            public readonly Fx Fx;
            public readonly float FireAt;
            public readonly float Hold;
            public readonly float Speed;

            public Move(string name, string clip, Fx fx, float fireAt, float hold = 0f, float speed = 1f)
            {
                Name = name;
                Clip = clip;
                Fx = fx;
                FireAt = fireAt;
                Hold = hold;
                Speed = speed;
            }
        }

        /// <summary>Each character's move at levels 1, 2 and 3 (see CharacterDef.Controls).</summary>
        static readonly Dictionary<CharacterId, Move[]> Moves = new Dictionary<CharacterId, Move[]>
        {
            [CharacterId.Technoman] = new[]
            {
                new Move("Hand laser", "Spell_Simple_Shoot", Fx.Laser, 0.3f, 1.1f),
                new Move("Repulsor blast", "Punch_Cross", Fx.Repulsor, 0.25f),
                new Move("Area repulsor", "Punch_Cross", Fx.AreaRepulsor, 0.25f),
            },
            [CharacterId.KiFighter] = new[]
            {
                new Move("Comet blast", "Punch_Jab", Fx.Comet, 0.18f),
                new Move("Giant energy ball", "Push_Loop", Fx.SpiritBall, 1.1f, 0.4f),
                new Move("Dragon fist", "Punch_Cross", Fx.DragonFist, 0.22f, 0f, 0.9f),
            },
            [CharacterId.Mage] = new[]
            {
                new Move("Fireball", "Spell_Simple_Shoot", Fx.Fireball, 0.3f),
                new Move("Flamethrower", "Spell_Simple_Shoot", Fx.Flame, 0.3f, 1.4f),
                new Move("Fire pillar", "Push_Loop", Fx.Pillar, 0.6f, 1.2f),
            },
            [CharacterId.Saber] = new[]
            {
                new Move("Blaster bolt", "Pistol_Shoot", Fx.Bolt, 0.1f),
                new Move("Laser sword", "Sword_Attack", Fx.Saber, 0f),
                new Move("Force lift and throw", "Spell_Simple_Shoot", Fx.ForceThrow, 0.75f),
            },
            [CharacterId.Archer] = new[]
            {
                new Move("Crossbow", "Pistol_Shoot", Fx.Crossbow, 0.1f),
                new Move("Longbow", "Pistol_Aim_Neutral", Fx.Bow, 1.3f),
                new Move("Smoke bomb", "Spell_Simple_Shoot", Fx.Smoke, 0.3f),
            },
            [CharacterId.Spider] = new[]
            {
                new Move("Web shot", "Spell_Simple_Shoot", Fx.Web, 0.3f),
                new Move("Web and fling", "Sword_Attack", Fx.WebFling, 0.05f, 1.1f),
                new Move("Black webs", "Spell_Simple_Shoot", Fx.BlackWeb, 0.3f),
            },
            [CharacterId.Elementalist] = new[]
            {
                new Move("Gust of wind", "Spell_Simple_Shoot", Fx.Gust, 0.3f),
                new Move("Chain lightning", "Push_Loop", Fx.Lightning, 0.3f, 1.4f),
                new Move("Tornado", "Push_Loop", Fx.Tornado, 0.5f, 1.6f),
            },
            [CharacterId.Earth] = new[]
            {
                new Move("Rock orb", "Punch_Jab", Fx.Rock, 0.18f),
                new Move("Rock spikes", "Punch_Cross", Fx.Spikes, 0.25f),
                new Move("Sticky rocks, snap", "Punch_Jab", Fx.StickySnap, 0.18f, 1f),
            },
        };

        /// <summary>The move's name at a level (1-3), for the screen.</summary>
        public static string MoveName(CharacterId id, int level) => Moves[id][Mathf.Clamp(level, 1, 3) - 1].Name;

        sealed class Live
        {
            public GameObject Go;
            public float Age;
            public float Life;
            public Action<Live, float> Step;
            public Action End;
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
        readonly List<Live> live = new List<Live>();
        readonly List<Live> running = new List<Live>();
        readonly EnemyLook dummy;
        readonly FxSpin aura;
        float dummyFlash;
        Vector3 dummyPush;

        CharacterId character;
        Color color = Color.white;
        int level;
        float time;
        bool fired;
        bool backToIdle;

        public int Level => level + 1;

        /// <summary>For tests: what the animation is doing and where the right hand is.</summary>
        public string DebugState
        {
            get
            {
                if (animation == null)
                    return "no Animation";
                var text = new System.Text.StringBuilder($"playing {animation.isPlaying}, enabled {animation.enabled}, hand {Hand(1):F2}:");
                foreach (AnimationState state in animation)
                    if (state.enabled || state.weight > 0f)
                        text.Append($" {state.name} t{state.time:0.00} w{state.weight:0.00}");
                return text.ToString();
            }
        }
        public CharacterId Character => character;
        public bool Shown => stage.gameObject.activeSelf;

        public CharacterShowcase(RepulsorGame game, Transform parent)
        {
            this.game = game;
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

            // The robot it practises on, facing it.
            dummy = EnemyLook.Create(EnemySpec.Of(EnemyKind.Robot).Model, stage);
            if (dummy != null)
            {
                dummy.transform.localPosition = new Vector3(0f, 0f, Reach);
                dummy.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                // Its eye glows are sized for the room; bring them down to the stage.
                foreach (FxSpin eye in dummy.GetComponentsInChildren<FxSpin>())
                    eye.Size *= Scale;
            }
            aura = BallLook.Sprite("Aura", stage, "FxGlow", 1f, Color.white).Set(billboard: true, flicker: 0.12f);
            aura.Place(new Vector3(0f, 0.95f, -0.15f), new Vector3(1.6f, 2.4f, 1f));
            stage.gameObject.SetActive(false);
        }

        /// <summary>Shows a character from level 1 (or keeps going if it's already the one shown).</summary>
        public void Show(CharacterId id, Color tint)
        {
            bool changed = id != character || !Shown;
            character = id;
            color = tint;
            TintFigure();
            if (!Shown)
                stage.gameObject.SetActive(true);
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
        }

        /// <summary>Jumps to a level (1-3), from the screen's arrows.</summary>
        public void SetLevel(int newLevel)
        {
            level = (newLevel - 1 + 3) % 3;
            StartMove();
        }

        /// <summary>Stands the stage with its feet at <paramref name="feet"/>, the moves heading along <paramref name="facing"/>.</summary>
        public void Place(Vector3 feet, Quaternion facing) => stage.SetPositionAndRotation(feet, facing);

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
            backToIdle = false;
            Move move = Current;
            if (animation != null && animation[move.Clip] != null)
            {
                AnimationState state = animation[move.Clip];
                state.speed = move.Speed;
                state.wrapMode = move.Clip.EndsWith("_Loop") ? WrapMode.Loop : Held(move) ? WrapMode.ClampForever : WrapMode.Once;
                state.time = 0f;
                animation.CrossFade(move.Clip, 0.12f);
                if (state.wrapMode == WrapMode.Once && animation["Idle_Loop"] != null)
                {
                    animation["Idle_Loop"].wrapMode = WrapMode.Loop;
                    animation.CrossFadeQueued("Idle_Loop", 0.2f);
                }
            }
            Equip(move);
            ShowAura();
        }

        /// <summary>Moves that hold a pose (loops, and the aim that's a single pose) until they've fired and held.</summary>
        static bool Held(Move move) => move.Clip.EndsWith("_Loop") || move.Hold > 0f || move.Clip == "Pistol_Aim_Neutral";

        float Duration(Move move)
        {
            float clip = animation != null && animation[move.Clip] != null && !Held(move)
                ? animation[move.Clip].length / move.Speed : 0f;
            return Mathf.Max(clip, move.FireAt + move.Hold + 0.6f) + Pause;
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
            // Holding moves (loops) end when their hold does.
            if (!backToIdle && Held(move) && time >= move.FireAt + move.Hold + 0.3f && animation != null && animation["Idle_Loop"] != null)
            {
                backToIdle = true;
                animation["Idle_Loop"].wrapMode = WrapMode.Loop;
                animation.CrossFade("Idle_Loop", 0.25f);
            }
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
            dummyFlash = Mathf.Max(0f, dummyFlash - dt);
            dummyPush = Vector3.Lerp(dummyPush, Vector3.zero, 1f - Mathf.Exp(-4f * dt));
            if (dummy != null)
            {
                dummy.transform.localPosition = new Vector3(0f, 0f, Reach) + dummyPush;
                dummy.Tick(dt, false, false, dummyFlash);
            }
            if (time >= Duration(move))
            {
                level = (level + 1) % 3;
                StartMove();
            }
        }

        // ---------------------------------------------------------------- where things are, in the stage's space

        Vector3 Local(Transform bone) => bone != null ? stage.InverseTransformPoint(bone.position) : new Vector3(0f, Chest, 0.3f);

        /// <summary>The hand further forward (the one doing the move).</summary>
        int LeadHand => Local(hands[0]).z > Local(hands[1]).z ? 0 : 1;

        Vector3 Hand(int side) => Local(hands[side]);

        Vector3 BothHands => (Hand(0) + Hand(1)) * 0.5f;

        static Vector3 Target => new Vector3(0f, 0.75f, Reach - 0.1f);

        void HitDummy(float push = 0.15f)
        {
            dummyFlash = 0.15f;
            dummyPush = new Vector3(0f, 0f, push);
            dummy?.PlayOnce("hit");
        }

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

        /// <summary>A glowing ball from a hand to the robot.</summary>
        void Ball(Vector3 from, float size, Color tint, float seconds, float burst, bool trail = false)
        {
            GameObject go = Holder("Ball");
            Glow(go.transform, "FxGlow", size * 1.8f, tint);
            Glow(go.transform, "FxGlow", size * 0.7f, Color.white * 1.3f);
            Fly(go, from, Target, seconds, () =>
            {
                Burst(Target, burst, tint);
                HitDummy();
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
                        go.transform.localPosition = Hand(1) + Vector3.forward * 0.05f;
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
                    Add(go, move.FireAt + 0.05f, (e, dt) =>
                    {
                        Vector3 grip = Hand(0);
                        Vector3 draw = Hand(1);
                        bow.localPosition = grip;
                        bow.localRotation = Quaternion.identity;
                        Vector3 top = grip + new Vector3(0f, 0.56f, -0.29f);
                        Vector3 bottom = grip + new Vector3(0f, -0.56f, -0.29f);
                        float pull = Mathf.Clamp01(e.Age / Mathf.Max(0.1f, move.FireAt));
                        Vector3 nock = Vector3.Lerp(grip + new Vector3(0f, 0f, -0.29f), new Vector3(grip.x, grip.y, Mathf.Min(draw.z, grip.z - 0.3f)), pull);
                        cord.SetPosition(0, top);
                        cord.SetPosition(1, nock);
                        cord.SetPosition(2, bottom);
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
                            HitDummy(0.05f);
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
                        if (Mathf.Repeat(e.Age, 0.4f) < dt)
                            HitDummy(0.04f);
                    });
                    break;
                }
                case Fx.Pillar:
                {
                    // A rune circle under the robot, then a column of fire.
                    GameObject go = Holder("Fire Pillar");
                    go.transform.localPosition = new Vector3(0f, 0.02f, Reach);
                    FxSpin circle = BallLook.Sprite("Circle", go.transform, "FxRing", 1.2f, new Color(1f, 0.4f, 0.1f));
                    circle.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    circle.Set(tumble: new Vector3(0f, 0f, 90f));
                    LineRenderer column = Line(go.transform, 0.9f, new Color(1f, 0.5f, 0.15f), 2);
                    column.SetPosition(0, Vector3.zero);
                    column.SetPosition(1, Vector3.zero);
                    float nextFlame = 0f;
                    Add(go, move.Hold + 0.2f, (e, dt) =>
                    {
                        float rise = Mathf.Clamp01((e.Age - 0.35f) / 0.2f);
                        column.SetPosition(1, Vector3.up * 2.6f * rise);
                        column.widthMultiplier = 0.9f * Scale * (0.8f + 0.2f * Mathf.Sin(e.Age * 40f));
                        if (rise > 0f && e.Age >= nextFlame)
                        {
                            nextFlame = e.Age + 0.05f;
                            GameObject puff = Holder("Pillar Flame");
                            Vector3 start = new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), 0.1f, Reach + UnityEngine.Random.Range(-0.3f, 0.3f));
                            FxSpin bit = Glow(puff.transform, "FxFlame", 0.4f, new Color(1f, 0.5f, 0.1f) * 1.5f, 0f);
                            Add(puff, 0.6f, (p, d) =>
                            {
                                float k = p.Age / p.Life;
                                puff.transform.localPosition = start + Vector3.up * 2.6f * k;
                                SetColor(bit, new Color(1f, 0.5f, 0.1f) * (1.5f * (1f - k)));
                            });
                        }
                        if (rise >= 1f && Mathf.Repeat(e.Age, 0.4f) < dt)
                            HitDummy(0f);
                    });
                    break;
                }
                case Fx.Bolt:
                    for (int i = 0; i < 2; i++)
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
                            HitDummy(0.08f);
                        });
                    }
                    break;
                case Fx.Saber:
                    // The blade is held all along (Equip); the swings hit the robot as they pass.
                    for (int i = 0; i < 3; i++)
                    {
                        float at = 0.35f + i * 0.45f;
                        Add(Holder("Swing"), at + 0.01f, null, () => HitDummy(0.1f));
                    }
                    break;
                case Fx.ForceThrow:
                {
                    // A rock lifts off the floor as the arm winds up, then flies at the robot.
                    GameObject go = Holder("Lifted Rock");
                    Transform rock = Prop("RockSmallA", go.transform);
                    rock.localScale *= 1.6f;
                    Glow(go.transform, "FxGlow", 0.5f, color * 0.8f);
                    Vector3 rest = new Vector3(0.5f, 0.1f, 0.9f);
                    Vector3 held = new Vector3(0.4f, 1.6f, 0.7f);
                    go.transform.localPosition = rest;
                    float lift = move.FireAt;
                    Add(go, lift, (e, dt) =>
                    {
                        float t = Mathf.SmoothStep(0f, 1f, e.Age / lift);
                        go.transform.localPosition = Vector3.Lerp(rest, held, t) + Vector3.up * (0.04f * Mathf.Sin(e.Age * 20f));
                        rock.localRotation = Quaternion.Euler(e.Age * 90f, e.Age * 60f, 0f);
                    });
                    // The throw itself: a new rock from where the lifted one was.
                    Add(Holder("Throw"), lift, null, () =>
                    {
                        GameObject thrown = Holder("Thrown Rock");
                        Transform flying = Prop("RockSmallA", thrown.transform);
                        flying.localScale *= 1.6f;
                        Fly(thrown, held, Target, 0.35f, () =>
                        {
                            Burst(Target, 0.9f, new Color(0.8f, 0.7f, 0.6f));
                            HitDummy(0.3f);
                        });
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
                        HitDummy(0.08f);
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
                        HitDummy(0.15f);
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
                    FxSpin splat = BallLook.Sprite("Splat", go.transform, "FxStar", 0.01f, strand * 1.3f).Set(billboard: true);
                    bool stuck = false;
                    Add(go, 0.9f, (e, dt) =>
                    {
                        float t = Mathf.Clamp01(e.Age / 0.15f);
                        Vector3 from = Hand(side) + Vector3.forward * 0.05f;
                        Vector3 end = Vector3.Lerp(from, Target, t);
                        line.SetPosition(0, from);
                        line.SetPosition(1, end);
                        splat.transform.localPosition = end;
                        splat.Size = Vector3.one * (t >= 1f ? 0.6f : 0.01f);
                        if (t >= 1f && !stuck)
                        {
                            stuck = true;
                            HitDummy(0.1f);
                        }
                    });
                    break;
                }
                case Fx.WebFling:
                {
                    // A web onto a rock, and the swing flings the rock round into the robot.
                    GameObject go = Holder("Web Fling");
                    LineRenderer line = Line(go.transform, 0.03f, new Color(0.92f, 0.92f, 0.95f), 2);
                    Transform rock = Prop("RockSmallB", go.transform);
                    rock.localScale *= 1.5f;
                    Vector3 rest = new Vector3(-1.2f, 0.15f, 1.2f);
                    Vector3 swing = new Vector3(-1f, 1.5f, 1.8f);
                    bool hit = false;
                    Add(go, move.Hold, (e, dt) =>
                    {
                        float t = e.Age / e.Life;
                        Vector3 at = t < 0.3f ? rest
                            : t < 0.75f ? Vector3.Lerp(Vector3.Lerp(rest, swing, (t - 0.3f) / 0.45f), swing, 0.5f)
                            : Vector3.Lerp(swing, Target, (t - 0.75f) / 0.25f);
                        rock.localPosition = at;
                        rock.localRotation = Quaternion.Euler(e.Age * 200f, e.Age * 90f, 0f);
                        line.SetPosition(0, Hand(1));
                        line.SetPosition(1, at);
                        line.enabled = t < 0.85f;
                        if (t >= 0.97f && !hit)
                        {
                            hit = true;
                            Burst(Target, 0.9f, new Color(0.8f, 0.75f, 0.7f));
                            HitDummy(0.35f);
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
                    Add(Holder("Gust Hit"), 0.4f, null, () => HitDummy(0.6f));
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
                        if (Mathf.Repeat(e.Age, 0.3f) < 0.05f)
                            HitDummy(0.05f);
                    });
                    break;
                }
                case Fx.Tornado:
                {
                    GameObject go = Holder("Tornado");
                    go.transform.localPosition = new Vector3(0f, 0f, Reach);
                    var rings = new FxSpin[6];
                    for (int i = 0; i < rings.Length; i++)
                    {
                        rings[i] = BallLook.Sprite("Wind", go.transform, "FxRing", 0.1f, new Color(0.8f, 0.95f, 1f) * 0.9f);
                        rings[i].transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                        rings[i].Set(tumble: new Vector3(0f, 0f, 400f + i * 60f));
                    }
                    Add(go, move.Hold, (e, dt) =>
                    {
                        float grow = Mathf.Clamp01(e.Age / 0.4f) * Mathf.Clamp01((e.Life - e.Age) / 0.3f);
                        for (int i = 0; i < rings.Length; i++)
                        {
                            float height = 0.1f + i * 0.28f;
                            rings[i].transform.localPosition = new Vector3(0.08f * Mathf.Sin(e.Age * 6f + i), height, 0.08f * Mathf.Cos(e.Age * 6f + i));
                            rings[i].Size = Vector3.one * (0.25f + i * 0.13f) * grow;
                        }
                        dummyPush = new Vector3(0.1f * Mathf.Sin(e.Age * 9f), 0f, -0.2f) * grow;
                        if (Mathf.Repeat(e.Age, 0.45f) < dt)
                            dummyFlash = 0.1f;
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
                        HitDummy(0.2f);
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
                                HitDummy(0.3f);
                            }
                        });
                    }
                    break;
                case Fx.StickySnap:
                {
                    GameObject go = Holder("Sticky Rock");
                    Prop("RockSmallA", go.transform);
                    FxSpin glow = Glow(go.transform, "FxGlow", 0.3f, new Color(1f, 0.55f, 0.15f));
                    Fly(go, hand, Target + new Vector3(0f, 0f, -0.25f), 0.35f, () =>
                    {
                        HitDummy(0.05f);
                        GameObject stuck = Holder("Stuck");
                        Prop("RockSmallA", stuck.transform);
                        FxSpin pulse = Glow(stuck.transform, "FxGlow", 0.3f, new Color(1f, 0.55f, 0.15f));
                        Add(stuck, move.Hold, (e, dt) =>
                        {
                            stuck.transform.localPosition = Target + new Vector3(0f, 0f, -0.25f) + dummyPush;
                            pulse.Size = Vector3.one * (0.3f + 0.25f * Mathf.Abs(Mathf.Sin(e.Age * (6f + e.Age * 12f))));
                        }, () =>
                        {
                            // The snap: a flash at the fingers, and the rock goes off.
                            Burst(Hand(LeadHand), 0.25f, Color.white);
                            Burst(Target, 1.5f, new Color(1f, 0.5f, 0.15f));
                            HitDummy(0.4f);
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
            bool on = def.AuraStyle[level] > 0 || GlowTimer.PermanentFor(character, level + 1) || (character == CharacterId.Elementalist && level > 0);
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
            dummyPush = Vector3.zero;
        }
    }
}
