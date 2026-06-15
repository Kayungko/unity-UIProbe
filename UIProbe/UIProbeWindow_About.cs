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
            EditorGUILayout.LabelField($"最新更新 (v{UIProbeUpdateChecker.VERSION})", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField("• 动画路径自动修复 — 节点删除检测 (v3.9.2)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 删除检测: 节点被移除后标记为 unresolved，区分"已删除"/"多个同名"", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 导出增强: JSON 包含 resolvedCount/unresolvedCount 统计", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 导入跳过: 自动跳过 unresolved 条目并记录日志", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("• 动画路径自动修复模块 (v3.9.1)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 快照机制: 预制体层级快照，支持移动/改名后导出 oldPath → newPath 映射", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 事件修复: 修复从历史配置恢复自动修复开关时监听未完整恢复的问题", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("• 预制体助手创建的节点 Layer 固定为 UI (v3.9.0)", EditorStyles.miniLabel);

            EditorGUILayout.LabelField("• 批量命名模块全面升级 (v3.8.0)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 预览缩略图 32x32 + 行级勾选排除指定文件", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 执行前自动备份、支持撤销恢复", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 预览列表自适应高度 + 所有输入框 ExpandWidth", EditorStyles.miniLabel);

            EditorGUILayout.LabelField("• 模块显示设置补齐 + 旧记录目录清理 (v3.8.0)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 新增 Filter节点排查 与 大红大金资源修改导入显示开关", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 大红大金子标签可单独隐藏，隐藏后自动回到图片规范化页", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 移除旧版 Assets/UIProbeRecords 自动创建与扫描逻辑", EditorStyles.miniLabel);

            EditorGUILayout.LabelField("• 品质可配置化 + 栈式多级撤销持久化 + 预览缩略图", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 可动态增删品质条目，每个品质独立配置关键字/路径/命名模板", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 撤销栈持久化到磁盘，重启 Unity 后仍可撤销，最多 10 层", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 预览列表每行 32x32 缩略图，项目内用 AssetPreview", EditorStyles.miniLabel);

            EditorGUILayout.LabelField("• 命名模板扩展 + 预设系统 + 增量生成", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 模板支持 {Name}/{Pinyin}/{Seq:3}/{Quality} 变量", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 配置预设保存/加载/删除，多项目间复用", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - “仅变更行”/“清除未变更”按钮，生成按钮显示变更数量", EditorStyles.miniLabel);

            EditorGUILayout.LabelField("• 行内编辑 + 冲突预警 + 批量操作栏", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 双击编辑名称、下拉改品质、批量前缀/后缀/替换", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 同源冲突/目标冲突自动检测，黄色背景标记", EditorStyles.miniLabel);

            EditorGUILayout.LabelField("• 多源文件夹 + Excel 解析 + 资源引用联动", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 支持多个源文件夹按优先级匹配，零外部依赖读取 .xlsx", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 生成后自动扫描受影响的预制体并弹窗展示", EditorStyles.miniLabel);

            EditorGUILayout.LabelField("• 后台分帧生成 + 差异报告", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - EditorApplication.update 分帧处理，编辑器不再卡死", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 支持取消按钮，生成 Markdown 差异报告", EditorStyles.miniLabel);

            EditorGUILayout.LabelField("• 面板布局自适应 + 全 Tab 溢出修复 (v3.7.0)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 根级 ScrollView + 侧栏可滚动 + 窗口最小尺寸 400x250", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - 所有 Tab 固定宽度控件改为 ExpandWidth 自适应", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  - Recorder 清空加确认、Picker 快捷键不再写死", EditorStyles.miniLabel);

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
