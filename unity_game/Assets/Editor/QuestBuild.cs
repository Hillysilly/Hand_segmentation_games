using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

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

    static bool HasMetaLibrary()
    {
        using ZipArchive apk = ZipFile.OpenRead(ApkPath);
        bool found = apk.Entries.Any(entry => entry.FullName == MetaLibrary);
        if (!found)
            Debug.LogError($"[Build] {MetaLibrary} is missing from the APK");
        return found;
    }
}
