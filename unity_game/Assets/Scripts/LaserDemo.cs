using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// Development builds only: runs the laser with no hands, from beside the head to wherever the
    /// headset faces, so its look and wall stopping can be checked on the headset. Set it before
    /// launching with: adb shell setprop debug.repulsor.laserdemo 1
    /// </summary>
    public sealed class LaserDemo : MonoBehaviour
    {
        const float ReportSeconds = 2f;

        [SerializeField] LaserBeamView beam;
        [SerializeField] RoomSense room;
        [SerializeField] RoomDim roomDim;
        [SerializeField] Transform head;

        readonly BeamLength length = new BeamLength();
        float nextReport;
        int frames;
        int surfaceFrames;

        void Start()
        {
            enabled = IsRequested();
            if (enabled)
                Debug.Log("[Repulsor] Laser demo on");
        }

        void Update()
        {
            Vector3 start = head.position + head.forward * 0.35f + head.right * 0.18f - head.up * 0.18f;
            Vector3 direction = (head.forward * 3f - head.right * 0.3f).normalized;

            bool surface = room.TryHit(start, direction, out float distance, out Vector3 normal);
            float beamLength = length.Step(surface, distance, Time.deltaTime);
            beam.Aim(start, start + direction * beamLength, length.Hitting, normal);
            roomDim.Hold();

            frames++;
            if (surface)
                surfaceFrames++;
            if (Time.time >= nextReport)
            {
                nextReport = Time.time + ReportSeconds;
                Debug.Log($"[Repulsor] Demo: surface on {surfaceFrames}/{frames} frames, beam {beamLength:0.00} m");
                frames = 0;
                surfaceFrames = 0;
            }
        }

        static bool IsRequested()
        {
#if DEVELOPMENT_BUILD && UNITY_ANDROID && !UNITY_EDITOR
            using var properties = new AndroidJavaClass("android.os.SystemProperties");
            return properties.CallStatic<string>("get", "debug.repulsor.laserdemo") == "1";
#else
            return false;
#endif
        }
    }
}
