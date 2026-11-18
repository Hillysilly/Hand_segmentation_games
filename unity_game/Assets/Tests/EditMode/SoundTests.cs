using NUnit.Framework;
using UnityEngine;

namespace Repulsor.Tests
{
    /// <summary>The synthesized sounds added 11/17 for the moves that had none: they render, aren't silent, don't clip.</summary>
    public class SoundTests
    {
        static void Check(AudioClip clip, float seconds)
        {
            Assert.That(clip, Is.Not.Null);
            Assert.That(clip.length, Is.EqualTo(seconds).Within(0.01f), clip.name);
            var data = new float[clip.samples];
            clip.GetData(data, 0);
            float peak = 0f;
            foreach (float v in data)
                peak = Mathf.Max(peak, Mathf.Abs(v));
            Assert.That(peak, Is.GreaterThan(0.05f), $"{clip.name} is silent");
            Assert.That(peak, Is.LessThanOrEqualTo(1f), $"{clip.name} clips");
        }

        [Test]
        public void NewMoveSoundsRenderWithoutClipping()
        {
            Check(Sfx.Charge(), 1f);
            Check(Sfx.Strain(), 1f);
            Check(Sfx.Wind(), 2f);
            Check(Sfx.Crackle(), 1f);
            Check(Sfx.Puff(), 0.6f);
        }
    }
}
