using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace UIProbe
{
    /// <summary>功能页签标识。Step 1 起提升为顶层 internal 枚举，供独立模块适配器引用。</summary>
    internal enum Tab
    {
        Picker,
        Indexer,
        Recorder,
        Browser,
        DuplicateChecker,
        AssetReferences,
        NestingOverview,
        ImageNormalizer,
        Screenshot,
        RichTextGenerator,
        Adaptor,
        AnimationAutoRepair,
        FilterNodeScanner,
        ResourceDetector,
        Settings,
        About
    }

    public partial class UIProbeWindow : EditorWindow
    {
        // Menu item to open the window
        [MenuItem("UI Probe/打开面板")]
        public static void ShowWindow()
        {
            var window = GetWindow<UIProbeWindow>("UI Probe 界面探针");
            window.minSize = new Vector2(400, 250);
        }

        [MenuItem("UI Probe/切换选择模式 _F1")]
        public static void ToggleSelectMode()
        {
            var window = GetWindow<UIProbeWindow>("UI Probe 界面探针");
            window.isPickerActive = !window.isPickerActive;
            window.Repaint();
        }

        private Tab currentTab = Tab.Picker;
        private Vector2 mainScrollPos;
        private Vector2 sidebarScrollPos;
        private string[] tabNames = new string[] { "运行时拾取", "预制体索引", "界面记录", "历史浏览", "重名检测", "资源引用", "嵌套总览", "图片规范化", "游戏截屏", "富文本生成", "适配助手", "动画修复", "Filter节点排查", "资源使用检测", "设置", "关于" };
        
        // 统一配置
        private UIProbeConfig config;

        // 模块注册表（Step 1：薄适配器，按侧栏顺序构造）
        private List<IUIProbeModule> modules;

        private void BuildModuleRegistry()
        {
            modules = new List<IUIProbeModule>
            {
                new PickerModule(),
                new IndexerModule(),
                new RecorderModule(),
                new BrowserModule(),
                new DuplicateCheckerModule(),
                new AssetReferencesModule(),
                new NestingOverviewModule(),
                new ImageNormalizerModule(),
                new ScreenshotModule(),
                new RichTextGeneratorModule(),
                new AdaptorModule(),
                new AnimationAutoRepairModule(),
                new FilterNodeScannerModule(),
                new ResourceDetectorModule(),
                new SettingsModule(),
                new AboutModule(),
            };
            foreach (var m in modules)
            {
                ((UIProbeModuleBase)m).Bind(this);
            }
        }

        private void OnEnable()
        {
            // 加载统一配置
            config = UIProbeConfigManager.Load();
            if (config == null)
            {
                // 首次运行，从EditorPrefs迁移
                config = UIProbeConfigManager.MigrateFromEditorPrefs();
            }

            // 构造模块注册表（生命周期 Apply/Collect 仍由窗口显式调度，Step 2 再迁移）
            BuildModuleRegistry();

            ApplyIndexerConfig(); // Was LoadAuxData
            LoadSettingsData();
            RefreshSessionList();
            InitPickerAutoMode();
            
            // 尝试加载索引缓存
            LoadIndexCache();
            
            // 应用配置到图片规范化工具
            EnsureRedGoldUndoManager();
            ApplyImageNormalizerConfig();
            ApplyHelperConfig();
            ApplyAnimationAutoRepairConfig();

            // 注册全局更新回调，使拾取功能在窗口未激活时也能工作
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            // 注销全局更新回调
            EditorApplication.update -= OnEditorUpdate;

            CollectIndexerConfig(); // Was SaveAuxData
            
            // 收集并保存配置
            CollectImageNormalizerConfig();
            CollectHelperConfig();
            CollectAnimationAutoRepairConfig();
            CollectPickerConfig();
            CollectSettingsData();
            if (config != null)
            {
                UIProbeConfigManager.Save(config);
            }
        }

        private void OnGUI()
        {
            // === 静默更新提醒横幅 ===
            if (UIProbeUpdateChecker.HasUpdateAvailable)
            {
                GUI.backgroundColor = new Color(1f, 0.9f, 0.6f); // 浅橙黄色
                if (GUILayout.Button($"✨ 发现新版本 UIProbe [{UIProbeUpdateChecker.LatestVersion}] 🚀 点击前往查看并下载最新特性！", GUILayout.Height(24)))
                {
                    Application.OpenURL(UIProbeUpdateChecker.ReleaseUrl);
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.Space(2);
            }
            // =========================

            // 越界保护：如果当前停留的Tab在配置中被隐藏了，强制跳回到 Settings
            if (modules != null)
            {
                var active = modules.FirstOrDefault(m => m.Tab == currentTab);
                if (active != null && !active.IsVisible(config))
                {
                    currentTab = Tab.Settings;
                }
            }

            GUILayout.BeginHorizontal();
            
            // Left Side: Sidebar
            DrawSidebar();
            
            // Right Side: Content
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            // 根级滚动视图（更新横幅 + Tab 内容统一滚动）
            mainScrollPos = EditorGUILayout.BeginScrollView(mainScrollPos, GUILayout.ExpandHeight(true));
            var current = modules.FirstOrDefault(m => m.Tab == currentTab) ?? modules[0];
            current.Draw();

            EditorGUILayout.EndScrollView();
            GUILayout.EndVertical();
            
            GUILayout.EndHorizontal();
        }

        private void DrawSidebar()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinWidth(80), GUILayout.MaxWidth(180), GUILayout.ExpandHeight(true));
            GUILayout.Space(5);
            sidebarScrollPos = GUILayout.BeginScrollView(sidebarScrollPos, GUILayout.ExpandHeight(true));

            foreach (var m in modules.Where(m => m.Section == SidebarSection.Top))
            {
                if (m.IsVisible(config))
                {
                    m.DrawSidebarButton(this);
                }
            }

            GUILayout.FlexibleSpace();

            foreach (var m in modules.Where(m => m.Section == SidebarSection.Bottom))
            {
                m.DrawSidebarButton(this);
            }

            GUILayout.EndScrollView();
            GUILayout.Space(5);
            GUILayout.EndVertical();
        }

        private void DrawSidebarButton(Tab tab, string label)
        {
            GUI.backgroundColor = currentTab == tab ? Color.cyan : Color.white;
            if (GUILayout.Button(label, GUILayout.Height(35)))
            {
                currentTab = tab;
            }
            GUI.backgroundColor = Color.white;
        }

        /// <summary>
        /// 全局编辑器更新回调，即使窗口未激活也会被调用
        /// 用于处理拾取功能的输入检测
        /// </summary>
        private void OnEditorUpdate()
        {
            // 只在拾取激活且游戏运行时处理拾取输入
            if (isPickerActive && Application.isPlaying)
            {
                HandlePickerInput();
            }
        }
        
        private void Update()
        {
            // 截屏快捷键（仅在 Play 模式且截屏页签激活时）
            if (Application.isPlaying && currentTab == Tab.Screenshot)
            {
                HandleScreenshotInput();
            }
        }

        private void OnDestroy()
        {
            // 确保注销全局更新回调（双重保险）
            EditorApplication.update -= OnEditorUpdate;
            
            RecorderOnDestroy();
        }
    }
}

