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
            Settings
        }

        private Tab currentTab = Tab.Picker;
        private string[] tabNames = new string[] { "运行时拾取", "预制体索引", "界面记录", "历史浏览", "重名检测", "设置" };

        private void OnEnable()
        {
            LoadAuxData();
            LoadSettingsData();
            RefreshSessionList();
            InitPickerAutoMode();
        }

        private void OnDisable()
        {
            SaveAuxData();
        }

        private void OnGUI()
        {
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
                case Tab.Settings:
                    DrawSettingsTab();
                    break;
            }
            
            GUILayout.EndVertical();
            
            GUILayout.EndHorizontal();
        }

        private void DrawSidebar()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(100), GUILayout.ExpandHeight(true));
            GUILayout.Space(5);

            DrawSidebarButton(Tab.Picker, "运行时拾取");
            DrawSidebarButton(Tab.Indexer, "预制体索引");
            DrawSidebarButton(Tab.Recorder, "界面记录");
            DrawSidebarButton(Tab.Browser, "历史浏览");
            DrawSidebarButton(Tab.DuplicateChecker, "重名检测");
            
            GUILayout.FlexibleSpace();
            
            DrawSidebarButton(Tab.Settings, "设置");
            
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

        private void Update()
        {
            if (isPickerActive && Application.isPlaying)
            {
                HandlePickerInput();
            }
        }

        private void OnDestroy()
        {
            RecorderOnDestroy();
        }
    }
}

