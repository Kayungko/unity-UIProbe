using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UIProbe
{
    /// <summary>
    /// 批量检测结果数据 - 单个预制体的检测结果
    /// </summary>
    [Serializable]
    public class PrefabDuplicateResult
    {
        public string PrefabPath;           // 预制体路径
        public string PrefabName;           // 预制体名称
        public string FolderPath;           // 所属文件夹
        public DuplicateNameResult Result;  // 检测结果
        public bool IsProcessed;            // 是否已处理
        public string ProcessedTime;        // 处理时间
        public bool IsDeprecated;           // 是否已弃用
        public string DeprecatedTime;       // 弃用时间
        
        public bool HasDuplicates => Result != null && Result.GroupCount > 0;
        
        public PrefabDuplicateResult(string path, string name, string folder, DuplicateNameResult result)
        {
            PrefabPath = path;
            PrefabName = name;
            FolderPath = folder;
            Result = result;
            IsProcessed = false;
            ProcessedTime = "";
        }
        
        /// <summary>
        /// 获取重名节点的汇总描述
        /// </summary>
        public string GetDuplicateSummary()
        {
            if (!HasDuplicates)
                return "无";
            
            List<string> duplicateNames = new List<string>();
            foreach (var group in Result.Groups)
            {
                duplicateNames.Add($"{group.NodeName}({group.Count})");
            }
            
            return string.Join(", ", duplicateNames);
        }
    }
    
    /// <summary>
    /// 批量检测结果集合
    /// </summary>
    [Serializable]
    public class BatchDuplicateResult
    {
        public List<PrefabDuplicateResult> Results = new List<PrefabDuplicateResult>();
        public string LastCheckTime;  // 检测时间
        
        public int TotalPrefabs => Results.Count;
        public int PrefabsWithDuplicates => Results.FindAll(r => r.HasDuplicates).Count;
        public int ProcessedCount => Results.Count(r => r.IsProcessed);
        
        public BatchDuplicateResult()
        {
            LastCheckTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
        
        public void AddResult(PrefabDuplicateResult result)
        {
            Results.Add(result);
        }
        
        public void Clear()
        {
            Results.Clear();
        }
        
        /// <summary>
        /// 获取汇总信息
        /// </summary>
        public string GetSummary()
        {
            if (TotalPrefabs == 0)
                return "未检测任何预制体";
            
            return $"已检测 {TotalPrefabs} 个预制体，其中 {PrefabsWithDuplicates} 个存在重名节点";
        }
    }
}
