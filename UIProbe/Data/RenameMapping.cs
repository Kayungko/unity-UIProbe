using System;
using System.Collections.Generic;
using UnityEngine;

namespace UIProbe
{
    /// <summary>
    /// 单个节点的重命名映射
    /// </summary>
    [Serializable]
    public class NodeRenameMapping
    {
        public string nodePath;      // 节点在预制体中的完整路径
        public string oldName;       // 原名称
        public string newName;       // 新名称
        public int instanceID;       // 实例ID（导出时记录，导入时可能不匹配）
        
        public NodeRenameMapping(string nodePath, string oldName, string newName, int instanceID = 0)
        {
            this.nodePath = nodePath;
            this.oldName = oldName;
            this.newName = newName;
            this.instanceID = instanceID;
        }
    }

    /// <summary>
    /// 预重命名映射文件数据结构
    /// </summary>
    [Serializable]
    public class RenameMappingData
    {
        public string version = "1.0";
        public string prefabName;        // 预制体名称
        public string prefabPath;        // 预制体路径
        public string exportTime;        // 导出时间
        public List<NodeRenameMapping> mappings = new List<NodeRenameMapping>();

        // 统计信息（仅用于显示，不序列化到JSON）
        [NonSerialized]
        public int validMappings = 0;    // 有效映射数（节点存在）
        [NonSerialized]
        public int invalidMappings = 0;  // 无效映射数（节点不存在）
        
        public RenameMappingData()
        {
        }
        
        public RenameMappingData(string prefabName, string prefabPath)
        {
            this.prefabName = prefabName;
            this.prefabPath = prefabPath;
            this.exportTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
        
        /// <summary>
        /// 添加映射
        /// </summary>
        public void AddMapping(string nodePath, string oldName, string newName, int instanceID = 0)
        {
            mappings.Add(new NodeRenameMapping(nodePath, oldName, newName, instanceID));
        }
        
        /// <summary>
        /// 导出为JSON字符串
        /// </summary>
        public string ToJson()
        {
            return JsonUtility.ToJson(this, true);
        }
        
        /// <summary>
        /// 从JSON字符串导入
        /// </summary>
        public static RenameMappingData FromJson(string json)
        {
            try
            {
                return JsonUtility.FromJson<RenameMappingData>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[UIProbe] 解析重命名映射JSON失败: {e.Message}");
                return null;
            }
        }
    }
}
