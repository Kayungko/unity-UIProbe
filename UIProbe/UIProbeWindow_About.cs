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
            DrawFeatureItem("大红大金资源导入", "按表格匹配图片、分品质输出并回写图标路径");
            DrawFeatureItem("游戏截屏", "Play模式及Scene/Prefab视图高清截屏");
            DrawFeatureItem("TMP富文本生成", "可视化生成TextMeshPro富文本代码");
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(15);
            
            // Version History Highlights
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("最新更新 (v3.4.0)", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            EditorGUILayout.LabelField("• 大红大金资源修改导入 (Red/Gold Resource Importer)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - CSV/TSV 表格驱动，按红/紫/金品质分流并回写图标路径", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 预览区区分新增/修改/无变化，自动处理同名源图取最新版本", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 支持输出文件名编辑、覆盖前备份与一键撤销", EditorStyles.miniLabel);
            
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
            
            EditorGUILayout.Space(5);
            
            GUI.backgroundColor = new Color(0.8f, 1f, 0.8f);
            if (GUILayout.Button("🔄 手动检查更新 (Check for Updates)", GUILayout.Height(30)))
            {
                UIProbeUpdateChecker.PerformCheck((hasUpdate, msg) =>
                {
                    if (hasUpdate)
                    {
                        if (EditorUtility.DisplayDialog("UIProbe 更新检测", msg, "前往下载", "稍后再说"))
                        {
                            Application.OpenURL(UIProbeUpdateChecker.ReleaseUrl);
                        }
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("UIProbe 更新检测", msg, "确定");
                    }
                });
            }
            GUI.backgroundColor = Color.white;
            
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
