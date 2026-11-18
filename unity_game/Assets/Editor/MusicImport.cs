using UnityEditor;
using UnityEngine;

/// <summary>
/// The soundtrack in Resources/Music streams from storage on the headset instead of being decompressed into memory
/// (each track is minutes long), and doesn't load until it's played.
/// </summary>
public sealed class MusicImport : AssetPostprocessor
{
    void OnPreprocessAudio()
    {
        if (!assetPath.Replace('\\', '/').Contains("/Resources/Music/"))
            return;
        var importer = (AudioImporter)assetImporter;
        importer.loadInBackground = true;
        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.Streaming;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        settings.quality = 0.6f;
        settings.preloadAudioData = false;
        importer.defaultSampleSettings = settings;
    }
}
