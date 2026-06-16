using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UIProbe.Core.Contract;
using UIProbe.Infrastructure.UnityAdapters;

namespace UIProbe.Core.Services
{
    /// <summary>
    /// prefab 索引底座(只读)。经 IAssetGateway/IFileSystem 接缝注入,可在内存假体下单测。
    /// 仅本 Service 持有 PrefabIndex,其他只读 Service 派生不另缓存。
    /// 同步形态 + IProgress&lt;float&gt;;jobId/Dispatcher 留 M3。
    /// </summary>
    public sealed class PrefabIndexService
    {
        public const int SchemaVersion = 1;
        private const string PrefabFilter = "t:Prefab";

        private readonly IAssetGateway _assets;
        private readonly IFileSystem _fs;
        private readonly IEditorPrefs _prefs;

        private PrefabIndex _current;

        public PrefabIndexService(IAssetGateway assets, IFileSystem fs, IEditorPrefs prefs)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _fs = fs ?? throw new ArgumentNullException(nameof(fs));
            _prefs = prefs ?? throw new ArgumentNullException(nameof(prefs));
        }

        /// <summary>
        /// 经 IAssetGateway 扫描 prefab 构建索引。RootFolders 为空表示全工程;Incremental 复用上次未变更项。
        /// 全程零静态 AssetDatabase 调用。进度单调非降,终值 1.0。
        /// </summary>
        public PrefabIndex BuildIndex(PrefabIndexBuildOptions options, IProgress<float> progress = null)
        {
            options = options ?? new PrefabIndexBuildOptions();
            string[] roots = options.RootFolders ?? Array.Empty<string>();

            var paths = new List<string>();
            foreach (string guid in _assets.FindAssets(PrefabFilter))
            {
                string path = _assets.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (roots.Length > 0 && !roots.Any(r => IsUnderRoot(path, r))) continue;
                paths.Add(path);
            }

            paths.Sort(StringComparer.Ordinal);

            Dictionary<string, PrefabIndexItem> prior =
                options.Incremental && _current != null
                    ? _current.Items.ToDictionary(i => i.Guid, i => i)
                    : null;

            var items = new List<PrefabIndexItem>(paths.Count);
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                string guid = _assets.AssetPathToGUID(path);

                PrefabIndexItem item;
                if (prior != null && guid.Length > 0 && prior.TryGetValue(guid, out PrefabIndexItem reused))
                {
                    item = reused; // 增量:复用未变更项实例
                }
                else
                {
                    item = new PrefabIndexItem
                    {
                        Guid = guid,
                        AssetPath = path,
                        Name = Path.GetFileNameWithoutExtension(path),
                        FolderPath = NormalizeFolder(Path.GetDirectoryName(path)),
                        ComponentSummary = string.Empty
                    };
                }
                items.Add(item);
                progress?.Report((float)(i + 1) / paths.Count);
            }

            if (paths.Count == 0) progress?.Report(1f);

            _current = new PrefabIndex
            {
                SchemaVersion = SchemaVersion,
                BuiltAt = DateTime.UtcNow.ToString("o"),
                RootPath = roots.Length > 0 ? string.Join(";", roots) : string.Empty,
                Items = items
            };
            return _current;
        }

        /// <summary>读取缓存 JSON。文件不存在或解析失败 → Found=false 且不抛 IO 异常;SchemaVersion 不符 → SchemaValid=false。</summary>
        public LoadCacheResult LoadCache(string cachePath)
        {
            if (!_fs.Exists(cachePath))
            {
                return new LoadCacheResult { Found = false, SchemaValid = false, Index = null };
            }

            PrefabIndex parsed;
            try
            {
                parsed = JsonUtility.FromJson<PrefabIndex>(_fs.ReadAllText(cachePath));
            }
            catch (ArgumentException)
            {
                parsed = null; // 非法 JSON
            }

            if (parsed == null)
            {
                return new LoadCacheResult { Found = false, SchemaValid = false, Index = null };
            }

            bool valid = parsed.SchemaVersion == SchemaVersion;
            if (valid) _current = parsed;
            return new LoadCacheResult { Found = true, SchemaValid = valid, Index = parsed };
        }

        /// <summary>写缓存 JSON。覆盖既有文件前先 Backup 留还原令牌(无既有文件返回空令牌)。</summary>
        public string SaveCache(PrefabIndex index, string cachePath)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            index.SchemaVersion = SchemaVersion;
            if (string.IsNullOrEmpty(index.BuiltAt)) index.BuiltAt = DateTime.UtcNow.ToString("o");

            string token = _fs.Exists(cachePath) ? _fs.Backup(cachePath) : string.Empty;
            _fs.WriteAllText(cachePath, JsonUtility.ToJson(index));
            return token;
        }

        /// <summary>在已持有索引上按路径/名称子串过滤。空 query → 全部;无命中 → 空列表(非 null)。</summary>
        public IReadOnlyList<PrefabIndexItem> Search(string query)
        {
            List<PrefabIndexItem> items = _current?.Items ?? new List<PrefabIndexItem>();
            if (string.IsNullOrEmpty(query)) return items.ToList();

            return items.Where(i =>
                    Contains(i.AssetPath, query) || Contains(i.Name, query))
                .ToList();
        }

        /// <summary>按 GUID 或路径取单条详情。缺失 → ToolError 码 TOOL_NOT_FOUND。</summary>
        public ToolResult GetPrefabDetail(string guidOrPath)
        {
            PrefabIndexItem item = _current?.Items
                .FirstOrDefault(i => i.Guid == guidOrPath || i.AssetPath == guidOrPath);

            if (item == null)
            {
                return new ToolResult
                {
                    Status = ToolStatus.Failed,
                    Error = new ToolError
                    {
                        Code = ToolErrorCodes.ToolNotFound,
                        Message = "prefab not found: " + guidOrPath,
                        Retriable = false
                    }
                };
            }

            return new ToolResult { Status = ToolStatus.Success, Data = JsonUtility.ToJson(item) };
        }

        private static bool Contains(string haystack, string needle) =>
            !string.IsNullOrEmpty(haystack) &&
            haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsUnderRoot(string path, string root)
        {
            if (string.IsNullOrEmpty(root)) return true;
            return path.Equals(root, StringComparison.Ordinal)
                   || path.StartsWith(root.EndsWith("/") ? root : root + "/", StringComparison.Ordinal);
        }

        private static string NormalizeFolder(string folder) =>
            string.IsNullOrEmpty(folder) ? string.Empty : folder.Replace('\\', '/');
    }
}
