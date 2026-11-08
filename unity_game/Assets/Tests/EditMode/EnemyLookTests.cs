using NUnit.Framework;
using UnityEngine;

namespace Repulsor.Tests
{
    public class EnemyLookTests
    {
        [Test]
        public void EveryEnemyModelLoadsWithGlowingEyes()
        {
            foreach (string key in EnemyLook.RobotKeys)
                CheckModel(key);
            foreach (string key in EnemyLook.DroneKeys)
                CheckModel(key);
        }

        static void CheckModel(string key)
        {
            if (key == null)
                return;   // the old placeholder shape
            EnemyLook look = EnemyLook.Create(key, null);
            try
            {
                Assert.That(look, Is.Not.Null, key);
                Assert.That(look.GetComponentsInChildren<EnemyEye>(), Is.Not.Empty, key + " has no eyes");
                Assert.That(look.GetComponentsInChildren<FxSpin>(), Is.Not.Empty, key + " eyes don't glow");
            }
            finally
            {
                if (look != null)
                    Object.DestroyImmediate(look.gameObject);
            }
        }

        [Test]
        public void TheLastChoiceIsThePlaceholder()
        {
            Assert.That(EnemyLook.KeyFor(EnemyKind.Robot, EnemyLook.RobotKeys.Length - 1), Is.Null);
            Assert.That(EnemyLook.KeyFor(EnemyKind.Drone, EnemyLook.DroneKeys.Length - 1), Is.Null);
            Assert.That(EnemyLook.RobotNames.Length, Is.EqualTo(EnemyLook.RobotKeys.Length));
            Assert.That(EnemyLook.DroneNames.Length, Is.EqualTo(EnemyLook.DroneKeys.Length));
        }
    }
}
