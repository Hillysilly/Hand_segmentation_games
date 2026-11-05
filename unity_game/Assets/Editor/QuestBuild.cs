using System.IO;
using System.IO.Compression;
using System.Linq;
using Repulsor;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

/// <summary>Builds the Android APK. Menu: Repulsor > Build APK. Batch: -executeMethod QuestBuild.Build</summary>
public static class QuestBuild
{
    public const string ApkPath = "Builds/RepulsorTest.apk";

    // Without this, Meta's features fail at startup, but the game still runs and looks mostly fine.
    const string MetaLibrary = "lib/arm64-v8a/libUnityARFoundationMeta.so";

    [MenuItem("Repulsor/Build APK")]
    public static void Build()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Main.unity" },
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            options = BuildOptions.Development,   // lets the Profiler attach and keeps logging on
        });

        BuildSummary summary = report.summary;
        Debug.Log($"[Build] {summary.result}: {summary.totalSize / (1024 * 1024)} MB, "
            + $"{summary.totalErrors} error(s), {summary.totalTime}");

        bool succeeded = summary.result == BuildResult.Succeeded && HasMetaLibrary();
        if (!succeeded && Application.isBatchMode)
            EditorApplication.Exit(1);
    }

    const string BenchApkPath = "Builds/RepulsorBench.apk";
    const string BenchScenePath = "Assets/Suits/Bench.unity";
    const string BenchApplicationId = "com.alberthan.repulsorbench";
    const string OpenXrLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";
    const string RenderSettingsPath = "Assets/Settings/QuestURP.asset";

    /// <summary>
    /// Builds the benchmark as a separate, ordinary Android app with no VR, which the headset will start even
    /// while it can't track itself. Its scene sits in the ignored suits folder. Settings changed for this
    /// build are put back afterwards.
    /// </summary>
    public static void BuildBench()
    {
        string applicationId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
        XRManagerSettings manager = perTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
        bool removed = XRPackageMetadataStore.RemoveLoader(manager, OpenXrLoader, BuildTargetGroup.Android);

        // A build without VR rewrites this to strip VR shaders, which would break the real game.
        byte[] renderSettings = File.ReadAllBytes(RenderSettingsPath);

        bool succeeded = false;
        try
        {
            CreateBenchScene();
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BenchApplicationId);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { BenchScenePath },
                locationPathName = BenchApkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            Debug.Log($"[Build] Bench {report.summary.result}: {report.summary.totalErrors} error(s), {report.summary.totalTime}");
            succeeded = report.summary.result == BuildResult.Succeeded;
        }
        finally
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, applicationId);
            if (removed)
                XRPackageMetadataStore.AssignLoader(manager, OpenXrLoader, BuildTargetGroup.Android);
            AssetDatabase.SaveAssets();
            File.WriteAllBytes(RenderSettingsPath, renderSettings);
            AssetDatabase.ImportAsset(RenderSettingsPath);
        }

        if (!succeeded && Application.isBatchMode)
            EditorApplication.Exit(1);
    }

    static void CreateBenchScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var eyeObject = new GameObject("Eye", typeof(Camera));
        var eye = eyeObject.GetComponent<Camera>();
        eye.clearFlags = CameraClearFlags.SolidColor;
        eye.backgroundColor = Color.black;
        eye.fieldOfView = 96f;

        var bench = new GameObject("Bench", typeof(BodyBench));
        var fields = new SerializedObject(bench.GetComponent<BodyBench>());
        fields.FindProperty("detector").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/Models/pose_detection.onnx");
        fields.FindProperty("landmarksLite").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/Models/pose_landmarks_detector_lite.onnx");
        fields.FindProperty("landmarksFull").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/Models/pose_landmarks_detector_full.onnx");
        fields.FindProperty("anchorsCsv").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Models/pose_anchors.csv");
        fields.FindProperty("eye").objectReferenceValue = eye;
        fields.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, BenchScenePath);
    }

    static bool HasMetaLibrary()
    {
        using ZipArchive apk = ZipFile.OpenRead(ApkPath);
        bool found = apk.Entries.Any(entry => entry.FullName == MetaLibrary);
        if (!found)
            Debug.LogError($"[Build] {MetaLibrary} is missing from the APK");
        return found;
    }
}
