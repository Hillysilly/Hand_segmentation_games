using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// The Mage's level 3 attack: a column of fire bursting up from the floor where the triangle aimed. It erupts
    /// fast, roars for a moment and fades. Built from glowing sprites and particles:
    /// - A INFERNO: a white-hot streaky core inside a wider flame column scrolling upward, ragged flames bursting off
    ///   the base and peeling off the sides, embers rising, and a pool of firelight on the floor.
    /// - B ENERGY: the column in the player's colour, with rings rising up it and sparks instead of flames.
    /// - C BOTH: the inferno with the rising rings.
    /// </summary>
    public sealed class FirePillar : MonoBehaviour
    {
        public static readonly string[] StyleNames = { "A  INFERNO", "B  ENERGY", "C  BOTH" };
        const float Height = 3f;
        const float Width = 0.9f;
        const float RiseSeconds = 0.25f;
        const float LifeSeconds = 1.8f;
        const float FadeSeconds = 0.6f;

        static readonly int ColorId = Shader.PropertyToID("_Color");

        struct Layer
        {
            public FxSpin Sprite;
            public Renderer Renderer;
            public Color Color;
            public float Width;
        }

        Layer[] columns;
        Layer pool;
        FxSpin[] rings;
        Renderer[] ringRenderers;
        ParticleSystem[] particles;
        MaterialPropertyBlock block;
        Color color;
        float age;
        bool stopped;

        /// <summary>Editor preview: shows the pillar this many seconds in, with its particles run up to then.</summary>
        public void ShowAt(float seconds)
        {
            age = seconds;
            foreach (ParticleSystem system in particles)
                system.Simulate(seconds, true, true, false);
            Update();
        }

        public static FirePillar Spawn(Vector3 floorPoint, Color color, int style, Transform parent)
        {
            var go = new GameObject("Fire Pillar");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(floorPoint, Quaternion.identity);
            var pillar = go.AddComponent<FirePillar>();
            pillar.Build(color, style);
            pillar.Update();
            return pillar;
        }

        void Build(Color playerColor, int style)
        {
            color = playerColor;
            block = new MaterialPropertyBlock();
            bool fire = style != 1;
            Color flame = fire ? Color.Lerp(playerColor, new Color(1f, 0.45f, 0.08f), 0.6f) : playerColor;
            Color hot = fire ? new Color(1f, 0.92f, 0.6f) : Color.Lerp(playerColor, Color.white, 0.6f);

            columns = new[]
            {
                Column("Outer Flame", flame * 0.9f, Width * 1.6f),
                Column("Flame", Color.Lerp(flame, hot, 0.3f), Width * 1.0f),
                Column("Core", hot, Width * 0.5f),
            };
            pool = Flat("Firelight", "FxGlow", flame, Width * 3f);

            var systems = new System.Collections.Generic.List<ParticleSystem>();
            if (fire)
            {
                // Ragged flames bursting out of the base and curling up.
                ParticleSystem burst = BallLook.Particles("Base Flames", transform, "FxFlame", new Color(1f, 0.85f, 0.4f),
                    new Color(0.55f, 0.05f, 0.02f), 0.55f, 0.7f, 90f, 1.4f, 0.2f);
                Shape(burst, ParticleSystemShapeType.Cone, 55f, 0.25f, Vector3.zero, Vector3.one);
                Rise(burst, -0.6f);
                systems.Add(burst);
                // Flames peeling off the sides of the column.
                ParticleSystem wisps = BallLook.Particles("Side Flames", transform, "FxFlame", new Color(1f, 0.6f, 0.2f),
                    new Color(0.6f, 0.05f, 0.02f), 0.5f, 0.55f, 80f, 0.8f, 0.3f);
                Shape(wisps, ParticleSystemShapeType.Box, 0f, 0f, new Vector3(0f, Height * 0.45f, 0f), new Vector3(Width * 0.5f, Height * 0.85f, Width * 0.5f));
                Rise(wisps, -0.8f);
                systems.Add(wisps);
            }
            // Embers (fire) or sparks (energy) shooting up past the top.
            ParticleSystem embers = BallLook.Particles("Embers", transform, "FxSpark", fire ? new Color(1f, 0.8f, 0.4f) : Color.white,
                fire ? new Color(1f, 0.25f, 0.05f) : playerColor, 0.08f, 1.1f, 35f, 3f, 0.3f);
            Shape(embers, ParticleSystemShapeType.Cone, 12f, 0.3f, Vector3.zero, Vector3.one);
            systems.Add(embers);
            particles = systems.ToArray();

            if (style != 0)
            {
                rings = new FxSpin[3];
                ringRenderers = new Renderer[3];
                for (int i = 0; i < rings.Length; i++)
                {
                    rings[i] = BallLook.Sprite("Ring " + i, transform, "FxRing", Width * 1.6f, hot).Set();
                    rings[i].transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    ringRenderers[i] = rings[i].GetComponent<Renderer>();
                }
            }
        }

        Layer Column(string name, Color tint, float width)
        {
            FxSpin sprite = BallLook.Sprite(name, transform, "FxBeam", 1f, tint).Set(axis: Vector3.up);
            return new Layer { Sprite = sprite, Renderer = sprite.GetComponent<Renderer>(), Color = tint, Width = width };
        }

        Layer Flat(string name, string material, Color tint, float size)
        {
            FxSpin sprite = BallLook.Sprite(name, transform, material, size, tint).Set();
            sprite.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            sprite.transform.localPosition = Vector3.up * 0.02f;
            return new Layer { Sprite = sprite, Renderer = sprite.GetComponent<Renderer>(), Color = tint, Width = size };
        }

        static void Shape(ParticleSystem system, ParticleSystemShapeType type, float angle, float radius, Vector3 position, Vector3 scale)
        {
            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = type;
            shape.angle = angle;
            shape.radius = radius;
            shape.position = position;
            shape.scale = scale;
            // Cones point up the column (the shape's forward is its local Z).
            shape.rotation = new Vector3(-90f, 0f, 0f);
            if (type == ParticleSystemShapeType.Box)
                shape.rotation = Vector3.zero;
        }

        /// <summary>Hot gas rises: negative gravity, in g.</summary>
        static void Rise(ParticleSystem system, float gravity)
        {
            ParticleSystem.MainModule main = system.main;
            main.gravityModifier = gravity;
        }

        void Update()
        {
            age += Time.deltaTime;
            float rise = Mathf.Clamp01(age / RiseSeconds);
            float eased = 1f - (1f - rise) * (1f - rise);
            float fade = Mathf.Clamp01((LifeSeconds - age) / FadeSeconds);
            float height = Mathf.Max(0.05f, Height * eased);
            // A bright flash as it erupts, settling to a roar.
            float flash = 1f + 0.6f * Mathf.Clamp01(1f - age / 0.4f);

            foreach (Layer column in columns)
            {
                float flicker = 1f + 0.06f * Mathf.Sin(age * 31f + column.Width * 20f);
                column.Sprite.transform.localPosition = Vector3.up * (height * 0.5f);
                column.Sprite.Size = new Vector3(column.Width * flicker * (2f - eased), height * 1.08f, 1f);
                Paint(column.Renderer, column.Color * (fade * flash));
            }
            pool.Sprite.Size = Vector3.one * (pool.Width * (0.6f + 0.4f * eased));
            Paint(pool.Renderer, pool.Color * (fade * 0.9f * flash));

            if (rings != null)
            {
                for (int i = 0; i < rings.Length; i++)
                {
                    float cycle = Mathf.Repeat(age * 1.4f + i / (float)rings.Length, 1f);
                    rings[i].transform.localPosition = Vector3.up * (cycle * height);
                    rings[i].Size = Vector3.one * (Width * Mathf.Lerp(1.7f, 1.1f, cycle));
                    Paint(ringRenderers[i], color * (fade * Mathf.Sin(cycle * Mathf.PI)));
                }
            }

            if (!stopped && age > LifeSeconds - FadeSeconds)
            {
                stopped = true;
                foreach (ParticleSystem system in particles)
                    system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            if (age >= LifeSeconds + 0.6f && Application.isPlaying)
                Destroy(gameObject);
        }

        void Paint(Renderer renderer, Color tint)
        {
            renderer.GetPropertyBlock(block);
            block.SetColor(ColorId, tint);
            renderer.SetPropertyBlock(block);
        }
    }
}
