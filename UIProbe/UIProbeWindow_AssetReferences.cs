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
        
        private class PrefabReferenceInfo
        {
            public string PrefabName;
            public string PrefabPath;
            public List<string> NodePaths = new List<string>();
            public List<string> ComponentTypes = new List<string>();
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
            
            // 搜索框
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("搜索图片资源:", GUILayout.Width(100));
            
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
                EditorGUILayout.HelpBox("请输入图片文件名或路径进行搜索。\n\n例如: \"icon_gold.png\" 或 \"UI/Icons/\"", MessageType.None);
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
                var matchingRefs = new List<ImageReference>();
                
                foreach (var imageRef in prefab.ImageReferences)
                {
                    // 检查资源路径或文件名是否匹配
                    if (imageRef.AssetPath.ToLower().Contains(query) || 
                        imageRef.AssetName.ToLower().Contains(query))
                    {
                        matchingRefs.Add(imageRef);
                    }
                }
                
                if (matchingRefs.Count > 0)
                {
                    var info = new PrefabReferenceInfo
                    {
                        PrefabName = prefab.Name,
                        PrefabPath = prefab.Path,
                        NodePaths = matchingRefs.Select(r => r.NodePath).ToList(),
                        ComponentTypes = matchingRefs.Select(r => r.ComponentType).ToList()
                    };
                    
                    assetSearchResults.Add(info);
                }
            }
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
            EditorGUILayout.LabelField($"引用位置 ({info.NodePaths.Count} 处):", EditorStyles.miniLabel);
            
            for (int i = 0; i < info.NodePaths.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(10);
                EditorGUILayout.LabelField($"📍 {info.NodePaths[i]}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"({info.ComponentTypes[i]})", EditorStyles.miniLabel, GUILayout.Width(80));
                GUILayout.EndHorizontal();
            }
            
            GUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }
    }
}
