using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UIProbe
{
    [Serializable]
    internal class RedGoldPreset
    {
        public string name = "";
        public string description = "";
        public List<QualityConfigEntry> qualityEntries;
        public int cellPixelSize = 100;
        public int maxOutputEdge = 512;
        public bool overrideGrid = false;
        public int overrideGridLong = 2;
        public int overrideGridWide = 3;
        public string version = "1.0";
    }

    internal static class RedGoldPresetManager
    {
        private static string PresetsPath =>
            Path.Combine(UIProbeStorage.GetMainFolderPath(), "RedGoldPresets");

        public static List<string> ListPresets()
        {
            if (!Directory.Exists(PresetsPath)) return new List<string>();
            return Directory.GetFiles(PresetsPath, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n)
                .ToList();
        }

        public static RedGoldPreset LoadPreset(string name)
        {
            string path = Path.Combine(PresetsPath, SanitizeFileName(name) + ".json");
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            return JsonUtility.FromJson<RedGoldPreset>(json);
        }

        public static void SavePreset(RedGoldPreset preset)
        {
            Directory.CreateDirectory(PresetsPath);
            string path = Path.Combine(PresetsPath, SanitizeFileName(preset.name) + ".json");
            string json = JsonUtility.ToJson(preset, true);
            File.WriteAllText(path, json);
        }

        public static bool DeletePreset(string name)
        {
            string path = Path.Combine(PresetsPath, SanitizeFileName(name) + ".json");
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }
    }
}
