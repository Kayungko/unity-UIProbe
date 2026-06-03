using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace UIProbe
{
    /// <summary>
    /// 输出文件自动编号分配器：检测目录中已有的命名模式，自动递增编号避免冲突
    /// </summary>
    internal class RedGoldNamingState
    {
        private string prefix = "";
        private string suffix = "";
        private int nextNumber = 1;
        private int digits = 3;
        private bool hasPattern;
        private readonly HashSet<string> reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> allocatedPreferredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static RedGoldNamingState Create(string folder)
        {
            var state = new RedGoldNamingState();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return state;

            int bestNumber = -1;
            var prefixCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(file);
                state.reservedNames.Add(fileName);

                string nameNoExt = Path.GetFileNameWithoutExtension(file);
                int lastUnderline = nameNoExt.LastIndexOf('_');
                if (lastUnderline >= 0)
                {
                    string commonPrefix = nameNoExt.Substring(0, lastUnderline + 1);
                    if (!prefixCounts.ContainsKey(commonPrefix))
                        prefixCounts[commonPrefix] = 0;
                    prefixCounts[commonPrefix]++;
                }

                MatchCollection matches = Regex.Matches(nameNoExt, "\\d+");
                if (matches.Count == 0) continue;

                Match numberMatch = matches[matches.Count - 1];
                if (!int.TryParse(numberMatch.Value, out int number)) continue;
                if (number <= bestNumber) continue;

                bestNumber = number;
                state.prefix = nameNoExt.Substring(0, numberMatch.Index);
                state.suffix = nameNoExt.Substring(numberMatch.Index + numberMatch.Length);
                state.digits = numberMatch.Length;
                state.nextNumber = number + 1;
                state.hasPattern = true;
            }

            if (!state.hasPattern && prefixCounts.Count > 0)
            {
                var bestPrefix = prefixCounts
                    .OrderByDescending(x => x.Value)
                    .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .First();
                if (bestPrefix.Value >= 2)
                {
                    state.prefix = bestPrefix.Key;
                    state.suffix = "";
                    state.digits = 3;
                    state.nextNumber = bestPrefix.Value + 1;
                    state.hasPattern = true;
                }
            }

            return state;
        }

        public string Allocate(string folder, string fallbackNameNoExt)
        {
            if (hasPattern)
            {
                while (true)
                {
                    string name = $"{prefix}{nextNumber.ToString("D" + digits)}{suffix}.png";
                    nextNumber++;
                    if (reservedNames.Add(name))
                        return Path.Combine(folder, name);
                }
            }

            string safeName = string.IsNullOrEmpty(fallbackNameNoExt) ? "image" : fallbackNameNoExt;
            string candidate = safeName + ".png";
            int index = 1;
            while (!reservedNames.Add(candidate))
            {
                candidate = $"{safeName}_{index}.png";
                index++;
            }

            return Path.Combine(folder, candidate);
        }

        public void ReserveFileName(string fileName)
        {
            if (!string.IsNullOrEmpty(fileName))
                reservedNames.Add(fileName);
        }

        public string ReservePreferred(string folder, string preferredFileName)
        {
            if (string.IsNullOrEmpty(preferredFileName))
                return "";

            string extension = Path.GetExtension(preferredFileName);
            string nameNoExt = Path.GetFileNameWithoutExtension(preferredFileName);
            if (string.IsNullOrEmpty(extension))
                extension = ".png";

            string candidate = nameNoExt + extension;
            if (allocatedPreferredNames.Add(candidate))
            {
                reservedNames.Add(candidate);
                return Path.Combine(folder, candidate);
            }

            int index = 2;
            while (true)
            {
                candidate = $"{nameNoExt}_{index}{extension}";
                if (allocatedPreferredNames.Add(candidate) && reservedNames.Add(candidate))
                    return Path.Combine(folder, candidate);
                index++;
            }
        }
    }
}
