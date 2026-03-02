using UnityEngine;
using UnityEditor;

namespace UIProbe
{
    public partial class UIProbeWindow : EditorWindow
    {
        // Menu item to open the window
        [MenuItem("UI Probe/打开面板")]
        public static void ShowWindow()
        {
            GetWindow<UIProbeWindow>("UI Probe 界面探针");
        }

        [MenuItem("UI Probe/切换选择模式 _F1")]
        public static void ToggleSelectMode()
        {
            var window = GetWindow<UIProbeWindow>("UI Probe 界面探针");
            window.isPickerActive = !window.isPickerActive;
            window.Repaint();
        }

        // Tabs
        private enum Tab
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
            ResourceDetector,
            PrefabCreator,  // 新增
            Settings,
            About
        }

        private Tab currentTab = Tab.Picker;
        private string[] tabNames = new string[] { "运行时拾取", "预制体索引", "界面记录", "历史浏览", "重名检测", "资源引用", "嵌套总览", "图片规范化", "游戏截屏", "富文本生成", "适配助手", "资源使用检测", "预制体创建", "设置", "关于" };
        
        // 统一配置
        private UIProbeConfig config;

        private void OnEnable()
        {
            // 加载统一配置
            config = UIProbeConfigManager.Load();
            if (config == null)
            {
                // 首次运行，从EditorPrefs迁移
                config = UIProbeConfigManager.MigrateFromEditorPrefs();
            }
            
            ApplyIndexerConfig(); // Was LoadAuxData
            LoadSettingsData();
            RefreshSessionList();
            InitPickerAutoMode();
            
            // 尝试加载索引缓存
            LoadIndexCache();
            
            // 应用配置到图片规范化工具
            ApplyImageNormalizerConfig();
            ApplyHelperConfig();
            
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
            CollectSettingsData();
            if (config != null)
            {
                UIProbeConfigManager.Save(config);
            }
        }

        private void OnGUI()
        {
            // 越界保护：如果当前停留的Tab在配置中被隐藏了，强制跳回到 Settings
            if (config != null)
            {
                bool isHidden = false;
                switch (currentTab)
                {
                    case Tab.Picker: isHidden = !config.modulesVisibility.showPicker; break;
                    case Tab.Indexer: isHidden = !config.modulesVisibility.showIndexer; break;
                    case Tab.Recorder: isHidden = !config.modulesVisibility.showRecorder; break;
                    case Tab.Browser: isHidden = !config.modulesVisibility.showBrowser; break;
                    case Tab.DuplicateChecker: isHidden = !config.modulesVisibility.showDuplicateChecker; break;
                    case Tab.AssetReferences: isHidden = !config.modulesVisibility.showAssetReferences; break;
                    case Tab.NestingOverview: isHidden = !config.modulesVisibility.showNestingOverview; break;
                    case Tab.ImageNormalizer: isHidden = !config.modulesVisibility.showImageNormalizer; break;
                    case Tab.Screenshot: isHidden = !config.modulesVisibility.showScreenshot; break;
                    case Tab.RichTextGenerator: isHidden = !config.modulesVisibility.showRichTextGenerator; break;
                    case Tab.Adaptor: isHidden = !config.modulesVisibility.showAdaptor; break;
                    case Tab.ResourceDetector: isHidden = !config.modulesVisibility.showResourceDetector; break;
                    case Tab.PrefabCreator: isHidden = !config.modulesVisibility.showPrefabCreator; break;
                }
                if (isHidden)
                {
                    currentTab = Tab.Settings;
                }
            }

            GUILayout.BeginHorizontal();
            
            // Left Side: Sidebar
            DrawSidebar();
            
            // Right Side: Content
            GUILayout.BeginVertical();
            switch (currentTab)
            {
                case Tab.Picker:
                    DrawPickerTab();
                    break;
                case Tab.Indexer:
                    DrawIndexerTab();
                    break;
                case Tab.Recorder:
                    DrawRecorderTab();
                    break;
                case Tab.Browser:
                    DrawBrowserTab();
                    break;
                case Tab.DuplicateChecker:
                    DrawDuplicateCheckerTab();
                    break;
                case Tab.AssetReferences:
                    DrawAssetReferencesTab();
                    break;
                case Tab.NestingOverview:
                    DrawNestingOverviewTab();
                    break;
                case Tab.ImageNormalizer:
                    DrawImageNormalizerTab();
                    break;
                case Tab.Screenshot:
                    DrawScreenshotTab();
                    break;
                case Tab.RichTextGenerator:
                    DrawRichTextGeneratorTab();
                    break;
                case Tab.Adaptor:
                    DrawAdaptorTab();
                    break;
                case Tab.ResourceDetector:
                    DrawResourceDetectorTab();
                    break;
                case Tab.PrefabCreator:
                    DrawPrefabCreatorTab();
                    break;
                case Tab.Settings:
                    DrawSettingsTab();
                    break;
                case Tab.About:
                    DrawAboutTab();
                    break;
            }
            
            GUILayout.EndVertical();
            
            GUILayout.EndHorizontal();
        }

        private void DrawSidebar()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(100), GUILayout.ExpandHeight(true));
            GUILayout.Space(5);

            if (config == null || config.modulesVisibility.showPicker) DrawSidebarButton(Tab.Picker, "运行时拾取");
            if (config == null || config.modulesVisibility.showIndexer) DrawSidebarButton(Tab.Indexer, "预制体索引");
            if (config == null || config.modulesVisibility.showRecorder) DrawSidebarButton(Tab.Recorder, "界面记录");
            if (config == null || config.modulesVisibility.showBrowser) DrawSidebarButton(Tab.Browser, "历史浏览");
            if (config == null || config.modulesVisibility.showDuplicateChecker) DrawSidebarButton(Tab.DuplicateChecker, "预制体综合检测");
            if (config == null || config.modulesVisibility.showAssetReferences) DrawSidebarButton(Tab.AssetReferences, "资源引用");
            if (config == null || config.modulesVisibility.showNestingOverview) DrawSidebarButton(Tab.NestingOverview, "嵌套总览");
            if (config == null || config.modulesVisibility.showImageNormalizer) DrawSidebarButton(Tab.ImageNormalizer, "图片规范化");
            if (config == null || config.modulesVisibility.showScreenshot) DrawSidebarButton(Tab.Screenshot, "游戏截屏");
            if (config == null || config.modulesVisibility.showRichTextGenerator) DrawSidebarButton(Tab.RichTextGenerator, "富文本生成");
            if (config == null || config.modulesVisibility.showAdaptor) DrawSidebarButton(Tab.Adaptor, "预制体助手");
            if (config == null || config.modulesVisibility.showResourceDetector) DrawSidebarButton(Tab.ResourceDetector, "资源使用检测");
            if (config == null || config.modulesVisibility.showPrefabCreator) DrawSidebarButton(Tab.PrefabCreator, "预制体创建");
            
            GUILayout.FlexibleSpace();
            
            DrawSidebarButton(Tab.Settings, "设置");
            DrawSidebarButton(Tab.About, "关于");
            
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

