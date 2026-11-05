using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// Drops the room's brightness while something bright is firing, like a camera's exposure, so the
    /// glow reads as powerful. A dark quad just in front of the camera. Call <see cref="Hold"/> each frame.
    /// </summary>
    public sealed class RoomDim : MonoBehaviour
    {
        const float MaxAmount = 0.34f;
        const float DimSeconds = 0.1f;
        const float RecoverSeconds = 0.45f;

        static readonly int Amount = Shader.PropertyToID("_Amount");

        Renderer quad;
        Material material;
        float amount;
        bool held;

        void Awake()
        {
            quad = GetComponent<Renderer>();
            material = quad.material;
            quad.enabled = false;
        }

        public void Hold() => held = true;

        void LateUpdate()
        {
            float seconds = held ? DimSeconds : RecoverSeconds;
            amount = Mathf.MoveTowards(amount, held ? MaxAmount : 0f, MaxAmount * Time.deltaTime / seconds);
            held = false;

            quad.enabled = amount > 0f;
            if (quad.enabled)
                material.SetFloat(Amount, amount);
        }
    }
}
