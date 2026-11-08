using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// The Mage's level 2 attack: hold a palm out and a gout of fire pours from it, a short cone that burns every enemy
    /// in it. Drawn as flame sprites streaming out of the palm, swelling and cooling from white-yellow to dark red,
    /// with a hot glow at the hand. <see cref="HandRepulsor"/> drives it in place of the Technoman's laser.
    /// </summary>
    public sealed class Flamethrower : MonoBehaviour
    {
        /// <summary>How far the fire reaches, and how wide its cone is (degrees either side).</summary>
        public static float Reach = 2.6f;
        public static float ConeDegrees = 14f;
        /// <summary>Flames burn harder than the laser (they're short range): this times the laser's damage per second.</summary>
        public static float DamageScale = 1.2f;

        ParticleSystem flames;
        ParticleSystem smoke;
        FxSpin glow;
        bool on;

        public static Flamethrower Create(Transform parent)
        {
            var go = new GameObject("Flamethrower");
            go.transform.SetParent(parent, false);
            var thrower = go.AddComponent<Flamethrower>();
            thrower.Build();
            return thrower;
        }

        void Build()
        {
            flames = BallLook.Particles("Flames", transform, "FxFlame", new Color(1f, 0.85f, 0.45f), new Color(0.6f, 0.06f, 0.02f),
                0.12f, 0.45f, 0f, 1f, 0f);
            ParticleSystem.MainModule main = flames.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(4.5f, 6f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.45f);
            main.maxParticles = 140;
            main.gravityModifier = -0.15f;
            ParticleSystem.ShapeModule shape = flames.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = ConeDegrees * 0.7f;
            shape.radius = 0.02f;
            ParticleSystem.SizeOverLifetimeModule growth = flames.sizeOverLifetime;
            // Small and hot at the palm, billowing out to half a metre.
            growth.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.4f, 1f, 4.5f));
            ParticleSystem.ColorOverLifetimeModule colour = flames.colorOverLifetime;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f), new GradientColorKey(new Color(1f, 0.55f, 0.12f), 0.35f),
                    new GradientColorKey(new Color(0.55f, 0.06f, 0.02f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            colour.color = gradient;
            ParticleSystem.RotationOverLifetimeModule spin = flames.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-3f, 3f);

            // Sparks thrown out ahead of the flames.
            smoke = BallLook.Particles("Sparks", transform, "FxSpark", new Color(1f, 0.8f, 0.4f), new Color(1f, 0.25f, 0.05f),
                0.03f, 0.5f, 0f, 1f, 0f);
            ParticleSystem.MainModule sparkMain = smoke.main;
            sparkMain.startSpeed = new ParticleSystem.MinMaxCurve(4f, 7f);
            ParticleSystem.ShapeModule sparkShape = smoke.shape;
            sparkShape.shapeType = ParticleSystemShapeType.Cone;
            sparkShape.angle = ConeDegrees;
            sparkShape.radius = 0.02f;

            glow = BallLook.Sprite("Palm Glow", transform, "FxGlow", 0.18f, new Color(1f, 0.55f, 0.15f)).Set(billboard: true, flicker: 0.25f);
            glow.gameObject.SetActive(false);
            SetOn(false);
        }

        /// <summary>Called every frame the fire pours: from here, this way.</summary>
        public void Pour(Vector3 from, Vector3 direction, Color tint)
        {
            transform.SetPositionAndRotation(from, Quaternion.LookRotation(direction));
            SetOn(true);
        }

        public void Stop() => SetOn(false);

        void SetOn(bool pouring)
        {
            if (pouring == on && flames != null)
                return;
            on = pouring;
            ParticleSystem.EmissionModule emission = flames.emission;
            emission.rateOverTime = pouring ? 110f : 0f;
            ParticleSystem.EmissionModule sparks = smoke.emission;
            sparks.rateOverTime = pouring ? 25f : 0f;
            glow.gameObject.SetActive(pouring);
        }

        /// <summary>Whether a live enemy is touched by the cone of fire (its size counts).</summary>
        public static bool InCone(Vector3 from, Vector3 direction, float reach, IHittable target)
        {
            if (!target.Alive || target.Team != Team.Enemies)
                return false;
            Vector3 to = target.Center - from;
            float distance = to.magnitude;
            if (distance > reach + target.Radius)
                return false;
            float edge = distance > 0.01f ? Mathf.Atan2(target.Radius, distance) * Mathf.Rad2Deg : 90f;
            return Vector3.Angle(direction, to) - edge <= ConeDegrees;
        }

        /// <summary>Burns every enemy inside the cone. True if anything was in it.</summary>
        public static bool Burn(RepulsorGame game, Vector3 from, Vector3 direction, float reach, float damagePerSecond, float dt)
        {
            bool any = false;
            var all = Hittables.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (i >= all.Count)
                    continue;
                IHittable target = all[i];
                if (!InCone(from, direction, reach, target))
                    continue;
                game.Strike(target, damagePerSecond * dt, from, "Flames");
                any = true;
            }
            return any;
        }
    }
}
