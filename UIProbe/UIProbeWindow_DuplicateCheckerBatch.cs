using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;
using System.IO;

namespace UIProbe
{
    public partial class UIProbeWindow
    {
        /// <summary>
        /// 实现从索引页面加载批量检测结果到重名检测页面
        /// </summary>
        partial void LoadBatchResultIntoChecker(BatchDuplicateResult result)
        {
            if (result == null)
                return;
            
            // 过滤掉已弃用的项
            int deprecatedCount = result.Results.Count(r => r.IsDeprecated);
            if (deprecatedCount > 0)
            {
                result.Results.RemoveAll(r => r.IsDeprecated);
                Debug.Log($"[UIProbe] 已过滤 {deprecatedCount} 个已弃用的预制体");
            }
            
            // 切换到批量模式
            isBatchMode = true;
            currentBatchResult = result;
            batchCardPageIndex = 0;
            
            // 切换到检测功能子标签
            duplicateCheckerSubTab = 0;
            
            Repaint();
        }
        
        /// <summary>
        /// 清除批量模式，返回单个检测模式
        /// </summary>
        private void ClearBatchMode()
        {
            isBatchMode = false;
            currentBatchResult = null;
            batchCardPageIndex = 0;
            currentBatchResultPath = "";
            isFromBatchMode = false;
            currentProcessingItem = null;
        }
        
        /// <summary>
        /// 返回批量检测结果列表
        /// </summary>
        private void ReturnToBatchMode()
        {
            // 保存当前状态到JSON
            SaveBatchResult();
            
            // 切换回批量模式
            isBatchMode = true;
            isFromBatchMode = false;
            currentProcessingItem = null;
            
            Repaint();
        }
        
