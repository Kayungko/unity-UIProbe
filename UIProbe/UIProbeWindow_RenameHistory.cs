using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.Linq;

namespace UIProbe
{
    public partial class UIProbeWindow
    {
        private Dictionary<string, bool> historyPrefabFoldouts = new Dictionary<string, bool>();
        
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
            
            // 按预制体分组统计
            var prefabGroups = group.Records.GroupBy(r => r.PrefabName).ToList();
            
            historyDateFoldouts[group.Date] = EditorGUILayout.Foldout(
                historyDateFoldouts[group.Date],
                $"📅 {group.Date} ({prefabGroups.Count} 个预制体, 共 {group.Records.Count} 条)",
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
            
            // 显示预制体分组
            if (historyDateFoldouts[group.Date])
            {
                EditorGUI.indentLevel++;
                
                foreach (var prefabGroup in prefabGroups)
                {
                    DrawPrefabGroup(prefabGroup.Key, prefabGroup.ToList());
                }
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }
        
        /// <summary>
        /// 绘制预制体分组
        /// </summary>
        private void DrawPrefabGroup(string prefabName, List<RenameRecord> records)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // 预制体折叠标题
            GUILayout.BeginHorizontal();
            
            string prefabKey = $"prefab_{prefabName}";
            if (!historyPrefabFoldouts.ContainsKey(prefabKey))
            {
                historyPrefabFoldouts[prefabKey] = false;
            }
            
            historyPrefabFoldouts[prefabKey] = EditorGUILayout.Foldout(
                historyPrefabFoldouts[prefabKey],
                $"📦 {prefabName} ({records.Count} 条记录)",
                true
            );
            
            GUILayout.FlexibleSpace();
            
            // 删除该预制体的所有记录（删除JSON文件）
            if (records.Count > 0 && GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                if (EditorUtility.DisplayDialog("确认", $"确定要删除 {prefabName} 的所有记录吗？", "确定", "取消"))
                {
                    // 删除JSON文件（所有记录共享同一个FilePath）
                    string filePath = records[0].FilePath;
                    RenameHistoryManager.DeleteRecord(filePath);
                }
            }
            
            GUILayout.EndHorizontal();
            
            // 显示记录列表
            if (historyPrefabFoldouts[prefabKey])
            {
                for (int i = 0; i < records.Count; i++)
                {
                    DrawHistoryRecordCompact(records[i], i + 1);
                }
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
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
        
        /// <summary>
        /// 绘制紧凑版历史记录（用于预制体分组内）
        /// </summary>
        private void DrawHistoryRecordCompact(RenameRecord record, int index)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            
            // 序号
            EditorGUILayout.LabelField($"{index}.", GUILayout.Width(30));
            
            // 时间
            string time = record.Timestamp.Split(' ').Length > 1 ? record.Timestamp.Split(' ')[1] : record.Timestamp;
            EditorGUILayout.LabelField($"🕐 {time}", EditorStyles.miniLabel, GUILayout.Width(80));
            
            // 节点路径（简化显示）
            string shortPath = record.NodePath.Contains("/") 
                ? ".../" + record.NodePath.Split('/').Last() 
                : record.NodePath;
            EditorGUILayout.LabelField(shortPath, EditorStyles.miniLabel, GUILayout.Width(120));
            
            // 旧名 → 新名
            EditorGUILayout.LabelField(record.OldName, GUILayout.Width(100));
            EditorGUILayout.LabelField("→", GUILayout.Width(20));
            EditorGUILayout.LabelField(record.NewName, EditorStyles.boldLabel, GUILayout.Width(100));
            
            GUILayout.FlexibleSpace();
            
            // 回滚按钮
            GUI.enabled = record.CanRollback;
            if (GUILayout.Button("回滚", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                if (EditorUtility.DisplayDialog("确认回滚", 
                    $"确定要回滚此重命名操作吗?\n\n{record.NewName} → {record.OldName}", 
                    "确定", "取消"))
                {
                    bool success = RenameHistoryManager.RollbackRename(record);
                    if (success)
                    {
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
            
            EditorGUILayout.EndHorizontal();
        }
    }
}
