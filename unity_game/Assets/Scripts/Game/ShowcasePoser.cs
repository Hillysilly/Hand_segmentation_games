using UnityEngine;

namespace Repulsor
{
    /// <summary>Calls back after the animations have run each frame, so the select screen can pose arms and hands over them.</summary>
    public sealed class ShowcasePoser : MonoBehaviour
    {
        public System.Action Late;

        void LateUpdate() => Late?.Invoke();
    }
}
