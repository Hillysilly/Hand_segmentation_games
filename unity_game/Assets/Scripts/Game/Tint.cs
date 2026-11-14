using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// Recolours an effect built from the energy shaders (ball, comet tail, laser, lightning, sparks) to a
    /// player's colour, without touching the shared materials.
    /// </summary>
    public static class Tint
    {
        static readonly int Core = Shader.PropertyToID("_CoreColor");
        static readonly int Outer = Shader.PropertyToID("_OuterColor");
        static readonly int Glow = Shader.PropertyToID("_GlowColor");
        static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly MaterialPropertyBlock lineBlock = new MaterialPropertyBlock();

        /// <summary>Colours a line drawn with the plain unlit aim-line material (it ignores the line's own colours).</summary>
        public static void Line(LineRenderer line, Color color)
        {
            line.GetPropertyBlock(lineBlock);
            lineBlock.SetColor(BaseColor, color);
            line.SetPropertyBlock(lineBlock);
        }

        public static void Apply(GameObject root, Color color)
        {
            Color core = Color.Lerp(color, Color.white, 0.55f);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer)
                    continue;
                renderer.GetPropertyBlock(block);
                block.SetColor(Core, core);
                block.SetColor(Outer, color);
                block.SetColor(Glow, core);
                renderer.SetPropertyBlock(block);
            }
            foreach (ParticleSystem particles in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particles.main;
                main.startColor = new ParticleSystem.MinMaxGradient(core, color);
            }
        }
    }
}
