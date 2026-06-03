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
        /// 构建图片文件名→路径映射，同名文件自动保留最后修改时间最新的那个
        /// </summary>
        /// <param name="folder">图片文件夹路径</param>
        /// <param name="includeSubfolders">是否包含子文件夹</param>
        /// <param name="duplicateWarnings">输出：所有被忽略的重复文件列表（可用于弹窗提示）</param>
        public static Dictionary<string, string> BuildImageMap(string folder, bool includeSubfolders, out List<string> duplicateWarnings)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var dups = new List<string>();
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
                        string existingPath = result[key];
                        DateTime existingTime = File.GetLastWriteTime(existingPath);

                        string existingRel = RedGoldPathHelper.ToTablePath(existingPath);
                        string newRel = RedGoldPathHelper.ToTablePath(file);
                        string existingTimeStr = existingTime.ToString("yyyy-MM-dd HH:mm:ss");
                        string newTimeStr = fileTime.ToString("yyyy-MM-dd HH:mm:ss");

                        if (fileTime > existingTime)
                        {
                            dups.Add($"  [{key}] 新版本: {newRel} ({newTimeStr}) → 覆盖旧版本: {existingRel} ({existingTimeStr})");
                            result[key] = file;
                        }
                        else if (fileTime == existingTime)
                        {
                            dups.Add($"  [{key}] 修改时间相同，保留: {existingRel}，忽略: {newRel}");
                        }
                        else
                        {
                            dups.Add($"  [{key}] 保留: {existingRel} ({existingTimeStr})，忽略旧版: {newRel} ({newTimeStr})");
                        }
                    }
                }
            }

            duplicateWarnings = dups;
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
