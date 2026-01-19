using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Linq;

namespace UIProbe
{
    public partial class UIProbeWindow
    {
        /// <summary>
        /// 绘制重命名历史记录区域
        /// </summary>
        private void DrawRenameHistorySection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // Header with foldout
            var history = RenameHistoryManager.LoadHistory();
            int recordCount = history.GetRecordCount();
            
            GUILayout.BeginHorizontal();
            showRenameHistory = EditorGUILayout.Foldout(showRenameHistory, $"📜 重命名历史记录 ({recordCount} 条)", true, EditorStyles.foldoutHeader);
            
            GUILayout.FlexibleSpace();
            
            if (recordCount > 0 && GUILayout.Button("清空历史", EditorStyles.miniButton, GUILayout.Width(70)))
            {
                if (EditorUtility.DisplayDialog("确认", "确定要清空所有重命名历史记录吗？", "确定", "取消"))
                {
                    RenameHistoryManager.ClearHistory();
                }
            }
            
            GUILayout.EndHorizontal();
            
            if (showRenameHistory)
            {
                EditorGUILayout.Space(5);
                
                if (recordCount == 0)
                {
                    EditorGUILayout.HelpBox("暂无重命名历史记录", MessageType.Info);
                }
                else
                {
                    // Scroll view for history
                    renameHistoryScrollPosition = EditorGUILayout.BeginScrollView(
                        renameHistoryScrollPosition, 
                        GUILayout.MaxHeight(300)
                    );
                    
                    foreach (var record in history.Records.ToArray())
                    {
                        DrawHistoryRecord(record);
                    }
                    
                    EditorGUILayout.EndScrollView();
                }
            }
            
            EditorGUILayout.EndVertical();
        }
        
        /// <summary>
        /// 绘制单条历史记录
        /// </summary>
        private void DrawHistoryRecord(RenameRecord record)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // Line 1: Time and prefab name
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"🕐 {record.Timestamp}", EditorStyles.miniLabel, GUILayout.Width(150));
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"📦 {record.PrefabName}", EditorStyles.boldLabel);
            GUILayout.EndHorizontal();
            
            EditorGUILayout.Space(2);
            
            // Line 2: Node path
            EditorGUILayout.LabelField($"路径: {record.NodePath}", EditorStyles.wordWrappedMiniLabel);
            
            EditorGUILayout.Space(3);
            
            // Line 3: Rename info with better layout
            GUILayout.BeginHorizontal();
            
            // Old name
            GUILayout.BeginVertical(GUILayout.Width(180));
            EditorGUILayout.LabelField("旧名称:", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(record.OldName, EditorStyles.label);
            GUILayout.EndVertical();
            
            // Arrow
            GUILayout.BeginVertical(GUILayout.Width(30));
            GUILayout.Space(10);
            EditorGUILayout.LabelField("→", GUILayout.Width(30));
            GUILayout.EndVertical();
            
            // New name
            GUILayout.BeginVertical(GUILayout.Width(180));
            EditorGUILayout.LabelField("新名称:", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(record.NewName, EditorStyles.boldLabel);
            GUILayout.EndVertical();
            
            GUILayout.FlexibleSpace();
            
            // Rollback button
            GUILayout.BeginVertical(GUILayout.Width(60));
            GUILayout.Space(5);
            GUI.enabled = record.CanRollback;
            if (GUILayout.Button("回滚", GUILayout.Width(50)))
            {
                if (EditorUtility.DisplayDialog("确认回滚", 
                    $"确定要回滚此重命名操作吗？\n\n{record.NewName} → {record.OldName}", 
                    "确定", "取消"))
                {
                    bool success = RenameHistoryManager.RollbackRename(record);
                    if (success)
                    {
                        EditorUtility.DisplayDialog("回滚成功", $"已将 '{record.NewName}' 恢复为 '{record.OldName}'", "确定");
                        
                        // 刷新检测结果
                        if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                        {
                            DetectCurrentPrefab();
                        }
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("回滚失败", "无法回滚：预制体或节点可能已被修改或删除", "确定");
                    }
                }
            }
            GUI.enabled = true;
            
            if (!record.CanRollback)
            {
                EditorGUILayout.LabelField("❌", GUILayout.Width(20));
            }
            GUILayout.EndVertical();
            
            GUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
            GUILayout.Space(5);
        }
    }
}
