using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;

namespace UIProbe
{
    /// <summary>
    /// CSV 导出工具类
    /// </summary>
    public static class CSVExporter
    {
        /// <summary>
        /// 导出单个预制体的检测结果到 CSV
        /// </summary>
        public static void ExportSingleResult(DuplicateNameResult result, string savePath)
        {
            if (result == null || result.GroupCount == 0)
            {
                Debug.LogWarning("No duplicate data to export.");
                return;
            }

            var data = new List<CSVRow>();
            string prefabName = result.Prefab != null ? result.Prefab.name : "Unknown";

            foreach (var group in result.Groups)
            {
                foreach (var path in group.Paths)
                {
                    data.Add(new CSVRow
                    {
                        PrefabName = prefabName,
                        NodeName = group.NodeName,
                        DuplicateCount = group.Count,
                        NodePath = path
                    });
                }
            }

            WriteToCSV(data, savePath);
        }

        /// <summary>
        /// 导出多个预制体的检测结果到 CSV
        /// </summary>
        public static void ExportBatchResults(Dictionary<GameObject, DuplicateNameResult> results, string savePath)
        {
            var data = new List<CSVRow>();

            foreach (var kvp in results)
            {
                var prefab = kvp.Key;
                var result = kvp.Value;
                
                if (result.GroupCount == 0)
                    continue;

                string prefabName = prefab != null ? prefab.name : "Unknown";

                foreach (var group in result.Groups)
                {
                    foreach (var path in group.Paths)
                    {
                        data.Add(new CSVRow
                        {
                            PrefabName = prefabName,
                            NodeName = group.NodeName,
                            DuplicateCount = group.Count,
                            NodePath = path
                        });
                    }
                }
            }

            if (data.Count == 0)
            {
                Debug.LogWarning("No duplicate data to export.");
                return;
            }

            WriteToCSV(data, savePath);
        }

