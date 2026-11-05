using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Repulsor;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Tools for bringing the suit models into the project. The models live in Assets/Suits, which git ignores,
/// and are copied from ../assets/models. Results go to Logs/suit_report.txt.
/// Batch: -executeMethod SuitLab.Inspect
/// </summary>
public static class SuitLab
{
    public const string Folder = "Assets/Suits";
    const string ReportPath = "Logs/suit_report.txt";
    static readonly string[] Models = { "suit.glb", "spiderman.glb", "xbot.glb" };

    [MenuItem("Repulsor/Import Suits")]
    public static void Import()
    {
        Directory.CreateDirectory(Folder);
        foreach (string model in Models)
        {
            string from = Path.Combine("..", "assets", "models", model);
            string to = Path.Combine(Folder, model);
            if (File.Exists(from) && !File.Exists(to))
                File.Copy(from, to);
        }
        AssetDatabase.Refresh();
    }

    [MenuItem("Repulsor/Inspect Suits")]
    public static void Inspect()
    {
        Import();
        var report = new StringBuilder();
        foreach (string model in Models)
        {
            string path = $"{Folder}/{model}";
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            report.AppendLine($"=== {model}: {(root == null ? "DID NOT IMPORT" : "imported")}");
            if (root == null)
                continue;

            SkinnedMeshRenderer[] skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            MeshFilter[] plain = root.GetComponentsInChildren<MeshFilter>(true);
            int triangles = skinned.Sum(s => s.sharedMesh.triangles.Length / 3)
                + plain.Sum(f => f.sharedMesh.triangles.Length / 3);
            report.AppendLine($"skinned meshes {skinned.Length}, plain meshes {plain.Length}, triangles {triangles}");

            var materials = new HashSet<Material>(skinned.SelectMany(s => s.sharedMaterials));
            report.AppendLine($"materials {materials.Count}: " + string.Join(", ", materials.Select(m => $"{m.name} [{m.shader.name}]")));

            var textures = new HashSet<Texture>();
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Texture texture)
                    textures.Add(texture);
            }
            report.AppendLine($"textures {textures.Count}: " + string.Join(", ", textures.Select(t => $"{t.name} {t.width}x{t.height}")));

