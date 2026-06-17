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
            window.TogglePicker();
            window.Repaint();
        }

        /// <summary>切换拾取模式（供 F1 菜单项调用）。拾取状态现归 PickerModule 持有。</summary>
        internal void TogglePicker()
        {
            var picker = modules?.OfType<PickerModule>().FirstOrDefault();
            if (picker != null) picker.IsPickerActive = !picker.IsPickerActive;
        }

        private Tab currentTab = Tab.Picker;
        private Vector2 mainScrollPos;
        private Vector2 sidebarScrollPos;
        private string[] tabNames = new string[] { "运行时拾取", "预制体索引", "界面记录", "历史浏览", "重名检测", "资源引用", "嵌套总览", "图片规范化", "游戏截屏", "富文本生成", "适配助手", "动画修复", "Filter节点排查", "资源使用检测", "设置", "关于" };
        
        // 统一配置（config 始终指向 configService.Config 同一实例，过渡期供未迁移模块直接访问）
        private ConfigService configService;
        private UIProbeConfig config;

        // 共享索引服务（持有 allPrefabs / folderTree / 版本号 / 缓存 I/O）。
        // 下列 shim 属性以原字段同名委托到服务，使尚未迁移的索引消费者
        // (Indexer / AssetReferences / NestingOverview / DuplicateChecker / FilterNodeScanner)
        // 编译不变；随各消费者迁移逐步移除。
        private PrefabIndexService indexService;
        private System.Collections.Generic.List<PrefabIndexItem> allPrefabs => indexService.AllPrefabs;
        private System.Collections.Generic.Dictionary<string, FolderNode> folderTree => indexService.FolderTree;
        private bool isIndexBuilt { get => indexService.IsIndexBuilt; set => indexService.IsIndexBuilt = value; }
        private string indexRootPath { get => indexService.IndexRootPath; set => indexService.IndexRootPath = value; }
        private string lastIndexUpdateTime => indexService.LastIndexUpdateTime;
        private int prefabIndexVersion { get => indexService.PrefabIndexVersion; set => indexService.PrefabIndexVersion = value; }

        // 模块注册表（Step 1：薄适配器，按侧栏顺序构造）
        private List<IUIProbeModule> modules;

        private void BuildModuleRegistry()
        {
            modules = new List<IUIProbeModule>
            {
                new PickerModule(configService),
                new IndexerModule(configService, indexService),
                new RecorderModule(),
                new BrowserModule(),
                new DuplicateCheckerModule(configService, indexService),
                new AssetReferencesModule(indexService),
                new NestingOverviewModule(indexService),
                new ImageNormalizerModule(configService),
                new ScreenshotModule(),
                new RichTextGeneratorModule(),
                new AdaptorModule(),
                new AnimationAutoRepairModule(),
                new FilterNodeScannerModule(indexService),
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
            // 加载统一配置（经服务统一管理；config 指向同一实例供过渡期模块直接读写）
            configService = new ConfigService();
            config = configService.Config;

            // 构造共享索引服务（在注册表/索引消费者使用前）
            indexService = new PrefabIndexService();

            // 构造模块注册表（生命周期 Apply/Collect 仍由窗口显式调度，Step 2 再迁移）
            BuildModuleRegistry();

            modules.OfType<IndexerModule>().First().Apply();
            LoadSettingsData();
            RefreshSessionList();
            modules.OfType<PickerModule>().First().Apply();
            
            // 尝试加载索引缓存（已迁入服务）
            indexService.LoadIndexCache();
            
            // 应用配置到图片规范化工具（含 RedGold Undo 管理初始化，迁入模块 Apply）
            modules.OfType<ImageNormalizerModule>().First().Apply();
            ApplyHelperConfig();
            modules.OfType<AnimationAutoRepairModule>().First().Apply();

            // 注册全局更新回调，使拾取功能在窗口未激活时也能工作
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            // 注销全局更新回调
            EditorApplication.update -= OnEditorUpdate;

            modules.OfType<IndexerModule>().First().Collect();
            
            // 收集并保存配置
            modules.OfType<ImageNormalizerModule>().First().Collect();
            CollectHelperConfig();
            modules.OfType<AnimationAutoRepairModule>().First().Collect();
            modules.OfType<PickerModule>().First().Collect();
            CollectSettingsData();
            configService?.Save();
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
            if (modules == null) return;
            foreach (var m in modules)
            {
                m.OnEditorUpdate();
            }
        }
        
        private void Update()
        {
            var active = modules.FirstOrDefault(m => m.Tab == currentTab);
            active?.OnWindowUpdate();
        }

        private void OnDestroy()
        {
            // 确保注销全局更新回调（双重保险）
            EditorApplication.update -= OnEditorUpdate;
            
            RecorderOnDestroy();
        }
    }
}

