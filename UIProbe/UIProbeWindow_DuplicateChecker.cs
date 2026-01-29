using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace UIProbe
{
    public partial class UIProbeWindow
    {
        // Duplicate Checker State
        private Vector2 duplicateCheckerScrollPosition;
        private DuplicateNameResult currentDuplicateResult;
        private GameObject lastCheckedPrefab;
        private DuplicateDetectionMode duplicateDetectionMode = DuplicateDetectionMode.Global;
        private Dictionary<string, bool> duplicateGroupFoldouts = new Dictionary<string, bool>();
        private Dictionary<GameObject, string> renameInputs = new Dictionary<GameObject, string>();
        private int duplicateCheckerSubTab = 0;  // 0=检测功能, 1=综合检查, 2=历史记录
        private string lastRenamedNodeName = "";  // 最后重命名的节点名（用于保持焦点）
        
        // Comprehensive Check State
        private List<UIProblem> currentGeneralProblems = new List<UIProblem>();
        private Vector2 comprehensiveScrollPosition;
        private GameObject lastComprehensiveCheckedPrefab;
        private Dictionary<string, bool> ruleVisibility = new Dictionary<string, bool>(); // Filter state
        private HashSet<UIProblem> selectedProblems = new HashSet<UIProblem>(); // Selection state


        
        // Batch Mode State
        private bool isBatchMode = false;
        private BatchDuplicateResult currentBatchResult = null;
        private int batchCardPageIndex = 0;
        private bool batchShowOnlyDuplicates = true;
        private const int CARDS_PER_PAGE = 5;
        
        // Folder Exclusion Filter State
        private HashSet<string> excludedFolders = new HashSet<string>();  // 当前排除的文件夹
        private bool showFolderFilter = false;  // 是否显示文件夹过滤面板
        private Dictionary<string, int> folderPrefabCounts = new Dictionary<string, int>();  // 各文件夹的预制体数量
        
        // Batch Mode Context (for return functionality)
        private string currentBatchResultPath = "";  // JSON文件路径
        private PrefabDuplicateResult currentProcessingItem = null;  // 当前处理的项
        private bool isFromBatchMode = false;  // 是否来自批量模式
        
        // Shared with Settings tab - declared in UIProbeWindow_Settings.cs
        // private DuplicateDetectionSettings duplicateSettings;
        
        // Rename History State
        private Vector2 renameHistoryScrollPosition;
        private Dictionary<string, bool> historyDateFoldouts = new Dictionary<string, bool>();
        
        /// <summary>
        /// 绘制重名检测标签页
        /// </summary>
        private void DrawDuplicateCheckerTab()
        {
            EditorGUILayout.LabelField("预制体综合检测 (Prefab Inspector)", EditorStyles.boldLabel);

            EditorGUILayout.Space(5);
            
            // 子标签工具栏 - 在检测功能和历史记录之间切换
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            if (GUILayout.Toggle(duplicateCheckerSubTab == 0, "综合检测", EditorStyles.toolbarButton))
            {
                duplicateCheckerSubTab = 0;
            }
            if (GUILayout.Toggle(duplicateCheckerSubTab == 1, "重命名修改", EditorStyles.toolbarButton))
            {
                duplicateCheckerSubTab = 1;
            }
            if (GUILayout.Toggle(duplicateCheckerSubTab == 2, "历史记录", EditorStyles.toolbarButton))
            {
                duplicateCheckerSubTab = 2;
            }
            
            GUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            // 根据子标签显示不同内容
            if (duplicateCheckerSubTab == 0)
            {
                // 综合检查标签
                DrawComprehensiveSubTab();
            }
            else if (duplicateCheckerSubTab == 1)
            {
                // 重命名修改（原检测功能）标签
                DrawDetectionSubTab();
            }
            else
            {
                // 历史记录标签
                DrawHistorySubTab();
            }
        }
        
        /// <summary>
        /// 绘制检测功能子标签
        /// </summary>
        private void DrawDetectionSubTab()
        {
            // 模式切换区域
            GUILayout.BeginHorizontal();
            
            if (isBatchMode)
            {
                EditorGUILayout.HelpBox($"批量模式: {currentBatchResult.TotalPrefabs} 个预制体，{currentBatchResult.PrefabsWithDuplicates} 个存在重名", MessageType.Info);
                if (GUILayout.Button("返回单个检测模式", GUILayout.Width(120)))
                {
                    ClearBatchMode();
                }
            }
            else
            {
                EditorGUILayout.HelpBox("检测配置在「设置」标签页中配置", MessageType.Info, true);
                GUILayout.FlexibleSpace();
            }
            
            GUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            // 根据模式绘制不同内容
            if (isBatchMode)
            {
                // 批量模式UI（使用主scrollview）
                duplicateCheckerScrollPosition = EditorGUILayout.BeginScrollView(duplicateCheckerScrollPosition);
                DrawBatchModeUI();
                EditorGUILayout.EndScrollView();
            }
            else
            {
                // 单个检测模式UI
                DrawSingleDetectionUI();
            }
        }
        
        /// <summary>
        /// 绘制历史记录子标签
        /// </summary>
        private void DrawHistorySubTab()
        {
            DrawRenameHistorySection();
        }

        /// <summary>
        /// 绘制综合检查子标签
        /// </summary>
        private void DrawComprehensiveSubTab()
        {
            EditorGUILayout.Space(5);
            
            // Auto-detect button
            GUILayout.BeginHorizontal();
            GUI.enabled = PrefabStageUtility.GetCurrentPrefabStage() != null;
            if (GUILayout.Button("检查当前预制体", GUILayout.Width(150), GUILayout.Height(30)))
            {
                RunComprehensiveCheck();
            }
            GUI.enabled = true;
            
            GUILayout.FlexibleSpace();
            
            if (currentGeneralProblems.Count > 0)
            {
                if (GUILayout.Button("清除结果", GUILayout.Width(80)))
                {
                    currentGeneralProblems.Clear();
                    lastComprehensiveCheckedPrefab = null;
                }
            }
            GUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            if (lastComprehensiveCheckedPrefab != null && currentGeneralProblems.Count > 0)
            {
                DrawFilterToolbar();
            }

            // ScrollView
            comprehensiveScrollPosition = EditorGUILayout.BeginScrollView(comprehensiveScrollPosition);
            
            if (lastComprehensiveCheckedPrefab != null)
            {
                if (currentGeneralProblems.Count == 0)
                {
                    EditorGUILayout.HelpBox("✓ 未发现任何问题", MessageType.Info);
                }
                else
                {
                    // Filtered count
                    int visibleCount = currentGeneralProblems.Count(p => !ruleVisibility.ContainsKey(p.RuleName) || ruleVisibility[p.RuleName]);
                    
                    EditorGUILayout.LabelField($"发现 {currentGeneralProblems.Count} 个问题 (显示 {visibleCount} 个):", EditorStyles.boldLabel);
                    EditorGUILayout.Space(5);
                    
                    // Group by rule
                    var groupedProblems = currentGeneralProblems
                        .Where(p => !ruleVisibility.ContainsKey(p.RuleName) || ruleVisibility[p.RuleName])
                        .GroupBy(p => p.RuleName)
                        .OrderBy(g => g.Key);
                        
                    foreach (var group in groupedProblems)
                    {
                        DrawProblemGroup(group.Key, group.ToList());
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("请打开预制体并点击上方按钮开始综合检查。\n\n此模式将运行所有启用的检测规则（如缺失图片、字体、RaycastTarget 等）。", MessageType.None);
            }
            
            EditorGUILayout.EndScrollView();
        }

        private void DrawFilterToolbar()
        {
            var rules = currentGeneralProblems.Select(p => p.RuleName).Distinct().OrderBy(r => r).ToList();
            if (rules.Count == 0) return;

            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("筛选:", GUILayout.Width(35));
            
            bool changed = false;
            
            // "All" button
            bool allVisible = rules.All(r => !ruleVisibility.ContainsKey(r) || ruleVisibility[r]);
            if (GUILayout.Button("全部", allVisible ? EditorStyles.toolbarButton : EditorStyles.toolbarButton, GUILayout.Width(40)))
            {
                foreach (var r in rules) ruleVisibility[r] = true;
                changed = true;
            }

            foreach (var rule in rules)
            {
                if (!ruleVisibility.ContainsKey(rule)) ruleVisibility[rule] = true;
                
                int count = currentGeneralProblems.Count(p => p.RuleName == rule);
                string label = $"{rule} ({count})";
                
                bool isVisible = ruleVisibility[rule];
                bool newVisible = GUILayout.Toggle(isVisible, label, EditorStyles.toolbarButton);
                
                if (newVisible != isVisible)
                {
                    ruleVisibility[rule] = newVisible;
                    changed = true;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            if (changed) Repaint();
        }
        
        private void DrawProblemGroup(string ruleName, List<UIProblem> problems)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // Header
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"▼ {ruleName} ({problems.Count})", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            
            // Batch actions for specific rules
            if (ruleName.Contains("Raycast Target"))
            {
                // Check if any in this group are selected
                int selectedCount = problems.Count(p => selectedProblems.Contains(p));
                
                if (GUILayout.Button("全选", EditorStyles.miniButton, GUILayout.Width(40)))
                {
                    foreach (var p in problems) selectedProblems.Add(p);
                }
                
                if (selectedCount > 0)
                {
                    if (GUILayout.Button("取消", EditorStyles.miniButton, GUILayout.Width(40)))
                    {
                        foreach (var p in problems) selectedProblems.Remove(p);
                    }

                    GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
                    if (GUILayout.Button($"关闭选中 ({selectedCount})", EditorStyles.miniButton))
                    {
                        BatchFixProblems(problems.Where(p => selectedProblems.Contains(p)).ToList());
                    }
                    GUI.backgroundColor = Color.white;
                }
            }
            
            GUILayout.EndHorizontal();
            
            GUILayout.Space(5); // Add spacing between title and content

            foreach (var problem in problems)
            {
                EditorGUILayout.BeginHorizontal();
                
                 // Checkbox
                bool isSelected = selectedProblems.Contains(problem);
                bool newSelected = GUILayout.Toggle(isSelected, "", GUILayout.Width(20));
                if (newSelected != isSelected)
                {
                    if (newSelected) selectedProblems.Add(problem);
                    else selectedProblems.Remove(problem);
                }

                // Icon
                GUI.backgroundColor = problem.GetColor();
                GUILayout.Label(problem.GetIcon(), EditorStyles.miniButton, GUILayout.Width(20));
                GUI.backgroundColor = Color.white;
                
                // Content
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(problem.Description, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(problem.NodePath, EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                
                // Close Raycast Button (Specific to Raycast Target rule)
                if (problem.RuleName.Contains("Raycast Target"))
                {
                     if (GUILayout.Button("关闭射线", EditorStyles.miniButton, GUILayout.Width(60)))
                     {
                         FixProblem(problem);
                     }
                }

                // Locate
                if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(40)))
                {
                    if (problem.Target != null)
                    {
                        Selection.activeGameObject = problem.Target;
                        EditorGUIUtility.PingObject(problem.Target);
                    }
                }
                
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(2);
            }
            
            EditorGUILayout.EndVertical();
            GUILayout.Space(5);
        }

        private void FixProblem(UIProblem problem)
        {
            if (problem.Target == null) return;
            
            if (problem.RuleName.Contains("Raycast Target"))
            {
                var graphic = problem.Target.GetComponent<UnityEngine.UI.Graphic>();
                if (graphic != null)
                {
                    Undo.RecordObject(graphic, "Fix Raycast Target");
                    graphic.raycastTarget = false;
                    EditorUtility.SetDirty(problem.Target);
                    
                    // Remove from list
                    currentGeneralProblems.Remove(problem);
                    selectedProblems.Remove(problem);
                    
                    Debug.Log($"[UIProbe] Closed Raycast Target for: {problem.Target.name}");
                }
            }
        }

        private void BatchFixProblems(List<UIProblem> problemsToFix)
        {
            foreach (var problem in problemsToFix)
            {
                FixProblem(problem);
            }
            Repaint();
        }
        
        /// <summary>
        /// 运行综合检查
        /// </summary>
        private void RunComprehensiveCheck()
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage == null)
            {
                EditorUtility.DisplayDialog("提示", "请先打开一个预制体进行编辑", "确定");
                return;
            }
            
            GameObject prefabRoot = prefabStage.prefabContentsRoot;
            lastComprehensiveCheckedPrefab = prefabRoot;
            
            // Run all checks
            var allProblems = UIProbeChecker.CheckAll(prefabRoot);
            
            // Filter out duplicate name results (as they are handled in the other tab)
            // Optional: keep them if we want "Comprehensive" to truly mean EVERYTHING
            // For now, let's keep them to be truly comprehensive
            
            currentGeneralProblems = allProblems;
            ruleVisibility.Clear(); // Reset filters
            selectedProblems.Clear(); // Reset selection
            
            Repaint();
        }
        
        /// <summary>
        /// 绘制单个检测模式UI
        /// </summary>
        private void DrawSingleDetectionUI()
        {
            // 如果来自批量模式，显示返回按钮
            if (isFromBatchMode && currentProcessingItem != null)
            {
                GUILayout.BeginHorizontal(EditorStyles.helpBox);
                if (GUILayout.Button("← 返回批量结果", GUILayout.Width(110)))
                {
                    ReturnToBatchMode();
                }
                EditorGUILayout.LabelField($"当前: {currentProcessingItem.PrefabName}", EditorStyles.boldLabel);
                GUILayout.EndHorizontal();
                EditorGUILayout.Space(5);
            }
            
            // Detection button
            GUILayout.BeginHorizontal();
            
            // 导入批量结果按钮
            if (GUILayout.Button("导入批量结果", GUILayout.Width(100)))
            {
                ImportBatchResult();
            }
            
            GUILayout.FlexibleSpace();
            
            // 完成修改按钮 (仅当有修改记录时显示)
            if (ModificationLogManager.HasLogs())
            {
                Color originalColor = GUI.backgroundColor;
                GUI.backgroundColor = Color.green;
                
                if (GUILayout.Button("完成修改并生成日志", GUILayout.Width(150), GUILayout.Height(30)))
                {
                    FinishModification();
                }
                
                GUI.backgroundColor = originalColor;
                GUILayout.Space(10);
            }
            
            // Auto-detect current prefab
            GUI.enabled = PrefabStageUtility.GetCurrentPrefabStage() != null;
            if (GUILayout.Button("检测当前预制体", GUILayout.Width(120), GUILayout.Height(30)))
            {
                DetectCurrentPrefab();
            }
            GUI.enabled = true;
            
            GUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            // Main content ScrollView
            duplicateCheckerScrollPosition = EditorGUILayout.BeginScrollView(duplicateCheckerScrollPosition);
            
            // Detection results
            if (currentDuplicateResult != null && lastCheckedPrefab != null)
            {
                DrawDuplicateResult();
            }
            else
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.HelpBox("请在预制体编辑模式下点击 '检测当前预制体' 按钮开始检测。\n\n步骤：\n1. 在 Project 窗口中双击打开一个预制体\n2. 选择检测模式（全局或同级）\n3. 点击上方的 '检测当前预制体' 按钮", MessageType.None);
            }
            
            // End main ScrollView
            EditorGUILayout.EndScrollView();
        }
        
        /// <summary>
        /// 检测当前打开的预制体
        /// </summary>
        private void DetectCurrentPrefab()
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage == null)
            {
                EditorUtility.DisplayDialog("提示", "请先打开一个预制体进行编辑", "确定");
                return;
            }
            
            // Ensure duplicateSettings is loaded
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
            
            GameObject prefabRoot = prefabStage.prefabContentsRoot;
            lastCheckedPrefab = prefabRoot;
            
            // Use detection scope from settings
            DuplicateDetectionMode scope = duplicateSettings.DetectionScope;
            
            // Pass user-configured settings to detection
            currentDuplicateResult = DuplicateNameRule.DetectDuplicates(prefabRoot, scope, duplicateSettings);
            duplicateGroupFoldouts.Clear();
            
            // 默认收起所有组，不再自动展开
            // 用户可以按需展开查看
            
            // 如果有刚重命名的节点，自动展开对应的组
            if (!string.IsNullOrEmpty(lastRenamedNodeName))
            {
                bool foundGroup = false;
                
                // 检查该组是否还存在
                foreach (var group in currentDuplicateResult.Groups)
                {
                    if (group.NodeName == lastRenamedNodeName)
                    {
                        // 自动展开该组，方便用户继续处理
                        duplicateGroupFoldouts[group.NodeName] = true;
                        foundGroup = true;
                        break;
                    }
                }
                
                // 如果组不存在了（重命名后无重名），显示提示
                if (!foundGroup && currentDuplicateResult.Groups.Count > 0)
                {
                    // 只在还有其他重名组的情况下显示提示
                    // 避免在完全解决所有重名时显示
                    Debug.Log($"[UIProbe] '{lastRenamedNodeName}' 已无重名节点");
                }
                
                // 清除记录
                lastRenamedNodeName = "";
            }
            
            Repaint();
        }
        
        /// <summary>
        /// 绘制检测结果
        /// </summary>
        private void DrawDuplicateResult()
        {
            EditorGUILayout.Space(5);
            
            // Summary
            GUILayout.BeginHorizontal(EditorStyles.helpBox);
            if (currentDuplicateResult.GroupCount == 0)
            {
                EditorGUILayout.LabelField("✓ 未发现重名节点", EditorStyles.boldLabel);
                GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
            }
            else
            {
                EditorGUILayout.LabelField($"⚠ {currentDuplicateResult.GetSummary()}", EditorStyles.boldLabel);
                GUI.backgroundColor = new Color(0.9f, 0.7f, 0.2f);
            }
            GUILayout.EndHorizontal();
            GUI.backgroundColor = Color.white;
            
            if (currentDuplicateResult.GroupCount == 0)
                return;
            if (currentDuplicateResult.GroupCount == 0)
            {
                EditorGUILayout.HelpBox("✓ 未发现重名节点", MessageType.Info);
                return;
            }
            
            EditorGUILayout.HelpBox($"⚠ {currentDuplicateResult.GetSummary()}", MessageType.Warning);
            
            EditorGUILayout.Space(5);
            
            // Action buttons
            GUILayout.BeginHorizontal();
            
            if (GUILayout.Button("导出 CSV", GUILayout.Width(80)))
            {
                ExportToCSV();
            }
            
            if (GUILayout.Button("导出报告", GUILayout.Width(80)))
            {
                ExportDuplicateReport();
            }
            
            if (GUILayout.Button("复制路径", GUILayout.Width(80)))
            {
                CopyDuplicatePaths();
            }
            
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("清除结果", GUILayout.Width(80)))
            {
                currentDuplicateResult = null;
                lastCheckedPrefab = null;
            }
            
            GUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            // Duplicate groups - No inner ScrollView
            foreach (var group in currentDuplicateResult.Groups)
            {
                DrawDuplicateGroup(group);
            }
        }
        
        /// <summary>
        /// 绘制单个重名分组
        /// </summary>
        private void DrawDuplicateGroup(DuplicateNameGroup group)
        {
            if (!duplicateGroupFoldouts.ContainsKey(group.NodeName))
                duplicateGroupFoldouts[group.NodeName] = true;
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // Group header
            GUILayout.BeginHorizontal();
            
            // Color indicator based on count
            Color indicatorColor = group.Count >= 5 ? new Color(0.9f, 0.3f, 0.3f) :
                                   group.Count >= 3 ? new Color(0.9f, 0.7f, 0.2f) :
                                   new Color(0.3f, 0.8f, 0.8f);
            
            GUI.backgroundColor = indicatorColor;
            GUILayout.Box("", GUILayout.Width(4), GUILayout.Height(20));
            GUI.backgroundColor = Color.white;
            
            GUILayout.Space(5);
            
            duplicateGroupFoldouts[group.NodeName] = EditorGUILayout.Foldout(
                duplicateGroupFoldouts[group.NodeName],
                $"\"{group.NodeName}\" ({group.Count} 个重名)",
                true,
                EditorStyles.foldoutHeader
            );
            
            GUILayout.EndHorizontal();
            
            // Group items
            if (duplicateGroupFoldouts[group.NodeName])
            {
                EditorGUI.indentLevel++;
                
                for (int i = 0; i < group.Objects.Count; i++)
                {
                    var obj = group.Objects[i];
                    string path = group.Paths[i];
                    
                    DrawDuplicateItem(obj, path, i + 1, group.Count);
                }
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndVertical();
        }
        
        /// <summary>
        /// 绘制单个重名节点项
        /// </summary>
        private void DrawDuplicateItem(GameObject obj, string path, int index, int total)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.BeginHorizontal();
            
            // Index label
            EditorGUILayout.LabelField($"#{index}", GUILayout.Width(30));
            
            // Path
            EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
            
            // Locate button (combined select + ping functionality)
            if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(50)))
            {
                LocateNodeInHierarchy(obj);
            }
            
            GUILayout.EndHorizontal();
            
            // Rename input field
            GUILayout.BeginHorizontal();
            GUILayout.Space(35);
            EditorGUILayout.LabelField("新名称:", GUILayout.Width(60));
            
            if (!renameInputs.ContainsKey(obj))
            {
                renameInputs[obj] = obj != null ? obj.name : "";
            }
            
            GUI.enabled = obj != null;
            renameInputs[obj] = EditorGUILayout.TextField(renameInputs[obj], GUILayout.Width(200));
            
            bool isValidName = IsValidNodeName(renameInputs[obj]);
            GUI.enabled = obj != null && isValidName && renameInputs[obj] != obj.name;
            
            if (GUILayout.Button("应用", EditorStyles.miniButton, GUILayout.Width(50)))
            {
                ApplyRename(obj, renameInputs[obj]);
            }
            
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            
            // Validation message
            if (!isValidName && !string.IsNullOrEmpty(renameInputs[obj]))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(35);
                EditorGUILayout.HelpBox("名称包含非法字符", MessageType.Warning);
                GUILayout.EndHorizontal();
            }
            
            GUILayout.EndVertical();
            GUILayout.Space(2);
        }
        
        /// <summary>
        /// 在 Hierarchy 中定位并选中节点
        /// </summary>
        private void LocateNodeInHierarchy(GameObject obj)
        {
            if (obj == null)
            {
                EditorUtility.DisplayDialog("提示", "节点已被删除或不存在", "确定");
                return;
            }
            
            // Select in hierarchy
            Selection.activeGameObject = obj;
            
            // Ping to highlight
            EditorGUIUtility.PingObject(obj);
            
            // Focus hierarchy window
            EditorApplication.ExecuteMenuItem("Window/General/Hierarchy");
        }
        
        /// <summary>
        /// 导出重名检测报告
        /// </summary>
        private void ExportDuplicateReport()
        {
            if (currentDuplicateResult == null || currentDuplicateResult.GroupCount == 0)
                return;
            
            string report = "=== UIProbe 重名节点检测报告 ===\n\n";
            report += $"预制体: {lastCheckedPrefab.name}\n";
            report += $"检测模式: {(duplicateDetectionMode == DuplicateDetectionMode.Global ? "全局" : "同级")}\n";
            report += $"检测时间: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}\n";
            report += $"{currentDuplicateResult.GetSummary()}\n\n";
            
            foreach (var group in currentDuplicateResult.Groups)
            {
                report += $"\n【{group.NodeName}】 - {group.Count} 个重名:\n";
                for (int i = 0; i < group.Paths.Count; i++)
                {
                    report += $"  {i + 1}. {group.Paths[i]}\n";
                }
            }
            
            // Copy to clipboard
            EditorGUIUtility.systemCopyBuffer = report;
            EditorUtility.DisplayDialog("导出成功", "报告已复制到剪贴板", "确定");
        }
        
        /// <summary>
        /// 复制所有重名节点的路径
        /// </summary>
        private void CopyDuplicatePaths()
        {
            if (currentDuplicateResult == null || currentDuplicateResult.GroupCount == 0)
                return;
            
            var allPaths = new List<string>();
            foreach (var group in currentDuplicateResult.Groups)
            {
                allPaths.AddRange(group.Paths);
            }
            
            string pathsText = string.Join("\n", allPaths);
            EditorGUIUtility.systemCopyBuffer = pathsText;
            EditorUtility.DisplayDialog("复制成功", $"已复制 {allPaths.Count} 个节点路径到剪贴板", "确定");
        }

        /// <summary>
        /// 导出为 CSV 文件
        /// </summary>
        private void ExportToCSV()
        {
            if (currentDuplicateResult == null || currentDuplicateResult.GroupCount == 0)
            {
                EditorUtility.DisplayDialog("提示", "没有检测结果可以导出", "确定");
                return;
            }

            string savePath = CSVExporter.GetSaveFilePath($"{lastCheckedPrefab.name}_DuplicateReport");
            if (!string.IsNullOrEmpty(savePath))
            {
                CSVExporter.ExportSingleResult(currentDuplicateResult, savePath);
            }
        }

        /// <summary>
        /// 验证节点名称是否合法
        /// </summary>
        private bool IsValidNodeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            // 检查非法字符
            char[] invalidChars = new char[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };
            foreach (char c in invalidChars)
            {
                if (name.Contains(c))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 应用重命名
        /// </summary>
        private void ApplyRename(GameObject obj, string newName)
        {
            if (obj == null || string.IsNullOrWhiteSpace(newName))
                return;

            if (!IsValidNodeName(newName))
            {
                EditorUtility.DisplayDialog("错误", "名称包含非法字符", "确定");
                return;
            }

            string oldName = obj.name;
            
            // 记录重命名的节点名称（用于保持焦点）
            lastRenamedNodeName = oldName;
            
            // 获取预制体路径和根节点
            string prefabPath = "";
            GameObject prefabRoot = null;
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                prefabPath = prefabStage.assetPath;
                prefabRoot = prefabStage.prefabContentsRoot;
            }
            
            // 检查并修复动画引用
            if (prefabRoot != null && !AnimationPathRepair.CheckAndRepairForRename(prefabRoot, obj.transform, newName))
            {
                // 用户取消了操作
                return;
            }
            
            // 使用 Undo 支持撤销
            Undo.RecordObject(obj, "Rename Node");
            obj.name = newName;
            EditorUtility.SetDirty(obj);
            
            // 保存重命名历史记录
            if (!string.IsNullOrEmpty(prefabPath))
            {
                RenameHistoryManager.AddRecord(obj, oldName, newName, prefabPath);
                
                // 记录到本次会话日志，用于生成CSV
                string prefabName = "";
                if (prefabRoot != null)
                {
                    prefabName = prefabRoot.name;
                }
                else
                {
                    prefabName = Path.GetFileNameWithoutExtension(prefabPath);
                }
                
                // 获取节点路径
                string nodePath = AnimationPathRepair.GetRelativePath(prefabRoot != null ? prefabRoot.transform : null, obj.transform);
                
                ModificationLogManager.AddLog(prefabName, oldName, newName, nodePath);
            }
            
            // 清除输入框状态，避免残留旧文本
            renameInputs.Remove(obj);
            
            // 清除当前焦点，避免输入框保持激活状态
            GUI.FocusControl(null);
            
            // 重新检测以更新结果
            DetectCurrentPrefab();
            
            Debug.Log($"[UIProbe] Renamed: {oldName} → {newName}");
        }
        
        /// <summary>
        /// 完成修改并生成日志
        /// </summary>
        private void FinishModification()
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            string prefabName = "Unknown";
            
            if (prefabStage != null)
            {
                prefabName = prefabStage.prefabContentsRoot.name;
            }
            else if (lastCheckedPrefab != null)
            {
                prefabName = lastCheckedPrefab.name;
            }
            
            string csvPath = ModificationLogManager.GenerateCSV(prefabName);
            
            if (!string.IsNullOrEmpty(csvPath))
            {
                EditorUtility.DisplayDialog("成功", $"修改日志已生成:\n{csvPath}", "确定");
                
                // 如果是从批量模式进入的，自动返回，并标记当前项已处理
                if (isFromBatchMode)
                {
                    if (currentProcessingItem != null)
                    {
                        currentProcessingItem.IsProcessed = true;
                        currentProcessingItem.ProcessedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                    ReturnToBatchMode();
                }
            }
        }
    }
}