        /// <summary>
        /// 保存批量检测结果到JSON文件
        /// </summary>
        private void SaveBatchResult()
        {
            if (currentBatchResult == null || string.IsNullOrEmpty(currentBatchResultPath))
                return;
            
            try
            {
                string json = JsonUtility.ToJson(currentBatchResult, true);
                File.WriteAllText(currentBatchResultPath, json);
                Debug.Log($"[UIProbe] 批量结果已更新: {currentBatchResultPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UIProbe] 保存批量结果失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 绘制批量模式UI（卡片视图）
        /// </summary>
        private void DrawBatchModeUI()
        {
            // 过滤工具栏
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            bool newShowOnlyDuplicates = GUILayout.Toggle(
                batchShowOnlyDuplicates, 
                "仅显示有重名", 
                EditorStyles.toolbarButton,
                GUILayout.Width(100)
            );
            if (newShowOnlyDuplicates != batchShowOnlyDuplicates)
            {
                batchShowOnlyDuplicates = newShowOnlyDuplicates;
                batchCardPageIndex = 0;
            }
            
            GUILayout.FlexibleSpace();
            
            // 显示处理进度
            int processedDuplicates = currentBatchResult.Results.Count(r => r.HasDuplicates && r.IsProcessed);
            EditorGUILayout.LabelField(
                $"已处理: {processedDuplicates}/{currentBatchResult.PrefabsWithDuplicates}", 
                EditorStyles.miniLabel, 
                GUILayout.Width(100)
            );
            
            // 导入历史结果按钮
            if (GUILayout.Button("导入历史结果", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                ImportBatchResult();
            }
            
            GUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            // 获取过滤后的预制体列表
            var displayResults = batchShowOnlyDuplicates 
                ? currentBatchResult.Results.Where(r => r.HasDuplicates).ToList()
                : currentBatchResult.Results;
            
            if (displayResults.Count == 0)
            {
                EditorGUILayout.HelpBox("没有符合条件的预制体", MessageType.Info);
                return;
            }
            
            int totalPages = Mathf.Max(1, Mathf.CeilToInt(displayResults.Count / (float)CARDS_PER_PAGE));
            batchCardPageIndex = Mathf.Clamp(batchCardPageIndex, 0, totalPages - 1);
            int startIndex = batchCardPageIndex * CARDS_PER_PAGE;
            
            // 分页信息
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"第 {batchCardPageIndex + 1} / {totalPages} 页", EditorStyles.miniLabel, GUILayout.Width(100));
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"共 {displayResults.Count} 个预制体", EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            // 卡片列表 (使用主scrollview)
            for (int i = startIndex; i < Mathf.Min(startIndex + CARDS_PER_PAGE, displayResults.Count); i++)
            {
                DrawPrefabCard(displayResults[i]);
            }
            
            EditorGUILayout.Space(10);
            
            // 分页控件
            GUILayout.BeginHorizontal();
            GUI.enabled = batchCardPageIndex > 0;
            if (GUILayout.Button("◀ 上一页", GUILayout.Width(80)))
            {
                batchCardPageIndex--;
            }
            GUI.enabled = true;
            
            GUILayout.FlexibleSpace();
            
            EditorGUILayout.LabelField($"{batchCardPageIndex + 1} / {totalPages}", 
                EditorStyles.boldLabel, GUILayout.Width(60));
            
            GUILayout.FlexibleSpace();
            
            GUI.enabled = batchCardPageIndex < totalPages - 1;
            if (GUILayout.Button("下一页 ▶", GUILayout.Width(80)))
            {
                batchCardPageIndex++;
            }
            GUI.enabled = true;
            
            GUILayout.EndHorizontal();
        }
        
        /// <summary>
        /// 绘制单个预制体卡片
        /// </summary>
        private void DrawPrefabCard(PrefabDuplicateResult result)
        {
            // 根据状态设置背景色
            if (result.IsDeprecated)
            {
                GUI.backgroundColor = new Color(0.7f, 0.7f, 0.7f);  // 灰色（已弃用）
            }
            else if (result.IsProcessed)
            {
                GUI.backgroundColor = new Color(0.85f, 1f, 0.85f);  // 淡绿色
            }
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // 标题行
            GUILayout.BeginHorizontal();
            
            // 状态图标
            string statusIcon = result.IsDeprecated ? "⛔" :
                                result.IsProcessed ? "✅" : 
                                result.HasDuplicates ? "🔴" : "✅";
            GUILayout.Label(statusIcon, GUILayout.Width(25));
            
            // 预制体名称
            if (result.IsDeprecated)
            {
                EditorGUILayout.LabelField($"{result.PrefabName} (已弃用)", EditorStyles.boldLabel);
            }
            else
            {
                EditorGUILayout.LabelField(result.PrefabName, EditorStyles.boldLabel);
            }
            
            GUILayout.FlexibleSpace();
            
            // 弃用切换按钮
            if (!result.IsProcessed)
            {
                string deprecateLabel = result.IsDeprecated ? "恢复" : "弃用";
                if (GUILayout.Button(deprecateLabel, EditorStyles.miniButton, GUILayout.Width(40)))
                {
                    result.IsDeprecated = !result.IsDeprecated;
                    result.DeprecatedTime = result.IsDeprecated ? 
                        System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : "";
                    SaveBatchResult();
                }
            }
            
            // 操作按钮
            if (!result.IsDeprecated && !result.IsProcessed && result.HasDuplicates)
            {
                if (GUILayout.Button("打开", GUILayout.Width(50)))
                {
                    OpenAndCheckPrefab(result);
                }
            }
            else if (result.IsProcessed)
            {
                GUILayout.Label("已处理", EditorStyles.miniLabel, GUILayout.Width(50));
            }
            
            GUILayout.EndHorizontal();
            
            // 路径
            EditorGUILayout.LabelField($"📂 {result.FolderPath}", EditorStyles.miniLabel);
            
            // 弃用信息
            if (result.IsDeprecated && !string.IsNullOrEmpty(result.DeprecatedTime))
            {
                EditorGUILayout.LabelField($"⛔ 已弃用于: {result.DeprecatedTime}", EditorStyles.miniLabel);
            }
            
            // 重名信息
            if (result.HasDuplicates && !result.IsDeprecated)
            {
                EditorGUILayout.Space(3);
                string duplicateInfo = result.GetDuplicateSummary();
                EditorGUILayout.LabelField($"🔴 重名节点: {duplicateInfo}", EditorStyles.wordWrappedLabel);
                
                // 数据时间提示
                if (!string.IsNullOrEmpty(currentBatchResult.LastCheckTime))
                {
                    EditorGUILayout.LabelField(
                        $"⚠ 检测于: {currentBatchResult.LastCheckTime} (打开时将重新检测)", 
                        EditorStyles.miniLabel
                    );
                }
            }
            else if (!result.IsDeprecated)
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("✅ 无重名节点", EditorStyles.miniLabel);
            }
            
            // 处理时间
            if (result.IsProcessed && !string.IsNullOrEmpty(result.ProcessedTime))
            {
                EditorGUILayout.LabelField($"✓ 处理于: {result.ProcessedTime}", EditorStyles.miniLabel);
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
            
            GUI.backgroundColor = Color.white;
        }
        
        /// <summary>
        /// 打开预制体并重新检测
        /// </summary>
        private void OpenAndCheckPrefab(PrefabDuplicateResult result)
        {
            // 加载预制体
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(result.PrefabPath);
            if (prefabAsset == null)
            {
                EditorUtility.DisplayDialog("错误", $"无法加载预制体:\n{result.PrefabPath}", "确定");
                return;
            }
            
            // 记录来源信息
            isFromBatchMode = true;
            currentProcessingItem = result;
            
            // 打开预制体编辑模式
            AssetDatabase.OpenAsset(prefabAsset);
            
            // 切换回单个检测模式
            isBatchMode = false;
            
            // 延迟执行检测（等待预制体打开）
            EditorApplication.delayCall += () =>
            {
                DetectCurrentPrefab();
                
                // 智能判断：如果无重名，自动标记为已处理
                if (currentDuplicateResult != null && currentDuplicateResult.GroupCount == 0)
                {
                    result.IsProcessed = true;
                    result.ProcessedTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    SaveBatchResult();
                    
                    EditorUtility.DisplayDialog("检测完成", 
                        "✅ 未发现重名节点\n已自动标记为完成", 
                        "确定");
                }
            };
        }
        
        /// <summary>
        /// 导入批量检测结果
        /// </summary>
        private void ImportBatchResult()
        {
            string path = EditorUtility.OpenFilePanel(
                "导入批量检测结果",
                UIProbeStorage.GetBatchResultsPath(),
                "json"
            );
            
            if (string.IsNullOrEmpty(path))
                return;
            
            try
            {
                string json = File.ReadAllText(path);
                var result = JsonUtility.FromJson<BatchDuplicateResult>(json);
                
                // 记录JSON路径
                currentBatchResultPath = path;
                
                LoadBatchResultIntoChecker(result);
                
                int processedCount = result.ProcessedCount;
                EditorUtility.DisplayDialog(
                    "导入成功",
                    $"已导入批量检测结果:\n共 {result.TotalPrefabs} 个预制体，{result.PrefabsWithDuplicates} 个存在重名\n已处理: {processedCount}",
                    "确定"
                );
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("错误", $"导入失败:\n{e.Message}", "确定");
            }
        }
    }
}