        /// <summary>
        /// 写入 CSV 文件
        /// </summary>
        private static void WriteToCSV(List<CSVRow> data, string savePath)
        {
            try
            {
                // 确保目录存在
                string directory = Path.GetDirectoryName(savePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (var writer = new StreamWriter(savePath, false, Encoding.UTF8))
                {
                    // 写入 BOM 以支持 Excel 正确显示中文
                    writer.Write('\uFEFF');
                    
                    // 写入表头
                    writer.WriteLine("预制体名称,节点名称,重复次数,节点路径");
                    
                    // 写入数据
                    foreach (var row in data)
                    {
                        writer.WriteLine($"{EscapeCSV(row.PrefabName)},{EscapeCSV(row.NodeName)},{row.DuplicateCount},{EscapeCSV(row.NodePath)}");
                    }
                }

                Debug.Log($"CSV exported successfully: {savePath}");
                EditorUtility.DisplayDialog("导出成功", $"CSV 文件已保存到：\n{savePath}", "确定");
                
                // 高亮显示导出的文件
                EditorUtility.RevealInFinder(savePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to export CSV: {e.Message}");
                EditorUtility.DisplayDialog("导出失败", $"无法导出 CSV 文件：\n{e.Message}", "确定");
            }
        }

        /// <summary>
        /// CSV 字段转义（处理逗号、引号等特殊字符）
        /// </summary>
        private static string EscapeCSV(string field)
        {
            if (string.IsNullOrEmpty(field))
                return "";

            // 如果包含逗号、引号或换行符，需要用引号包裹
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
            {
                // 将引号转义为两个引号
                field = field.Replace("\"", "\"\"");
                return $"\"{field}\"";
            }

            return field;
        }

        /// <summary>
        /// 打开文件保存对话框
        /// </summary>
        public static string GetSaveFilePath(string defaultName = "DuplicateReport")
        {
            string defaultPath = Path.Combine(
                UIProbeStorage.GetCSVExportPath(),
                $"{defaultName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            );

            string path = EditorUtility.SaveFilePanel(
                "导出 CSV 报告",
                Path.GetDirectoryName(defaultPath),
                Path.GetFileName(defaultPath),
                "csv"
            );

            return path;
        }
        
        /// <summary>
        /// 导出批量检测结果到 CSV（简化版：名称+数量）
        /// </summary>
        public static void ExportBatchDuplicateResults(BatchDuplicateResult batchResult)
        {
            ExportBatchDuplicateResults(batchResult, false);
        }
        
        /// <summary>
        /// 导出批量检测结果到 CSV
        /// </summary>
        /// <param name="batchResult">批量检测结果</param>
        /// <param name="detailedPaths">是否启用详细路径模式（每个重复节点路径单独一行）</param>
        public static void ExportBatchDuplicateResults(BatchDuplicateResult batchResult, bool detailedPaths)
        {
            if (batchResult == null || batchResult.TotalPrefabs == 0)
            {
                EditorUtility.DisplayDialog("提示", "没有可导出的检测结果", "确定");
                return;
            }
            
            // 过滤只保留有重复的预制体
            var resultsWithDuplicates = batchResult.Results.Where(r => r.HasDuplicates).ToList();
            
            if (resultsWithDuplicates.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "没有存在重复命名的预制体", "确定");
                return;
            }
            
            string fileName = detailedPaths 
                ? $"BatchDuplicateCheck_Detailed_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv"
                : $"BatchDuplicateCheck_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv";
            string savePath = Path.Combine(UIProbeStorage.GetCSVExportPath(), fileName);
            
            StringBuilder csv = new StringBuilder();
            int exportedRows = 0;
            
            if (detailedPaths)
            {
                // 详细模式：每个重复节点路径一行
                csv.AppendLine("所属文件夹,预制体名称,重复节点名称,节点完整路径");
                
                foreach (var result in resultsWithDuplicates)
                {
                    // 使用FolderPath而非PrefabPath，因为预制体名称已经有单独的列
                    string folderPath = EscapeCSV(result.FolderPath);
                    string prefabName = EscapeCSV(result.PrefabName);
                    
                    if (result.Result != null && result.Result.Groups != null)
                    {
                        foreach (var group in result.Result.Groups)
                        {
                            foreach (var nodePath in group.Paths)
                            {
                                csv.AppendLine($"{folderPath},{prefabName},{EscapeCSV(group.NodeName)},{EscapeCSV(nodePath)}");
                                exportedRows++;
                            }
                        }
                    }
                }
            }
            else
            {
                // 简化模式：汇总信息
                csv.AppendLine("预制体路径,预制体名称,重复节点汇总,是否已处理,处理时间,是否已弃用,弃用时间");
                
                foreach (var result in resultsWithDuplicates)
                {
                    string prefabPath = EscapeCSV(result.PrefabPath);
                    string prefabName = EscapeCSV(result.PrefabName);
                    string duplicates = EscapeCSV(result.GetDuplicateSummary());
                    string isProcessed = result.IsProcessed ? "是" : "否";
                    string processedTime = EscapeCSV(result.ProcessedTime ?? "");
                    string isDeprecated = result.IsDeprecated ? "是" : "否";
                    string deprecatedTime = EscapeCSV(result.DeprecatedTime ?? "");
                    
                    csv.AppendLine($"{prefabPath},{prefabName},{duplicates},{isProcessed},{processedTime},{isDeprecated},{deprecatedTime}");
                    exportedRows++;
                }
            }
            
            try
            {
                File.WriteAllText(savePath, csv.ToString(), Encoding.UTF8);
                
                string modeText = detailedPaths ? "详细路径模式" : "简化模式";
                EditorUtility.DisplayDialog("导出成功", 
                    $"批量检测结果已导出到:\n{savePath}\n\n模式: {modeText}\n导出预制体: {resultsWithDuplicates.Count} 个\n导出行数: {exportedRows} 行", 
                    "确定");
                EditorUtility.RevealInFinder(savePath);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("导出失败", $"导出失败: {e.Message}", "确定");
                Debug.LogError($"[CSVExporter] 导出失败: {e}");
            }
        }

        /// <summary>
        /// CSV 行数据
        /// </summary>
        private class CSVRow
        {
            public string PrefabName;
            public string NodeName;
            public int DuplicateCount;
            public string NodePath;
        }

        #region Resource Detector Export

        public static void ExportSummary(string path, ScanResult result)
        {
            StringBuilder sb = new StringBuilder();
            // BOM for Excel to recognize UTF-8
            sb.Append("\uFEFF"); 
            sb.AppendLine("资源路径,资源名称,文件大小(KB),资源类型,使用状态,引用次数,引用位置列表");

            foreach (var res in result.Resources)
            {
                string usage = res.IsUsed ? "已使用" : "未使用";
                string sizeKB = (res.FileSize / 1024f).ToString("F1");
                string refCount = res.References.Count.ToString();
                
                List<string> refPaths = new List<string>();
                foreach(var r in res.References) refPaths.Add(r.ReferrerPath);
                string refList = string.Join(";", refPaths);
                
                // Escape quotes for CSV
                refList = refList.Replace("\"", "\"\"");

                sb.AppendLine($"\"{res.AssetPath}\",\"{res.AssetName}\",{sizeKB},{res.AssetType},{usage},{refCount},\"{refList}\"");
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        public static void ExportDetailed(string path, ScanResult result)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("\uFEFF");
            sb.AppendLine("资源路径,资源名称,文件大小(KB),资源类型,使用状态,引用类型,引用资源路径,引用资源类型");

            foreach (var res in result.Resources)
            {
                string usage = res.IsUsed ? "已使用" : "未使用";
                string sizeKB = (res.FileSize / 1024f).ToString("F1");

                if (!res.IsUsed)
                {
                    sb.AppendLine($"\"{res.AssetPath}\",\"{res.AssetName}\",{sizeKB},{res.AssetType},{usage},,,");
                }
                else
                {
                    foreach (var refer in res.References)
                    {
                         // ComponentType is often unavailable in basic dependency scan, use ReferrerType or static string
                         string compType = string.IsNullOrEmpty(refer.ComponentType) ? "" : refer.ComponentType;
                         sb.AppendLine($"\"{res.AssetPath}\",\"{res.AssetName}\",{sizeKB},{res.AssetType},{usage},\"{compType}\",\"{refer.ReferrerPath}\",\"{refer.ReferrerType}\"");
                    }
                }
            }
             File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        public static void ExportFolderSummary(string path, ScanResult result)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("\uFEFF");
            sb.AppendLine("文件夹路径,总资源数,已使用数量,未使用数量,使用率(%),总大小(MB),未使用大小(MB)");

            foreach (var stat in result.FolderStats)
            {
                float usageRate = stat.TotalCount > 0 ? (float)stat.UsedCount / stat.TotalCount * 100f : 0f;
                float totalMB = stat.TotalSize / 1024f / 1024f;
                float unusedMB = stat.UnusedSize / 1024f / 1024f;

                sb.AppendLine($"\"{stat.FolderPath}\",{stat.TotalCount},{stat.UsedCount},{stat.UnusedCount},{usageRate:F1},{totalMB:F2},{unusedMB:F2}");
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        public static void ExportReport(string path, ScanResult result)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("\uFEFF");
            sb.AppendLine("统计项目,数值");
            
            sb.AppendLine($"扫描时间,{result.ScanTime.ToString("yyyy-MM-dd HH:mm:ss")}");
            sb.AppendLine($"总资源数,{result.Overall.TotalCount}");
            sb.AppendLine($"已使用资源,{result.Overall.UsedCount}");
            sb.AppendLine($"未使用资源,{result.Overall.UnusedCount}");
            
            float usageRate = result.Overall.TotalCount > 0 ? (float)result.Overall.UsedCount / result.Overall.TotalCount * 100f : 0f;
            sb.AppendLine($"使用率,{usageRate:F1}%");
            
            float totalMB = result.Overall.TotalSize / 1024f / 1024f;
            float unusedMB = result.Overall.UnusedSize / 1024f / 1024f;
            sb.AppendLine($"总文件大小(MB),{totalMB:F2}");
            sb.AppendLine($"未使用资源大小(MB),{unusedMB:F2}");
            sb.AppendLine($"可释放空间(MB),{unusedMB:F2}");

            // Count references by type
            int prefabRefs = 0;
            int sceneRefs = 0;
            int matRefs = 0;
            int animRefs = 0;
            int otherRefs = 0;

            foreach(var res in result.Resources)
            {
                foreach(var refer in res.References)
                {
                    if (refer.ReferrerType == "Prefab") prefabRefs++;
                    else if (refer.ReferrerType == "Scene") sceneRefs++;
                    else if (refer.ReferrerType == "Material") matRefs++;
                    else if (refer.ReferrerType == "Animation" || refer.ReferrerType == "Animator") animRefs++;
                    else otherRefs++;
                }
            }

            sb.AppendLine($"预制体引用数,{prefabRefs}");
            sb.AppendLine($"场景引用数,{sceneRefs}");
            sb.AppendLine($"材质引用数,{matRefs}");
            sb.AppendLine($"动画引用数,{animRefs}");
            sb.AppendLine($"其他引用数,{otherRefs}");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        #endregion
    }
}
