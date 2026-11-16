using System.Collections.Generic;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Repulsor
{
    /// <summary>
    /// Finds real surfaces with the headset's live depth, so it covers walls and furniture with no
    /// room scan. It only knows about what the headset can see right now, so a ray can come back with
    /// nothing; callers must cope with that. Needs the USE_SCENE permission.
    /// </summary>
    public sealed class RoomSense : MonoBehaviour
    {
        const string ScenePermission = "com.oculus.permission.USE_SCENE";

        [SerializeField] ARRaycastManager raycaster;
        [SerializeField] AROcclusionManager occlusion;
        [SerializeField] ARShaderOcclusion shaderOcclusion;

        readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();

        /// <summary>Furniture boxes from the headset's Space Setup, once the room permission is granted.</summary>
        public ARBoundingBoxManager Boxes { get; private set; }
        /// <summary>Floor, walls and tables from Space Setup, once the room permission is granted.</summary>
        public ARPlaneManager Planes { get; private set; }
        /// <summary>Spatial anchors: points the headset keeps fixed to the real room (the multiplayer calibration).</summary>
        public ARAnchorManager Anchors { get; private set; }

        void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Anchors = raycaster.gameObject.AddComponent<ARAnchorManager>();
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(ScenePermission))
            {
                StartSensing();
                return;
            }

            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => StartSensing();
            callbacks.PermissionDenied += _ => Debug.Log("[Repulsor] Room permission denied; beams won't stop at walls");
            Permission.RequestUserPermission(ScenePermission, callbacks);
#endif
        }

        void StartSensing()
        {
            raycaster.enabled = true;
            occlusion.enabled = true;
            shaderOcclusion.enabled = true;
            Boxes = raycaster.gameObject.AddComponent<ARBoundingBoxManager>();
            Planes = raycaster.gameObject.AddComponent<ARPlaneManager>();
            Planes.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            Debug.Log("[Repulsor] Room sensing on");
        }

        /// <summary>Distance along a ray to the nearest real surface, if the headset can tell.</summary>
        public bool TryDistance(Vector3 start, Vector3 direction, out float distance) =>
            TryHit(start, direction, out distance, out _);

        /// <summary>As <see cref="TryDistance"/>, and also which way that surface faces.</summary>
        public bool TryHit(Vector3 start, Vector3 direction, out float distance, out Vector3 normal)
        {
            distance = 0f;
            normal = -direction;
            if (!raycaster.enabled || !raycaster.Raycast(new Ray(start, direction), hits, TrackableType.Depth))
                return false;

            Pose pose = hits[0].pose;
            distance = hits[0].distance;
            normal = GameMath.SurfaceNormal(direction, pose.up, pose.forward, pose.right);
            return true;
        }
    }
}
