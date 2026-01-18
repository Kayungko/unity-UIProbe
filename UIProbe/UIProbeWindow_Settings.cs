using UnityEngine;
using UnityEditor;

namespace UIProbe
{
    public partial class UIProbeWindow
    {
        // Settings State
        private string recordStoragePath = "";
        private string newRuleKeyword = "";
        private string newRuleTag = "新标签";
        
        private void LoadSettingsData()
        {
            recordStoragePath = EditorPrefs.GetString("UIProbe_StoragePath", "");
        }
        
        private void SaveSettingsData()
        {
            EditorPrefs.SetString("UIProbe_StoragePath", recordStoragePath);
        }
        
        private void DrawSettingsTab()
        {
            EditorGUILayout.LabelField("设置 (Settings)", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Problem Detection Rules
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("问题检测规则 (Detection Rules)", EditorStyles.boldLabel);
            
            foreach (var rule in UIProbeChecker.Rules)
            {
                GUILayout.BeginHorizontal();
                rule.IsEnabled = EditorGUILayout.ToggleLeft(rule.RuleName, rule.IsEnabled);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(rule.Description, EditorStyles.miniLabel, GUILayout.Width(250));
                GUILayout.EndHorizontal();
            }
            
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // Storage Path Settings
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("存储路径 (Storage Path)", EditorStyles.boldLabel);
            
            GUILayout.BeginHorizontal();
            EditorGUILayout.TextField("当前路径:", string.IsNullOrEmpty(recordStoragePath) ? "(默认: AppData)" : recordStoragePath);
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("选择文件夹"))
            {
                string newPath = EditorUtility.OpenFolderPanel("选择记录存储路径", recordStoragePath, "");
                if (!string.IsNullOrEmpty(newPath))
                {
                    recordStoragePath = newPath;
                    SaveSettingsData();
                    EditorUtility.DisplayDialog("提示", $"存储路径已设置为:\n{newPath}", "OK");
                }
            }
            
            if (GUILayout.Button("使用默认路径"))
            {
                recordStoragePath = "";
                SaveSettingsData();
                EditorUtility.DisplayDialog("提示", $"已恢复默认路径:\n{UIRecordStorage.GetDefaultStoragePath()}", "OK");
            }
            GUILayout.EndHorizontal();
            
            // Show current effective path
            string effectivePath = string.IsNullOrEmpty(recordStoragePath) 
                ? UIRecordStorage.GetDefaultStoragePath() 
                : recordStoragePath;
            EditorGUILayout.LabelField($"实际路径: {effectivePath}", EditorStyles.miniLabel);
            
            // Warning for project-internal paths
            if (!string.IsNullOrEmpty(recordStoragePath) && recordStoragePath.Contains(Application.dataPath))
            {
                EditorGUILayout.HelpBox("注意: 此路径在项目内，可能会被 Git 提交。", MessageType.Warning);
            }
            
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // Prefab Index Root Path Settings
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("预制体索引根目录 (Prefab Index Root)", EditorStyles.boldLabel);
            
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
            
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // Custom Tag Rules
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("自定义标签规则 (Custom Tag Rules)", EditorStyles.boldLabel);
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
            
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // Data Management
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("数据管理 (Data Management)", EditorStyles.boldLabel);
            
            if (GUILayout.Button("清除搜索历史 (Clear Search History)"))
            {
                searchHistory.Clear();
                SaveAuxData();
                EditorUtility.DisplayDialog("提示", "搜索历史已清除。", "OK");
            }
            
            if (GUILayout.Button("清除收藏夹 (Clear Bookmarks)"))
            {
                bookmarks.Clear();
                SaveAuxData();
                EditorUtility.DisplayDialog("提示", "收藏夹已清除。", "OK");
            }

            if (GUILayout.Button("重置所有数据 (Reset All Data)"))
            {
                if (EditorUtility.DisplayDialog("确认", "确定要重置所有数据吗？此操作不可撤销。", "确定", "取消"))
                {
                    searchHistory.Clear();
                    bookmarks.Clear();
                    recordStoragePath = "";
                    SaveAuxData();
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
            EditorGUILayout.EndVertical();

            // Push About to bottom
            GUILayout.FlexibleSpace();

            // Version & Credits (at the very bottom)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("关于 (About)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Version: 1.0");
            EditorGUILayout.LabelField("Design & Dev: 柯家荣, 沈浩天");
            EditorGUILayout.EndVertical();
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
