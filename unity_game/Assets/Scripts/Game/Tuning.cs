using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// Development builds: changes gesture thresholds while the game runs, without a rebuild. Lines like
    /// <c>HandPose.ThumbOutAbove = 0.55</c> in files/tuning.txt are applied within two seconds of the file changing.
    /// On start it writes every tunable value to files/tuning_current.txt as a starting point.
    /// Only public static float fields of the gesture classes can be changed.
    /// </summary>
    public static class Tuning
    {
        static readonly Type[] Tunable =
        {
            typeof(GestureClassifier), typeof(HandPose), typeof(XArmsDetector), typeof(DoubleOkDetector), typeof(PowerUpDetector),
            typeof(TwoHandChargeDetector), typeof(TriangleDetector), typeof(BowDetector), typeof(FingerGunDetector),
            typeof(PunchDetector), typeof(SpiderSignDetector), typeof(ForceGrabDetector), typeof(SaberGripDetector),
            typeof(UnderhandThrowDetector), typeof(MenuLayout), typeof(Aim), typeof(Enemy), typeof(Arena),
        };

        static float nextCheck;
        static DateTime lastWrite;

        static string Folder => Application.persistentDataPath;

        public static void WriteCurrent()
        {
            var text = new StringBuilder("# Copy lines into tuning.txt and change the numbers.\n");
            foreach (Type type in Tunable)
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                    if (field.FieldType == typeof(float) && !field.IsLiteral && !field.IsInitOnly)
                        text.Append(type.Name).Append('.').Append(field.Name).Append(" = ")
                            .Append(((float)field.GetValue(null)).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            try
            {
                File.WriteAllText(Path.Combine(Folder, "tuning_current.txt"), text.ToString());
            }
            catch (IOException)
            {
            }
        }

        public static void Tick()
        {
            if (Time.unscaledTime < nextCheck)
                return;
            nextCheck = Time.unscaledTime + 2f;
            string path = Path.Combine(Folder, "tuning.txt");
            if (!File.Exists(path))
                return;
            DateTime written = File.GetLastWriteTimeUtc(path);
            if (written == lastWrite)
                return;
            lastWrite = written;
            Apply(File.ReadAllLines(path));
        }

        /// <summary>Applies "Type.Field = value" lines. Returns how many took.</summary>
        public static int Apply(string[] lines)
        {
            int applied = 0;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;
                int equals = line.IndexOf('=');
                int dot = line.IndexOf('.');
                if (equals < 0 || dot < 0 || dot > equals)
                    continue;
                string typeName = line.Substring(0, dot).Trim();
                string fieldName = line.Substring(dot + 1, equals - dot - 1).Trim();
                if (!float.TryParse(line.Substring(equals + 1).Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float value))
                    continue;
                Type type = Array.Find(Tunable, t => t.Name == typeName);
                FieldInfo field = type?.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
                if (field == null || field.FieldType != typeof(float) || field.IsLiteral || field.IsInitOnly)
                {
                    Debug.Log($"[Tuning] Unknown setting {typeName}.{fieldName}");
                    continue;
                }
                field.SetValue(null, value);
                applied++;
                Debug.Log($"[Tuning] {typeName}.{fieldName} = {value}");
            }
            return applied;
        }
    }
}
