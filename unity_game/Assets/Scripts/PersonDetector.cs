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

        // Where the camera sits on the head and how it's turned, from the headset's lens calibration. It's the
        // same side as the eye the default image comes from, and it looks about 11 degrees below the head.
        static readonly Vector3 LensOffset = new Vector3(-0.0319f, -0.0181f, 0.0627f);
        public static readonly Quaternion LensRotation = new Quaternion(0.0966f, 0.0038f, -0.0035f, 0.9953f);

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
        byte[][] replayPictures;
        int replayNext;
        int runs;
        int bodies;
        int detectorRuns;
        float detectorTotal;
        float landmarkTotal;
        float wholeTotal;
        float readTotal;
        float startTotal;

        /// <summary>Set while something needs tracking, since the model costs frame rate.</summary>
        public bool Running { get; set; }

        /// <summary>Run as often as the model can, for a suit, and not just a few times a second.</summary>
        public bool Fast { get; set; }

        /// <summary>The newest body in the room. Replaced whole each time, and counted by <see cref="BodyVersion"/>.</summary>
        public Vector3[] Joints { get; } = new Vector3[BodyFrame.JointCount];

        /// <summary>How sure the tracker is that each joint of the newest body is in view, 0 to 1.</summary>
        public float[] Seen => frame.Seen;

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
            if (Replaying)
                return;

            string folder = Path.Combine(Application.persistentDataPath, "frames");
            string[] files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.jpg").OrderBy(f => f).ToArray() : Array.Empty<string>();

            // Decoded now, all at once, because decoding during play would hold up frames as real play doesn't.
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var pictures = new byte[files.Length][];
            for (int i = 0; i < files.Length; i++)
            {
                texture.LoadImage(File.ReadAllBytes(files[i]));
                imageWidth = texture.width;
                imageHeight = texture.height;
                pictures[i] = new byte[imageWidth * imageHeight * 4];
                BodyTracker.CopyPicture(texture, pictures[i]);
            }

            Destroy(texture);
            replayPictures = pictures;
            replayNext = 0;
            Debug.Log($"[Person] Replaying {pictures.Length} pictures");
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

        static float[] toLinear;

        /// <summary>
        /// The average colour of what the camera sees right now (linear, 0 to 1), from a tiny copy of its picture: for
        /// lighting the enemies like the room. False when there's no camera picture (no permission, the editor).
        /// </summary>
        public bool TrySampleLight(out Color average)
        {
            average = Color.gray;
            if (cameraManager == null || !cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
                return false;
            using (image)
            {
                int width = Mathf.Max(8, image.width / 24), height = Mathf.Max(8, image.height / 24);
                var conversion = new XRCpuImage.ConversionParams(image, TextureFormat.RGBA32, XRCpuImage.Transformation.None)
                {
                    outputDimensions = new Vector2Int(width, height),
                };
                var pixels = new NativeArray<byte>(image.GetConvertedDataSize(conversion), Allocator.Temp);
                image.Convert(conversion, pixels);
                if (toLinear == null)
                {
                    toLinear = new float[256];
                    for (int i = 0; i < 256; i++)
                        toLinear[i] = Mathf.GammaToLinearSpace(i / 255f);
                }
                double r = 0, g = 0, b = 0;
                int count = pixels.Length / 4;
                for (int i = 0; i < count; i++)
                {
                    r += toLinear[pixels[i * 4]];
                    g += toLinear[pixels[i * 4 + 1]];
                    b += toLinear[pixels[i * 4 + 2]];
                }
                pixels.Dispose();
                if (count == 0)
                    return false;
                average = new Color((float)(r / count), (float)(g / count), (float)(b / count), 1f);
            }
            return true;
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
            capturedRotation = head.rotation * LensRotation;

            float readStarted = Time.realtimeSinceStartup;
            bool haveImage = Replaying ? ReadReplay() : ReadCamera();
            readTotal += Time.realtimeSinceStartup - readStarted;
            if (haveImage)
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

        bool Replaying => replayPictures != null && replayPictures.Length > 0;

        bool ReadReplay()
        {
            rgba = replayPictures[replayNext];
            replayNext = (replayNext + 1) % replayPictures.Length;

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
                // Everything up to the first wait holds up the frame being drawn.
                Awaitable<bool> run = tracker.RunAsync(rgba, imageWidth, imageHeight, frame);
                startTotal += Time.realtimeSinceStartup - started;
                bool found = await run;
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
            if (!Replaying)
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
                + $"holding up frames: read {readTotal / runs * 1000f:0} ms, start {startTotal / runs * 1000f:0} ms, "
                + $"presence {frame.Presence:0.00}, hips {(Joints[HipLeft] + Joints[HipRight]) * 0.5f:F2}");
            runs = bodies = detectorRuns = 0;
            detectorTotal = landmarkTotal = wholeTotal = readTotal = startTotal = 0f;
        }

        void OnDestroy()
        {
            tracker?.Dispose();
            if (converted.IsCreated)
                converted.Dispose();
        }
    }
}
