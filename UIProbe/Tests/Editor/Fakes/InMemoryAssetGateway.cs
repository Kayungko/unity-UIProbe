using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UIProbe.Infrastructure.UnityAdapters;

namespace UIProbe.Tests.Editor.Fakes
{
    /// <summary>
    /// IAssetGateway 的内存假体,用 Dictionary 模拟资源表,不依赖 Unity 运行环境。
    /// FindAssets 用预置的搜索词做简化匹配(包含即命中),够测试用,不复刻 AssetDatabase 全部 filter 语法。
    /// </summary>
    public sealed class InMemoryAssetGateway : IAssetGateway
    {
        private sealed class Entry
        {
            public string Guid;
            public string Path;
            public string[] SearchTerms;
            public UnityEngine.Object Asset;
        }

        private readonly List<Entry> _entries = new List<Entry>();

        /// <summary>可控数据规模上限。null 表示不限制;超过上限时 Seed 抛出。</summary>
        public int? MaxEntries { get; set; }

        public int Count => _entries.Count;

        public void Seed(string guid, string path, UnityEngine.Object asset = null, params string[] searchTerms)
        {
            if (MaxEntries.HasValue && _entries.Count >= MaxEntries.Value)
            {
                throw new InvalidOperationException("InMemoryAssetGateway capacity exceeded: " + MaxEntries.Value);
            }
            _entries.Add(new Entry { Guid = guid, Path = path, Asset = asset, SearchTerms = searchTerms ?? Array.Empty<string>() });
        }

        public string[] FindAssets(string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return _entries.Select(e => e.Guid).ToArray();
            }

            return _entries
                .Where(e => e.SearchTerms.Any(t => t.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                            || e.Path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(e => e.Guid)
                .ToArray();
        }

        public T LoadAssetAtPath<T>(string assetPath) where T : UnityEngine.Object
        {
            return _entries.FirstOrDefault(e => e.Path == assetPath)?.Asset as T;
        }

        public string MoveAsset(string sourcePath, string destinationPath)
        {
            var entry = _entries.FirstOrDefault(e => e.Path == sourcePath);
            if (entry == null)
            {
                return "source asset not found: " + sourcePath;
            }
            entry.Path = destinationPath;
            return string.Empty;
        }

        public string GUIDToAssetPath(string guid)
        {
            return _entries.FirstOrDefault(e => e.Guid == guid)?.Path ?? string.Empty;
        }

        public string AssetPathToGUID(string assetPath)
        {
            return _entries.FirstOrDefault(e => e.Path == assetPath)?.Guid ?? string.Empty;
        }
    }
}
