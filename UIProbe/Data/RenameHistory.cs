using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UIProbe
{
    /// <summary>
    /// 单条重命名记录
    /// </summary>
    [Serializable]
    public class RenameRecord
    {
        public string Timestamp;           // 时间戳
        public string PrefabPath;          // 预制体资源路径
        public string PrefabName;          // 预制体名称
        public string NodePath;            // 节点在预制体中的路径
        public string OldName;             // 旧名称
        public string NewName;             // 新名称
        public string Operator;            // 操作者（编辑器用户名）
        public bool CanRollback;           // 是否可回滚（节点是否还存在）
        
        [NonSerialized]
        public string FilePath;            // JSON文件路径（用于删除）

        public RenameRecord()
        {
            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Operator = Environment.UserName;
            CanRollback = true;
        }

        public string GetDisplayText()
        {
            return $"[{Timestamp}] {PrefabName} | {NodePath}: {OldName} → {NewName}";
        }
    }
    
    /// <summary>
    /// 日期文件夹分组
    /// </summary>
    public class DateFolderGroup
    {
        public string Date;                     // 日期，如 "2026-01-19"
        public List<RenameRecord> Records;      // 该日期的所有记录
        public bool IsExpanded;                 // 是否展开（用于UI）
        
        public DateFolderGroup()
        {
            Records = new List<RenameRecord>();
            IsExpanded = false;
        }
    }
    
    /// <summary>
    /// 预制体每日记录（同一预制体在同一天的所有重命名记录）
    /// </summary>
    [Serializable]
    public class PrefabDailyRecords
    {
        public string PrefabName;               // 预制体名称
        public List<RenameRecord> Records = new List<RenameRecord>();
        
        public int RecordCount => Records != null ? Records.Count : 0;
    }

    /// <summary>
    /// 重命名历史记录集合
    /// </summary>
    [Serializable]
    public class RenameHistoryData
    {
        public List<RenameRecord> Records = new List<RenameRecord>();

        public void AddRecord(RenameRecord record)
        {
            Records.Insert(0, record); // 插入到最前面（最新的在前）
        }

        public int GetRecordCount()
        {
            return Records.Count;
        }

        public void Clear()
        {
            Records.Clear();
        }
    }

    /// <summary>
    /// 重命名历史管理器
    /// </summary>
    public static class RenameHistoryManager
    {
        private const string HISTORY_FILE_NAME = "RenameHistory.json";
        private static RenameHistoryData cachedHistory;

        /// <summary>
        /// 获取历史记录文件路径
        /// </summary>
        public static string GetHistoryFilePath()
        {
            return Path.Combine(UIProbeStorage.GetRenameHistoryPath(), HISTORY_FILE_NAME);
        }

        /// <summary>
        /// 加载历史记录
        /// </summary>
        public static RenameHistoryData LoadHistory()
        {
            if (cachedHistory != null)
                return cachedHistory;

            string filePath = GetHistoryFilePath();
            
            if (File.Exists(filePath))
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    cachedHistory = JsonUtility.FromJson<RenameHistoryData>(json);
                    
                    if (cachedHistory == null)
                        cachedHistory = new RenameHistoryData();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[UIProbe] Failed to load rename history: {e.Message}");
                    cachedHistory = new RenameHistoryData();
                }
            }
            else
            {
                cachedHistory = new RenameHistoryData();
            }

            return cachedHistory;
        }

        /// <summary>
        /// 保存历史记录
        /// </summary>
        public static void SaveHistory()
        {
            if (cachedHistory == null)
                return;

            try
            {
                string filePath = GetHistoryFilePath();
                string json = JsonUtility.ToJson(cachedHistory, true);
                File.WriteAllText(filePath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[UIProbe] Failed to save rename history: {e.Message}");
            }
        }

        /// <summary>
        /// 添加重命名记录（保存为独立JSON文件）
        /// </summary>
        public static void AddRecord(GameObject obj, string oldName, string newName, string prefabPath)
        {
            var record = new RenameRecord
            {
                PrefabPath = prefabPath,
                PrefabName = Path.GetFileNameWithoutExtension(prefabPath),
                NodePath = GetNodePath(obj.transform),
                OldName = oldName,
                NewName = newName
            };

            // 保存为独立JSON文件
            SaveRecordToFile(record);

            Debug.Log($"[UIProbe] Rename recorded: {record.GetDisplayText()}");
        }
        
        /// <summary>
        /// 保存单条记录到文件（追加到预制体对应的JSON）
        /// </summary>
        private static void SaveRecordToFile(RenameRecord record)
        {
            try
            {
                // 获取日期文件夹路径（yyyy-MM-dd）
                DateTime now = DateTime.Parse(record.Timestamp);
                string dateFolder = now.ToString("yyyy-MM-dd");
                string dateFolderPath = Path.Combine(UIProbeStorage.GetRenameHistoryPath(), dateFolder);
                
                // 确保日期文件夹存在
                if (!Directory.Exists(dateFolderPath))
                {
                    Directory.CreateDirectory(dateFolderPath);
                }
                
                // 文件名：预制体名.json（不带时间戳）
                string fileName = $"{record.PrefabName}.json";
                string filePath = Path.Combine(dateFolderPath, fileName);
                
                // 加载现有记录或创建新列表
                PrefabDailyRecords dailyRecords;
                if (File.Exists(filePath))
                {
                    // 文件存在，加载并追加
                    string existingJson = File.ReadAllText(filePath);
                    dailyRecords = JsonUtility.FromJson<PrefabDailyRecords>(existingJson);
                    if (dailyRecords == null || dailyRecords.Records == null)
                    {
                        dailyRecords = new PrefabDailyRecords { PrefabName = record.PrefabName };
                    }
                }
                else
                {
                    // 新文件
                    dailyRecords = new PrefabDailyRecords { PrefabName = record.PrefabName };
                }
                
                // 追加新记录到列表顶部（最新的在前）
                dailyRecords.Records.Insert(0, record);
                
                // 保存JSON
                string json = JsonUtility.ToJson(dailyRecords, true);
                File.WriteAllText(filePath, json);
                
                Debug.Log($"[UIProbe] Saved record to: {filePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[UIProbe] Failed to save record: {e.Message}");
            }
        }
        
        /// <summary>
        /// 按日期分组加载历史记录
        /// </summary>
        public static List<DateFolderGroup> LoadHistoryGroupedByDate()
        {
            var groups = new List<DateFolderGroup>();
            string historyPath = UIProbeStorage.GetRenameHistoryPath();
            
            if (!Directory.Exists(historyPath))
                return groups;
            
            try
            {
                // 获取所有日期文件夹
                var dateFolders = Directory.GetDirectories(historyPath)
                    .Select(Path.GetFileName)
                    .Where(name => System.Text.RegularExpressions.Regex.IsMatch(name, @"^\d{4}-\d{2}-\d{2}$"))
                    .OrderByDescending(d => d)  // 最新日期在前
                    .ToList();
                
                foreach (var dateFolder in dateFolders)
                {
                    var group = new DateFolderGroup
                    {
                        Date = dateFolder,
                        Records = LoadRecordsFromDateFolder(dateFolder)
                    };
                    
                    if (group.Records.Count > 0)
                    {
                        groups.Add(group);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[UIProbe] Failed to load history groups: {e.Message}");
            }
            
            return groups;
        }
        
        /// <summary>
        /// 从日期文件夹加载所有记录
        /// </summary>
        private static List<RenameRecord> LoadRecordsFromDateFolder(string dateFolder)
        {
            var records = new List<RenameRecord>();
            string folderPath = Path.Combine(UIProbeStorage.GetRenameHistoryPath(), dateFolder);
            
            if (!Directory.Exists(folderPath))
                return records;
            
            try
            {
                var jsonFiles = Directory.GetFiles(folderPath, "*.json")
                    .OrderBy(f => Path.GetFileNameWithoutExtension(f));  // 按预制体名排序
                
                foreach (var filePath in jsonFiles)
                {
                    try
                    {
                        string json = File.ReadAllText(filePath);
                        var dailyRecords = JsonUtility.FromJson<PrefabDailyRecords>(json);
                        
                        if (dailyRecords != null && dailyRecords.Records != null)
                        {
                            // 添加所有记录到列表
                            foreach (var record in dailyRecords.Records)
                            {
                                // 添加文件路径信息用于删除
                                record.FilePath = filePath;
                                record.PrefabName = dailyRecords.PrefabName;  // 确保PrefabName正确
                                records.Add(record);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[UIProbe] Failed to load record from {filePath}: {e.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[UIProbe] Failed to read date folder {dateFolder}: {e.Message}");
            }
            
            return records;
        }
        
        /// <summary>
        /// 删除单条记录
        /// </summary>
        public static bool DeleteRecord(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    Debug.Log($"[UIProbe] Deleted record: {filePath}");
                    return true;
                }
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UIProbe] Failed to delete record: {e.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 删除整个日期文件夹
        /// </summary>
        public static bool DeleteDateFolder(string dateFolder)
        {
            try
            {
                string folderPath = Path.Combine(UIProbeStorage.GetRenameHistoryPath(), dateFolder);
                if (Directory.Exists(folderPath))
                {
                    Directory.Delete(folderPath, true);
                    Debug.Log($"[UIProbe] Deleted date folder: {dateFolder}");
                    return true;
                }
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UIProbe] Failed to delete date folder: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 回滚重命名操作
        /// </summary>
        public static bool RollbackRename(RenameRecord record)
        {
            try
            {
                // 加载预制体
                GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(record.PrefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[UIProbe] Cannot rollback: Prefab not found at {record.PrefabPath}");
                    record.CanRollback = false;
                    SaveHistory();
                    return false;
                }

                // 查找节点
                Transform node = FindNodeByPath(prefab.transform, record.NodePath);
                if (node == null)
                {
                    Debug.LogWarning($"[UIProbe] Cannot rollback: Node not found at {record.NodePath}");
                    record.CanRollback = false;
                    SaveHistory();
                    return false;
                }

                // 验证当前名称是否匹配
                if (node.name != record.NewName)
                {
                    Debug.LogWarning($"[UIProbe] Cannot rollback: Current name '{node.name}' does not match recorded new name '{record.NewName}'");
                    record.CanRollback = false;
                    SaveHistory();
                    return false;
                }

                // 执行回滚
                UnityEditor.Undo.RecordObject(node.gameObject, "Rollback Rename");
                node.name = record.OldName;
                UnityEditor.EditorUtility.SetDirty(node.gameObject);
                UnityEditor.AssetDatabase.SaveAssets();

                Debug.Log($"[UIProbe] Rollback successful: {record.NewName} → {record.OldName}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UIProbe] Rollback failed: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 清除所有历史记录
        /// </summary>
        public static void ClearHistory()
        {
            var history = LoadHistory();
            history.Clear();
            SaveHistory();
        }

        /// <summary>
        /// 获取节点路径
        /// </summary>
        private static string GetNodePath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        /// <summary>
        /// 根据路径查找节点
        /// </summary>
        private static Transform FindNodeByPath(Transform root, string path)
        {
            string[] parts = path.Split('/');
            Transform current = root;

            foreach (string part in parts)
            {
                if (current.name != part)
                {
                    // 在子节点中查找
                    Transform found = null;
                    foreach (Transform child in current)
                    {
                        if (child.name == part)
                        {
                            found = child;
                            break;
                        }
                    }
                    
                    if (found == null)
                        return null;
                    
                    current = found;
                }
            }

            return current;
        }
    }
}
