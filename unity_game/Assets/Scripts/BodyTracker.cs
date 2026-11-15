using System;
using System.Globalization;
using Unity.InferenceEngine;
using UnityEngine;

namespace Repulsor
{
    /// <summary>One person's body from one image.</summary>
    public sealed class BodyFrame
    {
        public const int JointCount = 33;

        /// <summary>Joints in the source image, in pixels from its top left.</summary>
        public readonly Vector2[] Image = new Vector2[JointCount];

        /// <summary>Joints in metres from the hips, with x to the image's right, y down and z away from the camera.</summary>
        public readonly Vector3[] World = new Vector3[JointCount];

        /// <summary>How sure the model is that each joint is in view, 0 to 1.</summary>
        public readonly float[] Seen = new float[JointCount];

        public float Presence;

        /// <summary>
        /// How far along the camera's axis the hips are, from how big the body looks. For when live depth has
        /// no answer. <paramref name="focalLength"/> is in the same pixels as <see cref="Image"/>.
        /// </summary>
        public float DistanceFromSize(float focalLength)
        {
            const int ShoulderLeft = 11, ShoulderRight = 12, HipLeft = 23, HipRight = 24;
            Vector2 imageUp = (Image[ShoulderLeft] + Image[ShoulderRight] - Image[HipLeft] - Image[HipRight]) * 0.5f;
            Vector3 worldUp = (World[ShoulderLeft] + World[ShoulderRight] - World[HipLeft] - World[HipRight]) * 0.5f;
            return focalLength * new Vector2(worldUp.x, worldUp.y).magnitude / Mathf.Max(imageUp.magnitude, 1f);
        }

        /// <summary>
        /// Places the joints in the room. Each lies on its own line of sight, so the body lines up with the
        /// person in the picture, at the hips' distance plus its depth from the hips.
        /// </summary>
        public void Place(Vector3 cameraPosition, Quaternion cameraRotation, Vector2 focalLength, Vector2 centre,
            float hipDistance, Vector3[] joints)
        {
            for (int i = 0; i < JointCount; i++)
            {
                var line = new Vector3((Image[i].x - centre.x) / focalLength.x, (centre.y - Image[i].y) / focalLength.y, 1f);
                joints[i] = cameraPosition + cameraRotation * (line * (hipDistance + World[i].z));
            }
        }
    }

    /// <summary>
    /// BlazePose: a detector finds someone, then a landmark model reads their 33 joints from a square crop
    /// around them. Each result says where the next crop goes, so the detector only runs to find someone new.
    /// The image is RGBA bytes with its first row at the top.
    /// </summary>
    public sealed class BodyTracker : IDisposable
    {
        const int DetectorSize = 224;
        const int LandmarkSize = 256;
        const int AnchorCount = 2254;
        const int ModelJoints = 39;
        const int HipCentre = 33;
        const int BodyEdge = 34;
        const int ShoulderLeft = 11, ShoulderRight = 12, HipLeft = 23, HipRight = 24;
        const float MinDetection = 0.5f;
        const float MinPresence = 0.5f;
        const float CropScale = 1.25f;

        // Where to search, in turn, as a square's centre across the image and its size against the image's
        // longest side. Someone far away is too small to find in the whole picture, so most tries zoom in.
        static readonly Vector3[] SearchWindows =
        {
            new Vector3(0.5f, 0.5f, 1f),
            new Vector3(0.5f, 0.5f, 0.56f),
            new Vector3(0.3f, 0.5f, 0.56f),
            new Vector3(0.7f, 0.5f, 0.56f),
        };

        readonly Worker detector;
        readonly Worker landmarker;
        readonly float[,] anchors;
        readonly float[] detectorPixels = new float[DetectorSize * DetectorSize * 3];
        readonly float[] landmarkPixels = new float[LandmarkSize * LandmarkSize * 3];

        bool tracking;
        int searchWindow;
        float windowLeft;
        float windowTop;
        float windowScale;
        Vector2 cropCentre;
        float cropRadius;
        float cropSin;
        float cropCos;

        /// <summary>The last crop given to the landmark model, as RGB rows from the top, for checking it.</summary>
        public float[] LastCrop => landmarkPixels;

        /// <summary>Where the crop was and how it was turned, for checking it.</summary>
        public string CropDescription => $"centre {cropCentre}, radius {cropRadius:0}, sin {cropSin:0.00}, cos {cropCos:0.00}";

        /// <summary>Milliseconds spent in the last run on the detector, or 0 if it wasn't needed.</summary>
        public float DetectorMilliseconds { get; private set; }

        /// <summary>Milliseconds spent in the last run on the landmark model.</summary>
        public float LandmarkMilliseconds { get; private set; }

        public BodyTracker(ModelAsset detectorModel, ModelAsset landmarkModel, string anchorsCsv, BackendType backend)
        {
            anchors = LoadAnchors(anchorsCsv);
            detector = new Worker(ModelLoader.Load(detectorModel), backend);
            landmarker = new Worker(ModelLoader.Load(landmarkModel), backend);
        }

