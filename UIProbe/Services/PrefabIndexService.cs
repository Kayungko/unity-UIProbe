using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace UIProbe
{
    /// <summary>
    /// 预制体索引项的资源引用（运行时态）。原为 UIProbeWindow 私有嵌套类型，
    /// 提升为程序集内 internal 顶层类型，供 Indexer/AssetReferences/NestingOverview 共享。
    /// </summary>
    [Serializable]
    internal class AssetReference
    {
        public string AssetPath;             // 资源路径
        public string NodePath;              // 使用该资源的节点路径
        public AssetReferenceType Type;      // 引用类型
        public string AssetName;             // 资源文件名（用于快速显示）
        public string ExtraInfo;             // 额外信息（如组件类型、预制体GUID等）
    }

    /// <summary>预制体索引项（运行时态）。</summary>
    internal class PrefabIndexItem
    {
        public string Name;
        public string Path;
        public string Guid;
        public string FolderPath;
        public List<AssetReference> AssetReferences = new List<AssetReference>();

        // 便捷属性：获取特定类型的引用
        public List<AssetReference> GetReferencesByType(AssetReferenceType type)
        {
            return AssetReferences.FindAll(r => r.Type == type);
        }

        // 便捷属性：获取引用数量统计
        public Dictionary<AssetReferenceType, int> GetReferenceCounts()
        {
            var counts = new Dictionary<AssetReferenceType, int>();
            foreach (var reference in AssetReferences)
            {
                if (!counts.ContainsKey(reference.Type))
                    counts[reference.Type] = 0;
                counts[reference.Type]++;
            }
            return counts;
        }
    }

    /// <summary>索引文件夹树节点。</summary>
    internal class FolderNode
    {
        public string Name;
        public string FullPath;
        public bool IsExpanded = false;
        public FolderNode Parent;  // 添加父节点引用
        public List<FolderNode> SubFolders = new List<FolderNode>();
        public List<PrefabIndexItem> Prefabs = new List<PrefabIndexItem>();
        public int TotalPrefabCount = 0;
    }

    /// <summary>
    /// 共享预制体索引服务：持有索引数据（allPrefabs / folderTree / 版本号 / 根目录 / 时间戳）
    /// 与磁盘缓存读写。Indexer 写入，DuplicateChecker / AssetReferences / NestingOverview /
    /// FilterNodeScanner 读取。各模块通过同一实例共享，避免模块间直连。
    /// </summary>
    internal sealed class PrefabIndexService
    {
        public List<PrefabIndexItem> AllPrefabs { get; set; } = new List<PrefabIndexItem>();
        public Dictionary<string, FolderNode> FolderTree { get; private set; } = new Dictionary<string, FolderNode>();
        public bool IsIndexBuilt { get; set; } = false;
        public string IndexRootPath { get; set; } = "";   // Configured in Settings
        public string LastIndexUpdateTime { get; set; } = "";
        public int PrefabIndexVersion { get; set; } = 0;

        /// <summary>获取索引缓存文件路径</summary>
        public string GetIndexCachePath()
        {
            string cachePath = System.IO.Path.Combine(
                UIProbeStorage.GetMainFolderPath(),
                "IndexCache.json"
            );
            return cachePath;
        }

        /// <summary>保存索引到磁盘</summary>
        public void SaveIndexCache()
        {
            try
            {
                var cache = new PrefabIndexCache
                {
                    IndexRootPath = IndexRootPath,
                    LastUpdateTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    TotalPrefabCount = AllPrefabs.Count,
                    AllPrefabs = AllPrefabs.Select(ConvertToSerializable).ToList(),
                    AllFolders = new List<SerializableFolderNode>()
                };

                // 扁平化收集所有文件夹
                CollectFoldersFlattened(FolderTree.Values.ToList(), cache.AllFolders, "");

                string json = JsonUtility.ToJson(cache, true);
                string cachePath = GetIndexCachePath();

                string dir = System.IO.Path.GetDirectoryName(cachePath);
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                System.IO.File.WriteAllText(cachePath, json);
                LastIndexUpdateTime = cache.LastUpdateTime;

                Debug.Log($"[UIProbe] 索引已保存: {AllPrefabs.Count} 个预制体");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UIProbe] 保存索引失败: {e.Message}");
            }
        }

        /// <summary>转换为可序列化的预制体索引项</summary>
        private SerializablePrefabIndexItem ConvertToSerializable(PrefabIndexItem item)
        {
            var serializable = new SerializablePrefabIndexItem
            {
                Name = item.Name,
                Path = item.Path,
                Guid = item.Guid,
                FolderPath = item.FolderPath
            };

            // 转换资源引用
            foreach (var assetRef in item.AssetReferences)
            {
                serializable.AssetReferences.Add(new SerializableAssetReference
                {
                    AssetPath = assetRef.AssetPath,
                    NodePath = assetRef.NodePath,
                    AssetName = assetRef.AssetName,
                    Type = (int)assetRef.Type,
                    ExtraInfo = assetRef.ExtraInfo
                });
            }

            return serializable;
        }

        /// <summary>转换为可序列化的文件夹节点 (扁平化)</summary>
        private SerializableFolderNode ConvertFolderToSerializable(FolderNode folder, string parentPath)
        {
            return new SerializableFolderNode
            {
                Name = folder.Name,
                FullPath = folder.FullPath,
                ParentPath = parentPath,
                IsExpanded = folder.IsExpanded,
                TotalPrefabCount = folder.TotalPrefabCount,
                Prefabs = folder.Prefabs.Select(ConvertToSerializable).ToList()
            };
        }

        /// <summary>递归收集所有文件夹到扁平列表</summary>
        private void CollectFoldersFlattened(List<FolderNode> nodes, List<SerializableFolderNode> targetList, string parentPath)
        {
            foreach (var node in nodes)
            {
                targetList.Add(ConvertFolderToSerializable(node, parentPath));
                CollectFoldersFlattened(node.SubFolders, targetList, node.FullPath);
            }
        }

        /// <summary>从磁盘加载索引</summary>
        public bool LoadIndexCache()
        {
            try
            {
                string cachePath = GetIndexCachePath();
                if (!System.IO.File.Exists(cachePath))
                {
                    Debug.Log("[UIProbe] 索引缓存不存在");
                    return false;
                }

                string json = System.IO.File.ReadAllText(cachePath);
                var cache = JsonUtility.FromJson<PrefabIndexCache>(json);

                // 检查索引根路径是否变化
                string currentRootPath = EditorPrefs.GetString("UIProbe_IndexRootPath", "");
                if (cache.IndexRootPath != currentRootPath)
                {
                    Debug.Log("[UIProbe] 索引根路径已变化，需要刷新");
                    return false;
                }

                // 恢复数据
                IndexRootPath = cache.IndexRootPath;
                LastIndexUpdateTime = cache.LastUpdateTime;
                AllPrefabs = cache.AllPrefabs.Select(ConvertFromSerializable).ToList();

                // 重建文件夹树
                FolderTree.Clear();
                var folderDict = new Dictionary<string, FolderNode>();

                // 第一步：创建所有节点
                foreach (var flatNode in cache.AllFolders)
                {
                    var folder = new FolderNode
                    {
                        Name = flatNode.Name,
                        FullPath = flatNode.FullPath,
                        IsExpanded = flatNode.IsExpanded,
                        TotalPrefabCount = flatNode.TotalPrefabCount,
                        Prefabs = flatNode.Prefabs.Select(ConvertFromSerializable).ToList()
                    };
                    folderDict[folder.FullPath] = folder;
                }

                // 第二步：构建层级关系
                foreach (var flatNode in cache.AllFolders)
                {
                    if (folderDict.TryGetValue(flatNode.FullPath, out var folder))
                    {
                        if (string.IsNullOrEmpty(flatNode.ParentPath))
                        {
                            // 根节点添加到 FolderTree
                            FolderTree[folder.Name] = folder;
                        }
                        else if (folderDict.TryGetValue(flatNode.ParentPath, out var parent))
                        {
                            folder.Parent = parent;
                            parent.SubFolders.Add(folder);
                        }
                        else
                        {
                            // 找不到父节点，作为根节点处理（防止孤儿节点）
                            FolderTree[folder.Name] = folder;
                        }
                    }
                }

                IsIndexBuilt = true;
                Debug.Log($"[UIProbe] 索引已加载: {AllPrefabs.Count} 个预制体 (上次更新: {LastIndexUpdateTime})");

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UIProbe] 加载索引失败: {e.Message}");
                // 如果加载失败，可能是旧格式数据，删除缓存强制刷新
                try { System.IO.File.Delete(GetIndexCachePath()); } catch {}
                return false;
            }
        }

        /// <summary>从可序列化对象转换回预制体索引项</summary>
        private PrefabIndexItem ConvertFromSerializable(SerializablePrefabIndexItem item)
        {
            var prefabItem = new PrefabIndexItem
            {
                Name = item.Name,
                Path = item.Path,
                Guid = item.Guid,
                FolderPath = item.FolderPath
            };

            // 转换资源引用（支持向后兼容）
            if (item.AssetReferences != null)
            {
                foreach (var serialRef in item.AssetReferences)
                {
                    prefabItem.AssetReferences.Add(new AssetReference
                    {
                        AssetPath = serialRef.AssetPath,
                        NodePath = serialRef.NodePath,
                        AssetName = serialRef.AssetName,
                        Type = (AssetReferenceType)serialRef.Type,
                        ExtraInfo = serialRef.ExtraInfo
                    });
                }
            }

            return prefabItem;
        }
    }
}
