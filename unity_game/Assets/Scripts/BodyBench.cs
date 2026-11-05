using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.InferenceEngine;
using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// A benchmark for the separate, non-VR test app: it runs on the headset with nobody wearing it. It times
    /// the tracking models on saved pictures and draws a suit twice per frame at headset resolution. Results
    /// go to the log, tagged [Bench], and to bench.txt in the app's files.
    /// </summary>
    public sealed class BodyBench : MonoBehaviour
    {
        const int Runs = 60;
        const int EyeSize = 2064;
        const int FramesPerRenderTest = 180;

        [SerializeField] ModelAsset detector;
        [SerializeField] ModelAsset landmarksLite;
        [SerializeField] ModelAsset landmarksFull;
        [SerializeField] TextAsset anchorsCsv;
        [SerializeField] Camera eye;

        readonly StringBuilder results = new StringBuilder();
        byte[][] pictures;
        int pictureWidth;
        int pictureHeight;

        void Start()
        {
            Application.targetFrameRate = 300;
            QualitySettings.vSyncCount = 0;
            StartCoroutine(RunAll());
        }

        IEnumerator RunAll()
        {
            Write($"device {SystemInfo.deviceModel}, {SystemInfo.processorCount} cores, {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}");
            yield return null;

            LoadPictures();
            if (pictures.Length == 0)
            {
                Write("no pictures found, skipping the tracking tests");
            }
            else
            {
                yield return Track("lite on CPU", landmarksLite, BackendType.CPU);
                yield return Track("lite on GPU", landmarksLite, BackendType.GPUCompute);
                yield return Track("full on CPU", landmarksFull, BackendType.CPU);
                yield return Track("full on GPU", landmarksFull, BackendType.GPUCompute);
            }

            foreach (string suitName in new[] { "IronMan", "SpiderMan" })
                yield return Render(suitName);

            Write("done");
            File.WriteAllText(Path.Combine(Application.persistentDataPath, "bench.txt"), results.ToString());
        }

        void Write(string line)
        {
            Debug.Log("[Bench] " + line);
            results.AppendLine(line);
        }

        void LoadPictures()
        {
            string folder = Path.Combine(Application.persistentDataPath, "frames");
            string[] files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.jpg").OrderBy(f => f).ToArray() : Array.Empty<string>();
            Write($"{files.Length} pictures in {folder}");
            pictures = new byte[files.Length][];

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            for (int i = 0; i < files.Length; i++)
            {
                texture.LoadImage(File.ReadAllBytes(files[i]));
                pictureWidth = texture.width;
                pictureHeight = texture.height;
                var bytes = new byte[pictureWidth * pictureHeight * 4];
                NativeArray<byte> pixels = texture.GetRawTextureData<byte>();
                int stride = pictureWidth * 4;
                for (int y = 0; y < pictureHeight; y++)
                    NativeArray<byte>.Copy(pixels, (pictureHeight - 1 - y) * stride, bytes, y * stride, stride);
                pictures[i] = bytes;
            }
            Destroy(texture);
        }

        IEnumerator Track(string label, ModelAsset landmarks, BackendType backend)
        {
            BodyTracker tracker;
            try
            {
                tracker = new BodyTracker(detector, landmarks, anchorsCsv.text, backend);
            }
            catch (Exception exception)
            {
                Write($"{label}: could not start: {exception.Message}");
                yield break;
            }

            var frame = new BodyFrame();
            var whole = new float[Runs];
            var landmark = new float[Runs];
            int found = 0, searches = 0;
            float detectorTotal = 0f;

            for (int i = 0; i < Runs; i++)
            {
                float started = Time.realtimeSinceStartup;
                Awaitable<bool> run = tracker.RunAsync(pictures[i % pictures.Length], pictureWidth, pictureHeight, frame);
                while (!run.GetAwaiter().IsCompleted)
                    yield return null;
                if (run.GetAwaiter().GetResult())
                    found++;
                whole[i] = (Time.realtimeSinceStartup - started) * 1000f;
                landmark[i] = tracker.LandmarkMilliseconds;
                if (tracker.DetectorMilliseconds > 0f)
                {
                    searches++;
                    detectorTotal += tracker.DetectorMilliseconds;
                }
            }

            // The first runs load everything, so they don't count.
            Write($"{label}: found {found}/{Runs}, landmarks median {Median(landmark.Skip(10)):0} ms, "
                + $"worst {landmark.Skip(10).Max():0} ms, first run {whole[0]:0} ms, whole run median {Median(whole.Skip(10)):0} ms, "
                + $"{searches} searches at {(searches > 0 ? detectorTotal / searches : 0f):0} ms");
            tracker.Dispose();
            yield return null;
        }

        IEnumerator Render(string suitName)
        {
            var prefab = Resources.Load<GameObject>("Suits/" + suitName);
            if (prefab == null)
            {
                Write($"{suitName}: not in this build");
                yield break;
            }

            var holder = new GameObject(suitName);
            Instantiate(prefab, holder.transform);
            var rig = new SuitRig(holder.transform);
            var joints = TestPose();

            var target = new RenderTexture(EyeSize, EyeSize, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            eye.targetTexture = target;
            eye.enabled = false;
            Shader.SetGlobalFloat("_IsOcclusionOn", 0f);

            float poseTotal = 0f;
            var frameTimes = new float[FramesPerRenderTest];
            var gpuTimes = new float[FramesPerRenderTest];
            var timings = new FrameTiming[1];
            float last = Time.realtimeSinceStartup;

            for (int i = 0; i < FramesPerRenderTest; i++)
            {
                float poseStart = Time.realtimeSinceStartup;
                rig.Pose(joints);
                poseTotal += Time.realtimeSinceStartup - poseStart;

                // Two eyes, each from a slightly different place.
                FrameTimingManager.CaptureFrameTimings();
                eye.transform.position = new Vector3(-0.032f, 1.4f, 0f);
                eye.Render();
                eye.transform.position = new Vector3(0.032f, 1.4f, 0f);
                eye.Render();

                yield return null;
                float now = Time.realtimeSinceStartup;
                frameTimes[i] = (now - last) * 1000f;
                last = now;
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                    gpuTimes[i] = (float)timings[0].gpuFrameTime;
            }

            Write($"{suitName}: pose {poseTotal / FramesPerRenderTest * 1000f:0.0} ms, two eyes at {EyeSize}: "
                + $"frame median {Median(frameTimes.Skip(30)):0.0} ms, worst {frameTimes.Skip(30).Max():0.0} ms, GPU median {Median(gpuTimes.Skip(30)):0.0} ms");

            eye.targetTexture = null;
            Destroy(target);
            Destroy(holder);
            yield return null;
        }

        /// <summary>A standing body two metres away with bent arms, close enough to fill much of the view.</summary>
        static Vector3[] TestPose()
        {
            var joints = new Vector3[BodyFrame.JointCount];
            void Set(int joint, float x, float y) => joints[joint] = new Vector3(x, y, 1.6f);

            Set(0, 0f, 1.62f);
            Set(7, 0.08f, 1.64f);
            Set(8, -0.08f, 1.64f);
            Set(11, 0.2f, 1.42f);
            Set(12, -0.2f, 1.42f);
            Set(13, 0.3f, 1.14f);
            Set(14, -0.3f, 1.14f);
            Set(15, 0.26f, 0.88f);
            Set(16, -0.26f, 0.88f);
            Set(23, 0.11f, 0.92f);
            Set(24, -0.11f, 0.92f);
            Set(25, 0.13f, 0.5f);
            Set(26, -0.13f, 0.5f);
            Set(27, 0.14f, 0.08f);
            Set(28, -0.14f, 0.08f);
            return joints;
        }

        static float Median(System.Collections.Generic.IEnumerable<float> values)
        {
            float[] sorted = values.OrderBy(v => v).ToArray();
            return sorted.Length == 0 ? 0f : sorted[sorted.Length / 2];
        }
    }
}
