using System.Collections.Generic;
using UnityEngine;

namespace Repulsor
{
    /// <summary>Puts one material on every slot of every renderer under a model (rocks have several).</summary>
    public static class Paint
    {
        public static void All(Component root, Material material)
        {
            if (material == null)
                return;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var slots = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < slots.Length; i++)
                    slots[i] = material;
                renderer.sharedMaterials = slots;
            }
        }
    }

    /// <summary>
    /// The Elementalist's gust: a blade of wind (three pale crescents bowed forward, turning about the line of
    /// flight, with streaks trailing) that flies straight, widening, hits each enemy once and shoves it back.
    /// </summary>
    public sealed class WindGust : MonoBehaviour
    {
        const float Speed = 5.5f;
        const float Range = 4.5f;
        public const float Damage = 6f;
        const float Shove = 3.2f;

        RepulsorGame game;
        Vector3 direction;
        float travelled;
        float wall = float.MaxValue;
        readonly HashSet<IHittable> hit = new HashSet<IHittable>();

        public static void Launch(RepulsorGame game, Vector3 from, Vector3 direction, float damage, Transform parent)
        {
            var go = new GameObject("Wind Gust");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(direction));
            var gust = go.AddComponent<WindGust>();
            gust.game = game;
            gust.direction = direction.normalized;
            gust.damage = damage;
            if (game.WallAhead(from, gust.direction, out float distance))
                gust.wall = distance;
            Color pale = new Color(0.85f, 0.97f, 1f);
            BallLook.Sprite("Core", go.transform, "FxGlow", 0.16f, pale * 0.5f).Set(billboard: true, pulse: 0.15f);
            // Crescents: arcs bowed toward where it's going, one flat and two tilted, each a different size.
            for (int i = 0; i < 3; i++)
            {
                LineRenderer arc = LineFade.Make("Crescent " + i, go.transform, new Color(pale.r, pale.g, pale.b, i == 0 ? 0.9f : 0.6f),
                    i == 0 ? 0.07f : 0.05f, 16, 0f);
                arc.useWorldSpace = false;
                arc.transform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 0f : i == 1 ? 62f : -62f);
                arc.transform.localPosition = Vector3.back * (i * 0.07f);
                float radius = 0.2f + i * 0.035f;
                for (int k = 0; k < 16; k++)
                {
                    float a = Mathf.Lerp(-75f, 75f, k / 15f) * Mathf.Deg2Rad;
                    arc.SetPosition(k, new Vector3(Mathf.Sin(a) * radius, 0f, (Mathf.Cos(a) - 0.55f) * radius));
                }
                gust.crescents.Add(arc.transform);
            }
            BallLook.Particles("Streaks", go.transform, "FxGlow", pale, new Color(0.7f, 0.9f, 1f, 0f), 0.05f, 0.35f, 60f, 0.3f, 0.5f);
            game.PlayAt(from, game.WhooshSound);
        }

        float damage;
        readonly List<Transform> crescents = new List<Transform>();

        void Update()
        {
            float dt = Time.deltaTime;
            Vector3 from = transform.position;
            Vector3 to = from + direction * Speed * dt;
            transform.position = to;
            travelled += Speed * dt;
            // The crescents turn about the line of flight, each at its own rate.
            for (int i = 0; i < crescents.Count; i++)
                crescents[i].Rotate(0f, 0f, (i == 0 ? 220f : i == 1 ? -160f : 300f) * dt, Space.Self);
            // Widens as it goes.
            float radius = Mathf.Lerp(0.2f, 0.45f, travelled / Range);
            transform.localScale = Vector3.one * (radius / 0.2f);
            var all = Hittables.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (i >= all.Count)
                    continue;
                IHittable target = all[i];
                if (!target.Alive || target.Team != Team.Enemies || hit.Contains(target) || !HitTest.Segment(target, from, to, radius))
                    continue;
                hit.Add(target);
                if (target is Enemy enemy)
                    enemy.Push(direction * Shove);
                game.Strike(target, damage, from, "Gust");
            }
            if (travelled > Range || travelled > wall)
                Destroy(gameObject);
        }
    }

    /// <summary>
    /// Chain lightning from one hand: jagged bolts to the nearest enemy along the arm, then on to up to two more
    /// near it. With nothing to hit, it crackles into the air. Draws itself with line renderers it owns.
    /// </summary>
    public sealed class ChainLightning
    {
        public const float Reach = 5f;
        public const float ConeDegrees = 35f;
        public const float JumpRange = 1.8f;
        const int Links = 3;
        const int Points = 12;

        readonly LineRenderer[] lines = new LineRenderer[Links];
        readonly Vector3[] path = new Vector3[Points];
        readonly System.Random random = new System.Random();
        readonly List<IHittable> chain = new List<IHittable>();

        public ChainLightning(Transform parent, Material material, Color color)
        {
            var holder = new GameObject("Chain Lightning").transform;
            holder.SetParent(parent, false);
            for (int i = 0; i < Links; i++)
            {
                var go = new GameObject("Lightning " + i, typeof(LineRenderer));
                go.transform.SetParent(holder, false);
                LineRenderer line = go.GetComponent<LineRenderer>();
                line.sharedMaterial = material;
                line.positionCount = Points;
                line.useWorldSpace = true;
                line.widthMultiplier = 0.035f;
                line.startColor = line.endColor = color;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.enabled = false;
                lines[i] = line;
            }
            Tint.Apply(holder.gameObject, color);
        }

        /// <summary>Strikes for this frame. Returns how many enemies it touched.</summary>
        public int Strike(RepulsorGame game, Vector3 from, Vector3 direction, float damagePerSecond, float dt)
        {
            chain.Clear();
            IHittable first = Aim.Target(from, direction, ConeDegrees, Reach);
            Vector3 start = from;
            if (first == null)
            {
                // Crackles into the air ahead.
                Vector3 end = from + Quaternion.Euler(Random.Range(-15f, 15f), Random.Range(-15f, 15f), 0f) * direction * Random.Range(0.6f, 1.4f);
                Draw(0, start, end);
                for (int i = 1; i < Links; i++)
                    lines[i].enabled = false;
                return 0;
            }
            chain.Add(first);
            while (chain.Count < Links)
            {
                IHittable last = chain[chain.Count - 1];
                IHittable next = null;
                float best = JumpRange;
                foreach (IHittable other in Hittables.All)
                {
                    if (!other.Alive || other.Team != Team.Enemies || chain.Contains(other) || other is Missile)
                        continue;
                    float d = Vector3.Distance(other.Center, last.Center);
                    if (d < best)
                    {
                        best = d;
                        next = other;
                    }
                }
                if (next == null)
                    break;
                chain.Add(next);
            }
            for (int i = 0; i < Links; i++)
            {
                if (i >= chain.Count)
                {
                    lines[i].enabled = false;
                    continue;
                }
                Vector3 end = HitTest.Nearest(chain[i], start);
                Draw(i, start, end);
                start = end;
            }
            // Strike only after drawing: a kill removes the target from the list.
            for (int i = 0; i < chain.Count; i++)
                if (Hittables.IsLive(chain[i]))
                    game.Strike(chain[i], damagePerSecond * dt * (i == 0 ? 1f : 0.7f), from, "Lightning");
            return chain.Count;
        }

        void Draw(int index, Vector3 from, Vector3 to)
        {
            GameMath.LightningPath(from, to, 0.08f + 0.02f * Vector3.Distance(from, to), path, random);
            lines[index].SetPositions(path);
            lines[index].enabled = true;
        }

        public void Hide()
        {
            foreach (LineRenderer line in lines)
                line.enabled = false;
        }
    }

    /// <summary>
    /// The Elementalist's tornado: a spinning funnel that drifts across the floor for a few seconds, hurting what's
    /// inside it and pulling enemies near it in toward its middle (so they bunch up). Bigger when fully charged.
    /// </summary>
    public sealed class Tornado : MonoBehaviour
    {
        const float Speed = 1.5f;
        const float Life = 5f;
        const float PullRange = 2.6f;
        const float PullSpeed = 1.6f;

        RepulsorGame game;
        Vector3 velocity;
        float age;
        float size;
        float dps;
        float floorY;
        readonly List<Transform> rings = new List<Transform>();
        Transform funnel;
        /// <summary>How see-through the funnel is (white, this much opacity).</summary>
        const float FunnelAlpha = 0.28f;

        public static void Launch(RepulsorGame game, Vector3 from, Vector3 direction, float power, float floorY, float dps, Transform parent)
        {
            var go = new GameObject("Tornado");
            go.transform.SetParent(parent, false);
            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            go.transform.position = new Vector3(from.x, floorY, from.z) + flat * 0.6f;
            var tornado = go.AddComponent<Tornado>();
            tornado.game = game;
            tornado.velocity = flat * Speed;
            tornado.size = Mathf.Lerp(0.7f, 1.2f, power);
            tornado.dps = dps;
            tornado.floorY = floorY;
            Color pale = new Color(0.85f, 0.95f, 1f);
            // The funnel: a tornado model (spun here), see-through white. Rings stand in if it's missing.
            var model = Resources.Load<GameObject>("Props/Tornado");
            if (model != null)
            {
                tornado.funnel = Object.Instantiate(model, go.transform, false).transform;
                // Taller than it is wide: the model's mouth is nearly as wide as it's tall.
                tornado.funnel.localScale = new Vector3(1.25f, 2f, 1.25f) * tornado.size;
                WebStrand.Tint(tornado.funnel.GetComponentsInChildren<Renderer>(), Color.white, FunnelAlpha);
            }
            for (int i = 0; i < (model != null ? 0 : 9); i++)
            {
                float t = i / 8f;
                FxSpin ring = BallLook.Sprite("Ring " + i, go.transform, "FxRing", Mathf.Lerp(0.25f, 1.1f, t) * tornado.size, pale * Mathf.Lerp(1f, 0.5f, t))
                    .Set(roll: Mathf.Lerp(900f, 500f, t) * (i % 2 == 0 ? 1f : -1f));
                ring.transform.localPosition = Vector3.up * (t * 2f * tornado.size);
                ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                tornado.rings.Add(ring.transform);
            }
            ParticleSystem dust = BallLook.Particles("Debris", go.transform, "FxGlow", pale, new Color(0.6f, 0.65f, 0.7f, 0f), 0.06f, 0.8f, 90f, 1.2f, 0.4f);
            ParticleSystem.ShapeModule shape = dust.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.3f * tornado.size;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            ParticleSystem.VelocityOverLifetimeModule spin = dust.velocityOverLifetime;
            spin.enabled = true;
            spin.orbitalY = 6f;
            spin.y = 1.6f;
            game.PlayAt(go.transform.position, game.WhooshSound);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            Vector3 step = velocity * dt;
            if (game.Room.TryDistance(transform.position + Vector3.up * 0.5f, velocity.normalized, out float wall) && wall < 0.4f)
                velocity = Vector3.zero;
            transform.position += step;
            transform.position = new Vector3(transform.position.x, floorY, transform.position.z);
            // The funnel spins and sways.
            if (funnel != null)
            {
                funnel.localRotation = Quaternion.Euler(Mathf.Sin(age * 2.4f) * 6f, age * 540f, Mathf.Cos(age * 3f) * 6f);
                funnel.localPosition = new Vector3(Mathf.Sin(age * 3f) * 0.05f, 0f, Mathf.Cos(age * 2.4f) * 0.05f);
            }
            for (int i = 0; i < rings.Count; i++)
            {
                float t = i / (rings.Count - 1f);
                rings[i].localPosition = new Vector3(Mathf.Sin(age * 3f + t * 3f) * 0.12f * t, t * 2f * size, Mathf.Cos(age * 2.4f + t * 2f) * 0.1f * t);
            }
            float fade = Mathf.Clamp01((Life - age) / 0.5f) * Mathf.Clamp01(age / 0.25f);
            transform.localScale = Vector3.one * Mathf.Max(0.01f, fade);

            Vector3 axisBottom = transform.position;
            Vector3 axisTop = transform.position + Vector3.up * 2f * size;
            var all = Hittables.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (i >= all.Count)
                    continue;
                IHittable target = all[i];
                if (!target.Alive || target.Team != Team.Enemies)
                    continue;
                Vector3 toAxis = Vector3.ProjectOnPlane(transform.position - target.Center, Vector3.up);
                float distance = toAxis.magnitude;
                if (distance < PullRange && target is Enemy enemy && distance > 0.15f)
                    enemy.Push(toAxis / distance * PullSpeed * dt * 8f);
                if (HitTest.Segment(target, axisBottom, axisTop, 0.45f * size))
                    game.Strike(target, dps * dt, transform.position, "Tornado");
            }
            if (age > Life)
                Destroy(gameObject);
        }
    }

    /// <summary>
    /// The Earth character's rock spikes: three stone spikes burst up from the floor one after another along a
    /// line, each a little bigger, hurt what's standing there, then sink back.
    /// </summary>
    public sealed class RockSpikes : MonoBehaviour
    {
        const int Count = 3;
        const float Gap = 0.14f;
        const float Rise = 0.12f;
        const float Stay = 1f;
        const float Sink = 0.4f;
        public const float Damage = 14f;

        RepulsorGame game;
        Vector3 start;
        Vector3 direction;
        float damage;
        float age;
        readonly Transform[] spikes = new Transform[Count];
        readonly float[] heights = new float[Count];
        readonly float[] radii = new float[Count];
        readonly bool[] struck = new bool[Count];

        public static void Erupt(RepulsorGame game, Vector3 floorStart, Vector3 direction, float damage, Material rock, Transform parent)
        {
            var go = new GameObject("Rock Spikes");
            go.transform.SetParent(parent, false);
            var spikes = go.AddComponent<RockSpikes>();
            spikes.game = game;
            spikes.start = floorStart;
            spikes.direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            spikes.damage = damage;
            string[] keys = { "SpikeC", "SpikeB", "SpikeA" };
            for (int i = 0; i < Count; i++)
            {
                // About a foot across, getting a little larger and taller each time.
                spikes.radii[i] = 0.3f + 0.05f * i;
                spikes.heights[i] = 0.6f + 0.18f * i;
                var prefab = Resources.Load<GameObject>("Props/" + keys[i]);
                Transform spike = prefab != null ? Instantiate(prefab, go.transform, false).transform : new GameObject("Spike").transform;
                spike.SetParent(go.transform, false);
                spike.position = floorStart + spikes.direction * (1f + 0.75f * i);
                spike.rotation = Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(0f, 360f), Random.Range(-8f, 8f));
                spike.localScale = Vector3.zero;
                Paint.All(spike, rock);
                spikes.spikes[i] = spike;
            }
        }

        void Update()
        {
            age += Time.deltaTime;
            for (int i = 0; i < Count; i++)
            {
                float t = age - i * Gap;
                if (t < 0f)
                    continue;
                float grow = t < Rise ? t / Rise : t < Rise + Stay ? 1f : Mathf.Clamp01(1f - (t - Rise - Stay) / Sink);
                float overshoot = t < Rise ? 1f : 1f + 0.12f * Mathf.Exp(-(t - Rise) * 12f);
                spikes[i].localScale = new Vector3(radii[i] * 2f, heights[i] * grow * overshoot, radii[i] * 2f);
                if (!struck[i] && t >= Rise * 0.5f)
                {
                    struck[i] = true;
                    Vector3 at = spikes[i].position;
                    game.PlayAt(at, game.BoomSound);
                    var all = Hittables.All;
                    for (int j = all.Count - 1; j >= 0; j--)
                    {
                        if (j >= all.Count)
                            continue;
                        IHittable target = all[j];
                        if (target.Alive && target.Team == Team.Enemies
                            && HitTest.Segment(target, at, at + Vector3.up * heights[i], radii[i]))
                        {
                            if (target is Enemy enemy)
                                enemy.Push(direction * 2f);
                            game.Strike(target, damage, at, "Rock spike");
                        }
                    }
                }
            }
            if (age > (Count - 1) * Gap + Rise + Stay + Sink)
                Destroy(gameObject);
        }
    }

    /// <summary>A rock orb stuck to an enemy (Earth level 3): it rides on it, glowing hotter, until a snap sets it off.</summary>
    public sealed class StuckRock : MonoBehaviour
    {
        public IHittable Target;
        float age;
        Renderer[] renderers;

        public static StuckRock Attach(Enemy enemy, Vector3 at, Material hot, Transform parent)
        {
            var prefab = Resources.Load<GameObject>("Props/RockSmallA");
            Transform rock = prefab != null ? Instantiate(prefab).transform : new GameObject("Rock").transform;
            rock.name = "Stuck Rock";
            rock.SetParent(enemy.transform, true);
            // On its surface, toward where it was hit.
            Vector3 core = HitTest.Nearest(enemy, at);
            Vector3 outward = (at - core).sqrMagnitude > 1e-4f ? (at - core).normalized : -enemy.transform.forward;
            rock.position = core + outward * enemy.Radius * 0.9f;
            rock.rotation = Random.rotation;
            rock.localScale = Vector3.one * 0.15f / Mathf.Max(0.01f, enemy.transform.lossyScale.x);
            var stuck = rock.gameObject.AddComponent<StuckRock>();
            stuck.Target = enemy;
            stuck.renderers = rock.GetComponentsInChildren<Renderer>();
            Paint.All(rock, hot);
            BallLook.Sprite("Glow", rock, "FxGlow", 1.4f, new Color(1f, 0.35f, 0.08f)).Set(billboard: true, flicker: 0.25f);
            return stuck;
        }

        void Update() => age += Time.deltaTime;
    }

    /// <summary>
    /// Character auras built from effects rather than the aura shader:
    /// - Elementalist: a white whirl of wind round your body (level 2), faster and thicker at level 3.
    /// - Earth: a cracked crater ring round your feet (level 2); at level 3 small rocks with gold and silver flecks
    ///   orbit you as well.
    /// You see them around your legs and at the edges of your view. They follow your head over the floor.
    /// </summary>
    public sealed class ElementAura
    {
        readonly Transform root;
        readonly ParticleSystem wind;
        readonly Transform crater;
        readonly List<Transform> orbiters = new List<Transform>();
        readonly ParticleSystem sparkles;
        float spin;

        public ElementAura(Transform parent, Material rock, Material gold)
        {
            root = new GameObject("Element Aura").transform;
            root.SetParent(parent, false);

            Color pale = new Color(0.9f, 0.97f, 1f, 0.7f);
            wind = BallLook.Particles("Wind", root, "FxGlow", pale, new Color(0.8f, 0.9f, 1f, 0f), 0.07f, 1.6f, 0f, 0.05f, 0.1f);
            ParticleSystem.ShapeModule shape = wind.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.45f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            ParticleSystem.VelocityOverLifetimeModule whirl = wind.velocityOverLifetime;
            whirl.enabled = true;
            whirl.space = ParticleSystemSimulationSpace.Local;
            whirl.orbitalY = 3f;
            whirl.y = 0.9f;
            ParticleSystem.MainModule windMain = wind.main;
            windMain.simulationSpace = ParticleSystemSimulationSpace.Local;

            crater = new GameObject("Crater").transform;
            crater.SetParent(root, false);
            var ring = BallLook.Sprite("Crack Ring", crater, "FxRing", 1.3f, new Color(1f, 0.4f, 0.1f) * 0.8f).Set(roll: 8f);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.transform.localPosition = Vector3.up * 0.01f;
            var prefab = Resources.Load<GameObject>("Props/RockSmallB");
            for (int i = 0; i < 10; i++)
            {
                float angle = i / 10f * Mathf.PI * 2f;
                Transform pebble = prefab != null ? Object.Instantiate(prefab, crater, false).transform : new GameObject("Pebble").transform;
                pebble.SetParent(crater, false);
                pebble.localPosition = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.62f;
                pebble.localRotation = Quaternion.Euler(Random.Range(-20f, 20f), Random.Range(0f, 360f), Random.Range(-20f, 20f));
                pebble.localScale = Vector3.one * Random.Range(0.12f, 0.2f);
                Paint.All(pebble, rock);
            }

            var small = Resources.Load<GameObject>("Props/RockSmallA");
            for (int i = 0; i < 14; i++)
            {
                Transform orbiter = small != null ? Object.Instantiate(small, root, false).transform : new GameObject("Orbiter").transform;
                orbiter.SetParent(root, false);
                orbiter.localScale = Vector3.one * Random.Range(0.05f, 0.1f);
                Paint.All(orbiter, gold);
                orbiters.Add(orbiter);
            }
            sparkles = BallLook.Particles("Sparkles", root, "FxStar", new Color(1f, 0.85f, 0.4f), new Color(0.85f, 0.9f, 1f, 0f), 0.04f, 0.8f, 0f, 0.05f, 0.6f);
            ParticleSystem.ShapeModule sparkleShape = sparkles.shape;
            sparkleShape.shapeType = ParticleSystemShapeType.Sphere;
            sparkleShape.radius = 0.6f;
            Show(CharacterId.Technoman, 1, Vector3.zero, 0f, 0f);
        }

        /// <summary>Shows the aura for this character and level, centred under your head.</summary>
        public void Show(CharacterId character, int level, Vector3 head, float floorY, float dt)
        {
            bool windOn = character == CharacterId.Elementalist && level >= 2;
            bool craterOn = character == CharacterId.Earth && level >= 2;
            bool orbitOn = character == CharacterId.Earth && level >= 3;
            root.position = new Vector3(head.x, floorY, head.z);

            ParticleSystem.EmissionModule windRate = wind.emission;
            windRate.rateOverTime = windOn ? (level >= 3 ? 140f : 50f) : 0f;
            ParticleSystem.VelocityOverLifetimeModule whirl = wind.velocityOverLifetime;
            whirl.orbitalY = level >= 3 ? 6f : 3f;

            if (crater.gameObject.activeSelf != craterOn)
                crater.gameObject.SetActive(craterOn);
            spin += dt;
            for (int i = 0; i < orbiters.Count; i++)
            {
                if (orbiters[i].gameObject.activeSelf != orbitOn)
                    orbiters[i].gameObject.SetActive(orbitOn);
                if (!orbitOn)
                    continue;
                float angle = spin * (1.2f + 0.15f * (i % 3)) + i * 2.4f;
                float height = 0.25f + 1.2f * ((i * 0.37f) % 1f) + 0.08f * Mathf.Sin(spin * 2f + i);
                float radius = 0.5f + 0.12f * (i % 3);
                orbiters[i].localPosition = new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
                orbiters[i].localRotation = Quaternion.Euler(spin * 90f + i * 30f, spin * 70f, 0f);
            }
            ParticleSystem.EmissionModule sparkleRate = sparkles.emission;
            sparkleRate.rateOverTime = orbitOn ? 25f : 0f;
            sparkles.transform.localPosition = Vector3.up * 0.9f;
        }
    }

    /// <summary>
    /// The Earth character's stone hands: chunks of rock on the back of each hand and over the knuckles, following
    /// the tracked joints, like a rock-skinned fighter's fists.
    /// </summary>
    public sealed class RockGauntlets
    {
        static readonly Joint[] Spots = { Joint.MiddleMetacarpal, Joint.IndexProximal, Joint.MiddleProximal, Joint.RingProximal, Joint.LittleProximal, Joint.Wrist };
        static readonly float[] Sizes = { 0.06f, 0.03f, 0.032f, 0.03f, 0.026f, 0.05f };
        readonly Transform[][] rocks = new Transform[2][];

        public RockGauntlets(Transform parent, Material rock)
        {
            string[] keys = { "RockSmallA", "RockSmallB", "RockSmallC" };
            for (int h = 0; h < 2; h++)
            {
                rocks[h] = new Transform[Spots.Length];
                for (int i = 0; i < Spots.Length; i++)
                {
                    var prefab = Resources.Load<GameObject>("Props/" + keys[i % keys.Length]);
                    Transform piece = prefab != null ? Object.Instantiate(prefab, parent, false).transform : new GameObject("Rock").transform;
                    piece.SetParent(parent, false);
                    piece.name = "Gauntlet Rock";
                    piece.localScale = Vector3.one * Sizes[i];
                    piece.localRotation = Random.rotation;
                    Paint.All(piece, rock);
                    piece.gameObject.SetActive(false);
                    rocks[h][i] = piece;
                }
            }
        }

        public void Tick(bool on, HandPose left, HandPose right)
        {
            for (int h = 0; h < 2; h++)
            {
                HandPose hand = h == 0 ? left : right;
                bool show = on && hand.Tracked;
                for (int i = 0; i < Spots.Length; i++)
                {
                    Transform piece = rocks[h][i];
                    if (piece.gameObject.activeSelf != show)
                        piece.gameObject.SetActive(show);
                    if (!show)
                        continue;
                    // On the back of the hand (away from the palm).
                    piece.position = hand[Spots[i]] - hand.PalmNormal * (Sizes[i] * 0.45f);
                    piece.rotation = Quaternion.LookRotation(hand.Forward, -hand.PalmNormal) * Quaternion.Euler(i * 37f, i * 53f, 0f);
                }
            }
        }
    }
}
