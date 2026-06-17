using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace UIProbe
{
    /// <summary>
    /// Indexer 迁移遗留的壳层桥接：保留耦合 DuplicateChecker/Settings 的批量检测入口
    /// （duplicateSettings / currentTab / LoadBatchResultIntoChecker / currentBatchResultPath），
    /// 待 DuplicateChecker 与 Settings 迁移后再收敛。
    /// </summary>
    public partial class UIProbeWindow
    {
        /// <summary>转发资源引用页的索引变更通知（OnPrefabIndexChangedForAssetReferences 尚在 partial）。</summary>
        internal void OnPrefabIndexChangedForAssetReferences_Bridge() => OnPrefabIndexChangedForAssetReferences();

        // ===== 过渡期 shim：未迁移的 Settings / AssetReferences partial 仍按原字段/方法名访问 =====
        // Settings 数据管理按钮清理 Indexer 的收藏/历史；AssetReferences 复用类型图标。
        // 待二者迁移到独立模块后删除。
        private List<string> searchHistory => modules.OfType<IndexerModule>().First().SearchHistory;
        private List<string> bookmarks => modules.OfType<IndexerModule>().First().Bookmarks;
        private string GetAssetTypeIcon(AssetReferenceType type) => modules.OfType<IndexerModule>().First().GetAssetTypeIconPublic(type);

        /// <summary>
        /// 批量检测预制体重名节点。IndexerModule 传入选中路径集合，返回检测结果。
        /// </summary>
        internal BatchDuplicateResult RunBatchDetectDuplicates_Bridge(HashSet<string> selectedPrefabPaths)
        {
            if (selectedPrefabPaths.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先选择要检测的预制体", "确定");
                return null;
            }

            // 加载检测设置
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

            var batchDuplicateResult = new BatchDuplicateResult();
            int processedCount = 0;
            int totalCount = selectedPrefabPaths.Count;

            try
            {
                foreach (var prefabPath in selectedPrefabPaths)
                {
                    processedCount++;

                    // 显示进度条
                    float progress = (float)processedCount / totalCount;
                    if (EditorUtility.DisplayCancelableProgressBar(
                        "批量检测重名",
                        $"正在检测: {Path.GetFileNameWithoutExtension(prefabPath)} ({processedCount}/{totalCount})",
                        progress))
                    {
                        break; // 用户取消
                    }

                    // 加载预制体
                    GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (prefabAsset == null)
                        continue;

                    // 执行重名检测（使用设置中配置的范围）
                    DuplicateDetectionMode scope = duplicateSettings.DetectionScope;
                    DuplicateNameResult result = DuplicateNameRule.DetectDuplicates(
                        prefabAsset,
                        scope,
                        duplicateSettings
                    );

                    // 记录结果
                    string folderPath = Path.GetDirectoryName(prefabPath);
                    string prefabName = Path.GetFileNameWithoutExtension(prefabPath);

                    batchDuplicateResult.AddResult(new PrefabDuplicateResult(
                        prefabPath,
                        prefabName,
                        folderPath,
                        result
                    ));
                }

                EditorUtility.ClearProgressBar();

                // 保存JSON结果到Batch_Results文件夹
                string jsonPath = "";
                try
                {
                    jsonPath = System.IO.Path.Combine(
                        UIProbeStorage.GetBatchResultsPath(),
                        $"BatchDuplicateCheck_{System.DateTime.Now:yyyyMMdd_HHmmss}.json"
                    );
                    string json = JsonUtility.ToJson(batchDuplicateResult, true);
                    System.IO.File.WriteAllText(jsonPath, json);
                    Debug.Log($"[UIProbe] 批量检测结果已保存到: {jsonPath}");
                }
                catch (Exception saveEx)
                {
                    Debug.LogWarning($"[UIProbe] JSON保存失败: {saveEx.Message}");
                }

                // 显示结果摘要
                string summary = batchDuplicateResult.GetSummary();

                // 如果有重名，询问是否切换到重名检测页面
                if (batchDuplicateResult.PrefabsWithDuplicates > 0)
                {
                    bool switchTab = EditorUtility.DisplayDialog(
                        "批量检测完成",
                        $"{summary}\n\n发现 {batchDuplicateResult.PrefabsWithDuplicates} 个预制体存在重名。\n\n是否切换到重名检测页面进行处理？",
                        "是，切换",
                        "稍后处理"
                    );

                    if (switchTab)
                    {
                        // 切换到重名检测标签页
                        currentTab = Tab.DuplicateChecker;
                        LoadBatchResultIntoCheckerWithPath(batchDuplicateResult, jsonPath);
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog("检测完成", summary, "确定");
                }

                Debug.Log($"[UIProbe] 批量检测完成: {summary}");
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("检测失败", $"批量检测失败: {e.Message}", "确定");
                Debug.LogError($"[UIProbe] 批量检测失败: {e}");
            }

            return batchDuplicateResult;
        }

        /// <summary>
        /// 加载批量检测结果到重名检测页面（带JSON路径）
        /// </summary>
        private void LoadBatchResultIntoCheckerWithPath(BatchDuplicateResult result, string jsonPath)
        {
            // 调用partial方法
            LoadBatchResultIntoChecker(result);

            // 在UIProbeWindow_DuplicateCheckerBatch.cs中会设置currentBatchResult
            // 这里我们需要另外设置路径
            currentBatchResultPath = jsonPath;
        }

        /// <summary>
        /// 加载批量检测结果到重名检测页面
        /// (此方法在UIProbeWindow_DuplicateCheckerBatch.cs中实现)
        /// </summary>
        partial void LoadBatchResultIntoChecker(BatchDuplicateResult result);
    }
}
