using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace UIProbe
{
    public partial class UIProbeWindow
    {
        // Browser State
        private List<(string Path, UIRecordSession Session)> savedSessions = new List<(string, UIRecordSession)>();
        private Vector2 browserListScroll;
        private Vector2 browserDetailScroll;
        private int selectedSessionIndex = -1;
        private int compareSessionIndex = -1;
        private bool showDiffView = false;
        private DiffResult currentDiff;
        private Vector2 diffScroll;
        private string tagFilter = "";
        private Texture2D cachedScreenshot;
        private int cachedScreenshotIndex = -1;
        
        // Batch Mode State
        private bool isBrowserBatchMode = false;
        private HashSet<int> selectedIndices = new HashSet<int>();
        
        private void DrawBrowserTab()
        {
            EditorGUILayout.LabelField("历史浏览 (History Browser)", EditorStyles.boldLabel);
            
            // Toolbar
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(40)))
            {
                RefreshSessionList();
            }
            
            if (GUILayout.Button("导入", EditorStyles.toolbarButton, GUILayout.Width(40)))
            {
                ImportRecord();
            }
            
            if (selectedSessionIndex >= 0 && selectedSessionIndex < savedSessions.Count)
            {
                if (GUILayout.Button("导出", EditorStyles.toolbarButton, GUILayout.Width(40)))
                {
                    ExportSelectedRecord();
                }
            }
            
            GUILayout.Space(10);
            
            // Batch Mode
            bool newBatchMode = GUILayout.Toggle(isBrowserBatchMode, "批量删除", EditorStyles.toolbarButton, GUILayout.Width(60));
            if (newBatchMode != isBrowserBatchMode)
            {
                isBrowserBatchMode = newBatchMode;
                selectedIndices.Clear();
            }
            
            GUILayout.FlexibleSpace();
            
            if (!isBrowserBatchMode)
            {
                if (selectedSessionIndex >= 0 && compareSessionIndex >= 0 && selectedSessionIndex != compareSessionIndex)
                {
                    if (GUILayout.Button("对比选中版本", EditorStyles.toolbarButton, GUILayout.Width(90)))
                    {
                        PerformDiff();
                    }
                }
                
                if (showDiffView && GUILayout.Button("返回列表", EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    showDiffView = false;
                }
            }
            GUILayout.EndHorizontal();
            
            if (showDiffView && currentDiff != null)
            {
                DrawDiffView();
                return;
            }
            
            // Main split view
            GUILayout.BeginHorizontal();
            
            // Left: Session list
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(180));
            
            if (isBrowserBatchMode)
            {
                // Batch Actions
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"选中: {selectedIndices.Count}", EditorStyles.boldLabel, GUILayout.Width(60));
                if (GUILayout.Button("全选", EditorStyles.miniButton, GUILayout.Width(35)))
                {
                    selectedIndices.Clear();
                    for(int i=0; i<savedSessions.Count; i++) selectedIndices.Add(i);
                }
                if (GUILayout.Button("删", EditorStyles.miniButton, GUILayout.Width(25)))
                {
                    BatchDeleteSessions();
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.LabelField("版本列表", EditorStyles.boldLabel);
            }
            
            browserListScroll = EditorGUILayout.BeginScrollView(browserListScroll);
            
            if (savedSessions.Count == 0)
            {
                EditorGUILayout.LabelField("暂无记录");
                EditorGUILayout.LabelField("请先在「界面记录」中保存");
            }
            else
            {
                for (int i = 0; i < savedSessions.Count; i++)
                {
                    var (path, session) = savedSessions[i];
                    
                    GUILayout.BeginHorizontal();
                    
                    if (isBrowserBatchMode)
                    {
                        bool isBatchSelected = selectedIndices.Contains(i);
                        bool newBatchSelected = GUILayout.Toggle(isBatchSelected, "", GUILayout.Width(20));
                        if (newBatchSelected != isBatchSelected)
                        {
                            if (newBatchSelected) selectedIndices.Add(i);
                            else selectedIndices.Remove(i);
                        }
                        
                        GUI.enabled = false; // Disable normal selection in batch mode
                    }
                    
                    // Selection checkbox for diff
                    bool isSelected = selectedSessionIndex == i;
                    bool isCompare = compareSessionIndex == i;
                    
                    if (isSelected)
                    {
                        GUI.backgroundColor = Color.cyan;
                    }
                    else if (isCompare)
                    {
                        GUI.backgroundColor = Color.yellow;
                    }
                    
                    if (GUILayout.Button($"v{session.Version}", GUILayout.Width(70)))
                    {
                        if (Event.current.shift && selectedSessionIndex >= 0)
                        {
                            // Shift-click to select for comparison
                            compareSessionIndex = i;
                        }
                        else
                        {
                            selectedSessionIndex = i;
                            LoadCachedScreenshot();
                        }
                    }
                    
                    GUI.backgroundColor = Color.white;
                    GUI.enabled = true; // Re-enable
                    
                    if (!isBrowserBatchMode)
                    {
                        // Compare checkbox
                        bool wantCompare = GUILayout.Toggle(isCompare, "", GUILayout.Width(20));
                        if (wantCompare != isCompare)
                        {
                            compareSessionIndex = wantCompare ? i : -1;
                        }
                        
                        // Delete button (Original single delete button)
                        if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(20)))
                        {
                            if (EditorUtility.DisplayDialog("确认删除", $"确定删除 v{session.Version} 的记录？", "删除", "取消"))
                            {
                                UIRecordStorage.DeleteSession(path);
                                RefreshSessionList();
                                break;
                            }
                        }
                    }
                    
                    GUILayout.EndHorizontal();
                    
                    // Show timestamp and screenshot indicator
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(session.Timestamp, EditorStyles.miniLabel);
                    if (!string.IsNullOrEmpty(session.ScreenshotPath))
                    {
                        GUILayout.Label("📷", GUILayout.Width(18));
                    }
                    GUILayout.EndHorizontal();
                    EditorGUILayout.Space(3);
                }
            }
            
            EditorGUILayout.EndScrollView();
            
            // Tips
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("点击选择，Shift+点击对比", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("勾选框选择对比目标", EditorStyles.miniLabel);
            
            GUILayout.EndVertical();
            
            // Right: Detail view
            GUILayout.BeginVertical();
            
            if (selectedSessionIndex >= 0 && selectedSessionIndex < savedSessions.Count)
            {
                var (path, session) = savedSessions[selectedSessionIndex];
                
                EditorGUILayout.LabelField($"版本 {session.Version} 详情", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"时间: {session.Timestamp}", EditorStyles.miniLabel);
                
                if (!string.IsNullOrEmpty(session.Description))
                {
                    EditorGUILayout.LabelField($"描述: {session.Description}", EditorStyles.miniLabel);
                }
                
                EditorGUILayout.LabelField($"事件数: {session.Events.Count}", EditorStyles.miniLabel);
                
                // Screenshot preview
                if (cachedScreenshot != null && cachedScreenshotIndex == selectedSessionIndex)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("截图预览:", EditorStyles.miniLabel);
                    float aspectRatio = (float)cachedScreenshot.width / cachedScreenshot.height;
                    float previewHeight = 120;
                    float previewWidth = previewHeight * aspectRatio;
                    Rect rect = GUILayoutUtility.GetRect(previewWidth, previewHeight, GUILayout.MaxWidth(previewWidth));
                    GUI.DrawTexture(rect, cachedScreenshot, ScaleMode.ScaleToFit);
                }
                
                // Tag filter
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("标签筛选:", GUILayout.Width(60));
                tagFilter = EditorGUILayout.TextField(tagFilter);
                if (GUILayout.Button("清空", GUILayout.Width(40)))
                {
                    tagFilter = "";
                }
                GUILayout.EndHorizontal();
                
                EditorGUILayout.Space();
                
                browserDetailScroll = EditorGUILayout.BeginScrollView(browserDetailScroll);
                
                foreach (var evt in session.Events)
                {
                    DrawBrowserEvent(evt, 0);
                }
                
                EditorGUILayout.EndScrollView();
            }
            else
            {
                EditorGUILayout.HelpBox("请从左侧列表选择一个版本查看详情", MessageType.Info);
            }
            
            GUILayout.EndVertical();
            
            GUILayout.EndHorizontal();
        }

        private void DrawBrowserEvent(UIRecordEvent evt, int indent)
        {
            // Apply tag filter
            if (!string.IsNullOrEmpty(tagFilter))
            {
                bool hasMatchingTag = evt.Tag.Contains(tagFilter) || evt.NodeName.ToLower().Contains(tagFilter.ToLower());
                bool childHasMatch = evt.Children.Any(c => MatchesFilter(c, tagFilter));
                
                if (!hasMatchingTag && !childHasMatch) return;
            }
            
            GUILayout.BeginHorizontal();
            GUILayout.Space(indent * 15);
            
            string icon = evt.IsPrefabInstance ? "📦" : "📄";
            string display = $"{icon} {evt.NodeName}";
            
            if (evt.Children.Count > 0)
            {
                evt.IsExpanded = EditorGUILayout.Foldout(evt.IsExpanded, display, true);
            }
            else
            {
                EditorGUILayout.LabelField(display, GUILayout.Width(200));
            }
            
            if (!string.IsNullOrEmpty(evt.Tag))
            {
                GUI.backgroundColor = UITagInferrer.GetTagColor(evt.Tag);
                GUILayout.Label(evt.Tag, EditorStyles.miniButton, GUILayout.Width(60));
                GUI.backgroundColor = Color.white;
            }
            
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("C", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                EditorGUIUtility.systemCopyBuffer = evt.NodePath;
            }
            
            GUILayout.EndHorizontal();
            
            if (evt.IsExpanded)
            {
                foreach (var child in evt.Children)
                {
                    DrawBrowserEvent(child, indent + 1);
                }
            }
        }

        private bool MatchesFilter(UIRecordEvent evt, string filter)
        {
            if (evt.Tag.Contains(filter) || evt.NodeName.ToLower().Contains(filter.ToLower()))
                return true;
                
            return evt.Children.Any(c => MatchesFilter(c, filter));
        }

        private void LoadCachedScreenshot()
        {
            // Clear previous
            if (cachedScreenshot != null)
            {
                DestroyImmediate(cachedScreenshot);
                cachedScreenshot = null;
            }
            cachedScreenshotIndex = -1;
            
            if (selectedSessionIndex < 0 || selectedSessionIndex >= savedSessions.Count) return;
            
            var (path, session) = savedSessions[selectedSessionIndex];
            string screenshotPath = UIRecordStorage.GetScreenshotPath(path, session);
            
            if (!string.IsNullOrEmpty(screenshotPath) && File.Exists(screenshotPath))
            {
                byte[] data = File.ReadAllBytes(screenshotPath);
                cachedScreenshot = new Texture2D(2, 2);
                cachedScreenshot.LoadImage(data);
                cachedScreenshotIndex = selectedSessionIndex;
            }
        }

        private void RefreshSessionList()
        {
            // Clear cached screenshot
            if (cachedScreenshot != null)
            {
                DestroyImmediate(cachedScreenshot);
                cachedScreenshot = null;
            }
            cachedScreenshotIndex = -1;
            
            savedSessions = UIRecordStorage.LoadAllSessions();
            savedSessions.Sort((a, b) => string.Compare(b.Session.Timestamp, a.Session.Timestamp));
            selectedSessionIndex = -1;
            compareSessionIndex = -1;
            showDiffView = false;
        }

        private void PerformDiff()
        {
            if (selectedSessionIndex < 0 || compareSessionIndex < 0) return;
            if (selectedSessionIndex == compareSessionIndex) return;
            
            var session1 = savedSessions[selectedSessionIndex].Session;
            var session2 = savedSessions[compareSessionIndex].Session;
            
            currentDiff = UIRecordDiffer.Compare(session1, session2);
            showDiffView = true;
        }

        private void DrawDiffView()
        {
            EditorGUILayout.LabelField(currentDiff.GetSummary(), EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            // Summary stats
            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
            GUILayout.Label($"+{currentDiff.AddedCount} 新增", EditorStyles.miniButton, GUILayout.Width(70));
            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
            GUILayout.Label($"-{currentDiff.RemovedCount} 删除", EditorStyles.miniButton, GUILayout.Width(70));
            GUI.backgroundColor = new Color(0.9f, 0.7f, 0.2f);
            GUILayout.Label($"~{currentDiff.ModifiedCount} 修改", EditorStyles.miniButton, GUILayout.Width(70));
            GUI.backgroundColor = Color.white;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            EditorGUILayout.Space();
            
            // Diff items
            diffScroll = EditorGUILayout.BeginScrollView(diffScroll);
            
            if (currentDiff.Items.Count == 0)
            {
                EditorGUILayout.HelpBox("两个版本完全相同，无差异。", MessageType.Info);
            }
            else
            {
                foreach (var item in currentDiff.Items)
                {
                    GUILayout.BeginHorizontal(EditorStyles.helpBox);
                    
                    GUI.backgroundColor = item.GetColor();
                    GUILayout.Label(item.GetIcon(), EditorStyles.boldLabel, GUILayout.Width(20));
                    GUI.backgroundColor = Color.white;
                    
                    GUILayout.Label(item.GetDescription());
                    
                    GUILayout.FlexibleSpace();
                    
                    if (GUILayout.Button("C", EditorStyles.miniButton, GUILayout.Width(20)))
                    {
                        EditorGUIUtility.systemCopyBuffer = item.NodePath;
                    }
                    
                    GUILayout.EndHorizontal();
                }
            }
            
            EditorGUILayout.EndScrollView();
        }

        private void ExportSelectedRecord()
        {
            if (selectedSessionIndex < 0 || selectedSessionIndex >= savedSessions.Count) return;
            
            var (path, session) = savedSessions[selectedSessionIndex];
            string defaultName = $"UIProbe_v{session.Version}_{session.Timestamp.Replace(":", "-").Replace(" ", "_")}.uiprobe";
            
            string exportPath = EditorUtility.SaveFilePanel(
                "导出 UI 记录",
                "",
                defaultName,
                "uiprobe"
            );
            
            if (!string.IsNullOrEmpty(exportPath))
            {
                if (UIRecordStorage.ExportSession(path, exportPath))
                {
                    EditorUtility.DisplayDialog("导出成功", $"记录已导出到:\n{exportPath}", "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("导出失败", "导出过程中发生错误，请查看 Console。", "OK");
                }
            }
        }

        private void ImportRecord()
        {
            string importPath = EditorUtility.OpenFilePanel(
                "导入 UI 记录",
                "",
                "uiprobe"
            );
            
            if (!string.IsNullOrEmpty(importPath))
            {
                // Get configured storage path
                string targetDir = EditorPrefs.GetString("UIProbe_StoragePath", "");
                if (string.IsNullOrEmpty(targetDir))
                {
                    targetDir = UIRecordStorage.GetDefaultStoragePath();
                }
                
                if (UIRecordStorage.ImportSession(importPath, targetDir))
                {
                    RefreshSessionList();
                    EditorUtility.DisplayDialog("导入成功", "记录已导入，列表已刷新。", "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("导入失败", "导入过程中发生错误，请查看 Console。", "OK");
                }
            }
        }

        private void BatchDeleteSessions()
        {
            if (selectedIndices.Count == 0) return;
            
            if (!EditorUtility.DisplayDialog("确认批量删除", $"确定要删除选中的 {selectedIndices.Count} 条记录吗？\n此操作不可撤销。", "删除", "取消"))
            {
                return;
            }
            
            int deletedCount = 0;
            // Sort descending to remove safely if we were removing from list, 
            // but here we use indices to get paths.
            // Better to collect paths first.
            var pathsToDelete = new List<string>();
            foreach(int index in selectedIndices)
            {
                if (index >= 0 && index < savedSessions.Count)
                {
                    pathsToDelete.Add(savedSessions[index].Path);
                }
            }
            
            foreach (var path in pathsToDelete)
            {
                UIRecordStorage.DeleteSession(path);
                deletedCount++;
            }
            
            selectedIndices.Clear();
            RefreshSessionList();
            
            Debug.Log($"[UI Probe] 批量删除了 {deletedCount} 条记录");
        }
    }
}

