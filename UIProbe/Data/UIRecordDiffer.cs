using System.Collections.Generic;
using UnityEngine;

namespace UIProbe
{
    /// <summary>
    /// Diff 结果类型
    /// </summary>
    public enum DiffType
    {
        Added,      // 新增
        Removed,    // 删除
        Modified,   // 修改 (标签变化等)
        Unchanged   // 未变化
    }

    /// <summary>
    /// 单个 Diff 项
    /// </summary>
    [System.Serializable]
    public class DiffItem
    {
        public DiffType Type;
        public string NodePath;
        public string NodeName;
        public string OldTag;
        public string NewTag;
        public string OldPrefab;
        public string NewPrefab;
        
        public Color GetColor()
        {
            switch (Type)
            {
                case DiffType.Added: return new Color(0.3f, 0.8f, 0.3f);   // Green
                case DiffType.Removed: return new Color(0.9f, 0.3f, 0.3f); // Red
                case DiffType.Modified: return new Color(0.9f, 0.7f, 0.2f); // Yellow
                default: return Color.gray;
            }
        }
        
        public string GetIcon()
        {
            switch (Type)
            {
                case DiffType.Added: return "+";
                case DiffType.Removed: return "-";
                case DiffType.Modified: return "~";
                default: return " ";
            }
        }
        
        public string GetDescription()
        {
            switch (Type)
            {
                case DiffType.Added: 
                    return $"[新增] {NodeName}";
                case DiffType.Removed: 
                    return $"[删除] {NodeName}";
                case DiffType.Modified:
                    if (OldTag != NewTag)
                        return $"[修改] {NodeName} (标签: {OldTag} → {NewTag})";
                    if (OldPrefab != NewPrefab)
                        return $"[修改] {NodeName} (预制体: {OldPrefab} → {NewPrefab})";
                    return $"[修改] {NodeName}";
                default: 
                    return NodeName;
            }
        }
    }

    /// <summary>
    /// Diff 结果
    /// </summary>
    public class DiffResult
    {
        public string Version1;
        public string Version2;
        public List<DiffItem> Items = new List<DiffItem>();
        
        public int AddedCount => Items.FindAll(i => i.Type == DiffType.Added).Count;
        public int RemovedCount => Items.FindAll(i => i.Type == DiffType.Removed).Count;
        public int ModifiedCount => Items.FindAll(i => i.Type == DiffType.Modified).Count;
        
        public string GetSummary()
        {
            return $"对比 {Version1} vs {Version2}: +{AddedCount} -{RemovedCount} ~{ModifiedCount}";
        }
    }

    /// <summary>
    /// 版本对比算法
    /// </summary>
    public static class UIRecordDiffer
    {
        /// <summary>
        /// 对比两个记录会话
        /// </summary>
        public static DiffResult Compare(UIRecordSession session1, UIRecordSession session2)
        {
            var result = new DiffResult
            {
                Version1 = session1.Version,
                Version2 = session2.Version
            };
            
            // Build path dictionaries for efficient lookup
            var paths1 = new Dictionary<string, UIRecordEvent>();
            var paths2 = new Dictionary<string, UIRecordEvent>();
            
            CollectPaths(session1.Events, paths1);
            CollectPaths(session2.Events, paths2);
            
            // Find added and modified
            foreach (var kvp in paths2)
            {
                string path = kvp.Key;
                var node2 = kvp.Value;
                
                if (!paths1.ContainsKey(path))
                {
                    // Added in session2
                    result.Items.Add(new DiffItem
                    {
                        Type = DiffType.Added,
                        NodePath = path,
                        NodeName = node2.NodeName,
                        NewTag = node2.Tag,
                        NewPrefab = node2.PrefabName
                    });
                }
                else
                {
                    // Exists in both, check for modifications
                    var node1 = paths1[path];
                    
                    if (node1.Tag != node2.Tag || node1.PrefabName != node2.PrefabName)
                    {
                        result.Items.Add(new DiffItem
                        {
                            Type = DiffType.Modified,
                            NodePath = path,
                            NodeName = node2.NodeName,
                            OldTag = node1.Tag,
                            NewTag = node2.Tag,
                            OldPrefab = node1.PrefabName,
                            NewPrefab = node2.PrefabName
                        });
                    }
                }
            }
            
            // Find removed
            foreach (var kvp in paths1)
            {
                string path = kvp.Key;
                var node1 = kvp.Value;
                
                if (!paths2.ContainsKey(path))
                {
                    result.Items.Add(new DiffItem
                    {
                        Type = DiffType.Removed,
                        NodePath = path,
                        NodeName = node1.NodeName,
                        OldTag = node1.Tag,
                        OldPrefab = node1.PrefabName
                    });
                }
            }
            
            // Sort by type: Added, Modified, Removed
            result.Items.Sort((a, b) => a.Type.CompareTo(b.Type));
            
            return result;
        }
        
        private static void CollectPaths(List<UIRecordEvent> events, Dictionary<string, UIRecordEvent> paths)
        {
            if (events == null) return;
            
            foreach (var evt in events)
            {
                if (!string.IsNullOrEmpty(evt.NodePath) && !paths.ContainsKey(evt.NodePath))
                {
                    paths[evt.NodePath] = evt;
                }
                
                CollectPaths(evt.Children, paths);
            }
        }
    }
}
