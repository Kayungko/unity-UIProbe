using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

namespace UIProbe
{
    /// <summary>
    /// 重名分组数据
    /// </summary>
    [System.Serializable]
    public class DuplicateNameGroup
    {
        public string NodeName;              // 重复的节点名称
        public List<GameObject> Objects;     // 所有同名的对象
        public List<string> Paths;           // 每个对象的完整路径
        public int Count => Objects?.Count ?? 0;   // 重复次数
        
        public DuplicateNameGroup()
        {
            Objects = new List<GameObject>();
            Paths = new List<string>();
        }
    }

    /// <summary>
    /// 重名检测结果
    /// </summary>
    [System.Serializable]
    public class DuplicateNameResult
    {
        public GameObject Prefab;                      // 被检测的预制体
        public List<DuplicateNameGroup> Groups;        // 重名分组
        public int TotalDuplicates => Groups?.Sum(g => g.Count) ?? 0;   // 总重名节点数
        public int GroupCount => Groups?.Count ?? 0;   // 重名组数
        
        public DuplicateNameResult()
        {
            Groups = new List<DuplicateNameGroup>();
        }
        
        public string GetSummary()
        {
            if (GroupCount == 0)
                return "未发现重名节点";
            return $"发现 {GroupCount} 组重名节点（共 {TotalDuplicates} 个节点）";
        }
    }

    /// <summary>
    /// 检测模式
    /// </summary>
    public enum DuplicateDetectionMode
    {
        Global,      // 全局重名检测（整个预制体）
        SameLevel    // 同级重名检测（仅同一父节点下）
    }

    /// <summary>
    /// 预制体重名节点检测规则
    /// </summary>
    public class DuplicateNameRule : IUICheckRule
    {
        public string RuleName => "重名节点检测";
        public string Description => "检测预制体中是否存在同名的 GameObject 节点";
        public bool IsEnabled { get; set; } = true;
        
        public DuplicateDetectionMode Mode { get; set; } = DuplicateDetectionMode.Global;
        
        // 白名单：这些常见名称允许重复
        private static readonly HashSet<string> WhiteList = new HashSet<string>
        {
            "Viewport", "Content", "Scrollbar", "Sliding Area", "Handle"
        };
        
        public List<UIProblem> Check(GameObject root)
        {
            var problems = new List<UIProblem>();
            var result = DetectDuplicates(root, Mode);
            
            // 将检测结果转换为 UIProblem 格式
            foreach (var group in result.Groups)
            {
                foreach (var obj in group.Objects)
                {
                    int index = group.Objects.IndexOf(obj);
                    problems.Add(new UIProblem
                    {
                        Type = UIProblemType.Warning,
                        RuleName = RuleName,
                        Description = $"节点名称重复: '{group.NodeName}' ({index + 1}/{group.Count})",
                        Target = obj,
                        NodePath = GetPath(obj.transform)
                    });
                }
            }
            
            return problems;
        }
        
        /// <summary>
        /// 检测重名节点（核心算法）
        /// </summary>
        public static DuplicateNameResult DetectDuplicates(GameObject prefab, DuplicateDetectionMode mode, DuplicateDetectionSettings settings = null)
        {
            var result = new DuplicateNameResult { Prefab = prefab };
            
            if (prefab == null)
                return result;

            // 使用默认设置（如果未提供）
            if (settings == null)
                settings = DuplicateDetectionSettings.GetDefault();
            
            var nameDict = new Dictionary<string, List<GameObject>>();
            var allTransforms = prefab.GetComponentsInChildren<Transform>(true);
            
            foreach (var t in allTransforms)
            {
                // 跳过根节点
                if (t == prefab.transform)
                    continue;
                
                // 应用设置规则过滤
                if (!settings.ShouldCheckDuplicate(t.name, t.gameObject))
                    continue;
                
                // 生成检测 key
                string key = mode == DuplicateDetectionMode.SameLevel
                    ? GetSiblingKey(t)  // 父节点路径 + 节点名
                    : t.name;           // 仅节点名
                
                if (!nameDict.ContainsKey(key))
                    nameDict[key] = new List<GameObject>();
                
                nameDict[key].Add(t.gameObject);
            }
            
            // 筛选出重复项（出现次数 > 1）
            foreach (var kvp in nameDict)
            {
                if (kvp.Value.Count > 1)
                {
                    var group = new DuplicateNameGroup
                    {
                        NodeName = ExtractNodeName(kvp.Key, mode),
                        Objects = kvp.Value,
                        Paths = new List<string>()
                    };
                    
                    // 生成每个对象的完整路径
                    foreach (var obj in kvp.Value)
                    {
                        group.Paths.Add(GetPath(obj.transform));
                    }
                    
                    result.Groups.Add(group);
                }
            }
            
            // 按重复次数降序排序
            result.Groups.Sort((a, b) => b.Count.CompareTo(a.Count));
            
            return result;
        }
        
        /// <summary>
        /// 获取同级检测的 key（父路径 + 节点名）
        /// </summary>
        private static string GetSiblingKey(Transform t)
        {
            string parentPath = t.parent != null ? GetPath(t.parent) : "";
            return parentPath + "/" + t.name;
        }
        
        /// <summary>
        /// 从 key 中提取节点名
        /// </summary>
        private static string ExtractNodeName(string key, DuplicateDetectionMode mode)
        {
            if (mode == DuplicateDetectionMode.SameLevel)
            {
                // 从 "parent/path/NodeName" 中提取 NodeName
                int lastSlash = key.LastIndexOf('/');
                return lastSlash >= 0 ? key.Substring(lastSlash + 1) : key;
            }
            return key;
        }
        
        /// <summary>
        /// 获取节点的完整路径
        /// </summary>
        private static string GetPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }
    }
    
    /// <summary>
    /// 重名检测工具类（高级功能）
    /// </summary>
    public static class DuplicateNameDetector
    {
        /// <summary>
        /// 快速检测（静态方法入口）
        /// </summary>
        public static DuplicateNameResult QuickCheck(GameObject prefab)
        {
            return DuplicateNameRule.DetectDuplicates(prefab, DuplicateDetectionMode.Global);
        }
        
        /// <summary>
        /// 批量检测多个预制体
        /// </summary>
        public static Dictionary<GameObject, DuplicateNameResult> BatchCheck(List<GameObject> prefabs, DuplicateDetectionMode mode)
        {
            var results = new Dictionary<GameObject, DuplicateNameResult>();
            
            foreach (var prefab in prefabs)
            {
                if (prefab != null)
                {
                    results[prefab] = DuplicateNameRule.DetectDuplicates(prefab, mode);
                }
            }
            
            return results;
        }
        
        /// <summary>
        /// 生成自动重命名建议
        /// </summary>
        public static Dictionary<GameObject, string> GenerateRenameSuggestions(DuplicateNameGroup group)
        {
            var suggestions = new Dictionary<GameObject, string>();
            
            for (int i = 0; i < group.Objects.Count; i++)
            {
                var obj = group.Objects[i];
                string suggestion;
                
                // 尝试基于父节点名称生成有意义的后缀
                if (obj.transform.parent != null)
                {
                    string parentName = obj.transform.parent.name;
                    suggestion = $"{group.NodeName}_{parentName}";
                }
                else
                {
                    // 默认使用数字后缀
                    suggestion = $"{group.NodeName}_{i + 1}";
                }
                
                suggestions[obj] = suggestion;
            }
            
            return suggestions;
        }
    }
}
