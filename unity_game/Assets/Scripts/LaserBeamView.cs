using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// The laser's look: a ribbon of light, a soft glow in the palm, and a jagged burst with sparks lying
    /// on whatever it hits. Call <see cref="Aim"/> every frame the laser is on; it powers down by itself
    /// when that stops.
    /// </summary>
    public sealed class LaserBeamView : MonoBehaviour
    {
        const float PowerUpSeconds = 0.08f;
        const float PowerDownSeconds = 0.2f;
        const float FlickerSpeed = 41f;
        const float WidthFlicker = 0.06f;
        const float HalfWidth = 0.34f;
        const float MuzzleSize = 0.26f;
        const float SplashSize = 0.9f;
        const float FlashSize = 0.34f;
        const float SurfaceOffset = 0.015f;
        const float NormalFollowSpeed = 12f;
        const float IgniteKick = 0.9f;
        const float KickFadeSpeed = 7f;

        [SerializeField] GameObject visuals;
        [SerializeField] Transform beam;
        [SerializeField] Transform muzzleGlow;
        [SerializeField] GameObject impact;
        [SerializeField] Transform impactSplash;
        [SerializeField] Transform impactFlash;
        [SerializeField] ParticleSystem sparks;
        [SerializeField] AudioSource sizzle;

        Vector3 start;
        Vector3 end;
        Vector3 surfaceNormal = Vector3.up;
        bool hitting;
        bool aimed;
        float power;
        float kick;

        void Awake()
        {
            sizzle.clip = Sfx.Sizzle();
            Apply(0f);
        }

        /// <summary>Points the beam for this frame. The normal says which way the surface it hits faces.</summary>
        public void Aim(Vector3 from, Vector3 to, bool onSurface, Vector3 normal)
        {
            if (power <= 0f)
                kick = 1f;

            // A burst that was already showing turns gently, because depth readings are noisy.
            bool wasHitting = hitting;
            start = from;
            end = to;
            hitting = onSurface;
            aimed = true;
            surfaceNormal = wasHitting
                ? Vector3.Slerp(surfaceNormal, normal, 1f - Mathf.Exp(-NormalFollowSpeed * Time.deltaTime)).normalized
                : normal;
        }

        void LateUpdate()
        {
            float rate = Time.deltaTime / (aimed ? PowerUpSeconds : PowerDownSeconds);
            Apply(Mathf.MoveTowards(power, aimed ? 1f : 0f, rate));
            kick *= Mathf.Exp(-KickFadeSpeed * Time.deltaTime);
            aimed = false;
        }

        /// <summary>Draws the beam at a power from 0 (off) to 1. Public so the editor preview can use it.</summary>
        public void Apply(float newPower)
        {
            power = newPower;
            bool visible = power > 0f;
            if (visuals.activeSelf != visible)
                visuals.SetActive(visible);

            bool burning = visible && aimed && hitting;
            ParticleSystem.EmissionModule emission = sparks.emission;
            emission.enabled = burning;
            UpdateSizzle(burning);

            if (!visible)
                return;

            Vector3 path = end - start;
            float length = Mathf.Max(path.magnitude, 0.001f);
            Vector3 direction = path / length;
            float flicker = 1f + Mathf.Sin(Time.time * FlickerSpeed) * WidthFlicker;
            float size = power * flicker * (1f + kick * IgniteKick);

            beam.SetPositionAndRotation(start, Quaternion.LookRotation(direction));
            beam.localScale = new Vector3(HalfWidth * size, 1f, length);

            muzzleGlow.position = start;
            muzzleGlow.localScale = Vector3.one * (MuzzleSize * size);

            if (impact.activeSelf != hitting)
                impact.SetActive(hitting);
            if (hitting)
            {
                Vector3 onSurface = end + surfaceNormal * SurfaceOffset;
                impactSplash.SetPositionAndRotation(onSurface, Quaternion.LookRotation(-surfaceNormal));
                impactSplash.localScale = Vector3.one * (SplashSize * size);
                impactFlash.position = onSurface + surfaceNormal * 0.03f;
                impactFlash.localScale = Vector3.one * (FlashSize * size);
                sparks.transform.SetPositionAndRotation(onSurface, Quaternion.LookRotation(surfaceNormal));
            }
        }

        void UpdateSizzle(bool burning)
        {
            if (sizzle.clip == null || sizzle.isPlaying == burning)
                return;
            if (burning)
                sizzle.Play();
            else
                sizzle.Stop();
        }
    }
}
