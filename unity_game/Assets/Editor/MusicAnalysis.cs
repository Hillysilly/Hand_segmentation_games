using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Loudness of each soundtrack over time, for finding where a track's intro ends (the streamed copies in
/// Resources/Music can't be read, so each is copied, imported decompressed, measured and deleted).
/// Batch: -executeMethod MusicAnalysis.Run. Writes Logs/music_energy.txt.
/// </summary>
public static class MusicAnalysis
{
    const string Temp = "Assets/MusicAnalysisTemp";
    const float Window = 0.5f;

    public static void Run()
    {
        var report = new StringBuilder();
        Directory.CreateDirectory(Temp);
        foreach (string source in Directory.GetFiles("Assets/Resources/Music", "*.mp3"))
        {
            string copy = $"{Temp}/{Path.GetFileName(source)}";
            File.Copy(source, copy, true);
            AssetDatabase.ImportAsset(copy);
            var importer = (AudioImporter)AssetImporter.GetAtPath(copy);
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(copy);
            clip.LoadAudioData();
            var data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0))
            {
                report.AppendLine($"{source}: couldn't read");
                continue;
            }
            int perWindow = Mathf.RoundToInt(clip.frequency * Window);
            report.AppendLine($"# {Path.GetFileName(source)}: {clip.length:0.0} s, {clip.frequency} Hz, {clip.channels} ch");
            float loudest = 0f;
            var levels = new System.Collections.Generic.List<float>();
            for (int start = 0; start + perWindow <= clip.samples; start += perWindow)
            {
                double sum = 0;
                for (int i = start; i < start + perWindow; i++)
                    for (int c = 0; c < clip.channels; c++)
                    {
                        float v = data[i * clip.channels + c];
                        sum += v * v;
                    }
                float rms = Mathf.Sqrt((float)(sum / (perWindow * clip.channels)));
                levels.Add(rms);
                loudest = Mathf.Max(loudest, rms);
            }
            float total = 0f;
            foreach (float l in levels)
                total += l * l;
            report.AppendLine($"overall rms {Mathf.Sqrt(total / Mathf.Max(1, levels.Count)):0.0000}, peak window {loudest:0.0000}");
            for (int w = 0; w < levels.Count; w++)
            {
                float share = loudest > 0f ? levels[w] / loudest : 0f;
                report.AppendLine($"{w * Window,6:0.0}  {share:0.00}  {new string('#', Mathf.RoundToInt(share * 40f))}");
            }
            report.AppendLine();
        }
        AssetDatabase.DeleteAsset(Temp);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/music_energy.txt", report.ToString());
        Debug.Log("[Music] Wrote Logs/music_energy.txt");
    }
}
