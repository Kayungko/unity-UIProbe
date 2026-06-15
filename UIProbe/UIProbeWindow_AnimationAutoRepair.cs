using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

namespace UIProbe
{
    public partial class UIProbeWindow
    {
        private bool animationAutoRepairEnabled;
        private Vector2 animationAutoRepairScrollPos;
        private List<string> animationAutoRepairLogs = new List<string>();
        private const int ANIMATION_AUTO_REPAIR_MAX_LOGS = 50;

        // ===== 侧边栏角标按钮 =====

        private void DrawAnimationAutoRepairSidebarButton()
        {
            bool hasAnim = HasAnimationInCurrentPrefab();
            string label = hasAnim ? "⚠ 动画修复" : "动画修复";

            GUI.backgroundColor = currentTab == Tab.AnimationAutoRepair ? Color.cyan : Color.white;
            if (GUILayout.Button(label, GUILayout.Height(35)))
            {
                currentTab = Tab.AnimationAutoRepair;
            }
            GUI.backgroundColor = Color.white;
        }

        private bool HasAnimationInCurrentPrefab()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null) return false;

            var root = stage.prefabContentsRoot;
            if (root == null) return false;

            // 有动画组件才算
            if (root.GetComponentInChildren<Animator>(true) != null) return true;
            if (root.GetComponentInChildren<Animation>(true) != null) return true;

            return false;
        }

        // ===== Tab 主体 =====

        private void DrawAnimationAutoRepairTab()
        {
            EditorGUILayout.LabelField("动画路径修复", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // ===== 区块 1: 自动修复（有权限时使用） =====
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("自动修复", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("开启后移动层级或修改节点命名时自动修复动画路径。适用于你有动画文件修改权限的场景。", MessageType.Info);
            EditorGUILayout.Space(3);

            EditorGUILayout.BeginHorizontal();
            bool newEnabled = EditorGUILayout.ToggleLeft("启用自动修复", animationAutoRepairEnabled, GUILayout.Width(130));
            if (newEnabled != animationAutoRepairEnabled)
            {
                animationAutoRepairEnabled = newEnabled;
                AnimationAutoRepair.SetEnabled(animationAutoRepairEnabled);
                SaveAnimationAutoRepairConfig();
            }

            GUILayout.FlexibleSpace();

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                bool hasAnim = HasAnimationInCurrentPrefab();
                if (hasAnim)
                {
                    GUI.color = new Color(1f, 0.85f, 0.3f);
                    EditorGUILayout.LabelField($"当前: {stage.prefabContentsRoot.name} (含动画)", EditorStyles.miniLabel);
                }
                else
                {
                    GUI.color = new Color(0.4f, 0.9f, 0.4f);
                    EditorGUILayout.LabelField($"当前: {stage.prefabContentsRoot.name}", EditorStyles.miniLabel);
                }
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                EditorGUILayout.LabelField("未在预制体编辑模式", EditorStyles.miniLabel);
                GUI.color = Color.white;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("手动扫描修复", GUILayout.Height(26)))
            {
                int count = AnimationAutoRepair.CheckAndRepair();
                if (count == 0)
                    AppendRepairLog("手动扫描完成: 未发现需要修复的动画路径");
            }

            if (animationAutoRepairLogs.Count > 0)
            {
                if (GUILayout.Button("清空日志", GUILayout.Height(26), GUILayout.Width(80)))
                {
                    animationAutoRepairLogs.Clear();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // ===== 区块 2: 协同导出/导入（无权限时使用） =====
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = new Color(0.25f, 0.25f, 0.3f);
            EditorGUILayout.LabelField("协同导出 / 导入", EditorStyles.boldLabel);
            GUI.backgroundColor = Color.white;
            EditorGUILayout.HelpBox("如果你没有动画文件修改权限：进入预制体并保持 UIProbe 面板打开 → 移动层级或改名 → 导出映射文件 → 交由 Vx 导入修复。", MessageType.None);
            EditorGUILayout.Space(3);

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.3f, 0.65f, 1f);
            if (GUILayout.Button("导出修复映射", GUILayout.Height(34)))
            {
                string path = AnimationAutoRepair.ExportMappings();
                if (!string.IsNullOrEmpty(path))
                    AppendRepairLog($"已导出修复映射: {path}");
            }
            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
            if (GUILayout.Button("导入并应用修复", GUILayout.Height(34)))
            {
                string path = EditorUtility.OpenFilePanel("选择动画修复映射文件", Application.dataPath, "json");
                if (!string.IsNullOrEmpty(path))
                {
                    int count = AnimationAutoRepair.ApplyMappings(path);
                    AppendRepairLog($"导入完成: 修复了 {count} 个动画剪辑");
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            // ===== 日志区 =====
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            string logLabel = animationAutoRepairLogs.Count > 0
                ? $"修复日志 ({animationAutoRepairLogs.Count})"
                : "修复日志 (无记录)";
            EditorGUILayout.LabelField(logLabel, EditorStyles.boldLabel);
            EditorGUILayout.Space(3);

            animationAutoRepairScrollPos = EditorGUILayout.BeginScrollView(animationAutoRepairScrollPos, GUILayout.ExpandHeight(true));

            if (animationAutoRepairLogs.Count == 0)
            {
                EditorGUILayout.LabelField("暂无修复记录", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                foreach (var log in animationAutoRepairLogs)
                {
                    bool isWarning = log.StartsWith("⚠");
                    bool isDetail = log.StartsWith("  [") || log.StartsWith("  ");
                    var style = new GUIStyle(EditorStyles.miniLabel);
                    if (isWarning)
                        style.normal.textColor = new Color(1f, 0.75f, 0.3f);
                    else if (isDetail)
                        style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                    else
                        style.normal.textColor = Color.white;
                    style.wordWrap = true;
                    EditorGUILayout.LabelField(log, style);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ===== 日志 =====

        private void AppendRepairLog(string msg)
        {
            animationAutoRepairLogs.Add($"[{System.DateTime.Now:HH:mm:ss}] {msg}");
            if (animationAutoRepairLogs.Count > ANIMATION_AUTO_REPAIR_MAX_LOGS)
                animationAutoRepairLogs.RemoveAt(0);
            Repaint();
        }

        // ===== 配置持久化 =====

        private void ApplyAnimationAutoRepairConfig()
        {
            animationAutoRepairEnabled = EditorPrefs.GetBool("UIProbe_AnimationAutoRepair", false);
            AnimationAutoRepair.OnRepairLog -= AppendRepairLog;
            AnimationAutoRepair.OnRepairLog += AppendRepairLog;

            AnimationAutoRepair.Initialize(animationAutoRepairEnabled);
        }

        private void CollectAnimationAutoRepairConfig()
        {
            EditorPrefs.SetBool("UIProbe_AnimationAutoRepair", animationAutoRepairEnabled);
        }

        private void SaveAnimationAutoRepairConfig()
        {
            EditorPrefs.SetBool("UIProbe_AnimationAutoRepair", animationAutoRepairEnabled);
        }
    }
}
