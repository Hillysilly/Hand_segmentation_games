using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// The energy ball's looks. A is the crackling ball the comet uses. The others replace it completely, so each
    /// reads differently even small and far away: B a white-hot plasma core inside two spinning rings, C a fireball
    /// that sheds flames as it flies, D a spinning star that trails sparkles. Built from the sprites in Resources/Fx.
    /// </summary>
    public static class BallLook
    {
        public static readonly string[] Names = { "A  CRACKLE", "B  PLASMA", "C  FIREBALL", "D  STAR" };

        /// <summary>Restyles a ball head (a fired one, or the one held while charging). Sizes are in ball diameters.</summary>
        public static void Apply(Transform head, int style, Color color)
        {
            if (style <= 0 || head == null)
                return;
            HideCrackle(head, style == 2);
            Color hot = Color.Lerp(color, Color.white, 0.55f);
            switch (style)
            {
                case 1:
                    Sprite("Halo", head, "FxGlow", 3.2f, color * 0.7f).Set(billboard: true, pulse: 0.08f);
                    Sprite("Core", head, "FxGlow", 1.5f, hot).Set(billboard: true, pulse: 0.12f);
                    Sprite("Ring A", head, "FxRing", 2.1f, color).Set(tumble: new Vector3(35f, 300f, 0f));
                    Sprite("Ring B", head, "FxRing", 1.75f, hot).Set(tumble: new Vector3(220f, 25f, 70f));
                    break;
                case 2:
                    Color flame = Color.Lerp(color, new Color(1f, 0.55f, 0.1f), 0.5f);
                    Sprite("Fire", head, "FxFlame", 2.2f, flame).Set(billboard: true, roll: 140f, flicker: 0.18f);
                    Sprite("Fire Inner", head, "FxFlame", 1.4f, Color.Lerp(flame, new Color(1f, 0.9f, 0.5f), 0.6f))
                        .Set(billboard: true, roll: -200f, flicker: 0.22f);
                    Sprite("Core", head, "FxGlow", 1.1f, new Color(1f, 0.95f, 0.8f)).Set(billboard: true, flicker: 0.1f);
                    Particles("Flames", head, "FxFlame", Color.Lerp(flame, Color.white, 0.3f), new Color(0.6f, 0.08f, 0.02f),
                        1.3f, 0.45f, 45f, 0.25f, 0.45f);
                    break;
                case 3:
                    Sprite("Star", head, "FxStar", 3.4f, color).Set(billboard: true, roll: 90f, pulse: 0.06f);
                    Sprite("Star Inner", head, "FxStar", 2.2f, hot).Set(billboard: true, roll: -150f, startRoll: 22f);
                    Sprite("Core", head, "FxGlow", 1.2f, Color.white).Set(billboard: true, pulse: 0.15f);
                    Particles("Sparkles", head, "FxSpark", Color.white, color, 0.35f, 0.6f, 40f, 0.6f, 0.5f);
                    break;
            }
        }

        /// <summary>Turns off the crackling ball: its arcs, its glow sphere, and on a fired shot the embers (and, for fire, the tail).</summary>
        static void HideCrackle(Transform head, bool fire)
        {
            if (head.TryGetComponent(out ElectricBall crackle))
                crackle.enabled = false;
            foreach (LineRenderer arc in head.GetComponentsInChildren<LineRenderer>())
                arc.enabled = false;
            Transform glow = head.Find("Glow");
            if (glow != null && glow.TryGetComponent(out Renderer glowRenderer))
                glowRenderer.enabled = false;
            Transform shot = head.parent;
            if (shot == null || !shot.TryGetComponent(out Blast _))
                return;
            Transform embers = shot.Find("Embers");
            if (embers != null && embers.TryGetComponent(out ParticleSystemRenderer emberRenderer))
                emberRenderer.enabled = false;
            Transform tail = shot.Find("Tail");
            if (fire && tail != null && tail.TryGetComponent(out Renderer tailRenderer))
                tailRenderer.enabled = false;
        }

        /// <summary>
        /// The tech comet: a white-hot rounded nose with a body of glowing circuit panels and traces streaming back
        /// along the flight path, drawn on a sprite that lies along the path and turns to face you.
        /// </summary>
        public static void ApplyTech(Transform head, Color color)
        {
            HideCrackle(head, false);
            Color hot = Color.Lerp(color, Color.white, 0.5f);
            // The shot flies along the head's local -Z (a Blast faces backward).
            Sprite("Tech Body", head, "FxTech", 1f, color).Set(axis: Vector3.back, alongU: true, flicker: 0.05f)
                .Place(Vector3.forward * 1.6f, new Vector3(4.4f, 1.9f, 1f));
            Sprite("Nose", head, "FxGlow", 1.9f, hot).Set(billboard: true, pulse: 0.06f);
            Sprite("Halo", head, "FxGlow", 3.6f, color * 0.45f).Set(billboard: true);
        }

        internal static FxSpin Sprite(string name, Transform parent, string material, float size, Color color)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * size;
            go.transform.localRotation = Random.rotation;
            go.GetComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>("QuadMesh");
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = Resources.Load<Material>("Fx/" + material);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
            return go.AddComponent<FxSpin>();
        }

        /// <summary>Sprites left behind in the room as the ball flies (simulated in world space), fading from one colour to another.</summary>
        internal static ParticleSystem Particles(string name, Transform parent, string material, Color start, Color end, float size, float life,
            float rate, float speed, float spread)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var particles = go.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particles.main;
            main.duration = 1f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 60;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = rate;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = spread;

            ParticleSystem.ColorOverLifetimeModule colour = particles.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            colour.color = gradient;

            ParticleSystem.SizeOverLifetimeModule growth = particles.sizeOverLifetime;
            growth.enabled = true;
            growth.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Resources.Load<Material>("Fx/" + material);
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            particles.Play();
            return particles;
        }
    }

    /// <summary>Moves an effect sprite: faces the viewer and rolls, or tumbles in 3D (rings), and pulses or flickers in size.</summary>
    public sealed class FxSpin : MonoBehaviour
    {
        /// <summary>The viewer's eyes; set by the game at start. Falls back to the main camera.</summary>
        public static Transform Eye;

        bool billboard;
        Vector3 axis;
        bool alongU;
        float rollSpeed;
        float roll;
        Vector3 tumble;
        float pulse;
        float flicker;
        float seed;
        Vector3 size;

        /// <param name="axis">For long sprites (beams, comets): a direction in the parent's space that the sprite lies along,
        /// turning about it to face you. Along the texture's V (up) by default, or its U with <paramref name="alongU"/>.</param>
        public FxSpin Set(bool billboard = false, float roll = 0f, float startRoll = 0f, Vector3 tumble = default, float pulse = 0f,
            float flicker = 0f, Vector3 axis = default, bool alongU = false)
        {
            this.billboard = billboard;
            this.axis = axis;
            this.alongU = alongU;
            rollSpeed = roll;
            this.roll = startRoll;
            this.tumble = tumble;
            this.pulse = pulse;
            this.flicker = flicker;
            seed = Random.value * 100f;
            size = transform.localScale;
            return this;
        }

        /// <summary>Moves the sprite within its parent and gives it a (non-square) size.</summary>
        public FxSpin Place(Vector3 localPosition, Vector3 localSize)
        {
            transform.localPosition = localPosition;
            transform.localScale = size = localSize;
            return this;
        }

        /// <summary>Changes the size the sprite pulses and flickers around (for effects that grow).</summary>
        public Vector3 Size
        {
            get => size;
            set
            {
                size = value;
                if (pulse == 0f && flicker == 0f)
                    transform.localScale = value;
            }
        }

        void LateUpdate() => Tick(Time.deltaTime);

        /// <summary>One step of motion. Public so editor previews can pose sprites without play mode.</summary>
        public void Tick(float dt)
        {
            float t = Time.time + seed;
            Transform viewer = Eye != null ? Eye : Camera.main != null ? Camera.main.transform : null;
            if (axis != Vector3.zero && viewer != null)
            {
                Vector3 along = transform.parent != null ? transform.parent.TransformDirection(axis).normalized : axis.normalized;
                Vector3 toEye = viewer.position - transform.position;
                Vector3 facing = toEye - along * Vector3.Dot(toEye, along);
                if (facing.sqrMagnitude > 1e-6f)
                    transform.rotation = alongU
                        ? Quaternion.LookRotation(facing, Vector3.Cross(facing, along))
                        : Quaternion.LookRotation(facing, along);
            }
            else if (billboard)
            {
                Transform eye = Eye != null ? Eye : Camera.main != null ? Camera.main.transform : null;
                roll += rollSpeed * dt;
                if (eye != null)
                    transform.rotation = Quaternion.LookRotation(transform.position - eye.position, eye.up) * Quaternion.Euler(0f, 0f, roll);
            }
            else if (tumble != Vector3.zero)
            {
                transform.Rotate(tumble * dt, Space.Self);
            }
            if (pulse == 0f && flicker == 0f)
                return;
            float scale = 1f + pulse * Mathf.Sin(t * 9f) + flicker * (Mathf.PerlinNoise(t * 11f, seed) - 0.5f) * 2f;
            transform.localScale = size * scale;
        }
    }
}
