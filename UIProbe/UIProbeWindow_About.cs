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
            EditorGUILayout.LabelField(UIProbeUpdateChecker.VERSION, GUILayout.Width(100));
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
            DrawFeatureItem("游戏截屏", "Play模式及Scene/Prefab视图高清截屏");
            DrawFeatureItem("TMP富文本生成", "可视化生成TextMeshPro富文本代码");
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(15);
            
            // Version History Highlights
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("最新更新 (v3.1.0)", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            EditorGUILayout.LabelField("• 游戏截屏增强 (Screenshot Enhanced)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 新增仅截 UI 层功能，通过双背景(黑+白)两帧渲染差值合成", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 彻底解决包含 VFX / UIParticle 的特效节点在截图时产生的黑色块难题", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("• 核心架构及稳定性优化 (Core Optimization)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 重构序列化架构，彻底解决 Unity 深度限制 (Serialization depth limit)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 支持任意无限深度的高复杂嵌套 UI 预制体索引加载", EditorStyles.miniLabel);
            
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