            int maxBones = skinned.Length == 0 ? 0 : skinned.Max(s => s.bones.Length);
            report.AppendLine($"bones per mesh, largest {maxBones}");
            Transform hips = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.ToLowerInvariant().Contains("hips") || t.name.ToLowerInvariant().Contains("pelvis"));
            report.AppendLine($"hips: {(hips == null ? "none" : hips.name)}");
            if (hips != null)
                report.AppendLine("bones: " + string.Join(" ", hips.GetComponentsInChildren<Transform>(true).Take(70).Select(t => t.name)));

            Bounds bounds = new Bounds();
            if (skinned.Length > 0)
            {
                bounds = skinned[0].bounds;
                foreach (SkinnedMeshRenderer s in skinned)
                    bounds.Encapsulate(s.bounds);
            }
            report.AppendLine($"root scale {root.transform.localScale}, bounds size {bounds.size}");
        }

        File.WriteAllText(ReportPath, report.ToString());
        Debug.Log("[Suit] Wrote " + ReportPath);
    }

    const string FramesFolder = "Logs/frames";
    const string BenchFolder = "Logs/bench";
    const float AssumedVerticalFov = 60f;
    const int SaveEvery = 12;

    static readonly int[,] Bones =
    {
        { 11, 12 }, { 11, 13 }, { 13, 15 }, { 12, 14 }, { 14, 16 }, { 11, 23 }, { 12, 24 }, { 23, 24 },
        { 23, 25 }, { 25, 27 }, { 24, 26 }, { 26, 28 }, { 7, 8 }, { 0, 7 }, { 0, 8 },
    };

    public static void BenchLite() => Bench("pose_landmarks_detector_lite");
    public static void BenchFull() => Bench("pose_landmarks_detector_full");

    /// <summary>
    /// Runs every picture in Logs/frames through the tracker, and saves the skeleton and posed suit over some
    /// of them to Logs/bench. The SUIT_MODEL environment variable picks the model, and BENCH_BACKEND the
    /// tracker's backend: CPU or GPUCompute.
    /// </summary>
    static void Bench(string landmarkModel)
    {
        Import();
        Directory.CreateDirectory(BenchFolder);
        string suitFile = Environment.GetEnvironmentVariable("SUIT_MODEL") ?? "spiderman.glb";
        BackendType backend = Environment.GetEnvironmentVariable("BENCH_BACKEND") == "GPUCompute" ? BackendType.GPUCompute : BackendType.CPU;
        string[] files = Directory.GetFiles(FramesFolder, "*.jpg").OrderBy(f => f).ToArray();

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cameraObject = new GameObject("Bench Camera", typeof(Camera));
        var camera = cameraObject.GetComponent<Camera>();
        camera.fieldOfView = AssumedVerticalFov;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 200f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;

        var lightObject = new GameObject("Light", typeof(Light));
        lightObject.GetComponent<Light>().type = LightType.Directional;
        lightObject.GetComponent<Light>().intensity = 2f;
        lightObject.transform.rotation = Quaternion.Euler(35f, 20f, 0f);
        RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);

        var holder = new GameObject("Suit").transform;
        Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/{suitFile}"), holder);
        var rig = new SuitRig(holder);

        var picture = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(picture.GetComponent<Collider>());
        var pictureMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        picture.GetComponent<Renderer>().sharedMaterial = pictureMaterial;

        var detector = AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/Models/pose_detection.onnx");
        var landmarks = AssetDatabase.LoadAssetAtPath<ModelAsset>($"Assets/Models/{landmarkModel}.onnx");
        string anchors = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Models/pose_anchors.csv").text;

        var results = new StringBuilder("frame,found,presence,detector_ms,landmark_ms,distance,scale\n");
        var frame = new BodyFrame();
        var joints = new Vector3[BodyFrame.JointCount];
        var landmarkTimes = new List<float>();
        var detectorTimes = new List<float>();
        int found = 0;

        using (var tracker = new BodyTracker(detector, landmarks, anchors, backend))
        {
            for (int i = 0; i < files.Length; i++)
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.LoadImage(File.ReadAllBytes(files[i]));
                int width = texture.width, height = texture.height;
                byte[] rgba = TopDownBytes(texture);

                string cropBefore = tracker.CropDescription;
                bool ok = tracker.Run(rgba, width, height, frame);
                if (i >= 82 && i <= 100 && i % 3 == 0)
                {
                    Debug.Log($"[Suit] frame {i} ok {ok} det {tracker.DetectorMilliseconds:0} before {cropBefore} after {tracker.CropDescription} hip {frame.Image[23]} {frame.Image[24]} shoulder {frame.Image[11]} {frame.Image[12]} nose {frame.Image[0]}");
                    SaveCrop(tracker.LastCrop, $"{BenchFolder}/crop_{i:000}.png");
                }
                float focal = height * 0.5f / Mathf.Tan(AssumedVerticalFov * 0.5f * Mathf.Deg2Rad);
                float distance = ok ? frame.DistanceFromSize(focal) : 0f;
                if (ok)
                {
                    found++;
                    landmarkTimes.Add(tracker.LandmarkMilliseconds);
                    frame.Place(Vector3.zero, Quaternion.identity, new Vector2(focal, focal), new Vector2(width, height) * 0.5f, distance, joints);
                    rig.Pose(joints);
                }
                if (tracker.DetectorMilliseconds > 0f)
                    detectorTimes.Add(tracker.DetectorMilliseconds);

                results.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:0.00},{3:0.0},{4:0.0},{5:0.00},{6:0.00}",
                    Path.GetFileName(files[i]), ok ? 1 : 0, frame.Presence, tracker.DetectorMilliseconds, tracker.LandmarkMilliseconds, distance, rig.Scale));

                if (i % SaveEvery == 0)
                {
                    SaveOverlay(camera, picture, pictureMaterial, texture, holder, ok, frame, $"{BenchFolder}/{landmarkModel}_{Path.GetFileNameWithoutExtension(suitFile)}_{i:000}");
                }
                Object.DestroyImmediate(texture);
            }
        }

        string summary = $"{landmarkModel} on {backend}, suit {suitFile}: found {found}/{files.Length}, "
            + $"landmark median {Median(landmarkTimes):0.0} ms, detector median {Median(detectorTimes):0.0} ms over {detectorTimes.Count} runs, "
            + $"model joints ok {rig.HasBone("LeftArm")}";
        File.WriteAllText($"{BenchFolder}/{landmarkModel}.csv", results.ToString());
        File.AppendAllText($"{BenchFolder}/summary.txt", summary + "\n");
        Debug.Log("[Suit] " + summary);
    }

    static void SaveCrop(float[] pixels, string path)
    {
        const int size = 256;
        var image = new Texture2D(size, size, TextureFormat.RGB24, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = (y * size + x) * 3;
                image.SetPixel(x, size - 1 - y, new Color(pixels[i], pixels[i + 1], pixels[i + 2]));
            }
        }
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        Object.DestroyImmediate(image);
    }

    static float Median(List<float> values)
    {
        if (values.Count == 0)
            return 0f;
        values.Sort();
        return values[values.Count / 2];
    }

    static byte[] TopDownBytes(Texture2D texture)
    {
        Color32[] pixels = texture.GetPixels32();
        int width = texture.width, height = texture.height;
        var bytes = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int row = (height - 1 - y) * width;
            for (int x = 0; x < width; x++)
            {
                Color32 c = pixels[row + x];
                int to = (y * width + x) * 4;
                bytes[to] = c.r;
                bytes[to + 1] = c.g;
                bytes[to + 2] = c.b;
                bytes[to + 3] = 255;
            }
        }
        return bytes;
    }

    /// <summary>Saves the frame with the skeleton on it, and the frame with the suit rendered over it.</summary>
    static void SaveOverlay(Camera camera, GameObject picture, Material material, Texture2D frameTexture, Transform holder, bool ok, BodyFrame frame, string path)
    {
        int width = frameTexture.width, height = frameTexture.height;

        // The picture on a far quad that exactly fills the view, with the suit in front of it.
        const float far = 150f;
        float pictureHeight = 2f * far * Mathf.Tan(AssumedVerticalFov * 0.5f * Mathf.Deg2Rad);
        picture.transform.SetPositionAndRotation(new Vector3(0f, 0f, far), Quaternion.identity);
        picture.transform.localScale = new Vector3(pictureHeight * width / height, pictureHeight, 1f);
        material.SetTexture("_BaseMap", frameTexture);
        holder.gameObject.SetActive(ok);

        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 2 };
        camera.aspect = width / (float)height;
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;
        File.WriteAllBytes(path + "_suit.png", image.EncodeToPNG());

        // The same picture with thin joint lines on top, to show where the suit misses.
        if (ok)
        {
            for (int b = 0; b < Bones.GetLength(0); b++)
                DrawLine(image, frame.Image[Bones[b, 0]], frame.Image[Bones[b, 1]], height, Color.yellow);
            image.Apply();
            File.WriteAllBytes(path + "_both.png", image.EncodeToPNG());
        }

        var skeleton = new Texture2D(width, height, TextureFormat.RGB24, false);
        skeleton.SetPixels32(frameTexture.GetPixels32());
        if (ok)
        {
            for (int b = 0; b < Bones.GetLength(0); b++)
                DrawLine(skeleton, frame.Image[Bones[b, 0]], frame.Image[Bones[b, 1]], height, Color.green);
        }
        skeleton.Apply();
        File.WriteAllBytes(path + "_skeleton.png", skeleton.EncodeToPNG());

        Object.DestroyImmediate(image);
        Object.DestroyImmediate(skeleton);
        Object.DestroyImmediate(target);
    }

    static void DrawLine(Texture2D texture, Vector2 from, Vector2 to, int height, Color color)
    {
        int steps = Mathf.CeilToInt(Vector2.Distance(from, to)) + 1;
        for (int i = 0; i <= steps; i++)
        {
            Vector2 p = Vector2.Lerp(from, to, i / (float)steps);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                    texture.SetPixel(Mathf.RoundToInt(p.x) + dx, height - 1 - Mathf.RoundToInt(p.y) + dy, color);
            }
        }
    }
}
