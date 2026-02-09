using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UIProbe
{
    public partial class UIProbeWindow
    {
        private ResourceScanner resourceScanner = new ResourceScanner();
        private ScanResult lastScanResult;
        private ScanOptions scanOptions = new ScanOptions();
        
        private bool isScanningResource = false;
        private Vector2 resourceListScrollPos;
        private string resourceSearchFilter = "";
        
        // UI State
        private bool foldoutTargetFolders = true;
        private bool foldoutScanOptions = true;
        private bool foldoutResults = true;
        
        private void DrawResourceDetectorTab()
        {
            GUILayout.Space(10);
            GUILayout.Label("资源使用检测", EditorStyles.boldLabel);
            GUILayout.Space(5);

            DrawScanSettings();
            
            GUILayout.Space(10);
            DrawScanControls();
            
            GUILayout.Space(10);
            DrawScanResults();
        }

        private void DrawScanSettings()
        {
            foldoutTargetFolders = EditorGUILayout.Foldout(foldoutTargetFolders, "扫描目标", true);
            if (foldoutTargetFolders)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                
                // 目标文件夹
                GUILayout.Label("检测文件夹:", EditorStyles.boldLabel);
                for (int i = 0; i < scanOptions.TargetFolders.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(scanOptions.TargetFolders[i]);
                    if (GUILayout.Button("X", GUILayout.Width(20)))
                    {
                        scanOptions.TargetFolders.RemoveAt(i);
                        break;
                    }
                    GUILayout.EndHorizontal();
                }

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("添加文件夹"))
                {
                    string path = EditorUtility.OpenFolderPanel("选择文件夹", "Assets", "");
                    if (!string.IsNullOrEmpty(path))
                    {
                        string relativePath = FileUtil.GetProjectRelativePath(path);
                        if (!string.IsNullOrEmpty(relativePath) && !scanOptions.TargetFolders.Contains(relativePath))
                        {
                            scanOptions.TargetFolders.Add(relativePath);
                        }
                    }
                }
                if (GUILayout.Button("清空"))
                {
                    scanOptions.TargetFolders.Clear();
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(5);

                // 排除文件夹
                GUILayout.Label("排除文件夹:", EditorStyles.boldLabel);
                for (int i = 0; i < scanOptions.ExcludeFolders.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(scanOptions.ExcludeFolders[i]);
                    if (GUILayout.Button("X", GUILayout.Width(20)))
                    {
                        scanOptions.ExcludeFolders.RemoveAt(i);
                        break;
                    }
                    GUILayout.EndHorizontal();
                }
                
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("添加排除"))
                {
                    string path = EditorUtility.OpenFolderPanel("选择排除文件夹", "Assets", "");
                    if (!string.IsNullOrEmpty(path))
                    {
                        string relativePath = FileUtil.GetProjectRelativePath(path);
                        if (!string.IsNullOrEmpty(relativePath) && !scanOptions.ExcludeFolders.Contains(relativePath))
                        {
                            scanOptions.ExcludeFolders.Add(relativePath);
                        }
                    }
                }
                if (GUILayout.Button("清空"))
                {
                    scanOptions.ExcludeFolders.Clear();
                }
                GUILayout.EndHorizontal();
                
                GUILayout.EndVertical();
            }

            GUILayout.Space(5);

            foldoutScanOptions = EditorGUILayout.Foldout(foldoutScanOptions, "扫描选项", true);
            if (foldoutScanOptions)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                
                GUILayout.Label("资源类型:", EditorStyles.boldLabel);
                GUILayout.BeginHorizontal();
                scanOptions.IncludeSprites = EditorGUILayout.ToggleLeft("Sprite", scanOptions.IncludeSprites, GUILayout.Width(100));
                scanOptions.IncludeTextures = EditorGUILayout.ToggleLeft("Texture2D", scanOptions.IncludeTextures, GUILayout.Width(100));
                GUILayout.EndHorizontal();
                
                GUILayout.Space(5);
                
                GUILayout.Label("检测范围:", EditorStyles.boldLabel);
                GUILayout.BeginHorizontal();
                scanOptions.CheckPrefabs = EditorGUILayout.ToggleLeft("Prefabs", scanOptions.CheckPrefabs, GUILayout.Width(80));
                scanOptions.CheckScenes = EditorGUILayout.ToggleLeft("Scenes", scanOptions.CheckScenes, GUILayout.Width(80));
                scanOptions.CheckMaterials = EditorGUILayout.ToggleLeft("Materials", scanOptions.CheckMaterials, GUILayout.Width(80));
                GUILayout.EndHorizontal();
                
                GUILayout.BeginHorizontal();
                scanOptions.CheckAnimations = EditorGUILayout.ToggleLeft("Animations", scanOptions.CheckAnimations, GUILayout.Width(80));
                scanOptions.CheckParticles = EditorGUILayout.ToggleLeft("Particles", scanOptions.CheckParticles, GUILayout.Width(80));
                GUILayout.EndHorizontal();
                
                GUILayout.Space(5);
                scanOptions.UseCache = EditorGUILayout.ToggleLeft("使用缓存加速 (增量扫描)", scanOptions.UseCache);
                
                GUILayout.EndVertical();
            }
        }

        private void DrawScanControls()
        {
            if (isScanningResource)
            {
                EditorGUI.ProgressBar(GUILayoutUtility.GetRect(18, 18), resourceScanner.Progress, "扫描中...");
                if (GUILayout.Button("停止扫描"))
                {
                    // TODO: Implement cancel token
                    isScanningResource = false;
                }
            }
            else
            {
                if (GUILayout.Button("开始扫描", GUILayout.Height(30)))
                {
                    StartResourceScan();
                }
            }
        }

        private async void StartResourceScan()
        {
            if (scanOptions.TargetFolders.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先添加目标文件夹", "确定");
                return;
            }

            isScanningResource = true;
            try
            {
                lastScanResult = await resourceScanner.ScanAsync(scanOptions, new Progress<float>(p => 
                {
                    Repaint();
                }));
            }
            catch (Exception e)
            {
                Debug.LogError($"扫描失败: {e}");
            }
            finally
            {
                isScanningResource = false;
                Repaint();
            }
        }

        private void DrawScanResults()
        {
            if (lastScanResult == null) return;

            foldoutResults = EditorGUILayout.Foldout(foldoutResults, "扫描结果", true);
            if (foldoutResults)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                
                // 统计信息
                EditorGUILayout.LabelField($"总资源数: {lastScanResult.Overall.TotalCount}");
                EditorGUILayout.LabelField($"已使用: {lastScanResult.Overall.UsedCount} ({(lastScanResult.Overall.TotalCount > 0 ? (float)lastScanResult.Overall.UsedCount/lastScanResult.Overall.TotalCount * 100 : 0):F1}%)");
                EditorGUILayout.LabelField($"未使用: {lastScanResult.Overall.UnusedCount}");
                EditorGUILayout.LabelField($"可释放空间: {lastScanResult.Overall.UnusedSize / 1024f / 1024f:F2} MB");

                GUILayout.Space(10);
                
                // 导出按钮
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("导出简洁版 CSV"))
                {
                    ExportCSV(CSVExporter.ExportSummary, "summary");
                }
                if (GUILayout.Button("导出详细版 CSV"))
                {
                    ExportCSV(CSVExporter.ExportDetailed, "detailed");
                }
                GUILayout.EndHorizontal();
                
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("导出文件夹统计"))
                {
                    ExportCSV(CSVExporter.ExportFolderSummary, "folder_summary");
                }
                if (GUILayout.Button("导出完整报告"))
                {
                    ExportCSV(CSVExporter.ExportReport, "report");
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(10);
                
                // 列表过滤器
                resourceSearchFilter = EditorGUILayout.TextField("搜索:", resourceSearchFilter);

                // 资源列表
                resourceListScrollPos = GUILayout.BeginScrollView(resourceListScrollPos);
                
                foreach (var res in lastScanResult.Resources)
                {
                    if (!string.IsNullOrEmpty(resourceSearchFilter) && !res.AssetName.Contains(resourceSearchFilter))
                        continue;

                    GUILayout.BeginHorizontal("box");
                    
                    // 简单图标
                    GUILayout.Label(res.IsUsed ? "OK" : "NO", GUILayout.Width(25));
                    
                    GUILayout.BeginVertical();
                    GUILayout.Label(res.AssetName, EditorStyles.boldLabel);
                    GUILayout.Label(res.AssetPath, EditorStyles.miniLabel);
                    GUILayout.EndVertical();
                    
                    if (res.IsUsed)
                    {
                         if (GUILayout.Button($"{res.References.Count} 引用", GUILayout.Width(60)))
                         {
                             Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(res.AssetPath);
                             EditorGUIUtility.PingObject(Selection.activeObject);
                             
                             // Print references to console
                             Debug.Log($"References for {res.AssetName}:\n" + string.Join("\n", res.References.Select(r => $"{r.ReferrerPath} ({r.ReferrerType})")));
                         }
                    }
                    else
                    {
                        GUILayout.Label("未使用", GUILayout.Width(60));
                    }
                    
                    GUILayout.EndHorizontal();
                }

                GUILayout.EndScrollView();
                GUILayout.EndVertical();
            }
        }

        private void ExportCSV(Action<string, ScanResult> exportFunc, string nameSuffix)
        {
            string defaultName = $"UIProbe_{nameSuffix}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            string path = EditorUtility.SaveFilePanel("导出 CSV", UIProbeStorage.GetCSVExportPath(), defaultName, "csv");
            if (!string.IsNullOrEmpty(path))
            {
                exportFunc(path, lastScanResult);
                Debug.Log($"导出成功: {path}");
                System.Diagnostics.Process.Start(path); // Open file
            }
        }
    }
}
