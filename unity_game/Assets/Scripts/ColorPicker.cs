using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// A colour wheel worked with bare hands. Turn an open palm towards your face and the wheel appears above
    /// it; touch the wheel with the other hand's index finger to pick a colour.
    /// </summary>
    public sealed class ColorPicker : MonoBehaviour
    {
        const float HoldSeconds = 0.35f;
        const float HideSeconds = 0.3f;
        const float MinFacingHead = 0.65f;
        const float WheelRadius = 0.1f;
        const float AbovePalm = 0.11f;
        const float TouchDepth = 0.06f;

        static readonly int Picked = Shader.PropertyToID("_Picked");

        [SerializeField] Renderer wheel;

        Material material;
        float held;
        float hidden;

        void Awake()
        {
            material = wheel.material;
            transform.localScale = Vector3.one * (WheelRadius * 2f);
            wheel.enabled = false;
        }

        public void Hide()
        {
            held = 0f;
            wheel.enabled = false;
        }

        /// <summary>Returns true with a colour when a finger is touching the wheel.</summary>
        public bool Tick(HandRepulsor left, HandRepulsor right, Vector3 head, float dt, out Color color)
        {
            color = default;
            HandRepulsor holder = FacesHead(left, head) ? left : FacesHead(right, head) ? right : null;

            if (holder == null)
            {
                held = 0f;
                hidden += dt;
                if (hidden >= HideSeconds)
                    wheel.enabled = false;
                return false;
            }

            hidden = 0f;
            held += dt;
            if (held < HoldSeconds)
                return false;

            wheel.enabled = true;
            Vector3 position = holder.PalmPosition + holder.PalmNormal * AbovePalm;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - head));

            HandRepulsor toucher = holder == left ? right : left;
            if (!toucher.IsTracked)
                return false;

            Vector3 local = Quaternion.Inverse(transform.rotation) * (toucher.IndexTip - position);
            var offset = new Vector2(local.x, local.y) / WheelRadius;
            if (Mathf.Abs(local.z) > TouchDepth || offset.magnitude > 1.05f)
                return false;

            material.SetVector(Picked, Vector2.ClampMagnitude(offset, 1f));
            color = GameMath.WheelColor(offset);
            return true;
        }

        static bool FacesHead(HandRepulsor hand, Vector3 head) =>
            hand.IsOpen && Vector3.Dot(hand.PalmNormal, (head - hand.PalmPosition).normalized) > MinFacingHead;
    }
}