        /// <summary>
        /// Copies a loaded picture into RGBA bytes for the tracker, top row first. Reads colours rather than the
        /// raw data, because how a decoded JPEG is stored differs between the editor and the headset.
        /// </summary>
        public static void CopyPicture(Texture2D picture, byte[] rgba)
        {
            Color32[] pixels = picture.GetPixels32();
            int width = picture.width, height = picture.height;
            for (int y = 0; y < height; y++)
            {
                int source = (height - 1 - y) * width;
                int target = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    Color32 pixel = pixels[source + x];
                    rgba[target + x * 4] = pixel.r;
                    rgba[target + x * 4 + 1] = pixel.g;
                    rgba[target + x * 4 + 2] = pixel.b;
                    rgba[target + x * 4 + 3] = pixel.a;
                }
            }
        }

        /// <summary>Forgets the person, so the next run searches the whole image.</summary>
        public void Reset() => tracking = false;

        /// <summary>Runs on the image, stalling until done. For tests: use <see cref="RunAsync"/> in the game.</summary>
        public bool Run(byte[] rgba, int width, int height, BodyFrame frame)
        {
            float started = Time.realtimeSinceStartup;
            DetectorMilliseconds = 0f;
            if (!tracking)
            {
                FillDetectorInput(rgba, width, height);
                using var input = new Tensor<float>(new TensorShape(1, DetectorSize, DetectorSize, 3), detectorPixels);
                detector.Schedule(input);
                using Tensor<float> boxes = (detector.PeekOutput(0) as Tensor<float>).ReadbackAndClone();
                using Tensor<float> scores = (detector.PeekOutput(1) as Tensor<float>).ReadbackAndClone();
                DetectorMilliseconds = (Time.realtimeSinceStartup - started) * 1000f;
                if (!StartCrop(boxes, scores, width, height))
                    return false;
            }

            started = Time.realtimeSinceStartup;
            FillLandmarkInput(rgba, width, height);
            using var crop = new Tensor<float>(new TensorShape(1, LandmarkSize, LandmarkSize, 3), landmarkPixels);
            landmarker.Schedule(crop);
            using Tensor<float> joints = (landmarker.PeekOutput("Identity") as Tensor<float>).ReadbackAndClone();
            using Tensor<float> presence = (landmarker.PeekOutput("Identity_1") as Tensor<float>).ReadbackAndClone();
            using Tensor<float> world = (landmarker.PeekOutput("Identity_4") as Tensor<float>).ReadbackAndClone();
            LandmarkMilliseconds = (Time.realtimeSinceStartup - started) * 1000f;
            return ReadJoints(joints, world, presence, frame);
        }

        /// <summary>Runs on the image without stalling the game.</summary>
        public async Awaitable<bool> RunAsync(byte[] rgba, int width, int height, BodyFrame frame)
        {
            float started = Time.realtimeSinceStartup;
            DetectorMilliseconds = 0f;
            if (!tracking)
            {
                FillDetectorInput(rgba, width, height);
                using var input = new Tensor<float>(new TensorShape(1, DetectorSize, DetectorSize, 3), detectorPixels);
                detector.Schedule(input);
                using Tensor<float> boxes = await (detector.PeekOutput(0) as Tensor<float>).ReadbackAndCloneAsync();
                using Tensor<float> scores = await (detector.PeekOutput(1) as Tensor<float>).ReadbackAndCloneAsync();
                DetectorMilliseconds = (Time.realtimeSinceStartup - started) * 1000f;
                if (!StartCrop(boxes, scores, width, height))
                    return false;
            }

            started = Time.realtimeSinceStartup;
            FillLandmarkInput(rgba, width, height);
            using var crop = new Tensor<float>(new TensorShape(1, LandmarkSize, LandmarkSize, 3), landmarkPixels);
            landmarker.Schedule(crop);
            using Tensor<float> joints = await (landmarker.PeekOutput("Identity") as Tensor<float>).ReadbackAndCloneAsync();
            using Tensor<float> presence = await (landmarker.PeekOutput("Identity_1") as Tensor<float>).ReadbackAndCloneAsync();
            using Tensor<float> world = await (landmarker.PeekOutput("Identity_4") as Tensor<float>).ReadbackAndCloneAsync();
            LandmarkMilliseconds = (Time.realtimeSinceStartup - started) * 1000f;
            return ReadJoints(joints, world, presence, frame);
        }

