using UnityEngine;
using UnityEditor;

namespace UIProbe
{
    public partial class UIProbeWindow
    {
        private Vector2 aboutScrollPosition;
        
        /// <summary>
        /// 绘制关于标签页
        /// </summary>
        private void DrawAboutTab()
        {
            EditorGUILayout.LabelField("关于 UIProbe", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);
            
            // Begin ScrollView
            aboutScrollPosition = EditorGUILayout.BeginScrollView(aboutScrollPosition, GUILayout.ExpandHeight(true));
            
            // Main info box
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.LabelField("UIProbe - Unity UI 界面探针工具", EditorStyles.largeLabel);
            EditorGUILayout.Space(5);
            
            // Version
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("版本:", EditorStyles.boldLabel, GUILayout.Width(60));
            EditorGUILayout.LabelField("2.7.0", GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();
            
            // Developers
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("开发者:", EditorStyles.boldLabel, GUILayout.Width(60));
            EditorGUILayout.LabelField("柯家荣, 沈浩天");
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            // Description
            EditorGUILayout.LabelField("简介:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Unity UI 界面探针工具，提供预制体索引、界面快照记录、重名检测等功能，旨在提高 UI 开发效率。", EditorStyles.wordWrappedLabel);
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(15);
            
            // Core Features
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("核心功能", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            DrawFeatureItem("运行时拾取", "在Play模式下点击拾取UI元素，查看层级和属性");
            DrawFeatureItem("预制体索引", "快速索引和搜索项目中的UI预制体");
            DrawFeatureItem("界面记录", "记录UI界面状态，保存快照和配置");
            DrawFeatureItem("历史浏览", "查看界面修改历史和快照记录");
            DrawFeatureItem("重名检测", "检测预制体中的重名节点，支持批量修复");
            DrawFeatureItem("资源引用", "追踪图片、预制体等资源的引用关系");
            DrawFeatureItem("图片规范化", "批量调整图片尺寸，保持内容不变形");
            DrawFeatureItem("游戏截屏", "Play模式高质量截屏，支持超采样");
            DrawFeatureItem("TMP富文本生成", "可视化生成TextMeshPro富文本代码");
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(15);
            
            // Version History Highlights
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("最新更新 (v2.7.0)", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            EditorGUILayout.LabelField("• 嵌套总览：修复实例统计Bug", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 修复统计逻辑：只统计直接嵌套的预制体实例", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 排除深层递归嵌套，避免重复计数", EditorStyles.miniLabel);
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("• 嵌套总览：多实例定位功能", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 支持展开/折叠，列出所有实例路径", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 每个实例独立定位：预制体内定位 + Project定位", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 智能预制体舞台切换，避免保存提示", EditorStyles.miniLabel);
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(15);
            
            // Links and Resources
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("资源链接", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("📖 查看 README", GUILayout.Height(25)))
            {
                string readmePath = System.IO.Path.Combine(Application.dataPath, "Editor/unity-UIProbe/README.md");
                if (System.IO.File.Exists(readmePath))
                {
                    Application.OpenURL("file:///" + readmePath);
                }
                else
                {
                    Application.OpenURL("https://github.com/Kayungko/unity-UIProbe");
                }
            }
            
            if (GUILayout.Button("🌐 GitHub 仓库", GUILayout.Height(25)))
            {
                Application.OpenURL("https://github.com/Kayungko/unity-UIProbe");
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
            
            
            // Footer
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("© 2024-2026 UIProbe Team. All Rights Reserved.", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndVertical();
            
            // End ScrollView
            EditorGUILayout.EndScrollView();
        }
        
        /// <summary>
        /// 绘制功能项
        /// </summary>
        private void DrawFeatureItem(string title, string description)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel, GUILayout.Width(150));
            EditorGUILayout.LabelField(description, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }
    }
}
