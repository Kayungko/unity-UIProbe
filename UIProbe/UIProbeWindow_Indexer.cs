using UnityEngine;
using UnityEditor;
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
        
        // Batch Operation State
        private bool isIndexerBatchMode = false;
        private HashSet<string> selectedPrefabPaths = new HashSet<string>();
        
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
                GUI.enabled = true;
                
                GUILayout.EndHorizontal();
            }

            // History
            if (!isIndexerBatchMode && searchHistory.Count > 0)
            {
                // ...
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("历史:", GUILayout.Width(35));
                for (int i = 0; i < Mathf.Min(5, searchHistory.Count); i++)
                {
                    var hist = searchHistory[i];
                    if (GUILayout.Button(hist, EditorStyles.miniButton, GUILayout.MaxWidth(80)))
                    {
                        searchString = hist;
                        AddToHistory(hist);
                        ExpandMatchingFolders();
                        GUI.FocusControl(null); 
                    }
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
                EditorGUILayout.HelpBox("索引未构建，请点击刷新。", MessageType.Info);
                if (GUILayout.Button("立即构建索引"))
                {
                    RefreshIndexWithTree();
                }
                return;
            }

            // Show root path info
            string displayRoot = string.IsNullOrEmpty(indexRootPath) ? "Assets/" : indexRootPath;
            EditorGUILayout.LabelField($"索引根目录: {displayRoot} | 共 {allPrefabs.Count} 个预制体", EditorStyles.miniLabel);

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
                if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(20)))
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
    }
}
