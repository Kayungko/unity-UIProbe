using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace UIProbe
{
    /// <summary>
    /// 图片重命名日志条目
    /// </summary>
    public class ImageRenameLogItem
    {
        public string OriginalName;     // 原始文件名（含扩展名）
        public string NewName;          // 新文件名（含扩展名）
        public string OriginalPath;     // 原始完整路径
        public string TargetFolder;     // 目标文件夹
        public string Mode;             // 操作模式：原路径覆盖 / 复制到目标
    }

    /// <summary>
    /// 图片重命名日志管理器
    /// 负责记录批量重命名操作并生成 CSV 日志文件
    /// 对齐 ModificationLogManager 风格
    /// </summary>
    public static class ImageRenameLogManager
    {
        /// <summary>
        /// 生成重命名日志 CSV 文件
        /// </summary>
        /// <param name="items">本次操作的日志条目列表</param>
        /// <returns>生成的 CSV 文件路径，失败返回 null</returns>
        public static string GenerateLog(List<ImageRenameLogItem> items)
        {
            if (items == null || items.Count == 0) return null;

            string baseDir = UIProbeStorage.GetImageRenameLogsPath();
            string dateDir  = Path.Combine(baseDir, DateTime.Now.ToString("yyyy-MM-dd"));

            if (!Directory.Exists(dateDir))
                Directory.CreateDirectory(dateDir);

            // 文件名带时间戳，同一天多次操作各自独立
            string fileName = $"ImageRename_{DateTime.Now:HHmmss}.csv";
            string filePath = Path.Combine(dateDir, fileName);

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("原文件名,新文件名,原路径,目标文件夹,操作模式,执行时间");

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                foreach (var item in items)
                {
                    sb.AppendLine(
                        $"{EscapeCSV(item.OriginalName)}," +
                        $"{EscapeCSV(item.NewName)}," +
                        $"{EscapeCSV(item.OriginalPath)}," +
                        $"{EscapeCSV(item.TargetFolder)}," +
                        $"{EscapeCSV(item.Mode)}," +
                        $"{timestamp}"
                    );
                }

                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
                Debug.Log($"[UIProbe] 图片重命名日志已生成: {filePath}");
                return filePath;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UIProbe] 生成图片重命名日志失败: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 打开日志根目录
        /// </summary>
        public static string GetLogsRootPath()
        {
            return UIProbeStorage.GetImageRenameLogsPath();
        }

        private static string EscapeCSV(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            if (str.Contains(",") || str.Contains("\"") || str.Contains("\r") || str.Contains("\n"))
                return $"\"{str.Replace("\"", "\"\"")}\"";
            return str;
        }
    }
}
