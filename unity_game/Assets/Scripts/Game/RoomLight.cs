using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// Lights the enemies (and everything else drawn with a lit material) like the room they're standing in: one
    /// overhead key light plus ambient light, both taking their colour and strength from the average of what the
    /// headset's camera sees. A dim room dims them, warm lamps warm them. The room is sampled between fights only
    /// (menus, the countdown, the break between rounds), never during one.
    /// The camera sets its own exposure, so brightness is a rough guide (it mostly shows up in dark rooms, where
    /// the camera runs out of gain); the colour is more trustworthy.
    /// </summary>
    public sealed class RoomLight
    {
        readonly Light key;
        Color tint = Color.white, wantTint = Color.white;
        float level = 1f, wantLevel = 1f;

        /// <summary>The brightness in use, 1 for an ordinary lit room (for logs and tests).</summary>
        public float Level => wantLevel;
        public Color Tint => wantTint;

        public RoomLight(Transform parent)
        {
            var go = new GameObject("Room Key Light", typeof(Light));
            go.transform.SetParent(parent, false);
            // Mostly from above, as ceiling lights and windows are, a little to one side so shapes show.
            go.transform.rotation = Quaternion.Euler(58f, 35f, 0f);
            key = go.GetComponent<Light>();
            key.type = LightType.Directional;
            key.shadows = LightShadows.None;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            Apply();
        }

        /// <summary>
        /// From the camera picture's average colour (linear, 0 to 1): the light's tint (average brightness 1) and its
        /// level (1 for a normally lit room, down to 0.3 in a dark one, up to 1.3).
        /// </summary>
        public static void Estimate(Color average, out Color tint, out float level)
        {
            float luminance = 0.2126f * average.r + 0.7152f * average.g + 0.0722f * average.b;
            // A well exposed picture averages about 0.2; the response is softened so it never swings wildly.
            level = Mathf.Clamp(Mathf.Pow(Mathf.Max(luminance, 0.001f) / 0.2f, 0.6f), 0.3f, 1.3f);
            Color hue = luminance > 0.002f ? average / luminance : Color.white;
            // Three quarters of the way to the room's colour, and never a wild cast.
            tint = new Color(
                Mathf.Clamp(Mathf.Lerp(1f, hue.r, 0.75f), 0.6f, 1.4f),
                Mathf.Clamp(Mathf.Lerp(1f, hue.g, 0.75f), 0.6f, 1.4f),
                Mathf.Clamp(Mathf.Lerp(1f, hue.b, 0.75f), 0.6f, 1.4f), 1f);
        }

        /// <summary>A new reading of the room; the lights ease over to it.</summary>
        public void Sample(Color average) => Estimate(average, out wantTint, out wantLevel);

        public void Tick(float dt)
        {
            float k = 1f - Mathf.Exp(-2.5f * dt);
            tint = Color.Lerp(tint, wantTint, k);
            level = Mathf.Lerp(level, wantLevel, k);
            Apply();
        }

        void Apply()
        {
            key.color = tint;
            key.intensity = 1.15f * level;
            RenderSettings.ambientSkyColor = tint * (0.62f * level);
            RenderSettings.ambientEquatorColor = tint * (0.45f * level);
            RenderSettings.ambientGroundColor = tint * (0.3f * level);
        }
    }
}
