using UnityEngine;
using UnityEditor;
using System.IO;

namespace UIProbe
{
    public partial class UIProbeWindow
    {
        // Settings State
        private string recordStoragePath = "";
        private string newRuleKeyword = "";
        private string newRuleTag = "新标签";
        private Vector2 settingsScrollPosition;
        
        // Foldout states for settings sections
        private bool showDetectionRules = true;
        private bool showStoragePath = false;
        private bool showIndexRoot = false;
        private bool showCustomTags = false;
        private bool showDataManagement = false;
        
        // Duplicate Detection Settings
        private DuplicateDetectionSettings duplicateSettings;
        private string newWhitelistName = "";
        private string newBlacklistName = "";
        private string newPrefixName = "";
        
        private void LoadSettingsData()
        {
            if (config == null) return;
            
            // Recorder Settings
            if (config.recorder != null)
                recordStoragePath = config.recorder.storagePath;
            
            // 问题检测规则状态
            if (config.checkerRules != null)
            {
                ApplyCheckerRulesConfig();
            }
            
            // Duplicate Settings
            if (duplicateSettings == null) duplicateSettings = new DuplicateDetectionSettings();
            
            if (config.duplicateChecker != null)
            {
                // Map config to settings object
                try { duplicateSettings.Mode = (DetectionMode)System.Enum.Parse(typeof(DetectionMode), config.duplicateChecker.mode); } catch {}
                try { duplicateSettings.DetectionScope = (DuplicateDetectionMode)System.Enum.Parse(typeof(DuplicateDetectionMode), config.duplicateChecker.detectionScope); } catch {}
                
                duplicateSettings.EnableWhitelist = config.duplicateChecker.enableWhitelist;
                if (config.duplicateChecker.allowedDuplicateNames != null)
                    duplicateSettings.AllowedDuplicateNames = new System.Collections.Generic.List<string>(config.duplicateChecker.allowedDuplicateNames);
                    
                duplicateSettings.CheckUGUIComponentNames = config.duplicateChecker.checkUGUIComponentNames;
                if (config.duplicateChecker.uguiComponentsToCheck != null)
                    duplicateSettings.UGUIComponentsToCheck = new System.Collections.Generic.List<string>(config.duplicateChecker.uguiComponentsToCheck);
                    
                duplicateSettings.EnablePrefixFilter = config.duplicateChecker.enablePrefixFilter;
                if (config.duplicateChecker.requiredPrefixes != null)
                    duplicateSettings.RequiredPrefixes = new System.Collections.Generic.List<string>(config.duplicateChecker.requiredPrefixes);
                    
                if (config.duplicateChecker.forbiddenDuplicateNames != null)
                    duplicateSettings.ForbiddenDuplicateNames = new System.Collections.Generic.List<string>(config.duplicateChecker.forbiddenDuplicateNames);
            }
        }
        
        /// <summary>
        /// 收集设置数据到 config 对象
        /// </summary>
        private void CollectSettingsData()
        {
            if (config == null) return;
            
            // Recorder Settings
            if (config.recorder == null) config.recorder = new RecorderConfig();
            config.recorder.storagePath = recordStoragePath;
            
            // 问题检测规则状态
            if (config.checkerRules == null) config.checkerRules = new CheckerRulesConfig();
            CollectCheckerRulesConfig();
            
            // Duplicate Settings
            if (duplicateSettings != null)
            {
                if (config.duplicateChecker == null) config.duplicateChecker = new DuplicateCheckerConfig();
                
                config.duplicateChecker.mode = duplicateSettings.Mode.ToString();
                config.duplicateChecker.detectionScope = duplicateSettings.DetectionScope.ToString();
                
                config.duplicateChecker.enableWhitelist = duplicateSettings.EnableWhitelist;
                config.duplicateChecker.allowedDuplicateNames = duplicateSettings.AllowedDuplicateNames.ToArray();
                
                config.duplicateChecker.checkUGUIComponentNames = duplicateSettings.CheckUGUIComponentNames;
                config.duplicateChecker.uguiComponentsToCheck = duplicateSettings.UGUIComponentsToCheck.ToArray();
                
                config.duplicateChecker.enablePrefixFilter = duplicateSettings.EnablePrefixFilter;
                config.duplicateChecker.requiredPrefixes = duplicateSettings.RequiredPrefixes.ToArray();
                
                config.duplicateChecker.forbiddenDuplicateNames = duplicateSettings.ForbiddenDuplicateNames.ToArray();
            }
        }

