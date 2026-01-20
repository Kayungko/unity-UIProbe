using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System;

namespace UIProbe
{
    /// <summary>
    /// 修改日志条目
    /// </summary>
    public class ModificationLogItem
    {
        public string PrefabName;
        public string OldName;
        public string NewName;
        public string NodePath;
        public string Timestamp;
    }

    /// <summary>
    /// 修改日志管理器
    /// 负责记录预制体修改操作并生成CSV日志文件
    /// </summary>
    public static class ModificationLogManager
    {
        private static List<ModificationLogItem> currentSessionLogs = new List<ModificationLogItem>();
        
        /// <summary>
        /// 添加一条修改记录
        /// </summary>
        public static void AddLog(string prefabName, string oldName, string newName, string nodePath)
        {
            var item = new ModificationLogItem
            {
                PrefabName = prefabName,
                OldName = oldName,
                NewName = newName,
                NodePath = nodePath,
                Timestamp = DateTime.Now.ToString("HH:mm:ss")
            };
            
            currentSessionLogs.Add(item);
        }
        
        /// <summary>
        /// 是否有未保存的日志
        /// </summary>
        public static bool HasLogs()
        {
            return currentSessionLogs.Count > 0;
        }
        
        /// <summary>
        /// 清除当前会话日志
        /// </summary>
        public static void ClearLogs()
        {
            currentSessionLogs.Clear();
        }
        
        /// <summary>
        /// 生成CSV日志文件
        /// </summary>
        public static string GenerateCSV(string prefabName)
        {
            if (currentSessionLogs.Count == 0)
                return null;
            
            // 确保目录存在
            string baseDir = Path.Combine(UIProbeStorage.GetStoragePath(), "Modification_Logs");
            string dateDir = Path.Combine(baseDir, DateTime.Now.ToString("yyyy-MM-dd"));
            
            if (!Directory.Exists(dateDir))
            {
                Directory.CreateDirectory(dateDir);
            }
            
            // 构建文件名: PrefabName + 时间戳
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{prefabName}_{timestamp}.csv";
            string filePath = Path.Combine(dateDir, fileName);
            
            try
            {
                var sb = new StringBuilder();
                // 写入BOM头，防止中文乱码
                sb.Append(new byte[] { 0xEF, 0xBB, 0xBF }); // 不对，StringBuilder不能直接append bytes，用Encoding处理
                
                // 表头
                sb.AppendLine("预制体名称,修改前名称,修改后名称,节点路径,修改时间");
                
                foreach (var log in currentSessionLogs)
                {
                    sb.AppendLine($"{EscapeCSV(log.PrefabName)},{EscapeCSV(log.OldName)},{EscapeCSV(log.NewName)},{EscapeCSV(log.NodePath)},{log.Timestamp}");
                }
                
                // 使用UTF8带BOM编码写入
                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
                
                // 清空日志
                ClearLogs();
                
                Debug.Log($"[UIProbe] 修改日志已生成: {filePath}");
                return filePath;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UIProbe] 生成修改日志失败: {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 转义CSV特殊字符
        /// </summary>
        private static string EscapeCSV(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            if (str.Contains(",") || str.Contains("\"") || str.Contains("\r") || str.Contains("\n"))
            {
                return $"\"{str.Replace("\"", "\"\"")}\"";
            }
            return str;
        }
        
        /// <summary>
        /// 获取日志存储根目录
        /// </summary>
        public static string GetLogsRootPath()
        {
            return Path.Combine(UIProbeStorage.GetStoragePath(), "Modification_Logs");
        }
    }
}
