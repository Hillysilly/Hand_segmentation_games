using System;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.InferenceEngine;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Repulsor
{
    /// <summary>
    /// Finds a person with BlazePose on the headset's camera, then uses live depth to place their body in the
    /// room. Development builds can replay saved pictures in place of the camera, so the whole path can be
    /// measured with nobody wearing the headset. Lines tagged [Person] appear in the headset's log.
    /// </summary>
    public sealed class PersonDetector : MonoBehaviour
    {
        const string CameraPermission = "horizonos.permission.HEADSET_CAMERA";
        const int ImageSize = 640;
        const float SecondsBetweenSlowRuns = 0.25f;
        const float SecondsBetweenFastRuns = 0.05f;
        const float SecondsBeforeCameraRestart = 3f;
        const float SecondsBetweenReports = 1f;
        const float BodyDepth = 0.12f;
        const float ReplayVerticalFov = 60f;
        const int HipLeft = 23, HipRight = 24, ShoulderLeft = 11, ShoulderRight = 12;

        // Where the camera sits on the head. It's the same side as the eye the default image comes from.
        static readonly Vector3 LensOffset = new Vector3(-0.032f, -0.02f, 0.06f);

        // Along the line from the hips to the shoulders. Rays can slip past a body, so try several.
        static readonly float[] BodyPoints = { 0f, 0.3f, 0.6f };

        [SerializeField] ModelAsset model;
        [SerializeField] ModelAsset landmarkModel;
        [SerializeField] TextAsset anchorsCsv;
        [SerializeField] ARCameraManager cameraManager;

        readonly BodyFrame frame = new BodyFrame();
        BodyTracker tracker;
        RoomSense room;
        XRCameraIntrinsics intrinsics;
        bool hasIntrinsics;
        byte[] rgba;
        NativeArray<byte> converted;
        int imageWidth;
        int imageHeight;
        Vector2 focalLength;
        Vector2 centre;
        Vector3 capturedPosition;
        Quaternion capturedRotation;
        Vector3 sighting;
        bool hasSighting;
        float nextRun;
        float nextReport;
        float failingSince = -1f;
        int imageFailures;
        bool busy;
        string[] replayFiles;
        int replayNext;
        Texture2D replayTexture;
        int runs;
        int bodies;
        int detectorRuns;
        float detectorTotal;
        float landmarkTotal;
        float wholeTotal;

        /// <summary>Set while something needs tracking, since the model costs frame rate.</summary>
        public bool Running { get; set; }

        /// <summary>Run as often as the model can, for a suit, and not just a few times a second.</summary>
        public bool Fast { get; set; }

        /// <summary>The newest body in the room. Replaced whole each time, and counted by <see cref="BodyVersion"/>.</summary>
        public Vector3[] Joints { get; } = new Vector3[BodyFrame.JointCount];

        public int BodyVersion { get; private set; }

        public void Init(RoomSense roomSense) => room = roomSense;

        /// <summary>The newest place someone was seen, once. False if nobody new has been seen.</summary>
        public bool TryTakeSighting(out Vector3 point)
        {
            point = sighting;
            bool had = hasSighting;
            hasSighting = false;
            return had;
        }

        /// <summary>Uses the pictures in the app's frames folder in place of the camera, on a loop.</summary>
        public void StartReplay()
        {
            string folder = Path.Combine(Application.persistentDataPath, "frames");
            replayFiles = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.jpg").OrderBy(f => f).ToArray() : Array.Empty<string>();
            replayNext = 0;
            replayTexture ??= new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Debug.Log($"[Person] Replaying {replayFiles.Length} pictures");
        }

        void Start()
        {
            // The CPU, not the GPU: drawing doesn't have to wait for the model.
            tracker = new BodyTracker(model, landmarkModel, anchorsCsv.text, BackendType.CPU);
            Debug.Log("[Person] Tracker ready");
            RequestCamera();
        }

        void RequestCamera()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(CameraPermission))
            {
                Debug.Log("[Person] Camera permission already granted");
                return;
            }

            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ =>
            {
                // The camera checks its permission only as it starts, so start it again.
                Debug.Log("[Person] Camera permission granted");
                RestartCamera();
            };
            callbacks.PermissionDenied += _ => Debug.Log("[Person] Camera permission denied");
            Permission.RequestUserPermission(CameraPermission, callbacks);
#endif
        }

        /// <summary>Starts the camera again. The system can close it at any time, and it won't come back alone.</summary>
        public void RestartCamera()
        {
            Debug.Log("[Person] Restarting the camera");
            cameraManager.enabled = false;
            cameraManager.enabled = true;
        }

        void Update()
        {
            Report();
            if (!Running || busy || Time.time < nextRun)
                return;
            nextRun = Time.time + (Fast ? SecondsBetweenFastRuns : SecondsBetweenSlowRuns);

            Transform head = cameraManager.transform;
            capturedPosition = head.position + head.rotation * LensOffset;
            capturedRotation = head.rotation;

            if (replayFiles != null && replayFiles.Length > 0 ? ReadReplay() : ReadCamera())
                Track();
        }

        bool ReadCamera()
        {
            if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
            {
                imageFailures++;
                if (imageFailures % 40 == 1)
                    Debug.Log($"[Person] No camera image yet ({imageFailures} tries), subsystem running: {cameraManager.subsystem?.running}");

                if (failingSince < 0f)
                {
                    failingSince = Time.time;
                }
                else if (Time.time - failingSince >= SecondsBeforeCameraRestart)
                {
                    failingSince = Time.time;
                    RestartCamera();
                }
                return false;
            }

            failingSince = -1f;
            using (image)
                Convert(image);
            return true;
        }

        /// <summary>Copies the camera image out at the size the tracker works at.</summary>
        void Convert(XRCpuImage image)
        {
            int longest = Mathf.Max(image.width, image.height);
            imageWidth = image.width * ImageSize / longest;
            imageHeight = image.height * ImageSize / longest;
            var conversion = new XRCpuImage.ConversionParams(image, TextureFormat.RGBA32, XRCpuImage.Transformation.None)
            {
                outputDimensions = new Vector2Int(imageWidth, imageHeight),
            };

            int size = image.GetConvertedDataSize(conversion);
            if (!converted.IsCreated || converted.Length != size)
            {
                if (converted.IsCreated)
                    converted.Dispose();
                converted = new NativeArray<byte>(size, Allocator.Persistent);
                rgba = new byte[size];
            }
            image.Convert(conversion, converted);
            converted.CopyTo(rgba);

            if (!hasIntrinsics)
            {
                hasIntrinsics = cameraManager.TryGetIntrinsics(out intrinsics);
                Debug.Log($"[Person] Camera image {image.width}x{image.height} format {image.format}, lens {hasIntrinsics}: "
                    + $"focal {intrinsics.focalLength}, centre {intrinsics.principalPoint}, size {intrinsics.resolution}");
            }

            var toImage = new Vector2(imageWidth / (float)intrinsics.resolution.x, imageHeight / (float)intrinsics.resolution.y);
            focalLength = Vector2.Scale(intrinsics.focalLength, toImage);
            centre = Vector2.Scale(intrinsics.principalPoint, toImage);
        }

        bool ReadReplay()
        {
            replayTexture.LoadImage(File.ReadAllBytes(replayFiles[replayNext]));
            replayNext = (replayNext + 1) % replayFiles.Length;
            imageWidth = replayTexture.width;
            imageHeight = replayTexture.height;

            int size = imageWidth * imageHeight * 4;
            if (rgba == null || rgba.Length != size)
                rgba = new byte[size];

            // Textures start at the bottom row, and the tracker wants the top row first.
            NativeArray<byte> pixels = replayTexture.GetRawTextureData<byte>();
            int stride = imageWidth * 4;
            for (int y = 0; y < imageHeight; y++)
                NativeArray<byte>.Copy(pixels, (imageHeight - 1 - y) * stride, rgba, y * stride, stride);

            float focal = imageHeight * 0.5f / Mathf.Tan(ReplayVerticalFov * 0.5f * Mathf.Deg2Rad);
            focalLength = new Vector2(focal, focal);
            centre = new Vector2(imageWidth, imageHeight) * 0.5f;
            return true;
        }

        async void Track()
        {
            busy = true;
            float started = Time.realtimeSinceStartup;
            try
            {
                bool found = await tracker.RunAsync(rgba, imageWidth, imageHeight, frame);
                runs++;
                wholeTotal += Time.realtimeSinceStartup - started;
                landmarkTotal += tracker.LandmarkMilliseconds;
                if (tracker.DetectorMilliseconds > 0f)
                {
                    detectorRuns++;
                    detectorTotal += tracker.DetectorMilliseconds;
                }

                if (found)
                {
                    bodies++;
                    Locate();
                }
            }
            catch (Exception exception)
            {
                Debug.Log($"[Person] Failed: {exception}");
            }
            busy = false;
        }

        /// <summary>Places the body in the room: live depth for how far away, or its size if that has no answer.</summary>
        void Locate()
        {
            Vector2 hips = (frame.Image[HipLeft] + frame.Image[HipRight]) * 0.5f;
            Vector2 shoulders = (frame.Image[ShoulderLeft] + frame.Image[ShoulderRight]) * 0.5f;

            bool found = false;
            float nearest = float.MaxValue;
            Vector3 direction = Vector3.forward;
            if (replayFiles == null || replayFiles.Length == 0)
            {
                foreach (float along in BodyPoints)
                {
                    Vector3 toBody = PixelDirection(Vector2.Lerp(hips, shoulders, along));
                    if (room != null && room.TryDistance(capturedPosition, toBody, out float distance) && distance < nearest)
                    {
                        found = true;
                        nearest = distance;
                        direction = toBody;
                    }
                }
            }

            float hipDistance;
            if (found)
            {
                Vector3 surface = Quaternion.Inverse(capturedRotation) * (direction * nearest);
                hipDistance = surface.z + BodyDepth;
            }
            else
            {
                hipDistance = frame.DistanceFromSize(focalLength.y);
            }

            frame.Place(capturedPosition, capturedRotation, focalLength, centre, hipDistance, Joints);
            BodyVersion++;

            sighting = (Joints[HipLeft] + Joints[HipRight]) * 0.5f;
            hasSighting = true;
        }

        /// <summary>The direction in the room through a point in the tracker's picture.</summary>
        Vector3 PixelDirection(Vector2 pixel)
        {
            var local = new Vector3((pixel.x - centre.x) / focalLength.x, (centre.y - pixel.y) / focalLength.y, 1f);
            return capturedRotation * local.normalized;
        }

        void Report()
        {
            if (Time.time < nextReport)
                return;
            nextReport = Time.time + SecondsBetweenReports;
            if (runs == 0)
                return;

            Debug.Log($"[Person] {runs} runs, {bodies} bodies, {detectorRuns} searches, landmarks {landmarkTotal / runs:0} ms, "
                + $"search {(detectorRuns > 0 ? detectorTotal / detectorRuns : 0f):0} ms, whole run {wholeTotal / runs * 1000f:0} ms, "
                + $"presence {frame.Presence:0.00}, hips {(Joints[HipLeft] + Joints[HipRight]) * 0.5f:F2}");
            runs = bodies = detectorRuns = 0;
            detectorTotal = landmarkTotal = wholeTotal = 0f;
        }

        void OnDestroy()
        {
            tracker?.Dispose();
            if (converted.IsCreated)
                converted.Dispose();
        }
    }
}
