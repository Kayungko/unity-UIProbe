using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace UIProbe
{
    partial class UIProbeWindow
    {
        // 资源引用标签页状态
        private string assetSearchQuery = "";
        private Vector2 assetReferencesScrollPos;
        private List<PrefabReferenceInfo> assetSearchResults = new List<PrefabReferenceInfo>();
        private int assetSearchIndexVersion = -1;
        private AssetReferenceType selectedAssetType = AssetReferenceType.Image; // 默认搜索图片
        
        private class PrefabReferenceInfo
        {
            public string PrefabName;
            public string PrefabPath;
            public List<AssetReference> MatchingReferences = new List<AssetReference>();
        }
        
        /// <summary>
        /// 绘制资源引用标签页
        /// </summary>
        private void DrawAssetReferencesTab()
        {
            EditorGUILayout.LabelField("资源引用 (Asset References)", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            // Play模式提示
            if (Application.isPlaying && !isIndexBuilt)
            {
                EditorGUILayout.HelpBox("Play模式下也可以使用资源引用查找！\n如果索引未加载，请先退出Play模式，在「预制体索引」标签页点击「刷新」建立索引即可。", MessageType.Info);
                return;
            }
            
            if (!isIndexBuilt)
            {
                EditorGUILayout.HelpBox("请先在「预制体索引」标签页点击「刷新」按钮建立索引。", MessageType.Info);
                return;
            }
            
            // 资源类型选择
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("资源类型:", GUILayout.Width(70));
            
            EnsureAssetReferenceResultsFresh();

            var newType = (AssetReferenceType)EditorGUILayout.EnumPopup(selectedAssetType, GUILayout.Width(150));
            if (newType != selectedAssetType)
            {
                selectedAssetType = newType;
                if (!string.IsNullOrEmpty(assetSearchQuery))
                {
                    SearchAssetReferences();
                }
            }
            
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            // 搜索框
            GUILayout.BeginHorizontal();
            string searchLabel = GetSearchLabelByType(selectedAssetType);
            EditorGUILayout.LabelField(searchLabel, GUILayout.Width(100));
            
            EditorGUI.BeginChangeCheck();
            assetSearchQuery = EditorGUILayout.TextField(assetSearchQuery, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck() && !string.IsNullOrEmpty(assetSearchQuery))
            {
                SearchAssetReferences();
            }
            
            if (GUILayout.Button("🔍 搜索", GUILayout.Width(60)))
            {
                SearchAssetReferences();
            }
            
            if (!string.IsNullOrEmpty(assetSearchQuery))
            {
                if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(25)))
                {
                    assetSearchQuery = "";
                    assetSearchResults.Clear();
                }
            }
            GUILayout.EndHorizontal();
            
            EditorGUILayout.Space(10);
            
            // 搜索结果
            if (string.IsNullOrEmpty(assetSearchQuery))
            {
                string helpText = GetHelpTextByType(selectedAssetType);
                EditorGUILayout.HelpBox(helpText, MessageType.None);
            }
            else if (assetSearchResults.Count == 0)
            {
                EditorGUILayout.HelpBox($"未找到引用 \"{assetSearchQuery}\" 的预制体。", MessageType.Warning);
            }
            else
            {
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"找到 {assetSearchResults.Count} 个预制体引用该资源:", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("导出CSV", GUILayout.Width(80)))
                {
                    ExportAssetReferenceResultsToCSV();
                }
                GUILayout.EndHorizontal();
                EditorGUILayout.Space(5);
                
                assetReferencesScrollPos = EditorGUILayout.BeginScrollView(assetReferencesScrollPos);
                
                foreach (var result in assetSearchResults)
                {
                    DrawPrefabReferenceCard(result);
                }
                
                EditorGUILayout.EndScrollView();
            }
        }
        
        /// <summary>
        /// 根据资源类型获取搜索标签
        /// </summary>
        private string GetSearchLabelByType(AssetReferenceType type)
        {
            switch (type)
            {
                case AssetReferenceType.Image:
                case AssetReferenceType.RawImage:
                    return "搜索图片资源:";
                case AssetReferenceType.Prefab:
                    return "搜索预制体:";
                case AssetReferenceType.Material:
                    return "搜索纹理资源:";
                case AssetReferenceType.Font:
                    return "搜索字体:";
                default:
                    return "搜索资源:";
            }
        }
        
        /// <summary>
        /// 根据资源类型获取帮助文本
        /// </summary>
        private string GetHelpTextByType(AssetReferenceType type)
        {
            switch (type)
            {
                case AssetReferenceType.Image:
                case AssetReferenceType.RawImage:
                    return "请输入图片文件名进行搜索。\n\n搜索范围：\n1. UI组件（Image/RawImage）\n2. 材质球纹理引用\n\n例如: \"icon_gold\" 或 \"wood_texture\"";
                case AssetReferenceType.Prefab:
                    return "请输入预制体文件名或路径进行搜索。\n\n例如: \"Button.prefab\" 或 \"UI/Prefabs/\"";
                case AssetReferenceType.Material:
                    return "可搜索两种类型：\n1. 材质球文件（输入.mat文件名，如\"Glass.mat\"）\n2. 纹理图片（输入纹理名，查找哪些材质球使用了该纹理）";
                case AssetReferenceType.Font:
                    return "请输入字体文件名或路径进行搜索。\n\n例如: \"Arial.ttf\" 或 \"Fonts/\"";
                default:
                    return "请输入资源文件名或路径进行搜索。";
            }
        }
        
        /// <summary>
        /// 搜索资源引用
        /// </summary>
        private void OnPrefabIndexChangedForAssetReferences()
        {
            assetSearchIndexVersion = -1;
            EnsureAssetReferenceResultsFresh();
        }

        private void EnsureAssetReferenceResultsFresh()
        {
            if (assetSearchIndexVersion == prefabIndexVersion)
                return;

            if (string.IsNullOrEmpty(assetSearchQuery))
            {
                assetSearchResults.Clear();
                assetSearchIndexVersion = prefabIndexVersion;
                return;
            }

            SearchAssetReferences();
        }

        private void SearchAssetReferences()
        {
            assetSearchResults.Clear();
            assetSearchIndexVersion = prefabIndexVersion;
            
            if (string.IsNullOrEmpty(assetSearchQuery))
                return;
            
            string query = assetSearchQuery.ToLower();
            
            // 遍历所有预制体
            foreach (var prefab in allPrefabs)
            {
                var matchingRefs = new List<AssetReference>();
                
                // 根据选择的资源类型过滤引用
                foreach (var assetRef in prefab.AssetReferences)
                {
                    // 类型匹配检查
                    bool typeMatches = false;
                    if (selectedAssetType == AssetReferenceType.Image)
                    {
                        // 搜索图片时同时包含 Image、RawImage 和 Material（材质球中的纹理）
                        typeMatches = (assetRef.Type == AssetReferenceType.Image || 
                                      assetRef.Type == AssetReferenceType.RawImage ||
                                      assetRef.Type == AssetReferenceType.Material);
                    }
                    else
                    {
                        typeMatches = (assetRef.Type == selectedAssetType);
                    }
                    
                    if (!typeMatches)
                        continue;
                    
                    // 检查资源路径或文件名是否匹配
                    if (assetRef.AssetPath.ToLower().Contains(query) || 
                        assetRef.AssetName.ToLower().Contains(query))
                    {
                        matchingRefs.Add(assetRef);
                    }
                }
                
                if (matchingRefs.Count > 0)
                {
                    var info = new PrefabReferenceInfo
                    {
                        PrefabName = prefab.Name,
                        PrefabPath = prefab.Path,
                        MatchingReferences = matchingRefs
                    };
                    
                    assetSearchResults.Add(info);
                }
            }
            
            // 按预制体名称排序
            assetSearchResults = assetSearchResults.OrderBy(r => r.PrefabName).ToList();
        }
        
        /// <summary>
        /// 绘制预制体引用卡片
        /// </summary>
        private void DrawPrefabReferenceCard(PrefabReferenceInfo info)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            
            // 预制体名称和路径
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"📦 {info.PrefabName}", EditorStyles.boldLabel, GUILayout.MinWidth(80), GUILayout.ExpandWidth(true));
            
            if (GUILayout.Button("打开", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                var obj = AssetDatabase.LoadAssetAtPath<GameObject>(info.PrefabPath);
                if (obj != null) AssetDatabase.OpenAsset(obj);
            }
            
            if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                var obj = AssetDatabase.LoadAssetAtPath<GameObject>(info.PrefabPath);
                if (obj != null) EditorGUIUtility.PingObject(obj);
            }
            
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            // 路径
            EditorGUILayout.LabelField(info.PrefabPath, EditorStyles.miniLabel);
            
            // 引用位置
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField($"引用位置 ({info.MatchingReferences.Count} 处):", EditorStyles.miniLabel);
            
            foreach (var reference in info.MatchingReferences)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.BeginHorizontal();
                GUILayout.Space(10);
                
                // 资源类型图标
                string icon = GetAssetTypeIcon(reference.Type);
                EditorGUILayout.LabelField(icon, GUILayout.Width(20));
                
                // 节点路径
                GUIStyle nodePathStyle = new GUIStyle(EditorStyles.linkLabel);
                nodePathStyle.wordWrap = true;
                nodePathStyle.normal.textColor = new Color(0.25f, 0.55f, 1f);
                nodePathStyle.hover.textColor = new Color(0.45f, 0.7f, 1f);
                nodePathStyle.active.textColor = new Color(0.2f, 0.45f, 0.9f);
                float nodePathWidth = Mathf.Max(120f, EditorGUIUtility.currentViewWidth - 100f);
                float nodePathHeight = Mathf.Max(EditorGUIUtility.singleLineHeight, nodePathStyle.CalcHeight(new GUIContent(reference.NodePath), nodePathWidth));
                if (GUILayout.Button(reference.NodePath, nodePathStyle, GUILayout.MinWidth(120), GUILayout.Height(nodePathHeight), GUILayout.ExpandWidth(true)))
                {
                    OpenPrefabAndSelectNode(info.PrefabPath, reference.NodePath);
                }

                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Space(40);
                EditorGUILayout.LabelField("资源:", EditorStyles.miniLabel, GUILayout.Width(35));
                
                // 资源名称（可点击）
                if (GUILayout.Button(reference.AssetName, EditorStyles.linkLabel, GUILayout.MinWidth(60), GUILayout.ExpandWidth(true)))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(reference.AssetPath);
                    if (asset != null)
                    {
                        EditorGUIUtility.PingObject(asset);
                        // 如果是预制体，选中它
                        if (reference.Type == AssetReferenceType.Prefab)
                        {
                            Selection.activeObject = asset;
                        }
                    }
                }
                
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                
                // 额外信息（材质球等）- 单独一行显示
                if (!string.IsNullOrEmpty(reference.ExtraInfo))
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(30);
                    
                    GUIStyle infoStyle = new GUIStyle(EditorStyles.miniLabel);
                    infoStyle.normal.textColor = IsImageReference(reference.Type)
                        ? new Color(1f, 0.82f, 0.2f)
                        : new Color(0.5f, 0.7f, 1f);
                    
                    EditorGUILayout.LabelField($"↳ {reference.ExtraInfo}", infoStyle);
                    GUILayout.EndHorizontal();
                }
                
                GUILayout.EndVertical();
                GUILayout.Space(2);
            }
            
            GUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }

        /// <summary>
        /// 导出当前资源引用搜索结果到 CSV
        /// </summary>
        private bool IsImageReference(AssetReferenceType type)
        {
            return type == AssetReferenceType.Image || type == AssetReferenceType.RawImage;
        }

        private void OpenPrefabAndSelectNode(string prefabPath, string nodePath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                return;

            AssetDatabase.OpenAsset(prefab);
            EditorApplication.delayCall += () => SelectNodeInOpenedPrefab(prefabPath, nodePath);
        }

        private void SelectNodeInOpenedPrefab(string prefabPath, string nodePath)
        {
            GameObject root = null;

            object prefabStage = GetCurrentPrefabStage();
            if (prefabStage != null && GetPrefabStageAssetPath(prefabStage) == prefabPath)
            {
                root = GetPrefabStageRoot(prefabStage);
            }

            if (root == null)
            {
                root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            }

            if (root == null)
                return;

            Transform target = FindNodeByPath(root.transform, nodePath);
            if (target == null)
            {
                EditorGUIUtility.PingObject(root);
                return;
            }

            Selection.activeObject = target.gameObject;
            EditorGUIUtility.PingObject(target.gameObject);
        }

        private object GetCurrentPrefabStage()
        {
            Type utilityType = Type.GetType("UnityEditor.SceneManagement.PrefabStageUtility, UnityEditor")
                ?? Type.GetType("UnityEditor.Experimental.SceneManagement.PrefabStageUtility, UnityEditor");
            if (utilityType == null)
                return null;

            var method = utilityType.GetMethod("GetCurrentPrefabStage", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            return method != null ? method.Invoke(null, null) : null;
        }

        private string GetPrefabStageAssetPath(object prefabStage)
        {
            var property = prefabStage.GetType().GetProperty("assetPath");
            return property != null ? property.GetValue(prefabStage, null) as string : null;
        }

        private GameObject GetPrefabStageRoot(object prefabStage)
        {
            var property = prefabStage.GetType().GetProperty("prefabContentsRoot");
            return property != null ? property.GetValue(prefabStage, null) as GameObject : null;
        }

        private Transform FindNodeByPath(Transform root, string nodePath)
        {
            if (root == null || string.IsNullOrEmpty(nodePath))
                return root;

            string[] parts = nodePath.Replace("\\", "/").Split('/');
            int startIndex = parts.Length > 0 && parts[0] == root.name ? 1 : 0;

            Transform current = root;
            for (int i = startIndex; i < parts.Length; i++)
            {
                if (string.IsNullOrEmpty(parts[i]))
                    continue;

                Transform next = null;
                for (int childIndex = 0; childIndex < current.childCount; childIndex++)
                {
                    Transform child = current.GetChild(childIndex);
                    if (child.name == parts[i])
                    {
                        next = child;
                        break;
                    }
                }

                if (next == null)
                    return null;

                current = next;
            }

            return current;
        }

        private void ExportAssetReferenceResultsToCSV()
        {
            if (assetSearchResults == null || assetSearchResults.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "没有资源引用结果可以导出", "确定");
                return;
            }

            string safeQuery = SanitizeFileName(assetSearchQuery);
            string defaultName = $"AssetReferences_{selectedAssetType}_{safeQuery}";
            string savePath = CSVExporter.GetSaveFilePath(defaultName);
            if (string.IsNullOrEmpty(savePath))
                return;

            var csv = new System.Text.StringBuilder();
            csv.Append("\uFEFF");
            csv.AppendLine("序号,搜索资源类型,搜索关键字,引用类型,预制体名称,预制体路径,节点路径,资源名称,资源路径,额外信息");

            int index = 1;
            foreach (var result in assetSearchResults)
            {
                foreach (var reference in result.MatchingReferences)
                {
                    csv.AppendLine(string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}",
                        index,
                        EscapeCSV(selectedAssetType.ToString()),
                        EscapeCSV(assetSearchQuery),
                        EscapeCSV(reference.Type.ToString()),
                        EscapeCSV(result.PrefabName),
                        EscapeCSV(result.PrefabPath),
                        EscapeCSV(reference.NodePath),
                        EscapeCSV(reference.AssetName),
                        EscapeCSV(reference.AssetPath),
                        EscapeCSV(reference.ExtraInfo)
                    ));
                    index++;
                }
            }

            try
            {
                string directory = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(savePath, csv.ToString(), System.Text.Encoding.UTF8);
                EditorUtility.DisplayDialog("导出成功", $"资源引用结果已导出到:\n{savePath}\n\n共导出 {index - 1} 条记录。", "确定");
                EditorUtility.RevealInFinder(savePath);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UIProbe] 导出资源引用 CSV 失败: {e}");
                EditorUtility.DisplayDialog("导出失败", $"导出资源引用 CSV 时发生错误:\n{e.Message}", "确定");
            }
        }

        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "Search";

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }

            return fileName;
        }

        private string EscapeCSV(string field)
        {
            if (string.IsNullOrEmpty(field))
                return "";

            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            {
                field = field.Replace("\"", "\"\"");
                return $"\"{field}\"";
            }

            return field;
        }
    }
}
