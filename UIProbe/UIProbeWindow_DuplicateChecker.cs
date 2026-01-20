using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
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
        private int duplicateCheckerSubTab = 0;  // 0=检测功能, 1=历史记录
        private string lastRenamedNodeName = "";  // 最后重命名的节点名（用于保持焦点）
        
        // Batch Mode State
        private bool isBatchMode = false;
        private BatchDuplicateResult currentBatchResult = null;
        private int batchCardPageIndex = 0;
        private bool batchShowOnlyDuplicates = true;
        private const int CARDS_PER_PAGE = 5;
        private Vector2 batchScrollPosition;
        
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
        private bool showRenameHistory = false;
        private Vector2 renameHistoryScrollPosition;
        private Dictionary<string, bool> historyDateFoldouts = new Dictionary<string, bool>();
        
        /// <summary>
        /// 绘制重名检测标签页
        /// </summary>
        private void DrawDuplicateCheckerTab()
        {
            EditorGUILayout.LabelField("重名节点检测", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            // 子标签工具栏 - 在检测功能和历史记录之间切换
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            if (GUILayout.Toggle(duplicateCheckerSubTab == 0, "检测功能", EditorStyles.toolbarButton))
            {
                duplicateCheckerSubTab = 0;
            }
            if (GUILayout.Toggle(duplicateCheckerSubTab == 1, "历史记录", EditorStyles.toolbarButton))
            {
                duplicateCheckerSubTab = 1;
            }
            
            GUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            // 根据子标签显示不同内容
            if (duplicateCheckerSubTab == 0)
            {
                // 检测功能标签
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
            GUILayout.Box("", GUILayout.Width(4), GUILayout.ExpandHeight(true));
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
            if (prefabRoot != null && !AnimationPathRepair.CheckAndRepairForRename(prefabRoot, obj, newName))
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
            }
            
            // 清除输入框状态，避免残留旧文本
            renameInputs.Remove(obj);
            
            // 清除当前焦点，避免输入框保持激活状态
            GUI.FocusControl(null);
            
            // 重新检测以更新结果
            DetectCurrentPrefab();
            
            Debug.Log($"[UIProbe] Renamed: {oldName} → {newName}");
        }
    }
}
