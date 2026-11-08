using System;
using System.IO;
using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// Keeps the game's own log lines ([Arena], [Repulsor], [Enemy], [Menu], [Variant], [Perf], [Gesture] and so on)
    /// plus every warning and error in files/game_log.txt, because the headset's logcat buffer is small and a test
    /// session scrolls out of it. The previous run is kept as game_log_prev.txt. Read them with
    /// <c>adb pull /sdcard/Android/data/com.alberthan.repulsortest/files/game_log.txt</c>.
    /// </summary>
    public static class GameLog
    {
        const long MaxBytes = 20L * 1024 * 1024;
        static readonly object Gate = new object();
        static StreamWriter writer;

        public static string FilePath => Path.Combine(Application.persistentDataPath, "game_log.txt");

        public static void Start()
        {
            if (writer != null || Application.isEditor)
                return;
            try
            {
                string previous = Path.Combine(Application.persistentDataPath, "game_log_prev.txt");
                if (File.Exists(FilePath))
                    File.Copy(FilePath, previous, true);
                writer = new StreamWriter(FilePath, false) { AutoFlush = true };
                writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {Application.productName} {Application.version} started");
                Application.logMessageReceivedThreaded += OnLog;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameLog] Can't write the log file: {e.Message}");
                writer = null;
            }
        }

        static void OnLog(string message, string stack, LogType type)
        {
            bool problem = type != LogType.Log;
            bool ours = message.Length > 1 && message[0] == '[' && !message.StartsWith("[XR]");
            if (!problem && !ours)
                return;
            lock (Gate)
            {
                if (writer == null)
                    return;
                try
                {
                    if (writer.BaseStream.Length > MaxBytes)
                        return;
                    writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {(problem ? type + ": " : "")}{message}");
                    if (type == LogType.Exception || type == LogType.Error)
                        writer.WriteLine(stack);
                }
                catch (Exception)
                {
                    writer = null;
                }
            }
        }
    }
}
