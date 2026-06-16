using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace UIProbe
{
    public partial class UIProbeWindow
    {
        // Indexer State
        private string searchString = "";
        private Vector2 indexerScrollPosition;
        private Dictionary<string, FolderNode> folderTree = new Dictionary<string, FolderNode>();
        private List<PrefabIndexItem> allPrefabs = new List<PrefabIndexItem>();
        private bool isIndexBuilt = false;
        private string indexRootPath = "";  // Configured in Settings
        private string lastIndexUpdateTime = "";
        private int prefabIndexVersion = 0;
        
        // Batch Operation State
        private bool isIndexerBatchMode = false;
        private HashSet<string> selectedPrefabPaths = new HashSet<string>();
        
        // Batch Duplicate Detection State
        private BatchDuplicateResult batchDuplicateResult = null;
        
        // Aux State
        private List<string> bookmarks = new List<string>();
        private List<string> searchHistory = new List<string>();
        private bool showBookmarks = false;
        private Dictionary<string, bool> prefabDetailsExpanded = new Dictionary<string, bool>();  // 追踪预制体详情展开状态

        [Serializable]
        private class AssetReference
        {
            public string AssetPath;             // 资源路径
            public string NodePath;              // 使用该资源的节点路径
            public AssetReferenceType Type;      // 引用类型
            public string AssetName;             // 资源文件名（用于快速显示）
            public string ExtraInfo;             // 额外信息（如组件类型、预制体GUID等）
        }
        
        private class PrefabIndexItem
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

        private class FolderNode
        {
            public string Name;
            public string FullPath;
            public bool IsExpanded = false;
            public FolderNode Parent;  // 添加父节点引用
            public List<FolderNode> SubFolders = new List<FolderNode>();
            public List<PrefabIndexItem> Prefabs = new List<PrefabIndexItem>();
            public int TotalPrefabCount = 0;
        }

        private void DrawIndexerTab()
        {
            EditorGUILayout.LabelField("预制体索引 (Prefab Indexer)", EditorStyles.boldLabel);
            
            // Search bar
            GUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            searchString = EditorGUILayout.TextField("", searchString, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck())
            {
                if (!string.IsNullOrEmpty(searchString))
                {
                    ExpandMatchingFolders();
                }
            }
            if (GUILayout.Button("搜索", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                AddToHistory(searchString);
                ExpandMatchingFolders();
            }
            
            // Clear search button
            if (!string.IsNullOrEmpty(searchString))
            {
                if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(25)))
                {
                    searchString = "";
                    CollapseAllFolders();
                    GUI.FocusControl(null);
                }
            }
            
            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                RefreshIndexWithTree();
            }
            
            if (GUILayout.Button("全部折叠", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                CollapseAllFolders();
            }
            
            GUILayout.Space(10);
            
            // Batch Mode Toggle
            bool newBatchMode = GUILayout.Toggle(isIndexerBatchMode, "批量操作", EditorStyles.toolbarButton, GUILayout.Width(60));
            if (newBatchMode != isIndexerBatchMode)
            {
                isIndexerBatchMode = newBatchMode;
                selectedPrefabPaths.Clear();
            }
            
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Batch Operation Toolbar
            if (isIndexerBatchMode)
            {
                GUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"已选中: {selectedPrefabPaths.Count}", GUILayout.Width(80));
                
                if (GUILayout.Button("全选", EditorStyles.miniButton, GUILayout.Width(40)))
                {
                    SelectAllPrefabs();
                }
                
                if (GUILayout.Button("全不选", EditorStyles.miniButton, GUILayout.Width(50)))
                {
                    selectedPrefabPaths.Clear();
                }
                
                GUILayout.FlexibleSpace();
                
                GUI.enabled = selectedPrefabPaths.Count > 0;
                if (GUILayout.Button("在 Project 中选中", EditorStyles.miniButton))
                {
                    BatchSelectInProject();
                }
                
                if (GUILayout.Button("批量检测重名", EditorStyles.miniButton, GUILayout.Width(100)))
                {
                    BatchDetectDuplicates();
                }
                GUI.enabled = true;
                
                GUILayout.EndHorizontal();
                
                // Batch detection results
                if (batchDuplicateResult != null && batchDuplicateResult.TotalPrefabs > 0)
                {
                    EditorGUILayout.Space(5);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    
                    EditorGUILayout.LabelField("批量检测结果", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(batchDuplicateResult.GetSummary(), EditorStyles.wordWrappedLabel);
                    
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("导出CSV(简化)", GUILayout.Width(100)))
                    {
                        CSVExporter.ExportBatchDuplicateResults(batchDuplicateResult, false);
                    }
                    
                    if (GUILayout.Button("导出CSV(详细)", GUILayout.Width(100)))
                    {
                        CSVExporter.ExportBatchDuplicateResults(batchDuplicateResult, true);
                    }
                    
                    if (GUILayout.Button("清除结果", GUILayout.Width(80)))
                    {
                        batchDuplicateResult = null;
                    }
                    GUILayout.EndHorizontal();
                    
                    EditorGUILayout.LabelField("简化: 预制体+汇总 | 详细: 每个重复路径单独一行", EditorStyles.miniLabel);
                    
                    EditorGUILayout.EndVertical();
                }
            }

            // History
            if (!isIndexerBatchMode && searchHistory.Count > 0)
            {
                // ...
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("历史:", GUILayout.Width(35));
                
                string historyToRemove = null;
                
                for (int i = 0; i < Mathf.Min(5, searchHistory.Count); i++)
                {
                    var hist = searchHistory[i];
                    
                    // 搜索历史按钮
                    if (GUILayout.Button(hist, EditorStyles.miniButton, GUILayout.MaxWidth(80)))
                    {
                        searchString = hist;
                        AddToHistory(hist);
                        ExpandMatchingFolders();
                        GUI.FocusControl(null); 
                    }
                    
                    // 删除按钮
                    if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(18)))
                    {
                        historyToRemove = hist;
                    }
                }
                
                // 执行删除操作
                if (historyToRemove != null)
                {
                    searchHistory.Remove(historyToRemove);
                }
                
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            // Bookmarks
            showBookmarks = EditorGUILayout.Foldout(showBookmarks, $"★ 收藏夹 ({bookmarks.Count})", true);
            if (showBookmarks)
            {
                DrawBookmarks();
            }

            // Index status
            if (!isIndexBuilt)
            {
                EditorGUILayout.HelpBox("索引未加载。", MessageType.Info);
                if (GUILayout.Button("立即构建索引"))
                {
                    RefreshIndexWithTree();
                }
                return;
            }

            // Show root path info with last update time
            string displayRoot = string.IsNullOrEmpty(indexRootPath) ? "Assets/" : indexRootPath;
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"索引根目录: {displayRoot} | 共 {allPrefabs.Count} 个预制体", EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(lastIndexUpdateTime))
            {
                EditorGUILayout.LabelField($"上次更新: {lastIndexUpdateTime}", EditorStyles.miniLabel, GUILayout.Width(200));
            }
            GUILayout.EndHorizontal();

            // Folder tree
            indexerScrollPosition = EditorGUILayout.BeginScrollView(indexerScrollPosition);
            
            foreach (var folder in folderTree.Values.OrderBy(f => f.Name))
            {
                DrawFolderNode(folder, 0);
            }
            
            EditorGUILayout.EndScrollView();
        }

        private void DrawFolderNode(FolderNode folder, int indent)
        {
            // Filter check
            bool hasMatchingContent = FolderHasMatchingContent(folder, searchString);
            if (!string.IsNullOrEmpty(searchString) && !hasMatchingContent)
                return;

            GUILayout.BeginHorizontal();
            GUILayout.Space(indent * 15);
            
            // Folder icon and name
            string icon = folder.IsExpanded ? "📂" : "📁";
            string label = $"{icon} {folder.Name} ({folder.TotalPrefabCount})";
            
            bool hasContent = folder.SubFolders.Count > 0 || folder.Prefabs.Count > 0;
            if (hasContent)
            {
                folder.IsExpanded = EditorGUILayout.Foldout(folder.IsExpanded, label, true);
            }
            else
            {
                EditorGUILayout.LabelField(label);
            }
            
            GUILayout.EndHorizontal();

            if (folder.IsExpanded)
            {
                // Draw subfolders
                foreach (var subFolder in folder.SubFolders.OrderBy(f => f.Name))
                {
                    DrawFolderNode(subFolder, indent + 1);
                }

                // Draw prefabs in this folder
                foreach (var prefab in folder.Prefabs)
                {
                    if (!string.IsNullOrEmpty(searchString) && 
                        !prefab.Name.ToLower().Contains(searchString.ToLower()))
                        continue;
                        
                    DrawPrefabItem(prefab, indent + 1);
                }
            }
        }

        private void DrawPrefabItem(PrefabIndexItem item, int indent)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(indent * 15);
            
            if (isIndexerBatchMode)
            {
                bool isSelected = selectedPrefabPaths.Contains(item.Path);
                bool newSelected = GUILayout.Toggle(isSelected, "", GUILayout.Width(20));
                if (newSelected != isSelected)
                {
                    if (newSelected) selectedPrefabPaths.Add(item.Path);
                    else selectedPrefabPaths.Remove(item.Path);
                }
            }
            
            // 展开/折叠按钮
            bool isExpanded = prefabDetailsExpanded.ContainsKey(item.Path) && prefabDetailsExpanded[item.Path];
            string expandIcon = isExpanded ? "▼" : "▶";
            if (GUILayout.Button(expandIcon, GUILayout.Width(20)))
            {
                prefabDetailsExpanded[item.Path] = !isExpanded;
            }
            
            EditorGUILayout.LabelField($"📦 {item.Name}", GUILayout.Width(200));
            
            // 资源引用统计
            if (item.AssetReferences.Count > 0)
            {
                var counts = item.GetReferenceCounts();
                string statsText = "";
                
                if (counts.ContainsKey(AssetReferenceType.Image) || counts.ContainsKey(AssetReferenceType.RawImage))
                {
                    int imageCount = (counts.ContainsKey(AssetReferenceType.Image) ? counts[AssetReferenceType.Image] : 0) +
                                    (counts.ContainsKey(AssetReferenceType.RawImage) ? counts[AssetReferenceType.RawImage] : 0);
                    statsText += $"🖼 {imageCount} ";
                }
                
                if (counts.ContainsKey(AssetReferenceType.Prefab))
                {
                    statsText += $"📦 {counts[AssetReferenceType.Prefab]} ";
                }
                
                if (counts.ContainsKey(AssetReferenceType.Material))
                {
                    statsText += $"🎨 {counts[AssetReferenceType.Material]} ";
                }
                
                if (counts.ContainsKey(AssetReferenceType.Font))
                {
                    statsText += $"🔤 {counts[AssetReferenceType.Font]} ";
                }
                
                EditorGUILayout.LabelField(statsText.TrimEnd(), EditorStyles.miniLabel, GUILayout.Width(80));
            }
            
            // Bookmark star
            bool isBookmarked = bookmarks.Contains(item.Path);
            if (GUILayout.Button(isBookmarked ? "★" : "☆", GUILayout.Width(25)))
            {
                ToggleBookmark(item.Path);
            }

            if (GUILayout.Button("打开", GUILayout.Width(40)))
            {
                var obj = AssetDatabase.LoadAssetAtPath<GameObject>(item.Path);
                if (obj != null) AssetDatabase.OpenAsset(obj);
            }
            if (GUILayout.Button("定位", GUILayout.Width(40)))
            {
                var obj = AssetDatabase.LoadAssetAtPath<GameObject>(item.Path);
                if (obj != null) EditorGUIUtility.PingObject(obj);
            }
            
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            // 详情面板（展开时显示）
            if (isExpanded && item.AssetReferences.Count > 0)
            {
                DrawPrefabAssetDetails(item, indent);
            }
        }
        
        /// <summary>
        /// 绘制预制体资源详情（按类型分组）
        /// </summary>
        private void DrawPrefabAssetDetails(PrefabIndexItem item, int indent)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(indent * 15 + 40);
            
            EditorGUILayout.LabelField($"📋 使用的资源 ({item.AssetReferences.Count})", EditorStyles.boldLabel);
            
            // 按类型分组显示
            var groupedReferences = item.AssetReferences
                .GroupBy(r => r.Type)
                .OrderBy(g => g.Key);
            
            foreach (var group in groupedReferences)
            {
                AssetReferenceType type = group.Key;
                var references = group.ToList();
                
                // 类型标题
                GUILayout.Space(5);
                string typeIcon = GetAssetTypeIcon(type);
                string typeName = GetAssetTypeName(type);
                EditorGUILayout.LabelField($"{typeIcon} {typeName} ({references.Count})", EditorStyles.boldLabel);
                
                // 显示每个引用
                foreach (var assetRef in references)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(10);
                    
                    // 资源图标
                    EditorGUILayout.LabelField(typeIcon, GUILayout.Width(20));
                    
                    // 资源名称（可点击定位）
                    string displayName = assetRef.AssetName;
                    if (GUILayout.Button(displayName, EditorStyles.linkLabel, GUILayout.Width(150)))
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetRef.AssetPath);
                        if (asset != null) 
                        {
                            EditorGUIUtility.PingObject(asset);
                            // 如果是预制体，还可以打开它
                            if (type == AssetReferenceType.Prefab)
                            {
                                Selection.activeObject = asset;
                            }
                        }
                    }
                    
                    // 额外信息（如果有）
                    if (!string.IsNullOrEmpty(assetRef.ExtraInfo))
                    {
                        EditorGUILayout.LabelField($"({assetRef.ExtraInfo})", EditorStyles.miniLabel, GUILayout.Width(80));
                    }
                    
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    
                    // 节点路径
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(30);
                    EditorGUILayout.LabelField($"📍 {assetRef.NodePath}", EditorStyles.miniLabel);
                    GUILayout.EndHorizontal();
                    
                    GUILayout.Space(3);
                }
            }
            
            GUILayout.EndVertical();
        }
        
        /// <summary>
        /// 获取资源类型图标
        /// </summary>
        private string GetAssetTypeIcon(AssetReferenceType type)
        {
            switch (type)
            {
                case AssetReferenceType.Image:
                case AssetReferenceType.RawImage:
                    return "🖼";
                case AssetReferenceType.Prefab:
                    return "📦";
                case AssetReferenceType.Material:
                    return "🎨";
                case AssetReferenceType.Font:
                    return "🔤";
                default:
                    return "📄";
            }
        }
        
        /// <summary>
        /// 获取资源类型名称
        /// </summary>
        private string GetAssetTypeName(AssetReferenceType type)
        {
            switch (type)
            {
                case AssetReferenceType.Image:
                    return "图片 (Image)";
                case AssetReferenceType.RawImage:
                    return "纹理 (RawImage)";
                case AssetReferenceType.Prefab:
                    return "预制体 (Prefab)";
                case AssetReferenceType.Material:
                    return "材质 (Material)";
                case AssetReferenceType.Font:
                    return "字体 (Font)";
                default:
                    return "未知资源";
            }
        }

        private void DrawBookmarks()
        {
            if (bookmarks.Count == 0)
            {
                EditorGUILayout.LabelField("  暂无收藏");
                return;
            }
            
            foreach (var bm in bookmarks.ToList())
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                string name = Path.GetFileNameWithoutExtension(bm);
                EditorGUILayout.LabelField($"📦 {name}", GUILayout.Width(180));
                if (GUILayout.Button("打开", EditorStyles.miniButton, GUILayout.Width(35)))
                {
                    var obj = AssetDatabase.LoadAssetAtPath<GameObject>(bm);
                    if (obj != null) AssetDatabase.OpenAsset(obj);
                }
                if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(35)))
                {
                    var obj = AssetDatabase.LoadAssetAtPath<GameObject>(bm);
                    if (obj != null) EditorGUIUtility.PingObject(obj);
                }
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20)))
                {
                    ToggleBookmark(bm);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.Space();
        }

        private void RefreshIndexWithTree()
        {
            allPrefabs.Clear();
            folderTree.Clear();
            
            // Ensure we use the latest configured path from EditorPrefs (Settings tab writes to this)
            indexRootPath = EditorPrefs.GetString("UIProbe_IndexRootPath", "");

            // Load configured root path
            string searchPath = string.IsNullOrEmpty(indexRootPath) ? "Assets" : indexRootPath;
            
            // Find all prefabs
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { searchPath });
            
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string folderPath = Path.GetDirectoryName(path).Replace("\\", "/");
                
                var item = new PrefabIndexItem
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    Path = path,
                    Guid = guid,
                    FolderPath = folderPath
                };
                
                // 收集所有资源引用
                CollectAssetReferences(item);
                
                allPrefabs.Add(item);
                AddToFolderTree(item, folderPath);
            }
            
            // Calculate total counts
            foreach (var root in folderTree.Values)
            {
                CalculateTotalCount(root);
            }
            
            isIndexBuilt = true;
            
            // 保存索引缓存
            SaveIndexCache();
            NotifyPrefabIndexChanged();
        }

        private void NotifyPrefabIndexChanged()
        {
            prefabIndexVersion++;
            OnPrefabIndexChangedForAssetReferences();
        }

        private void AddToFolderTree(PrefabIndexItem item, string folderPath)
        {
            string[] parts = folderPath.Split('/');
            
            // Get or create root folder
            string rootName = parts[0];
            if (!folderTree.ContainsKey(rootName))
            {
                folderTree[rootName] = new FolderNode { Name = rootName, FullPath = rootName };
            }
            
            FolderNode current = folderTree[rootName];
            string currentPath = rootName;
            
            // Navigate/create subfolder structure
            for (int i = 1; i < parts.Length; i++)
            {
                currentPath += "/" + parts[i];
                var subFolder = current.SubFolders.FirstOrDefault(f => f.Name == parts[i]);
                
                if (subFolder == null)
                {
                    subFolder = new FolderNode { Name = parts[i], FullPath = currentPath };
                    current.SubFolders.Add(subFolder);
                }
                
                current = subFolder;
            }
            
            // Add prefab to the final folder
            current.Prefabs.Add(item);
        }

        private int CalculateTotalCount(FolderNode folder)
        {
            int count = folder.Prefabs.Count;
            foreach (var sub in folder.SubFolders)
            {
                count += CalculateTotalCount(sub);
            }
            folder.TotalPrefabCount = count;
            return count;
        }

        private bool FolderHasMatchingContent(FolderNode folder, string search)
        {
            if (string.IsNullOrEmpty(search)) return true;
            
            string lowerSearch = search.ToLower();
            
            // Check folder name
            if (folder.Name.ToLower().Contains(lowerSearch)) return true;
            
            // Check prefabs in this folder
            if (folder.Prefabs.Any(p => p.Name.ToLower().Contains(lowerSearch))) return true;
            
            // Check subfolders
            return folder.SubFolders.Any(f => FolderHasMatchingContent(f, search));
        }

        private void ExpandMatchingFolders()
        {
            if (string.IsNullOrEmpty(searchString)) return;
            
            foreach (var root in folderTree.Values)
            {
                ExpandIfMatching(root, searchString.ToLower());
            }
        }

        private bool ExpandIfMatching(FolderNode folder, string search)
        {
            bool hasMatch = folder.Prefabs.Any(p => p.Name.ToLower().Contains(search));
            
            foreach (var sub in folder.SubFolders)
            {
                if (ExpandIfMatching(sub, search))
                {
                    hasMatch = true;
                }
            }
            
            if (hasMatch)
            {
                folder.IsExpanded = true;
            }
            
            return hasMatch;
        }

        private void CollapseAllFolders()
        {
            foreach (var root in folderTree.Values)
            {
                CollapseFolder(root);
            }
        }

        private void CollapseFolder(FolderNode folder)
        {
            folder.IsExpanded = false;
            foreach (var sub in folder.SubFolders)
            {
                CollapseFolder(sub);
            }
        }
        
        /// <summary>
        /// 获取索引缓存文件路径
        /// </summary>
        private string GetIndexCachePath()
        {
            string cachePath = System.IO.Path.Combine(
                UIProbeStorage.GetMainFolderPath(), 
                "IndexCache.json"
            );
            return cachePath;
        }
        
        /// <summary>
        /// 保存索引到磁盘
        /// </summary>
        private void SaveIndexCache()
        {
            try
            {
                var cache = new PrefabIndexCache
                {
                    IndexRootPath = indexRootPath,
                    LastUpdateTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    TotalPrefabCount = allPrefabs.Count,
                    AllPrefabs = allPrefabs.Select(ConvertToSerializable).ToList(),
                    AllFolders = new List<SerializableFolderNode>()
                };
                
                // 扁平化收集所有文件夹
                CollectFoldersFlattened(folderTree.Values.ToList(), cache.AllFolders, "");
                
                string json = JsonUtility.ToJson(cache, true);
                string cachePath = GetIndexCachePath();
                
                string dir = System.IO.Path.GetDirectoryName(cachePath);
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                    
                System.IO.File.WriteAllText(cachePath, json);
                lastIndexUpdateTime = cache.LastUpdateTime;
                
                Debug.Log($"[UIProbe] 索引已保存: {allPrefabs.Count} 个预制体");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UIProbe] 保存索引失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 转换为可序列化的预制体索引项
        /// </summary>
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
        
        /// <summary>
        /// 转换为可序列化的文件夹节点 (扁平化)
        /// </summary>
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
        
        /// <summary>
        /// 递归收集所有文件夹到扁平列表
        /// </summary>
        private void CollectFoldersFlattened(List<FolderNode> nodes, List<SerializableFolderNode> targetList, string parentPath)
        {
            foreach (var node in nodes)
            {
                targetList.Add(ConvertFolderToSerializable(node, parentPath));
                CollectFoldersFlattened(node.SubFolders, targetList, node.FullPath);
            }
        }

        /// <summary>
        /// 从磁盘加载索引
        /// </summary>
        private bool LoadIndexCache()
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
                indexRootPath = cache.IndexRootPath;
                lastIndexUpdateTime = cache.LastUpdateTime;
                allPrefabs = cache.AllPrefabs.Select(ConvertFromSerializable).ToList();
                
                // 重建文件夹树
                folderTree.Clear();
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
                            // 根节点添加到 folderTree
                            folderTree[folder.Name] = folder;
                        }
                        else if (folderDict.TryGetValue(flatNode.ParentPath, out var parent))
                        {
                            folder.Parent = parent;
                            parent.SubFolders.Add(folder);
                        }
                        else
                        {
                            // 找不到父节点，作为根节点处理（防止孤儿节点）
                            folderTree[folder.Name] = folder;
                        }
                    }
                }
                
                isIndexBuilt = true;
                Debug.Log($"[UIProbe] 索引已加载: {allPrefabs.Count} 个预制体 (上次更新: {lastIndexUpdateTime})");
                
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

        
        /// <summary>
        /// 从可序列化对象转换回预制体索引项
        /// </summary>
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
        


        private void ApplyIndexerConfig()
        {
            if (config == null || config.indexer == null) return;
            
            indexRootPath = config.indexer.rootPath;
            
            if (config.indexer.bookmarks != null)
                bookmarks = new List<string>(config.indexer.bookmarks);
            else
                bookmarks = new List<string>();
                
            if (config.indexer.searchHistory != null)
                searchHistory = new List<string>(config.indexer.searchHistory);
            else
                searchHistory = new List<string>();
        }

        private void CollectIndexerConfig()
        {
            if (config == null) return;
            
            if (config.indexer == null) config.indexer = new IndexerConfig();
            
            config.indexer.rootPath = indexRootPath;
            config.indexer.bookmarks = bookmarks.ToArray();
            config.indexer.searchHistory = searchHistory.ToArray();
        }

        private void AddToHistory(string query)
        {
            if (string.IsNullOrEmpty(query)) return;
            if (searchHistory.Contains(query)) searchHistory.Remove(query);
            searchHistory.Insert(0, query);
            if (searchHistory.Count > 10) searchHistory.RemoveAt(searchHistory.Count - 1);
        }

        private void ToggleBookmark(string path)
        {
            if (bookmarks.Contains(path)) bookmarks.Remove(path);
            else bookmarks.Add(path);
        }

        private void SelectAllPrefabs()
        {
            selectedPrefabPaths.Clear();
            foreach (var prefab in allPrefabs)
            {
                selectedPrefabPaths.Add(prefab.Path);
            }
        }

        private void BatchSelectInProject()
        {
            var objects = new List<UnityEngine.Object>();
            foreach (var path in selectedPrefabPaths)
            {
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (obj != null) objects.Add(obj);
            }
            
            if (objects.Count > 0)
            {
                Selection.objects = objects.ToArray();
                EditorGUIUtility.PingObject(objects[0]);
            }
        }
        
        /// <summary>
        /// 批量检测预制体重名节点
        /// </summary>
        private void BatchDetectDuplicates()
        {
            if (selectedPrefabPaths.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先选择要检测的预制体", "确定");
                return;
            }
            
            // 加载检测设置
            if (duplicateSettings == null)
            {
                string settingsJson = EditorPrefs.GetString("UIProbe_DuplicateSettings", "");
                if (!string.IsNullOrEmpty(settingsJson))
                {
                    try
                    {
                        duplicateSettings = JsonUtility.FromJson<DuplicateDetectionSettings>(settingsJson);
                    }
                    catch
                    {
                        duplicateSettings = DuplicateDetectionSettings.GetDefault();
                    }
                }
                else
                {
                    duplicateSettings = DuplicateDetectionSettings.GetDefault();
                }
            }
            
            batchDuplicateResult = new BatchDuplicateResult();
            int processedCount = 0;
            int totalCount = selectedPrefabPaths.Count;
            
            try
            {
                foreach (var prefabPath in selectedPrefabPaths)
                {
                    processedCount++;
                    
                    // 显示进度条
                    float progress = (float)processedCount / totalCount;
                    if (EditorUtility.DisplayCancelableProgressBar(
                        "批量检测重名", 
                        $"正在检测: {Path.GetFileNameWithoutExtension(prefabPath)} ({processedCount}/{totalCount})", 
                        progress))
                    {
                        break; // 用户取消
                    }
                    
                    // 加载预制体
                    GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (prefabAsset == null)
                        continue;
                    
                    // 执行重名检测（使用设置中配置的范围）
                    DuplicateDetectionMode scope = duplicateSettings.DetectionScope;
                    DuplicateNameResult result = DuplicateNameRule.DetectDuplicates(
                        prefabAsset, 
                        scope, 
                        duplicateSettings
                    );
                    
                    // 记录结果
                    string folderPath = Path.GetDirectoryName(prefabPath);
                    string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
                    
                    batchDuplicateResult.AddResult(new PrefabDuplicateResult(
                        prefabPath,
                        prefabName,
                        folderPath,
                        result
                    ));
                }
                
                EditorUtility.ClearProgressBar();
                
                // 保存JSON结果到Batch_Results文件夹
                string jsonPath = "";
                try
                {
                    jsonPath = System.IO.Path.Combine(
                        UIProbeStorage.GetBatchResultsPath(),
                        $"BatchDuplicateCheck_{System.DateTime.Now:yyyyMMdd_HHmmss}.json"
                    );
                    string json = JsonUtility.ToJson(batchDuplicateResult, true);
                    System.IO.File.WriteAllText(jsonPath, json);
                    Debug.Log($"[UIProbe] 批量检测结果已保存到: {jsonPath}");
                }
                catch (Exception saveEx)
                {
                    Debug.LogWarning($"[UIProbe] JSON保存失败: {saveEx.Message}");
                }
                
                // 显示结果摘要
                string summary = batchDuplicateResult.GetSummary();
                
                // 如果有重名，询问是否切换到重名检测页面
                if (batchDuplicateResult.PrefabsWithDuplicates > 0)
                {
                    bool switchTab = EditorUtility.DisplayDialog(
                        "批量检测完成",
                        $"{summary}\n\n发现 {batchDuplicateResult.PrefabsWithDuplicates} 个预制体存在重名。\n\n是否切换到重名检测页面进行处理？",
                        "是，切换",
                        "稍后处理"
                    );
                    
                    if (switchTab)
                    {
                        // 切换到重名检测标签页
                        currentTab = Tab.DuplicateChecker;
                        LoadBatchResultIntoCheckerWithPath(batchDuplicateResult, jsonPath);
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog("检测完成", summary, "确定");
                }
                
                Debug.Log($"[UIProbe] 批量检测完成: {summary}");
                
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("检测失败", $"批量检测失败: {e.Message}", "确定");
                Debug.LogError($"[UIProbe] 批量检测失败: {e}");
            }
        }
        
        /// <summary>
        /// 加载批量检测结果到重名检测页面（带JSON路径）
        /// </summary>
        private void LoadBatchResultIntoCheckerWithPath(BatchDuplicateResult result, string jsonPath)
        {
            // 调用partial方法
            LoadBatchResultIntoChecker(result);
            
            // 在UIProbeWindow_DuplicateCheckerBatch.cs中会设置currentBatchResult
            // 这里我们需要另外设置路径
            currentBatchResultPath = jsonPath;
        }
        
        
        /// <summary>
        /// 收集预制体中的所有资源引用
        /// </summary>
        private void CollectAssetReferences(PrefabIndexItem item)
        {
            // 加载预制体
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(item.Path);
            if (prefab == null) return;
            
            item.AssetReferences.Clear();
            
            // 收集各类资源引用
            CollectImageReferences(item, prefab);
            CollectPrefabReferences(item, prefab);
            CollectMaterialReferences(item, prefab); // 启用材质球引用收集
            // 可选：CollectFontReferences(item, prefab);
        }
        
        /// <summary>
        /// 收集预制体中的图片引用
        /// </summary>
        private void CollectImageReferences(PrefabIndexItem item, GameObject prefab)
        {
            // 扫描所有 Image 组件
            var images = prefab.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            foreach (var image in images)
            {
                if (image.sprite != null)
                {
                    string spritePath = AssetDatabase.GetAssetPath(image.sprite);
                    if (!string.IsNullOrEmpty(spritePath))
                    {
                        string nodePath = GetNodePath(prefab.transform, image.transform);
                        // 使用 Sprite 的实际名称，而不是文件名
                        // 这样可以搜索图集中的 sprite
                        string spriteName = image.sprite.name;
                        string fileName = Path.GetFileName(spritePath);
                        
                        item.AssetReferences.Add(new AssetReference
                        {
                            AssetPath = spritePath,
                            NodePath = nodePath,
                            Type = AssetReferenceType.Image,
                            AssetName = spriteName, // 改为sprite名称，而不是文件名
                            ExtraInfo = spriteName != fileName ? $"Image ({fileName})" : "Image"
                        });
                    }
                }
            }
            
            // 扫描所有 RawImage 组件
            var rawImages = prefab.GetComponentsInChildren<UnityEngine.UI.RawImage>(true);
            foreach (var rawImage in rawImages)
            {
                if (rawImage.texture != null)
                {
                    string texturePath = AssetDatabase.GetAssetPath(rawImage.texture);
                    if (!string.IsNullOrEmpty(texturePath))
                    {
                        string nodePath = GetNodePath(prefab.transform, rawImage.transform);
                        item.AssetReferences.Add(new AssetReference
                        {
                            AssetPath = texturePath,
                            NodePath = nodePath,
                            Type = AssetReferenceType.RawImage,
                            AssetName = Path.GetFileName(texturePath),
                            ExtraInfo = "RawImage"
                        });
                    }
                }
            }
        }
        
        /// <summary>
        /// 收集预制体中嵌套的预制体引用
        /// </summary>
        private void CollectPrefabReferences(PrefabIndexItem item, GameObject prefab)
        {
            // 遍历所有 Transform，找到嵌套的预制体实例
            Transform[] allTransforms = prefab.GetComponentsInChildren<Transform>(true);
            
            HashSet<GameObject> processedPrefabs = new HashSet<GameObject>();
            
            foreach (Transform t in allTransforms)
            {
                // 跳过根节点
                if (t == prefab.transform) continue;
                
                // 检查是否是预制体实例根节点
                GameObject prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject);
                
                // 如果这个节点是预制体根，且未处理过
                if (prefabRoot != null && prefabRoot == t.gameObject && !processedPrefabs.Contains(prefabRoot))
                {
                    processedPrefabs.Add(prefabRoot);
                    
                    // 获取预制体源资源
                    GameObject prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(prefabRoot);
                    if (prefabSource != null)
                    {
                        string prefabPath = AssetDatabase.GetAssetPath(prefabSource);
                        if (!string.IsNullOrEmpty(prefabPath))
                        {
                            string nodePath = GetNodePath(prefab.transform, t);
                            string prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
                            
                            item.AssetReferences.Add(new AssetReference
                            {
                                AssetPath = prefabPath,
                                NodePath = nodePath,
                                Type = AssetReferenceType.Prefab,
                                AssetName = Path.GetFileName(prefabPath),
                                ExtraInfo = prefabSource.name
                            });
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// 收集预制体中材质球的纹理引用
        /// </summary>
        private void CollectMaterialReferences(PrefabIndexItem item, GameObject prefab)
        {
            // 扫描所有 Renderer 组件（包括 MeshRenderer, SpriteRenderer, UI 组件等）
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            
            foreach (var renderer in renderers)
            {
                if (renderer.sharedMaterials == null) continue;

                HashSet<Material> processedMaterialsOnRenderer = new HashSet<Material>();

                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null || processedMaterialsOnRenderer.Contains(mat)) continue;
                    processedMaterialsOnRenderer.Add(mat);
                    
                    // 获取材质路径
                    string materialPath = AssetDatabase.GetAssetPath(mat);
                    if (string.IsNullOrEmpty(materialPath)) continue;
                    
                    string nodePath = GetNodePath(prefab.transform, renderer.transform);
                    
                    // 遍历材质的所有纹理属性
                    var shader = mat.shader;
                    if (shader != null)
                    {
                        int propertyCount = ShaderUtil.GetPropertyCount(shader);
                        for (int i = 0; i < propertyCount; i++)
                        {
                            if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                            {
                                string propertyName = ShaderUtil.GetPropertyName(shader, i);
                                Texture texture = mat.GetTexture(propertyName);
                                
                                if (texture != null)
                                {
                                    string texturePath = AssetDatabase.GetAssetPath(texture);
                                    if (!string.IsNullOrEmpty(texturePath))
                                    {
                                        // 添加纹理引用
                                        item.AssetReferences.Add(new AssetReference
                                        {
                                            AssetPath = texturePath,
                                            NodePath = nodePath,
                                            Type = AssetReferenceType.Material,
                                            AssetName = texture.name, // 纹理名称
                                            ExtraInfo = $"Material: {mat.name} ({propertyName})"
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// 获取节点的层级路径
        /// </summary>
        private string GetNodePath(Transform root, Transform target)
        {
            if (target == root) return root.name;

            List<string> path = new List<string>();
            Transform current = target;

            while (current != null)
            {
                path.Insert(0, current.name);
                if (current == root)
                    break;

                current = current.parent;
            }

            return string.Join("/", path);
        }
        
        /// <summary>
        /// 加载批量检测结果到重名检测页面
        /// (此方法在UIProbeWindow_DuplicateChecker.cs中实现)
        /// </summary>
        partial void LoadBatchResultIntoChecker(BatchDuplicateResult result);
    }
}
