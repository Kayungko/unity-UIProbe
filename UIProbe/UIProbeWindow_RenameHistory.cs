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
            
            // Header
            var dateGroups = RenameHistoryManager.LoadHistoryGroupedByDate();
            int totalCount = dateGroups.Sum(g => g.Records.Count);
            
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"📜 重命名历史记录 ({totalCount} 条)", EditorStyles.boldLabel);
            
            GUILayout.FlexibleSpace();
            
            if (totalCount > 0 && GUILayout.Button("清空全部", EditorStyles.miniButton, GUILayout.Width(70)))
            {
                if (EditorUtility.DisplayDialog("确认", "确定要清空所有重命名历史记录吗？", "确定", "取消"))
                {
                    RenameHistoryManager.ClearHistory();
                }
            }
            
            GUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            if (totalCount == 0)
            {
                EditorGUILayout.HelpBox("暂无重命名历史记录", MessageType.Info);
            }
            else
            {
                // Scroll view for history
                renameHistoryScrollPosition = EditorGUILayout.BeginScrollView(
                    renameHistoryScrollPosition, 
                    GUILayout.MaxHeight(400)
                );
                
                // 按日期分组显示
                foreach (var group in dateGroups)
                {
                    DrawDateGroup(group);
                }
                
                EditorGUILayout.EndScrollView();
            }
            
            EditorGUILayout.EndVertical();
        }
        
        /// <summary>
        /// 绘制日期分组
        /// </summary>
        private void DrawDateGroup(DateFolderGroup group)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // 日期折叠标题
            GUILayout.BeginHorizontal();
            
            if (!historyDateFoldouts.ContainsKey(group.Date))
            {
                historyDateFoldouts[group.Date] = false;
            }
            
            historyDateFoldouts[group.Date] = EditorGUILayout.Foldout(
                historyDateFoldouts[group.Date],
               $"📅 {group.Date} ({group.Records.Count} 条)",
                true
            );
            
            GUILayout.FlexibleSpace();
            
            // 删除该日期所有记录
            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                if (EditorUtility.DisplayDialog("确认", $"确定要删除 {group.Date} 的所有记录吗？", "确定", "取消"))
                {
                    RenameHistoryManager.DeleteDateFolder(group.Date);
                }
            }
            
            GUILayout.EndHorizontal();
            
            // 显示记录
            if (historyDateFoldouts[group.Date])
            {
                foreach (var record in group.Records)
                {
                    DrawHistoryRecord(record);
                }
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
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
            
            // Delete button
            GUILayout.BeginVertical(GUILayout.Width(25));
            GUILayout.Space(5);
            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                if (EditorUtility.DisplayDialog("确认删除", 
                    $"确定要删除这条重命名记录吗？\n\n{record.OldName} → {record.NewName}", 
                    "删除", "取消"))
                {
                    RenameHistoryManager.DeleteRecord(record.FilePath);
                }
            }
            GUILayout.EndVertical();
            
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
