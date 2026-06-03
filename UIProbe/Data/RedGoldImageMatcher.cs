using System;
using System.Collections.Generic;
using System.IO;

namespace UIProbe
{
    /// <summary>
    /// 图片资源映射构建与源文件匹配器
    /// </summary>
    internal static class RedGoldImageMatcher
    {
        /// <summary>
        /// 构建图片文件名→路径映射（单文件夹）
        /// </summary>
        public static Dictionary<string, string> BuildImageMap(string folder, bool includeSubfolders, out List<string> duplicateWarnings)
        {
            return BuildImageMap(new List<string> { folder }, includeSubfolders, out duplicateWarnings);
        }

        /// <summary>
        /// 构建图片文件名→路径映射（多文件夹，按优先级）
        /// 同名文件时，靠前的文件夹优先级更高（先到先得）
        /// </summary>
        public static Dictionary<string, string> BuildImageMap(List<string> folders, bool includeSubfolders, out List<string> duplicateWarnings)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var allDups = new List<string>();

            foreach (var folder in folders)
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;

                var folderMap = BuildSingleFolderMap(folder, includeSubfolders);
                foreach (var kvp in folderMap)
                {
                    if (!result.ContainsKey(kvp.Key))
                    {
                        result[kvp.Key] = kvp.Value;
                    }
                    else
                    {
                        string existingRel = RedGoldPathHelper.ToTablePath(result[kvp.Key]);
                        string skippedRel = RedGoldPathHelper.ToTablePath(kvp.Value);
                        allDups.Add($"  [{kvp.Key}] 优先级较低，忽略: {skippedRel}（已有: {existingRel}）");
                    }
                }
            }

            duplicateWarnings = allDups;
            return result;
        }

        /// <summary>
        /// 单文件夹扫描，返回完整映射（含内部同名冲突，已按最新修改时间处理）
        /// </summary>
        private static Dictionary<string, string> BuildSingleFolderMap(string folder, bool includeSubfolders)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            SearchOption option = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            string[] extensions = { "*.png", "*.jpg", "*.jpeg" };
            foreach (string pattern in extensions)
            {
                foreach (string file in Directory.GetFiles(folder, pattern, option))
                {
                    string key = Path.GetFileNameWithoutExtension(file);
                    DateTime fileTime = File.GetLastWriteTime(file);

                    if (!result.ContainsKey(key))
                    {
                        result.Add(key, file);
                    }
                    else
                    {
                        DateTime existingTime = File.GetLastWriteTime(result[key]);
                        if (fileTime > existingTime)
                            result[key] = file;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 从源文件夹或旧路径查找匹配的源图片
        /// 匹配优先级：源文件夹(按图标文件名) > 源文件夹(按名称列) > 旧表格路径(兜底)
        /// </summary>
        public static string FindSourceImage(Dictionary<string, string> imageMap, string name, string iconPath)
        {
            // ① 优先从源文件夹查找：按表格图标路径列的文件名匹配
            if (!string.IsNullOrEmpty(iconPath))
            {
                string iconName = Path.GetFileNameWithoutExtension(iconPath);
                if (!string.IsNullOrEmpty(iconName) && imageMap.TryGetValue(iconName, out string iconMatchPath))
                    return iconMatchPath;
            }

            // ② 从源文件夹查找：按表格名称列匹配
            if (!string.IsNullOrEmpty(name))
            {
                if (imageMap.TryGetValue(name, out string exactPath)) return exactPath;

                string normalizedName = NormalizeFileKey(name);
                foreach (var pair in imageMap)
                {
                    if (NormalizeFileKey(pair.Key) == normalizedName)
                        return pair.Value;
                }
            }

            // ③ 兜底：源文件夹找不到时，才用旧表格路径（已有输出文件）
            if (!string.IsNullOrEmpty(iconPath))
            {
                string absoluteIconPath = RedGoldPathHelper.ToAbsolutePath(iconPath);
                if (!string.IsNullOrEmpty(absoluteIconPath) && File.Exists(absoluteIconPath))
                    return absoluteIconPath;
            }

            return "";
        }

        private static string NormalizeFileKey(string value)
        {
            return (value ?? "").Trim().Replace(" ", "").ToLowerInvariant();
        }
    }
}