        /// <summary>Shrinks this turn's search window into the detector's square. Anything outside the image is black.</summary>
        void FillDetectorInput(byte[] rgba, int width, int height)
        {
            Vector3 window = SearchWindows[searchWindow];
            searchWindow = (searchWindow + 1) % SearchWindows.Length;

            int longest = Mathf.Max(width, height);
            float side = longest * window.z;
            windowLeft = width * window.x - side * 0.5f;
            windowTop = height * window.y - side * 0.5f;
            windowScale = side / DetectorSize;

            Array.Clear(detectorPixels, 0, detectorPixels.Length);
            for (int y = 0; y < DetectorSize; y++)
            {
                int sourceY = (int)(windowTop + (y + 0.5f) * windowScale);
                if (sourceY < 0 || sourceY >= height)
                    continue;
                for (int x = 0; x < DetectorSize; x++)
                {
                    int sourceX = (int)(windowLeft + (x + 0.5f) * windowScale);
                    if (sourceX < 0 || sourceX >= width)
                        continue;
                    int from = (sourceY * width + sourceX) * 4;
                    int to = (y * DetectorSize + x) * 3;
                    detectorPixels[to] = rgba[from] / 255f;
                    detectorPixels[to + 1] = rgba[from + 1] / 255f;
                    detectorPixels[to + 2] = rgba[from + 2] / 255f;
                }
            }
        }

        /// <summary>Finds the best detection and aims the first crop at it. False if nobody is there.</summary>
        bool StartCrop(Tensor<float> boxes, Tensor<float> scores, int width, int height)
        {
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

            if (1f / (1f + Mathf.Exp(-bestScore)) < MinDetection)
                return false;

            float anchorX = DetectorSize * anchors[best, 0], anchorY = DetectorSize * anchors[best, 1];
            var hips = new Vector2(windowLeft + (anchorX + boxes[0, best, 4]) * windowScale, windowTop + (anchorY + boxes[0, best, 5]) * windowScale);
            var above = new Vector2(windowLeft + (anchorX + boxes[0, best, 6]) * windowScale, windowTop + (anchorY + boxes[0, best, 7]) * windowScale);

            // The detector's idea of up comes largely from the face, and a headset hides that. People in a
            // room are nearly always upright, so start from upright and use its point only for the size.
            AimCrop(hips, Vector3.Distance(hips, above), Vector2.down);
            return true;
        }

        /// <summary>Centres the crop on the hips and turns it so the body stands upright in it.</summary>
        void AimCrop(Vector2 hips, float bodyRadius, Vector2 up)
        {
            up = up.normalized;
            cropCentre = hips;
            cropRadius = Mathf.Max(bodyRadius, 1f) * CropScale;
            cropSin = up.x;
            cropCos = -up.y;
        }

        Vector2 CropToImage(float x, float y)
        {
            float scale = cropRadius / (LandmarkSize * 0.5f);
            float dx = (x - LandmarkSize * 0.5f) * scale;
            float dy = (y - LandmarkSize * 0.5f) * scale;
            return cropCentre + new Vector2(cropCos * dx - cropSin * dy, cropSin * dx + cropCos * dy);
        }

        void FillLandmarkInput(byte[] rgba, int width, int height)
        {
            int to = 0;
            for (int y = 0; y < LandmarkSize; y++)
            {
                for (int x = 0; x < LandmarkSize; x++, to += 3)
                {
                    Vector2 source = CropToImage(x + 0.5f, y + 0.5f);
                    int sourceX = (int)source.x, sourceY = (int)source.y;
                    if (sourceX < 0 || sourceY < 0 || sourceX >= width || sourceY >= height)
                    {
                        landmarkPixels[to] = landmarkPixels[to + 1] = landmarkPixels[to + 2] = 0f;
                        continue;
                    }
                    int from = (sourceY * width + sourceX) * 4;
                    landmarkPixels[to] = rgba[from] / 255f;
                    landmarkPixels[to + 1] = rgba[from + 1] / 255f;
                    landmarkPixels[to + 2] = rgba[from + 2] / 255f;
                }
            }
        }

        bool ReadJoints(Tensor<float> joints, Tensor<float> world, Tensor<float> presence, BodyFrame frame)
        {
            frame.Presence = presence[0];
            if (frame.Presence < MinPresence)
            {
                tracking = false;
                return false;
            }

            for (int i = 0; i < BodyFrame.JointCount; i++)
            {
                frame.Image[i] = CropToImage(joints[5 * i], joints[5 * i + 1]);
                frame.Seen[i] = 1f / (1f + Mathf.Exp(-joints[5 * i + 3]));

                // The world joints come out upright in the crop, so turn them back to the image's way up.
                float x = world[3 * i], y = world[3 * i + 1];
                frame.World[i] = new Vector3(cropCos * x - cropSin * y, cropSin * x + cropCos * y, world[3 * i + 2]);
            }

            Vector2 centre = CropToImage(joints[5 * HipCentre], joints[5 * HipCentre + 1]);
            Vector2 edge = CropToImage(joints[5 * BodyEdge], joints[5 * BodyEdge + 1]);
            Vector2 hips = (frame.Image[HipLeft] + frame.Image[HipRight]) * 0.5f;
            Vector2 shoulders = (frame.Image[ShoulderLeft] + frame.Image[ShoulderRight]) * 0.5f;
            AimCrop(centre, Vector2.Distance(centre, edge), shoulders - hips);
            tracking = true;
            return true;
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

        public void Dispose()
        {
            detector.Dispose();
            landmarker.Dispose();
        }
    }
}
