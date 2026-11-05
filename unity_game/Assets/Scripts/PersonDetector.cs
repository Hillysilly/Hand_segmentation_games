using System;
using System.Globalization;
using System.IO;
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
    /// Finds a person with a model: it runs BlazePose's detector on the headset's camera a few times a
    /// second, then uses live depth along that direction to place them in the room. Development builds also
    /// save what the model saw to the app's files. Lines tagged [Person] appear in the headset's log.
    /// </summary>
    public sealed class PersonDetector : MonoBehaviour
    {
        const string CameraPermission = "horizonos.permission.HEADSET_CAMERA";
        const int InputSize = 224;
        const int AnchorCount = 2254;
        const float SecondsBetweenRuns = 0.25f;
        const float SecondsBetweenSnapshots = 2f;
        const float MinScore = 0.5f;
        const int SnapshotsKept = 6;
        const float BodyDepth = 0.12f;
        const float BodyHalfWidth = 0.2f;

        // Where the camera sits on the head. It's the same side as the eye the default image comes from.
        static readonly Vector3 LensOffset = new Vector3(-0.032f, -0.02f, 0.06f);

        // Along the line from the hips towards the upper body. Rays can slip past a body, so try several.
        static readonly float[] BodyPoints = { 0f, 0.25f, 0.5f };

        [SerializeField] ModelAsset model;
        [SerializeField] TextAsset anchorsCsv;
        [SerializeField] ARCameraManager cameraManager;

        /// <summary>Set while something needs tracking, since the model costs frame rate.</summary>
        public bool Running { get; set; }

        readonly float[] pixels = new float[InputSize * InputSize * 3];
        RoomSense room;
        XRCameraIntrinsics intrinsics;
        bool hasIntrinsics;
        Vector2Int imageSize;
        Vector2Int imageCorner;
        float imageScale;
        Vector3 capturedPosition;
        Quaternion capturedRotation;
        Vector3 sighting;
        bool hasSighting;
        float[,] anchors;
        Worker worker;
        NativeArray<byte> converted;
        float nextRun;
        float nextSnapshot;
        int snapshotNumber;
        int imageFailures;
        bool busy;

        public void Init(RoomSense roomSense) => room = roomSense;

        /// <summary>The newest place someone was seen, once. False if nobody new has been seen.</summary>
        public bool TryTakeSighting(out Vector3 point)
        {
            point = sighting;
            bool had = hasSighting;
            hasSighting = false;
            return had;
        }

        void Start()
        {
            anchors = LoadAnchors(anchorsCsv.text);

            // The CPU, not the GPU: the model takes about as long, but drawing doesn't have to wait for it.
            worker = new Worker(ModelLoader.Load(model), BackendType.CPU);
            Debug.Log("[Person] Detector ready");
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
                cameraManager.enabled = false;
                cameraManager.enabled = true;
            };
            callbacks.PermissionDenied += _ => Debug.Log("[Person] Camera permission denied");
            Permission.RequestUserPermission(CameraPermission, callbacks);
#endif
        }

        void Update()
        {
            if (!Running || busy || Time.time < nextRun)
                return;
            nextRun = Time.time + SecondsBetweenRuns;

            if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
            {
                imageFailures++;
                if (imageFailures % 40 == 1)
                    Debug.Log($"[Person] No camera image yet ({imageFailures} tries), subsystem running: {cameraManager.subsystem?.running}");
                return;
            }

            // The image is from about now, so this is close to where the camera was when it was taken.
            Transform head = cameraManager.transform;
            capturedPosition = head.position + head.rotation * LensOffset;
            capturedRotation = head.rotation;

            float started = Time.realtimeSinceStartup;
            using (image)
                FillInput(image);
            float prepared = Time.realtimeSinceStartup;

            Detect(started, prepared);
        }

        /// <summary>Shrinks the image to fit the model's square input, keeping its shape, with black bars.</summary>
        void FillInput(XRCpuImage image)
        {
            int longest = Mathf.Max(image.width, image.height);
            int width = image.width * InputSize / longest;
            int height = image.height * InputSize / longest;
            var conversion = new XRCpuImage.ConversionParams(image, TextureFormat.RGBA32, XRCpuImage.Transformation.None)
            {
                outputDimensions = new Vector2Int(width, height),
            };

            int size = image.GetConvertedDataSize(conversion);
            if (!converted.IsCreated || converted.Length != size)
            {
                if (converted.IsCreated)
                    converted.Dispose();
                converted = new NativeArray<byte>(size, Allocator.Persistent);
            }
            image.Convert(conversion, converted);

            Array.Clear(pixels, 0, pixels.Length);
            int left = (InputSize - width) / 2, top = (InputSize - height) / 2;
            imageSize = new Vector2Int(image.width, image.height);
            imageCorner = new Vector2Int(left, top);
            imageScale = longest / (float)InputSize;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int from = (y * width + x) * 4;
                    int to = ((y + top) * InputSize + x + left) * 3;
                    pixels[to] = converted[from] / 255f;
                    pixels[to + 1] = converted[from + 1] / 255f;
                    pixels[to + 2] = converted[from + 2] / 255f;
                }
            }

            if (imageFailures >= 0)
            {
                hasIntrinsics = cameraManager.TryGetIntrinsics(out intrinsics);
                Debug.Log($"[Person] Camera image {image.width}x{image.height} format {image.format}, lens {hasIntrinsics}: "
                    + $"focal {intrinsics.focalLength}, centre {intrinsics.principalPoint}, size {intrinsics.resolution}");
                imageFailures = -1;
            }
        }

        async void Detect(float started, float prepared)
        {
            busy = true;
            try
            {
                using var input = new Tensor<float>(new TensorShape(1, InputSize, InputSize, 3), pixels);
                worker.Schedule(input);

                using Tensor<float> boxes = await (worker.PeekOutput(0) as Tensor<float>).ReadbackAndCloneAsync();
                using Tensor<float> scores = await (worker.PeekOutput(1) as Tensor<float>).ReadbackAndCloneAsync();
                float finished = Time.realtimeSinceStartup;

                int best = 0;
                float bestScore = float.MinValue;
                for (int i = 0; i < AnchorCount; i++)
                {
                    if (scores[0, i, 0] > bestScore)
                    {
                        bestScore = scores[0, i, 0];
                        best = i;
                    }
                }

                float score = 1f / (1f + Mathf.Exp(-bestScore));
                float anchorX = InputSize * anchors[best, 0], anchorY = InputSize * anchors[best, 1];
                float hipX = anchorX + boxes[0, best, 4], hipY = anchorY + boxes[0, best, 5];
                float aboveX = anchorX + boxes[0, best, 6], aboveY = anchorY + boxes[0, best, 7];
                float radius = Vector2.Distance(new Vector2(hipX, hipY), new Vector2(aboveX, aboveY));

                Debug.Log($"[Person] {(score >= MinScore ? "FOUND" : "none")} score {score:0.00}, hips ({hipX:0}, {hipY:0}), "
                    + $"radius {radius:0}, prepare {(prepared - started) * 1000f:0} ms, model {(finished - prepared) * 1000f:0} ms");

                if (score >= MinScore)
                    Locate(new Vector2(hipX, hipY), new Vector2(aboveX, aboveY));

#if DEVELOPMENT_BUILD
                if (Time.time >= nextSnapshot)
                {
                    nextSnapshot = Time.time + SecondsBetweenSnapshots;
                    SaveSnapshot(score, hipX, hipY, radius);
                }
#endif
            }
            catch (Exception exception)
            {
                Debug.Log($"[Person] Failed: {exception}");
            }
            busy = false;
        }

        /// <summary>Turns where the model saw someone into a point in the room, using live depth.</summary>
        void Locate(Vector2 hips, Vector2 upperBody)
        {
            if (!hasIntrinsics || room == null)
                return;

            bool found = false;
            float nearest = float.MaxValue;
            Vector3 direction = Vector3.forward;
            foreach (float along in BodyPoints)
            {
                Vector3 toBody = PixelDirection(Vector2.Lerp(hips, upperBody, along));
                if (room.TryDistance(capturedPosition, toBody, out float distance) && distance < nearest)
                {
                    found = true;
                    nearest = distance;
                    direction = toBody;
                }
            }

            if (!found)
                return;

            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            sighting = capturedPosition + direction * nearest + flat * BodyDepth;
            hasSighting = true;
            Debug.Log($"[Person] Sighting at ({sighting.x:0.00}, {sighting.y:0.00}, {sighting.z:0.00}), {nearest:0.00} m away");
        }

        /// <summary>The direction in the room through a point in the model's picture.</summary>
        Vector3 PixelDirection(Vector2 modelPixel)
        {
            float toLensX = intrinsics.resolution.x / (float)imageSize.x;
            float toLensY = intrinsics.resolution.y / (float)imageSize.y;
            float x = (modelPixel.x - imageCorner.x) * imageScale * toLensX;
            float y = (modelPixel.y - imageCorner.y) * imageScale * toLensY;

            var local = new Vector3(
                (x - intrinsics.principalPoint.x) / intrinsics.focalLength.x,
                (intrinsics.principalPoint.y - y) / intrinsics.focalLength.y,
                1f);
            return capturedRotation * local.normalized;
        }

        /// <summary>Saves what the model saw, with a green mark on the hips if it found someone.</summary>
        void SaveSnapshot(float score, float hipX, float hipY, float radius)
        {
            var picture = new Texture2D(InputSize, InputSize, TextureFormat.RGB24, false);
            for (int y = 0; y < InputSize; y++)
            {
                for (int x = 0; x < InputSize; x++)
                {
                    int i = (y * InputSize + x) * 3;
                    picture.SetPixel(x, InputSize - 1 - y, new Color(pixels[i], pixels[i + 1], pixels[i + 2]));
                }
            }

            Color mark = score >= MinScore ? Color.green : Color.red;
            for (int angle = 0; angle < 360; angle += 3)
            {
                float a = angle * Mathf.Deg2Rad;
                picture.SetPixel(Mathf.RoundToInt(hipX + Mathf.Cos(a) * radius),
                    InputSize - 1 - Mathf.RoundToInt(hipY + Mathf.Sin(a) * radius), mark);
                picture.SetPixel(Mathf.RoundToInt(hipX + Mathf.Cos(a) * 4f),
                    InputSize - 1 - Mathf.RoundToInt(hipY + Mathf.Sin(a) * 4f), mark);
            }
            picture.Apply();

            string path = Path.Combine(Application.persistentDataPath, $"person_{snapshotNumber++ % SnapshotsKept}.png");
            File.WriteAllBytes(path, picture.EncodeToPNG());
            Destroy(picture);
        }

        static float[,] LoadAnchors(string csv)
        {
            var result = new float[AnchorCount, 4];
            string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < AnchorCount; i++)
            {
                string[] values = lines[i].Split(',');
                for (int j = 0; j < 4; j++)
                    result[i, j] = float.Parse(values[j], CultureInfo.InvariantCulture);
            }
            return result;
        }

        void OnDestroy()
        {
            worker?.Dispose();
            if (converted.IsCreated)
                converted.Dispose();
        }
    }
}
