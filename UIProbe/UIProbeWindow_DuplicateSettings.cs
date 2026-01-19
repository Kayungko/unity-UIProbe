using UnityEngine;
using UnityEditor;
using System.Linq;

namespace UIProbe
{
    public partial class UIProbeWindow
    {
        // Foldout states for duplicate detection settings
        private bool showDuplicateModeSettings = true;
        private bool showPrefixFilterSettings = false;
        private bool showWhitelistSettings = false;
        private bool showBlacklistSettings = false;
        private bool showUGUISettings = false;
        
        /// <summary>
        /// 绘制重名检测规则配置区域
        /// </summary>
        private void DrawDuplicateDetectionSettings()
        {
            if (duplicateSettings == null)
                duplicateSettings = DuplicateDetectionSettings.GetDefault();

            // ========== 重名检测规则 ==========
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("【重名检测】检测规则配置 (Duplicate Detection Rules)", EditorStyles.boldLabel);
            
            EditorGUILayout.Space(5);
            
            // ===== 检测模式 =====
            showDuplicateModeSettings = EditorGUILayout.Foldout(showDuplicateModeSettings, "检测配置", true, EditorStyles.foldoutHeader);
            if (showDuplicateModeSettings)
            {
                EditorGUI.indentLevel++;
                
                // 检测范围
                EditorGUILayout.LabelField("检测范围:", EditorStyles.boldLabel);
                var newScope = (DuplicateDetectionMode)EditorGUILayout.EnumPopup("范围:", duplicateSettings.DetectionScope);
                if (newScope != duplicateSettings.DetectionScope)
                {
                    duplicateSettings.DetectionScope = newScope;
                    SaveSettingsData();
                }
                string scopeDesc = duplicateSettings.DetectionScope == DuplicateDetectionMode.Global
                    ? "全局：检测整个预制体中所有同名节点"
                    : "同级：仅检测同一父节点下的同名节点";
                EditorGUILayout.HelpBox(scopeDesc, MessageType.None);
                
                EditorGUILayout.Space(3);
                
                // 过滤模式
                EditorGUILayout.LabelField("过滤规则:", EditorStyles.boldLabel);
                var newMode = (DetectionMode)EditorGUILayout.EnumPopup("模式:", duplicateSettings.Mode);
                if (newMode != duplicateSettings.Mode)
                {
                    duplicateSettings.Mode = newMode;
                    SaveSettingsData();
                }
                if (duplicateSettings.Mode == DetectionMode.Strict)
                {
                    EditorGUILayout.HelpBox("严格模式：检测并报告所有重名节点（忽略以下规则）", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("智能模式：根据以下规则过滤", MessageType.Info);
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(5);
            }
            
            // ===== 前缀过滤 =====
            showPrefixFilterSettings = EditorGUILayout.Foldout(showPrefixFilterSettings, "前缀过滤", true, EditorStyles.foldoutHeader);
            if (showPrefixFilterSettings)
            {
                EditorGUI.indentLevel++;
                bool newPrefixFilter = EditorGUILayout.ToggleLeft("只检测特定前缀的节点", duplicateSettings.EnablePrefixFilter);
                if (newPrefixFilter != duplicateSettings.EnablePrefixFilter)
                {
                    duplicateSettings.EnablePrefixFilter = newPrefixFilter;
                    SaveSettingsData();
                }
                
                if (duplicateSettings.EnablePrefixFilter)
                {
                    EditorGUILayout.LabelField("需要检测的前缀列表:", EditorStyles.miniLabel);
                    
                    // 显示现有前缀
                    string prefixToRemove = null;
                    foreach (var prefix in duplicateSettings.RequiredPrefixes.ToArray())
                    {
                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"  \"{prefix}\"", GUILayout.Width(100));
                        if (GUILayout.Button("删除", EditorStyles.miniButton, GUILayout.Width(40)))
                        {
                            prefixToRemove = prefix;
                        }
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                    }
                    
                    if (prefixToRemove != null)
                    {
                        duplicateSettings.RequiredPrefixes.Remove(prefixToRemove);
                        SaveSettingsData();
                    }
                    
                    // 添加新前缀
                    GUILayout.BeginHorizontal();
                    newPrefixName = EditorGUILayout.TextField("新前缀:", newPrefixName, GUILayout.Width(200));
                    if (GUILayout.Button("添加", EditorStyles.miniButton, GUILayout.Width(50)))
                    {
                        if (!string.IsNullOrEmpty(newPrefixName) && !duplicateSettings.RequiredPrefixes.Contains(newPrefixName))
                        {
                            duplicateSettings.RequiredPrefixes.Add(newPrefixName);
                            newPrefixName = "";
                            SaveSettingsData();
                        }
                    }
                    GUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(5);
            }
            
            // ===== 白名单（允许重复）=====
            showWhitelistSettings = EditorGUILayout.Foldout(showWhitelistSettings, "白名单（允许重复的节点名）", true, EditorStyles.foldoutHeader);
            if (showWhitelistSettings)
            {
                EditorGUI.indentLevel++;
                
                // 启用白名单开关
                bool newEnableWhitelist = EditorGUILayout.ToggleLeft("启用白名单", duplicateSettings.EnableWhitelist);
                if (newEnableWhitelist != duplicateSettings.EnableWhitelist)
                {
                    duplicateSettings.EnableWhitelist = newEnableWhitelist;
                    SaveSettingsData();
                }
                
                GUI.enabled = duplicateSettings.EnableWhitelist;
                
                EditorGUILayout.LabelField("以下节点名允许重复:", EditorStyles.miniLabel);
                
                string whitelistToRemove = null;
                foreach (var name in duplicateSettings.AllowedDuplicateNames.ToArray())
                {
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"  ✓ {name}", GUILayout.Width(150));
                    if (GUILayout.Button("删除", EditorStyles.miniButton, GUILayout.Width(40)))
                    {
                        whitelistToRemove = name;
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
                
                if (whitelistToRemove != null)
                {
                    duplicateSettings.AllowedDuplicateNames.Remove(whitelistToRemove);
                    SaveSettingsData();
                }
                
                GUILayout.BeginHorizontal();
                newWhitelistName = EditorGUILayout.TextField("添加白名单:", newWhitelistName, GUILayout.Width(200));
                if (GUILayout.Button("添加", EditorStyles.miniButton, GUILayout.Width(50)))
                {
                    if (!string.IsNullOrEmpty(newWhitelistName) && !duplicateSettings.AllowedDuplicateNames.Contains(newWhitelistName))
                    {
                        duplicateSettings.AllowedDuplicateNames.Add(newWhitelistName);
                        newWhitelistName = "";
                        SaveSettingsData();
                    }
                }
                GUILayout.EndHorizontal();
                
                GUI.enabled = true;
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(5);
            }
            
            // ===== 黑名单（禁止重复）=====
            showBlacklistSettings = EditorGUILayout.Foldout(showBlacklistSettings, "黑名单（禁止重复的节点名）", true, EditorStyles.foldoutHeader);
            if (showBlacklistSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("优先级最高！即使在白名单中，黑名单中的节点名也不允许重复。", MessageType.Warning);
                EditorGUILayout.LabelField("禁止重复:", EditorStyles.miniLabel);
                
                string blacklistToRemove = null;
                foreach (var name in duplicateSettings.ForbiddenDuplicateNames.ToArray())
                {
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"  ✗ {name}", GUILayout.Width(150));
                    if (GUILayout.Button("删除", EditorStyles.miniButton, GUILayout.Width(40)))
                    {
                        blacklistToRemove = name;
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
                
                if (blacklistToRemove != null)
                {
                    duplicateSettings.ForbiddenDuplicateNames.Remove(blacklistToRemove);
                    SaveSettingsData();
                }
                
                GUILayout.BeginHorizontal();
                newBlacklistName = EditorGUILayout.TextField("添加黑名单:", newBlacklistName, GUILayout.Width(200));
                if (GUILayout.Button("添加", EditorStyles.miniButton, GUILayout.Width(50)))
                {
                    if (!string.IsNullOrEmpty(newBlacklistName) && !duplicateSettings.ForbiddenDuplicateNames.Contains(newBlacklistName))
                    {
                        duplicateSettings.ForbiddenDuplicateNames.Add(newBlacklistName);
                        newBlacklistName = "";
                        SaveSettingsData();
                    }
                }
                GUILayout.EndHorizontal();
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(5);
            }
            
            // ===== UGUI 组件检测 =====
            showUGUISettings = EditorGUILayout.Foldout(showUGUISettings, "UGUI 组件名称检测", true, EditorStyles.foldoutHeader);
            if (showUGUISettings)
            {
                EditorGUI.indentLevel++;
                bool newUGUICheck = EditorGUILayout.ToggleLeft("检测 UGUI 组件类型同名 (如多个 'Image', 'Button')", duplicateSettings.CheckUGUIComponentNames);
                if (newUGUICheck != duplicateSettings.CheckUGUIComponentNames)
                {
                    duplicateSettings.CheckUGUIComponentNames = newUGUICheck;
                    SaveSettingsData();
                }
                
                if (duplicateSettings.CheckUGUIComponentNames)
                {
                    EditorGUILayout.LabelField("需要检测的组件类型:", EditorStyles.miniLabel);
                    
                    string[] commonComponents = new[] { "Image", "Text", "Button", "Toggle", "Slider", "ScrollRect", "InputField", "Dropdown" };
                    
                    foreach (var comp in commonComponents)
                    {
                        bool isChecked = duplicateSettings.UGUIComponentsToCheck.Contains(comp);
                        bool newChecked = EditorGUILayout.ToggleLeft(comp, isChecked);
                        
                        if (newChecked != isChecked)
                        {
                            if (newChecked)
                                duplicateSettings.UGUIComponentsToCheck.Add(comp);
                            else
                                duplicateSettings.UGUIComponentsToCheck.Remove(comp);
                            SaveSettingsData();
                        }
                    }
                }
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.Space(5);
            
            // 操作按钮
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("恢复默认设置"))
            {
                if (EditorUtility.DisplayDialog("确认", "确定要恢复默认检测规则设置吗？", "确定", "取消"))
                {
                    duplicateSettings = DuplicateDetectionSettings.GetDefault();
                    SaveSettingsData();
                }
            }
            GUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
        }
    }
}
