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
        
        // Batch Operation State
        private bool isIndexerBatchMode = false;
        private HashSet<string> selectedPrefabPaths = new HashSet<string>();
        
        // Batch Duplicate Detection State
        private BatchDuplicateResult batchDuplicateResult = null;
        private bool isBatchDetecting = false;
        
        // Aux State
        private List<string> bookmarks = new List<string>();
        private List<string> searchHistory = new List<string>();
        private bool showBookmarks = false;

        private class PrefabIndexItem
        {
            public string Name;
            public string Path;
            public string Guid;
            public string FolderPath;
        }

        private class FolderNode
        {
            public string Name;
            public string FullPath;
            public bool IsExpanded = false;
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
                    if (GUILayout.Button("导出 CSV", GUILayout.Width(80)))
                    {
                        CSVExporter.ExportBatchDuplicateResults(batchDuplicateResult);
                    }
                    
                    if (GUILayout.Button("清除结果", GUILayout.Width(80)))
                    {
                        batchDuplicateResult = null;
                    }
                    GUILayout.EndHorizontal();
                    
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
                    SaveAuxData();
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
            
            EditorGUILayout.LabelField($"📦 {item.Name}", GUILayout.Width(200));
            
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
            
            // Load configured root path
            indexRootPath = EditorPrefs.GetString("UIProbe_IndexRootPath", "");
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
                    RootFolders = folderTree.Values.Select(ConvertFolderToSerializable).ToList()
                };
                
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
            return new SerializablePrefabIndexItem
            {
                Name = item.Name,
                Path = item.Path,
                Guid = item.Guid,
                FolderPath = item.FolderPath
            };
        }
        
        /// <summary>
        /// 转换为可序列化的文件夹节点
        /// </summary>
        private SerializableFolderNode ConvertFolderToSerializable(FolderNode folder)
        {
            return new SerializableFolderNode
            {
                Name = folder.Name,
                FullPath = folder.FullPath,
                IsExpanded = folder.IsExpanded,
                TotalPrefabCount = folder.TotalPrefabCount,
                SubFolders = folder.SubFolders.Select(ConvertFolderToSerializable).ToList(),
                Prefabs = folder.Prefabs.Select(ConvertToSerializable).ToList()
            };
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
                foreach (var rootFolder in cache.RootFolders)
                {
                    folderTree[rootFolder.Name] = ConvertFolderFromSerializable(rootFolder);
                }
                
                isIndexBuilt = true;
                
                Debug.Log($"[UIProbe] 索引已加载: {allPrefabs.Count} 个预制体 (上次更新: {lastIndexUpdateTime})");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UIProbe] 加载索引失败: {e.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 从可序列化对象转换回预制体索引项
        /// </summary>
        private PrefabIndexItem ConvertFromSerializable(SerializablePrefabIndexItem item)
        {
            return new PrefabIndexItem
            {
                Name = item.Name,
                Path = item.Path,
                Guid = item.Guid,
                FolderPath = item.FolderPath
            };
        }
        
        /// <summary>
        /// 从可序列化对象转换回文件夹节点
        /// </summary>
        private FolderNode ConvertFolderFromSerializable(SerializableFolderNode folder)
        {
            return new FolderNode
            {
                Name = folder.Name,
                FullPath = folder.FullPath,
                IsExpanded = folder.IsExpanded,
                TotalPrefabCount = folder.TotalPrefabCount,
                SubFolders = folder.SubFolders.Select(ConvertFolderFromSerializable).ToList(),
                Prefabs = folder.Prefabs.Select(ConvertFromSerializable).ToList()
            };
        }

        private void LoadAuxData()
        {
            string bookmarksStr = EditorPrefs.GetString("UIProbe_Bookmarks", "");
            if (!string.IsNullOrEmpty(bookmarksStr)) 
                bookmarks = new List<string>(bookmarksStr.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries));
            
            string historyStr = EditorPrefs.GetString("UIProbe_History", "");
            if (!string.IsNullOrEmpty(historyStr)) 
                searchHistory = new List<string>(historyStr.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries));
        }

        private void SaveAuxData()
        {
            EditorPrefs.SetString("UIProbe_Bookmarks", string.Join(";", bookmarks));
            EditorPrefs.SetString("UIProbe_History", string.Join(";", searchHistory));
        }

        private void AddToHistory(string query)
        {
            if (string.IsNullOrEmpty(query)) return;
            if (searchHistory.Contains(query)) searchHistory.Remove(query);
            searchHistory.Insert(0, query);
            if (searchHistory.Count > 10) searchHistory.RemoveAt(searchHistory.Count - 1);
            SaveAuxData();
        }

        private void ToggleBookmark(string path)
        {
            if (bookmarks.Contains(path)) bookmarks.Remove(path);
            else bookmarks.Add(path);
            SaveAuxData();
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
        /// 加载批量检测结果到重名检测页面
        /// (此方法在UIProbeWindow_DuplicateChecker.cs中实现)
        /// </summary>
        partial void LoadBatchResultIntoChecker(BatchDuplicateResult result);
    }
}
