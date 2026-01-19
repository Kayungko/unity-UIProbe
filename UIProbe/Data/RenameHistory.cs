using System;
using System.Collections.Generic;
using System.IO;
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
        /// 添加重命名记录
        /// </summary>
        public static void AddRecord(GameObject obj, string oldName, string newName, string prefabPath)
        {
            var history = LoadHistory();
            
            var record = new RenameRecord
            {
                PrefabPath = prefabPath,
                PrefabName = Path.GetFileNameWithoutExtension(prefabPath),
                NodePath = GetNodePath(obj.transform),
                OldName = oldName,
                NewName = newName
            };

            history.AddRecord(record);
            SaveHistory();

            Debug.Log($"[UIProbe] Rename recorded: {record.GetDisplayText()}");
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