        private void SaveSettingsData()
        {
            CollectSettingsData();
            
            // Save config
            UIProbeConfigManager.Save(config);
        }
        
        /// <summary>
        /// 应用检测规则配置到UIProbeChecker.Rules
        /// </summary>
        private void ApplyCheckerRulesConfig()
        {
            if (config.checkerRules == null) return;
            
            var rules = UIProbeChecker.Rules;
            if (rules.Count >= 7)
            {
                rules[0].IsEnabled = config.checkerRules.missingImageSprite;  // MissingImageSpriteRule
                rules[1].IsEnabled = config.checkerRules.missingTextFont;     // MissingTextFontRule
                rules[2].IsEnabled = config.checkerRules.unnecessaryRaycastTarget; // UnnecessaryRaycastTargetRule
                rules[3].IsEnabled = config.checkerRules.badNaming;            // BadNamingRule
                rules[4].IsEnabled = config.checkerRules.emptyText;            // EmptyTextRule
                rules[5].IsEnabled = config.checkerRules.missingCanvasGroup;   // MissingCanvasGroupRule
                rules[6].IsEnabled = config.checkerRules.duplicateName;        // DuplicateNameRule
            }
        }
        
        /// <summary>
        /// 从 UIProbeChecker.Rules 收集配置
        /// </summary>
        private void CollectCheckerRulesConfig()
        {
            var rules = UIProbeChecker.Rules;
            if (rules.Count >= 7)
            {
                config.checkerRules.missingImageSprite = rules[0].IsEnabled;
                config.checkerRules.missingTextFont = rules[1].IsEnabled;
                config.checkerRules.unnecessaryRaycastTarget = rules[2].IsEnabled;
                config.checkerRules.badNaming = rules[3].IsEnabled;
                config.checkerRules.emptyText = rules[4].IsEnabled;
                config.checkerRules.missingCanvasGroup = rules[5].IsEnabled;
                config.checkerRules.duplicateName = rules[6].IsEnabled;
            }
        }
        
