using NUnit.Framework;
using UnityEngine;

namespace Repulsor.Tests
{
    /// <summary>Every PlayMode test runs silent (the user asked: no sound from the PC while tests run).</summary>
    [SetUpFixture]
    public class Muted
    {
        [OneTimeSetUp]
        public void Mute() => AudioListener.volume = 0f;

        [OneTimeTearDown]
        public void Unmute() => AudioListener.volume = 1f;
    }
}
