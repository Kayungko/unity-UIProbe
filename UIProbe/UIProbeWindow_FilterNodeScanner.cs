using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UIProbe
{
    internal sealed partial class FilterNodeScannerModule
    {
        private const string FilterNodeReplacementPrefabPath = "Assets/UI/Prefabs/UI_CommonTemplate/Common_Dropdown_Filter_Universal.prefab";

        private string filterNodeScanRoot = "Assets/UI/Prefabs";
        private string filterNodeKeyword = "Filter";
        private string filterNodeResultFilter = "";
        private bool filterNodeCaseSensitive = false;
        private bool filterNodeExcludeReplacementPrefab = true;
        private bool filterNodeIgnoreTabDropdown = true;
        private Vector2 filterNodeScrollPosition;
        private List<FilterNodeHit> filterNodeHits = new List<FilterNodeHit>();
        private int filterNodeLastScannedPrefabCount = 0;
        private int filterNodeLastMatchedPrefabCount = 0;
        private int filterNodeIgnoredTabDropdownCount = 0;
        private int filterNodeIgnoredReplacementPrefabCount = 0;
        private string filterNodeLastScanTime = "";

        private class FilterNodeHit
        {
            public string PrefabName;
            public string PrefabPath;
            public string NodeName;
            public string NodePath;
            public string SourcePrefabPath;
            public string SourcePrefabName;
            public string ComponentNames;
            public int FoldedMatchCount;
            public string FoldedMatchPaths;
        }

        private void DrawFilterNodeScannerTab()
        {
            EditorGUILayout.LabelField("Filter节点排查 (Temporary)", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.HelpBox(
                $"用于排查所有预制体内名称包含指定关键字、且尚未使用目标模板的筛选模块根节点，评估后续替换为:\n{FilterNodeReplacementPrefabPath}\n同一层级下如果父子节点都包含关键字，只导出最外层父级节点。默认忽略页签下拉类 Filter。",
                MessageType.Info);

            DrawFilterNodeScannerSettings();
            EditorGUILayout.Space(8);
            DrawFilterNodeScannerControls();
            EditorGUILayout.Space(8);
            DrawFilterNodeScannerResults();
        }

        private void DrawFilterNodeScannerSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("扫描设置", EditorStyles.boldLabel);

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("扫描根目录:", GUILayout.Width(80));
            filterNodeScanRoot = EditorGUILayout.TextField(filterNodeScanRoot);
            if (GUILayout.Button("选择", GUILayout.Width(50)))
            {
                string path = EditorUtility.OpenFolderPanel("选择预制体扫描根目录", string.IsNullOrEmpty(filterNodeScanRoot) ? "Assets" : filterNodeScanRoot, "");
                if (!string.IsNullOrEmpty(path))
                {
                    string relativePath = FileUtil.GetProjectRelativePath(path);
                    if (!string.IsNullOrEmpty(relativePath))
                    {
                        filterNodeScanRoot = relativePath.Replace("\\", "/");
                    }
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("使用预制体索引根目录", EditorStyles.miniButton))
            {
                filterNodeScanRoot = string.IsNullOrEmpty(indexRootPath) ? "Assets" : indexRootPath;
            }
            if (GUILayout.Button("Assets/UI/Prefabs", EditorStyles.miniButton))
            {
                filterNodeScanRoot = "Assets/UI/Prefabs";
            }
            if (GUILayout.Button("Assets", EditorStyles.miniButton))
            {
                filterNodeScanRoot = "Assets";
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("节点关键字:", GUILayout.Width(80));
            filterNodeKeyword = EditorGUILayout.TextField(filterNodeKeyword, GUILayout.Width(180));
            filterNodeCaseSensitive = EditorGUILayout.ToggleLeft("大小写敏感", filterNodeCaseSensitive, GUILayout.Width(90));
            filterNodeExcludeReplacementPrefab = EditorGUILayout.ToggleLeft("忽略已用目标模板", filterNodeExcludeReplacementPrefab, GUILayout.Width(130));
            filterNodeIgnoreTabDropdown = EditorGUILayout.ToggleLeft("忽略页签下拉", filterNodeIgnoreTabDropdown, GUILayout.Width(110));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawFilterNodeScannerControls()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("开始扫描", GUILayout.Height(28), GUILayout.Width(120)))
            {
                ScanFilterNodes();
            }

            EditorGUI.BeginDisabledGroup(filterNodeHits.Count == 0);
            if (GUILayout.Button("导出CSV", GUILayout.Height(28), GUILayout.Width(90)))
            {
                ExportFilterNodeHitsToCSV();
            }
            if (GUILayout.Button("清空结果", GUILayout.Height(28), GUILayout.Width(90)))
            {
                filterNodeHits.Clear();
                filterNodeLastScannedPrefabCount = 0;
                filterNodeLastMatchedPrefabCount = 0;
                filterNodeIgnoredTabDropdownCount = 0;
                filterNodeIgnoredReplacementPrefabCount = 0;
                filterNodeLastScanTime = "";
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawFilterNodeScannerResults()
        {
            if (filterNodeLastScannedPrefabCount <= 0 && filterNodeHits.Count == 0)
            {
                EditorGUILayout.HelpBox("尚未扫描。默认会查找节点名包含 Filter 的预制体节点。", MessageType.None);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                $"扫描预制体: {filterNodeLastScannedPrefabCount} | 命中预制体: {filterNodeLastMatchedPrefabCount} | 导出节点: {filterNodeHits.Count} | 忽略已用目标: {filterNodeIgnoredReplacementPrefabCount} | 忽略页签下拉: {filterNodeIgnoredTabDropdownCount} | 时间: {filterNodeLastScanTime}",
                EditorStyles.boldLabel);

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("结果过滤:", GUILayout.Width(70));
            filterNodeResultFilter = EditorGUILayout.TextField(filterNodeResultFilter, EditorStyles.toolbarSearchField);
            if (!string.IsNullOrEmpty(filterNodeResultFilter) && GUILayout.Button("X", EditorStyles.toolbarButton, GUILayout.Width(24)))
            {
                filterNodeResultFilter = "";
                GUI.FocusControl(null);
            }
            GUILayout.EndHorizontal();

            List<FilterNodeHit> visibleHits = GetVisibleFilterNodeHits();
            EditorGUILayout.LabelField($"当前显示: {visibleHits.Count}", EditorStyles.miniLabel);

            filterNodeScrollPosition = EditorGUILayout.BeginScrollView(filterNodeScrollPosition);
            foreach (var group in visibleHits.GroupBy(h => h.PrefabPath))
            {
                DrawFilterNodePrefabGroup(group.Key, group.ToList());
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawFilterNodePrefabGroup(string prefabPath, List<FilterNodeHit> hits)
        {
            FilterNodeHit first = hits[0];

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{first.PrefabName} ({hits.Count})", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("打开", EditorStyles.miniButton, GUILayout.Width(45)))
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab != null)
                {
                    AssetDatabase.OpenAsset(prefab);
                }
            }
            if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(45)))
            {
                PingAsset(prefabPath);
            }
            GUILayout.EndHorizontal();
            EditorGUILayout.LabelField(prefabPath, EditorStyles.miniLabel);

            foreach (FilterNodeHit hit in hits)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(hit.NodePath, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (!string.IsNullOrEmpty(hit.SourcePrefabPath) && GUILayout.Button("源预制体", EditorStyles.miniButton, GUILayout.Width(70)))
                {
                    PingAsset(hit.SourcePrefabPath);
                }
                GUILayout.EndHorizontal();

                EditorGUILayout.LabelField($"节点: {hit.NodeName}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"组件: {hit.ComponentNames}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"折叠命中: {hit.FoldedMatchCount}", EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(hit.SourcePrefabPath))
                {
                    EditorGUILayout.LabelField($"嵌套源: {hit.SourcePrefabPath}", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        private void ScanFilterNodes()
        {
            if (string.IsNullOrEmpty(filterNodeKeyword))
            {
                EditorUtility.DisplayDialog("提示", "请先输入节点关键字。", "确定");
                return;
            }

            string scanRoot = string.IsNullOrEmpty(filterNodeScanRoot) ? "Assets" : filterNodeScanRoot.Trim().Replace("\\", "/");
            if (!AssetDatabase.IsValidFolder(scanRoot))
            {
                EditorUtility.DisplayDialog("提示", $"扫描根目录无效:\n{scanRoot}", "确定");
                return;
            }

            filterNodeHits.Clear();
            filterNodeLastScannedPrefabCount = 0;
            filterNodeLastMatchedPrefabCount = 0;
            filterNodeIgnoredTabDropdownCount = 0;
            filterNodeIgnoredReplacementPrefabCount = 0;

            string[] prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { scanRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Where(p => !filterNodeExcludeReplacementPrefab || p != FilterNodeReplacementPrefabPath)
                .Distinct()
                .OrderBy(p => p)
                .ToArray();

            try
            {
                for (int i = 0; i < prefabPaths.Length; i++)
                {
                    string prefabPath = prefabPaths[i];
                    if (EditorUtility.DisplayCancelableProgressBar(
                        "Filter节点排查",
                        $"扫描 {Path.GetFileNameWithoutExtension(prefabPath)} ({i + 1}/{prefabPaths.Length})",
                        prefabPaths.Length == 0 ? 1f : (float)(i + 1) / prefabPaths.Length))
                    {
                        break;
                    }

                    filterNodeLastScannedPrefabCount++;
                    ScanSinglePrefabForFilterNodes(prefabPath);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            filterNodeLastMatchedPrefabCount = filterNodeHits.Select(h => h.PrefabPath).Distinct().Count();
            filterNodeLastScanTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Debug.Log($"[UIProbe] Filter节点排查完成: 扫描 {filterNodeLastScannedPrefabCount} 个预制体，导出 {filterNodeHits.Count} 个最外层命中节点。");
            Repaint();
        }

        private void ScanSinglePrefabForFilterNodes(string prefabPath)
        {
            GameObject prefabRoot = null;

            try
            {
                prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                if (prefabRoot == null)
                {
                    return;
                }

                Transform[] transforms = prefabRoot.GetComponentsInChildren<Transform>(true);
                Dictionary<Transform, List<Transform>> foldedMatches = new Dictionary<Transform, List<Transform>>();
                foreach (Transform transform in transforms)
                {
                    if (!FilterNodeNameMatches(transform.name))
                    {
                        continue;
                    }

                    Transform exportRoot = GetOutermostMatchingAncestor(transform, prefabRoot.transform);
                    if (!foldedMatches.TryGetValue(exportRoot, out var matches))
                    {
                        matches = new List<Transform>();
                        foldedMatches.Add(exportRoot, matches);
                    }
                    matches.Add(transform);
                }

                foreach (var pair in foldedMatches.OrderBy(p => GetFilterNodeHierarchyPath(p.Key, prefabRoot.transform)))
                {
                    Transform exportRoot = pair.Key;
                    string sourcePrefabPath = GetNestedSourcePrefabPath(exportRoot.gameObject, prefabRoot);

                    if (filterNodeExcludeReplacementPrefab && IsUsingReplacementPrefab(exportRoot, prefabPath, sourcePrefabPath))
                    {
                        filterNodeIgnoredReplacementPrefabCount++;
                        continue;
                    }

                    if (filterNodeIgnoreTabDropdown && IsTabDropdownFilterNode(exportRoot, prefabPath, sourcePrefabPath))
                    {
                        filterNodeIgnoredTabDropdownCount++;
                        continue;
                    }

                    filterNodeHits.Add(new FilterNodeHit
                    {
                        PrefabName = Path.GetFileNameWithoutExtension(prefabPath),
                        PrefabPath = prefabPath,
                        NodeName = exportRoot.name,
                        NodePath = GetFilterNodeHierarchyPath(exportRoot, prefabRoot.transform),
                        SourcePrefabPath = sourcePrefabPath,
                        SourcePrefabName = string.IsNullOrEmpty(sourcePrefabPath) ? "" : Path.GetFileNameWithoutExtension(sourcePrefabPath),
                        ComponentNames = GetComponentNames(exportRoot.gameObject),
                        FoldedMatchCount = pair.Value.Count,
                        FoldedMatchPaths = string.Join(" | ", pair.Value
                            .Select(t => GetFilterNodeHierarchyPath(t, prefabRoot.transform))
                            .Distinct()
                            .ToArray())
                    });
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UIProbe] 扫描预制体失败: {prefabPath}\n{e.Message}");
            }
            finally
            {
                if (prefabRoot != null)
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
        }

        private bool FilterNodeNameMatches(string nodeName)
        {
            StringComparison comparison = filterNodeCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return nodeName != null && nodeName.IndexOf(filterNodeKeyword, comparison) >= 0;
        }

        private Transform GetOutermostMatchingAncestor(Transform transform, Transform prefabRoot)
        {
            Transform outermost = transform;
            Transform current = transform.parent;
            while (current != null)
            {
                if (FilterNodeNameMatches(current.name))
                {
                    outermost = current;
                }

                if (current == prefabRoot)
                {
                    break;
                }

                current = current.parent;
            }

            return outermost;
        }

        private bool IsTabDropdownFilterNode(Transform exportRoot, string prefabPath, string sourcePrefabPath)
        {
            if (ContainsIgnoreCase(prefabPath, "Tab") || ContainsIgnoreCase(sourcePrefabPath, "Tab"))
            {
                return true;
            }

            Transform current = exportRoot;
            while (current != null)
            {
                if (ContainsIgnoreCase(current.name, "Tab"))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private bool IsUsingReplacementPrefab(Transform exportRoot, string prefabPath, string sourcePrefabPath)
        {
            if (SameAssetPath(prefabPath, FilterNodeReplacementPrefabPath) ||
                SameAssetPath(sourcePrefabPath, FilterNodeReplacementPrefabPath))
            {
                return true;
            }

            Transform current = exportRoot;
            while (current != null)
            {
                string nearestInstancePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(current.gameObject);
                if (SameAssetPath(nearestInstancePath, FilterNodeReplacementPrefabPath))
                {
                    return true;
                }

                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(current.gameObject);
                if (source != null && SameAssetPath(AssetDatabase.GetAssetPath(source), FilterNodeReplacementPrefabPath))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private bool SameAssetPath(string pathA, string pathB)
        {
            if (string.IsNullOrEmpty(pathA) || string.IsNullOrEmpty(pathB))
            {
                return false;
            }

            return string.Equals(
                pathA.Replace("\\", "/"),
                pathB.Replace("\\", "/"),
                StringComparison.OrdinalIgnoreCase);
        }

        private string GetFilterNodeHierarchyPath(Transform target, Transform root)
        {
            if (target == root)
            {
                return target.name;
            }

            List<string> path = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }

            if (current == root)
            {
                path.Insert(0, root.name);
            }

            return string.Join("/", path);
        }

        private string GetNestedSourcePrefabPath(GameObject node, GameObject prefabRoot)
        {
            GameObject nearestRoot = PrefabUtility.GetNearestPrefabInstanceRoot(node);
            if (nearestRoot == null || nearestRoot == prefabRoot)
            {
                return "";
            }

            string sourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(nearestRoot);
            if (!string.IsNullOrEmpty(sourcePath) && sourcePath != AssetDatabase.GetAssetPath(prefabRoot))
            {
                return sourcePath;
            }

            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(nearestRoot);
            return source == null ? "" : AssetDatabase.GetAssetPath(source);
        }

        private string GetComponentNames(GameObject node)
        {
            Component[] components = node.GetComponents<Component>();
            return string.Join(" | ", components.Select(c => c == null ? "MissingScript" : c.GetType().Name).ToArray());
        }

        private List<FilterNodeHit> GetVisibleFilterNodeHits()
        {
            if (string.IsNullOrEmpty(filterNodeResultFilter))
            {
                return filterNodeHits;
            }

            string query = filterNodeResultFilter.Trim();
            return filterNodeHits.Where(hit =>
                ContainsIgnoreCase(hit.PrefabName, query) ||
                ContainsIgnoreCase(hit.PrefabPath, query) ||
                ContainsIgnoreCase(hit.NodeName, query) ||
                ContainsIgnoreCase(hit.NodePath, query) ||
                ContainsIgnoreCase(hit.SourcePrefabPath, query) ||
                ContainsIgnoreCase(hit.ComponentNames, query) ||
                ContainsIgnoreCase(hit.FoldedMatchPaths, query)).ToList();
        }

        private bool ContainsIgnoreCase(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void PingAsset(string assetPath)
        {
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
        }

        private void ExportFilterNodeHitsToCSV()
        {
            if (filterNodeHits == null || filterNodeHits.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "没有可导出的 Filter 节点结果。", "确定");
                return;
            }

            string defaultName = $"FilterNodeScan_{filterNodeKeyword}";
            string savePath = CSVExporter.GetSaveFilePath(SanitizeFilterNodeFileName(defaultName));
            if (string.IsNullOrEmpty(savePath))
            {
                return;
            }

            try
            {
                StringBuilder csv = new StringBuilder();
                csv.Append("\uFEFF");
                csv.AppendLine("序号,替换目标预制体,扫描根目录,关键字,预制体名称,预制体路径,导出节点名称,导出节点路径,折叠命中数量,折叠命中节点路径,嵌套源预制体名称,嵌套源预制体路径,组件列表");

                for (int i = 0; i < filterNodeHits.Count; i++)
                {
                    FilterNodeHit hit = filterNodeHits[i];
                    csv.AppendLine(string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12}",
                        i + 1,
                        EscapeFilterNodeCsv(FilterNodeReplacementPrefabPath),
                        EscapeFilterNodeCsv(filterNodeScanRoot),
                        EscapeFilterNodeCsv(filterNodeKeyword),
                        EscapeFilterNodeCsv(hit.PrefabName),
                        EscapeFilterNodeCsv(hit.PrefabPath),
                        EscapeFilterNodeCsv(hit.NodeName),
                        EscapeFilterNodeCsv(hit.NodePath),
                        hit.FoldedMatchCount,
                        EscapeFilterNodeCsv(hit.FoldedMatchPaths),
                        EscapeFilterNodeCsv(hit.SourcePrefabName),
                        EscapeFilterNodeCsv(hit.SourcePrefabPath),
                        EscapeFilterNodeCsv(hit.ComponentNames)));
                }

                string directory = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(savePath, csv.ToString(), Encoding.UTF8);
                EditorUtility.DisplayDialog("导出成功", $"Filter 节点结果已导出:\n{savePath}\n\n共 {filterNodeHits.Count} 条。", "确定");
                EditorUtility.RevealInFinder(savePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[UIProbe] 导出 Filter 节点 CSV 失败: {e}");
                EditorUtility.DisplayDialog("导出失败", $"导出 Filter 节点 CSV 时发生错误:\n{e.Message}", "确定");
            }
        }

        private string SanitizeFilterNodeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return "FilterNodeScan";
            }

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }

            return fileName;
        }

        private string EscapeFilterNodeCsv(string field)
        {
            if (string.IsNullOrEmpty(field))
            {
                return "";
            }

            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            {
                field = field.Replace("\"", "\"\"");
                return $"\"{field}\"";
            }

            return field;
        }
    }
}
