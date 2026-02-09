using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UIProbe
{
    public static class ResourceCacheManager
    {
        [System.Serializable]
        private class DependencyCacheData
        {
             public List<DependencyEntry> entries = new List<DependencyEntry>();
        }

        [System.Serializable]
        private class DependencyEntry
        {
            public string key;
            public List<string> values;
        }

        public static void SaveDependencyCache(Dictionary<string, HashSet<string>> map)
        {
            var data = new DependencyCacheData();
            foreach(var kvp in map)
            {
                data.entries.Add(new DependencyEntry { key = kvp.Key, values = kvp.Value.ToList() });
            }
            
            string json = JsonUtility.ToJson(data);
            File.WriteAllText(GetCacheFilePath(), json);
        }

        public static Dictionary<string, HashSet<string>> LoadDependencyCache()
        {
            string path = GetCacheFilePath();
            if (!File.Exists(path)) return null;
            
            try 
            {
                string json = File.ReadAllText(path);
                var data = JsonUtility.FromJson<DependencyCacheData>(json);
                if (data == null) return null;

                var map = new Dictionary<string, HashSet<string>>();
                foreach(var entry in data.entries)
                {
                    map[entry.key] = new HashSet<string>(entry.values);
                }
                return map;
            }
            catch
            {
                return null;
            }
        }
        
        public static string GetCacheFilePath()
        {
             return Path.Combine(UIProbeStorage.GetResourceCachePath(), "dependency_cache.json");
        }
    }
}