        private void DrawSettingsTab()
        {
            EditorGUILayout.LabelField("设置 (Settings)", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            // Add ScrollView for entire settings content
            settingsScrollPosition = EditorGUILayout.BeginScrollView(settingsScrollPosition, GUILayout.ExpandHeight(true));

            // ===== Problem Detection Rules =====
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            showDetectionRules = EditorGUILayout.Foldout(showDetectionRules, "问题检测规则 (Detection Rules)", true, EditorStyles.foldoutHeader);
            
            if (showDetectionRules)
            {
            
            foreach (var rule in UIProbeChecker.Rules)
            {
                GUILayout.BeginHorizontal();
                bool newEnabled = EditorGUILayout.ToggleLeft(rule.RuleName, rule.IsEnabled);
                if (newEnabled != rule.IsEnabled)
                {
                    rule.IsEnabled = newEnabled;
                    // 规则状态变化时自动保存
                    SaveSettingsData();
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(rule.Description, EditorStyles.miniLabel, GUILayout.Width(250));
                GUILayout.EndHorizontal();
            }
            
            }  // End of showDetectionRules if block
            
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // ===== Storage Path Settings =====
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            showStoragePath = EditorGUILayout.Foldout(showStoragePath, "【通用】存储路径 (Storage Path)", true, EditorStyles.foldoutHeader);
            
            if (showStoragePath)
            {
            
            string currentMainPath = UIProbeStorage.GetMainFolderPath();
            string defaultMainPath = UIProbeStorage.GetDefaultMainPath();
            bool isUsingCustomPath = currentMainPath != defaultMainPath;
            
            EditorGUILayout.LabelField("主文件夹路径:", EditorStyles.boldLabel);
            EditorGUILayout.TextField(isUsingCustomPath ? currentMainPath : "(默认: AppData)", EditorStyles.miniLabel);
            
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("选择文件夹"))
            {
                string startPath = Path.GetDirectoryName(currentMainPath);
                string newPath = EditorUtility.OpenFolderPanel("选择 UIProbe 主文件夹位置", startPath, "");
                if (!string.IsNullOrEmpty(newPath))
                {
                    UIProbeStorage.SetCustomMainPath(newPath);
                    EditorUtility.DisplayDialog("提示", $"主文件夹已设置为：\n{Path.Combine(newPath, "UIProbe")}", "确定");
                }
            }
            
            if (GUILayout.Button("使用默认路径 (AppData)"))
            {
                UIProbeStorage.SetCustomMainPath("");
                EditorUtility.DisplayDialog("提示", $"已恢复默认路径：\n{defaultMainPath}", "确定");
            }
            
            if (GUILayout.Button("打开文件夹"))
            {
                if (Directory.Exists(currentMainPath))
                {
                    EditorUtility.RevealInFinder(currentMainPath);
                }
                else
                {
                    Directory.CreateDirectory(currentMainPath);
                    EditorUtility.RevealInFinder(currentMainPath);
                }
            }
            GUILayout.EndHorizontal();
            
            // 显示文件夹结构
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("文件夹结构:", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(UIProbeStorage.GetFolderStructureDescription(), MessageType.None);

            
            // 快速打开子文件夹按钮
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("打开修改日志", EditorStyles.miniButton))
            {
                EditorUtility.RevealInFinder(UIProbeStorage.GetModificationLogsPath());
            }
            if (GUILayout.Button("打开重命名历史", EditorStyles.miniButton))
            {
                EditorUtility.RevealInFinder(UIProbeStorage.GetRenameHistoryPath());
            }
            if (GUILayout.Button("打开CSV导出", EditorStyles.miniButton))
            {
                EditorUtility.RevealInFinder(UIProbeStorage.GetCSVExportPath());
            }
            GUILayout.EndHorizontal();
            
            EditorGUILayout.Space(2);
            if (currentMainPath.Contains(Application.dataPath))
            {
                EditorGUILayout.HelpBox("⚠ 注意：当前路径在项目内，可能会被 Git 提交！建议使用默认路径或项目外路径。", MessageType.Warning);
            }
            
            }  // End of showStoragePath if block
            
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // ===== Prefab Index Root Path Settings =====
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            showIndexRoot = EditorGUILayout.Foldout(showIndexRoot, "预制体索引根目录 (Prefab Index Root)", true, EditorStyles.foldoutHeader);
            
            if (showIndexRoot)
            {
            
            string indexRoot = EditorPrefs.GetString("UIProbe_IndexRootPath", "");
            GUILayout.BeginHorizontal();
            EditorGUILayout.TextField("根目录:", string.IsNullOrEmpty(indexRoot) ? "(默认: Assets/)" : indexRoot);
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("选择文件夹"))
            {
                string startPath = string.IsNullOrEmpty(indexRoot) ? Application.dataPath : indexRoot;
                string newPath = EditorUtility.OpenFolderPanel("选择预制体索引根目录", startPath, "");
                if (!string.IsNullOrEmpty(newPath))
                {
                    // Convert to relative path if inside Assets
                    if (newPath.StartsWith(Application.dataPath))
                    {
                        newPath = "Assets" + newPath.Substring(Application.dataPath.Length);
                    }
                    EditorPrefs.SetString("UIProbe_IndexRootPath", newPath);
                    EditorUtility.DisplayDialog("提示", $"索引根目录已设置为:\n{newPath}\n\n请点击「刷新」重建索引。", "OK");
                }
            }
            
            if (GUILayout.Button("使用默认 (Assets)"))
            {
                EditorPrefs.SetString("UIProbe_IndexRootPath", "");
                EditorUtility.DisplayDialog("提示", "已恢复默认索引所有 Assets。\n\n请点击「刷新」重建索引。", "OK");
            }
            GUILayout.EndHorizontal();
            
            EditorGUILayout.HelpBox("设置后只索引该目录下的预制体，减少大项目的加载时间。", MessageType.Info);
            
            }  // End of showIndexRoot if block
            
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();
            
            // ===== Picker Input Mode Settings =====
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("运行时拾取方式 (Picker Input Mode)", EditorStyles.boldLabel);
            EditorGUILayout.Space(3);
            
            if (config != null && config.picker != null)
            {
                EditorGUILayout.HelpBox("选择在 Play 模式下拾取 UI 元素的输入方式。推荐使用右键以避免触发按钮点击。", MessageType.Info);
                EditorGUILayout.Space(5);
                
                PickerInputMode currentMode = (PickerInputMode)config.picker.inputMode;
                PickerInputMode newMode = (PickerInputMode)EditorGUILayout.EnumPopup("拾取方式:", currentMode);
                
                if (newMode != currentMode)
                {
                    config.picker.inputMode = (int)newMode;
                    UIProbeConfigManager.Save(config);
                }
                
                EditorGUILayout.Space(5);
                
                // 显示各模式说明
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("模式说明:", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("• 右键: 不会触发按钮，推荐使用", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("• Ctrl+左键: 需要双手操作，但逻辑清晰", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("• 中键: 不会触发按钮，部分鼠标无中键", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("• Alt+左键: 需要双手操作", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
            }
            
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();
            
            // Duplicate Detection Settings
            DrawDuplicateDetectionSettings();

            EditorGUILayout.Space();

            // ===== Custom Tag Rules =====
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            showCustomTags = EditorGUILayout.Foldout(showCustomTags, "自定义标签规则 (Custom Tag Rules)", true, EditorStyles.foldoutHeader);
            
            if (showCustomTags)
            {
            EditorGUILayout.HelpBox("规则按顺序匹配，优先于内置规则。包含关键字即可匹配。", MessageType.None);
            
            var rules = UITagInferrer.GetCustomRules();
            
            // List existing rules
            if (rules.Count > 0)
            {
                GUILayout.Space(5);
                EditorGUILayout.LabelField("现有规则:", EditorStyles.boldLabel);
                
                UICustomTagRule ruleToRemove = null;
                
                for (int i = 0; i < rules.Count; i++)
                {
                    var rule = rules[i];
                    GUILayout.BeginHorizontal();
                    
                    bool newEnabled = EditorGUILayout.Toggle(rule.IsEnabled, GUILayout.Width(20));
                    if (newEnabled != rule.IsEnabled)
                    {
                        rule.IsEnabled = newEnabled;
                        UITagInferrer.SaveRules();
                    }
                    
                    EditorGUILayout.LabelField($"包含 \"{rule.Keyword}\"", GUILayout.Width(150));
                    EditorGUILayout.LabelField("➜", GUILayout.Width(20));
                    EditorGUILayout.LabelField($"[{rule.Tag}]", GUILayout.Width(100));
                    
                    if (GUILayout.Button("删除", EditorStyles.miniButton, GUILayout.Width(40)))
                    {
                        ruleToRemove = rule;
                    }
                    
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
                
                if (ruleToRemove != null)
                {
                    UITagInferrer.RemoveRule(ruleToRemove);
                }
            }
            else
            {
                EditorGUILayout.LabelField("暂无自定义规则");
            }
            
            GUILayout.Space(10);
            
            // Add new rule
            EditorGUILayout.LabelField("添加新规则:", EditorStyles.boldLabel);
            GUILayout.BeginHorizontal();
            newRuleKeyword = EditorGUILayout.TextField(newRuleKeyword, GUILayout.Width(120));
            GUILayout.Label("➜", GUILayout.Width(20));
            newRuleTag = EditorGUILayout.TextField(newRuleTag, GUILayout.Width(80));
            
            if (GUILayout.Button("添加", GUILayout.Width(50)))
            {
                if (!string.IsNullOrEmpty(newRuleKeyword) && !string.IsNullOrEmpty(newRuleTag))
                {
                    UITagInferrer.AddRule(newRuleKeyword, newRuleTag);
                    newRuleKeyword = "";
                    newRuleTag = "新标签";
                    GUI.FocusControl(null);
                }
                else
                {
                    EditorUtility.DisplayDialog("提示", "关键字和标签不能为空", "OK");
                }
            }
            GUILayout.EndHorizontal();
            EditorGUILayout.LabelField("关键字 (小写)", EditorStyles.miniLabel);
            
            }  // End of showCustomTags if block
            
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // ===== Data Management =====
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            showDataManagement = EditorGUILayout.Foldout(showDataManagement, "数据管理 (Data Management)", true, EditorStyles.foldoutHeader);
            
            if (showDataManagement)
            {
            
            if (GUILayout.Button("清除搜索历史 (Clear Search History)"))
            {
                searchHistory.Clear();
                EditorUtility.DisplayDialog("提示", "搜索历史已清除。", "OK");
            }
            
            if (GUILayout.Button("清除收藏夹 (Clear Bookmarks)"))
            {
                bookmarks.Clear();
                EditorUtility.DisplayDialog("提示", "收藏夹已清除。", "OK");
            }

            if (GUILayout.Button("重置所有数据 (Reset All Data)"))
            {
                if (EditorUtility.DisplayDialog("确认", "确定要重置所有数据吗？此操作不可撤销。", "确定", "取消"))
                {
                    searchHistory.Clear();
                    bookmarks.Clear();
                    recordStoragePath = "";
                    SaveSettingsData();
                    Debug.Log("UI Probe 数据已重置");
                }
            }
            
            if (GUILayout.Button("打开存储文件夹"))
            {
                string path = string.IsNullOrEmpty(recordStoragePath) 
                    ? UIRecordStorage.GetDefaultStoragePath() 
                    : recordStoragePath;
                    
                if (System.IO.Directory.Exists(path))
                {
                    EditorUtility.RevealInFinder(path);
                }
                else
                {
                    EditorUtility.DisplayDialog("提示", "文件夹不存在，请先保存一次记录。", "OK");
                }
            }
            
            }  // End of showDataManagement if block
            
            EditorGUILayout.EndVertical();
            
            // ===== 保存/重置设置区域 =====
            EditorGUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();
            
            GUILayout.FlexibleSpace();
            
            // 重置默认按钮
            if (GUILayout.Button("重置为默认", GUILayout.Width(100), GUILayout.Height(28)))
            {
                if (EditorUtility.DisplayDialog("确认重置", "确定要将所有设置重置为默认值吗？", "确定", "取消"))
                {
                    duplicateSettings = DuplicateDetectionSettings.GetDefault();
                    recordStoragePath = "";
                    SaveSettingsData();
                    Debug.Log("[UIProbe] 设置已重置为默认值");
                }
            }
            
            GUILayout.Space(10);
            
            // 保存设置按钮
            GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
            if (GUILayout.Button("💾 保存设置", GUILayout.Width(120), GUILayout.Height(28)))
            {
                SaveSettingsData();
                EditorUtility.DisplayDialog("保存成功", "设置已保存", "确定");
            }
            GUI.backgroundColor = Color.white;
            
            
            // End ScrollView
            EditorGUILayout.EndScrollView();
        }
        
        private string GetConfiguredStoragePath()
        {
            if (string.IsNullOrEmpty(recordStoragePath))
            {
                recordStoragePath = EditorPrefs.GetString("UIProbe_StoragePath", "");
            }
            
            return string.IsNullOrEmpty(recordStoragePath) 
                ? UIRecordStorage.GetDefaultStoragePath() 
                : recordStoragePath;
        }
    }
}
