using UnityEngine;
using UnityEditor;
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
            
            if (!isIndexBuilt)
            {
                EditorGUILayout.HelpBox("请先在「预制体索引」标签页点击「刷新」按钮建立索引。", MessageType.Info);
                return;
            }
            
            // 资源类型选择
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("资源类型:", GUILayout.Width(70));
            
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
                EditorGUILayout.LabelField($"找到 {assetSearchResults.Count} 个预制体引用该资源:", EditorStyles.boldLabel);
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
                    return "搜索材质:";
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
                    return "请输入图片文件名或路径进行搜索。\n\n例如: \"icon_gold.png\" 或 \"UI/Icons/\"";
                case AssetReferenceType.Prefab:
                    return "请输入预制体文件名或路径进行搜索。\n\n例如: \"Button.prefab\" 或 \"UI/Prefabs/\"";
                case AssetReferenceType.Material:
                    return "请输入材质文件名或路径进行搜索。\n\n例如: \"Glass.mat\" 或 \"Materials/\"";
                case AssetReferenceType.Font:
                    return "请输入字体文件名或路径进行搜索。\n\n例如: \"Arial.ttf\" 或 \"Fonts/\"";
                default:
                    return "请输入资源文件名或路径进行搜索。";
            }
        }
        
        /// <summary>
        /// 搜索资源引用
        /// </summary>
        private void SearchAssetReferences()
        {
            assetSearchResults.Clear();
            
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
                        // 搜索图片时同时包含 Image 和 RawImage
                        typeMatches = (assetRef.Type == AssetReferenceType.Image || 
                                      assetRef.Type == AssetReferenceType.RawImage);
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
            EditorGUILayout.LabelField($"📦 {info.PrefabName}", EditorStyles.boldLabel, GUILayout.Width(200));
            
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
                GUILayout.BeginHorizontal();
                GUILayout.Space(10);
                
                // 资源类型图标
                string icon = GetAssetTypeIcon(reference.Type);
                EditorGUILayout.LabelField(icon, GUILayout.Width(20));
                
                // 节点路径
                EditorGUILayout.LabelField($"{reference.NodePath}", EditorStyles.miniLabel);
                
                // 资源名称（可点击）
                if (GUILayout.Button(reference.AssetName, EditorStyles.linkLabel, GUILayout.Width(150)))
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
                
                // 额外信息
                if (!string.IsNullOrEmpty(reference.ExtraInfo))
                {
                    EditorGUILayout.LabelField($"({reference.ExtraInfo})", EditorStyles.miniLabel, GUILayout.Width(80));
                }
                
                GUILayout.EndHorizontal();
            }
            
            GUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }
    }
}
