using System;
using System.IO;
using UnityEngine;

namespace UIProbe
{
    internal static class RedGoldPathHelper
    {
        /// <summary>
        /// 将绝对路径转换为 Unity Assets 相对路径（用于表格写回）
        /// </summary>
        public static string ToTablePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return "";

            string full = Path.GetFullPath(absolutePath).Replace('\\', '/');
            string dataPath = Application.dataPath.Replace('\\', '/');
            if (full.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                return "Assets" + full.Substring(dataPath.Length);

            return full;
        }

        /// <summary>
        /// 将相对路径或 Assets 路径转换为绝对文件系统路径
        /// </summary>
        public static string ToAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";

            string normalized = path.Replace('\\', '/').Trim();
            if (string.IsNullOrEmpty(normalized)) return "";
            if (Path.IsPathRooted(normalized))
                return normalized;

            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || normalized == "Assets")
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                return Path.Combine(projectRoot, normalized);
            }

            return Path.GetFullPath(normalized);
        }

        /// <summary>
        /// 根据表格路径生成默认输出表格路径（{name}_导入结果{ext}）
        /// </summary>
        public static string GetDefaultOutputTablePath(string tablePath)
        {
            string absPath = ToAbsolutePath(tablePath);
            if (string.IsNullOrEmpty(absPath)) return "";
            string dir = Path.GetDirectoryName(absPath);
            string name = Path.GetFileNameWithoutExtension(absPath);
            string ext = Path.GetExtension(absPath);
            return Path.Combine(dir, $"{name}_导入结果{ext}");
        }

        /// <summary>
        /// 获取默认输出表格文件名（不含目录）
        /// </summary>
        public static string GetDefaultOutputTableName(string tablePath)
        {
            string absPath = ToAbsolutePath(tablePath);
            if (string.IsNullOrEmpty(absPath)) return "导入结果" + GetTableExtension(tablePath);
            return Path.GetFileNameWithoutExtension(absPath) + "_导入结果";
        }

        /// <summary>
        /// 获取表格文件扩展名，默认为 csv
        /// </summary>
        public static string GetTableExtension(string tablePath)
        {
            string ext = Path.GetExtension(tablePath);
            if (string.IsNullOrEmpty(ext)) return "csv";
            return ext.TrimStart('.');
        }

        /// <summary>
        /// 获取路径中最近的已存在祖先目录（用于文件对话框初始目录）
        /// </summary>
        public static string GetExistingDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string absolute = ToAbsolutePath(path);
            if (string.IsNullOrEmpty(absolute)) return "";
            if (Directory.Exists(absolute)) return absolute;

            string directory = Path.GetDirectoryName(absolute);
            return !string.IsNullOrEmpty(directory) && Directory.Exists(directory) ? directory : "";
        }
    }
}
