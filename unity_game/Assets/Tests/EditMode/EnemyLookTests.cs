using NUnit.Framework;
using UnityEngine;

namespace Repulsor.Tests
{
    public class EnemyLookTests
    {
        [Test]
        public void EveryEnemyModelLoadsWithGlowingEyes()
        {
            foreach (EnemySpec spec in EnemySpec.All)
            {
                EnemyLook look = EnemyLook.Create(spec.Model, null);
                try
                {
                    Assert.That(look, Is.Not.Null, spec.Model);
                    if (spec.Kind == EnemyKind.Boss)
                        continue;   // its glow is its own emissive lights
                    Assert.That(look.GetComponentsInChildren<EnemyEye>(), Is.Not.Empty, spec.Model + " has no eyes");
                    Assert.That(look.GetComponentsInChildren<FxSpin>(), Is.Not.Empty, spec.Model + " eyes don't glow");
                }
                finally
                {
                    if (look != null)
                        Object.DestroyImmediate(look.gameObject);
                }
            }
        }

        [Test]
        public void TallEnemiesAreHitOnTheHeadAndLegs()
        {
            // A capsule from 0.25 to 1.05 m with radius 0.24 (a 1.3 m gunner standing at the origin).
            Vector3 bottom = new Vector3(0f, 0.24f, 0f), top = new Vector3(0f, 1.06f, 0f);
            Assert.That(GameMath.SegmentHitsCapsule(new Vector3(-1f, 1.2f, 0f), new Vector3(1f, 1.2f, 0f), bottom, top, 0.24f), Is.True, "head");
            Assert.That(GameMath.SegmentHitsCapsule(new Vector3(-1f, 0.15f, 0f), new Vector3(1f, 0.15f, 0f), bottom, top, 0.24f), Is.True, "shins");
            Assert.That(GameMath.SegmentHitsCapsule(new Vector3(-1f, 1.4f, 0f), new Vector3(1f, 1.4f, 0f), bottom, top, 0.24f), Is.False, "over its head");
            Assert.That(GameMath.RayHitsCapsule(new Vector3(0f, 1.2f, -3f), Vector3.forward, 10f, bottom, top, 0.24f, out float d), Is.True);
            Assert.That(d, Is.InRange(2.7f, 2.95f));
        }
    }
}
