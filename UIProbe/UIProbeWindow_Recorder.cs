using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace UIProbe
{
    public partial class UIProbeWindow
    {
        // Recorder V2 State
        private bool isRecordingV2 = false;
        private Transform uiRootV2;
        private Vector2 recorderScrollPositionV2;
        private UIRecordSession currentSession;
        private HashSet<string> trackedPaths = new HashSet<string>();
        
        // Tag editing state
        private UIRecordEvent editingTagNode;
        private string editingTagValue;
        
        private void DrawRecorderTab()
        {
            EditorGUILayout.LabelField("界面记录 V2 (UI Recorder)", EditorStyles.boldLabel);

            // UI Root selection
            GUILayout.BeginHorizontal();
            uiRootV2 = (Transform)EditorGUILayout.ObjectField("UI Root", uiRootV2, typeof(Transform), true);
            
            if (GUILayout.Button("自动检测", GUILayout.Width(60)))
            {
                DetectUIRootV2();
            }
            GUILayout.EndHorizontal();

            if (uiRootV2 == null)
            {
                EditorGUILayout.HelpBox("请指定 UI Root 节点以开始记录。", MessageType.Warning);
            }

            // Control buttons
            GUILayout.BeginHorizontal();
            
            GUI.backgroundColor = isRecordingV2 ? Color.red : Color.green;
            if (GUILayout.Button(isRecordingV2 ? "停止记录" : "开始记录", GUILayout.Height(25)))
            {
                ToggleRecordingV2();
            }
            GUI.backgroundColor = Color.white;
            
            GUI.enabled = currentSession != null && currentSession.Events.Count > 0;
            if (GUILayout.Button("保存", GUILayout.Height(25)))
            {
                ShowSaveDialog();
            }
            GUI.enabled = true;
            
            if (GUILayout.Button("浏览历史", GUILayout.Height(25)))
            {
                currentTab = Tab.Browser;
            }
            
            if (GUILayout.Button("清空", GUILayout.Height(25)))
            {
                ClearCurrentSession();
            }
            GUILayout.EndHorizontal();

            // Status
            EditorGUILayout.Space();
            if (isRecordingV2)
            {
                EditorGUILayout.HelpBox($"正在记录... 已捕获 {(currentSession?.Events.Count ?? 0)} 个事件", MessageType.Info);
            }
            else if (currentSession != null && currentSession.Events.Count > 0)
            {
                EditorGUILayout.HelpBox($"已暂停。共 {currentSession.Events.Count} 个事件。", MessageType.None);
            }

            // Event tree
            recorderScrollPositionV2 = EditorGUILayout.BeginScrollView(recorderScrollPositionV2);
            
            if (currentSession != null && currentSession.Events.Count > 0)
            {
                foreach (var evt in currentSession.Events)
                {
                    DrawRecordEventV2(evt, 0);
                }
            }
            else
            {
                EditorGUILayout.LabelField("暂无记录。点击「开始记录」后操作界面。");
            }
            
            EditorGUILayout.EndScrollView();
        }

        private void ToggleRecordingV2()
        {
            isRecordingV2 = !isRecordingV2;
            
            if (isRecordingV2)
            {
                if (currentSession == null)
                {
                    currentSession = new UIRecordSession();
                    trackedPaths.Clear();
                }
                
                // Initial snapshot
                if (uiRootV2 != null)
                {
                    CaptureCurrentState();
                }
                
                EditorApplication.hierarchyChanged += OnHierarchyChangedV2;
            }
            else
            {
                EditorApplication.hierarchyChanged -= OnHierarchyChangedV2;
            }
            
            Repaint();
        }

        private void OnHierarchyChangedV2()
        {
            if (!isRecordingV2 || uiRootV2 == null) return;
            
            // Capture changes
            CaptureCurrentState();
            Repaint();
        }

        private void CaptureCurrentState()
        {
            if (uiRootV2 == null) return;
            
            // Rebuild the event tree
            currentSession.Events.Clear();
            
            foreach (Transform child in uiRootV2)
            {
                var evt = BuildEventFromTransform(child, 0);
                if (evt != null)
                {
                    currentSession.Events.Add(evt);
                }
            }
        }

        private UIRecordEvent BuildEventFromTransform(Transform t, int depth)
        {
            bool isPrefab = PrefabUtility.IsPartOfAnyPrefab(t.gameObject);
            string prefabName = "";
            string prefabPath = "";
            
            if (isPrefab)
            {
                var prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject);
                if (prefabRoot != null)
                {
                    var source = PrefabUtility.GetCorrespondingObjectFromSource(prefabRoot);
                    if (source != null)
                    {
                        prefabName = source.name;
                        prefabPath = AssetDatabase.GetAssetPath(source);
                    }
                }
            }
            
            var evt = new UIRecordEvent
            {
                EventType = isPrefab ? "Prefab" : "Child",
                NodeName = t.name,
                NodePath = GetFullPath(t),
                PrefabName = prefabName,
                PrefabPath = prefabPath,
                IsPrefabInstance = isPrefab,
                Tag = UITagInferrer.InferTag(t.name, depth),
                Timestamp = System.DateTime.Now.ToString("HH:mm:ss"),
                GameObjectRef = t.gameObject
            };
            
            foreach (Transform child in t)
            {
                var childEvt = BuildEventFromTransform(child, depth + 1);
                if (childEvt != null)
                {
                    evt.Children.Add(childEvt);
                }
            }
            
            return evt;
        }

        private void DrawRecordEventV2(UIRecordEvent evt, int indent)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(indent * 15);
            
            // Expand/collapse
            string icon = evt.IsPrefabInstance ? "📦" : "📄";
            string display = $"{icon} {evt.NodeName}";
            
            if (evt.Children.Count > 0)
            {
                evt.IsExpanded = EditorGUILayout.Foldout(evt.IsExpanded, display, true);
            }
            else
            {
                EditorGUILayout.LabelField(display, GUILayout.Width(200));
            }
            
            // Tag display/edit
            if (editingTagNode == evt)
            {
                editingTagValue = EditorGUILayout.TextField(editingTagValue, GUILayout.Width(80));
                if (GUILayout.Button("OK", EditorStyles.miniButton, GUILayout.Width(25)))
                {
                    evt.Tag = editingTagValue;
                    editingTagNode = null;
                }
                if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(20)))
                {
                    editingTagNode = null;
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(evt.Tag))
                {
                    GUI.backgroundColor = UITagInferrer.GetTagColor(evt.Tag);
                    if (GUILayout.Button(evt.Tag, EditorStyles.miniButton, GUILayout.Width(60)))
                    {
                        editingTagNode = evt;
                        editingTagValue = evt.Tag;
                    }
                    GUI.backgroundColor = Color.white;
                }
                else
                {
                    if (GUILayout.Button("+标签", EditorStyles.miniButton, GUILayout.Width(45)))
                    {
                        editingTagNode = evt;
                        editingTagValue = "";
                    }
                }
            }
            
            GUILayout.FlexibleSpace();
            
            // Action buttons
            if (GUILayout.Button("C", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                EditorGUIUtility.systemCopyBuffer = evt.NodePath;
                Debug.Log($"路径已复制: {evt.NodePath}");
            }
            
            if (evt.GameObjectRef != null)
            {
                if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(35)))
                {
                    Selection.activeGameObject = evt.GameObjectRef;
                    EditorGUIUtility.PingObject(evt.GameObjectRef);
                }
            }
            
            GUILayout.EndHorizontal();
            
            // Children
            if (evt.IsExpanded && evt.Children.Count > 0)
            {
                foreach (var child in evt.Children)
                {
                    DrawRecordEventV2(child, indent + 1);
                }
            }
        }

        private void ShowSaveDialog()
        {
            var saveWindow = ScriptableObject.CreateInstance<UIRecordSaveDialog>();
            saveWindow.Session = currentSession;
            saveWindow.OnSaveComplete = () => {
                ClearCurrentSession();
            };
            saveWindow.ShowUtility();
        }

        private void ClearCurrentSession()
        {
            currentSession = null;
            trackedPaths.Clear();
            Repaint();
        }

        private void DetectUIRootV2()
        {
            // Try to find Canvas
            var canvas = FindObjectOfType<Canvas>();
            if (canvas != null)
            {
                uiRootV2 = canvas.transform;
                var rootChild = canvas.transform.Find("UIRoot");
                if (rootChild != null) uiRootV2 = rootChild;
                return;
            }
            
            // Try to find by name
            var obj = GameObject.Find("UIRoot");
            if (obj != null) uiRootV2 = obj.transform;
        }

        private void RecorderOnDestroy()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChangedV2;
        }
    }

    /// <summary>
    /// 保存对话框
    /// </summary>
    public class UIRecordSaveDialog : EditorWindow
    {
        public UIRecordSession Session;
        public System.Action OnSaveComplete;
        
        private string versionInput = "1.0.0";
        private string descriptionInput = "";
        private string storagePath = "";
        private bool captureScreenshot = true;
        private Texture2D capturedScreenshot;
        
        private void OnEnable()
        {
            titleContent = new GUIContent("保存记录");
            minSize = new Vector2(400, 220);
            maxSize = new Vector2(400, 220);
            
            // Load configured path from Settings
            // Load configured path from Settings
            var config = UIProbeConfigManager.Load();
            if (config != null && config.recorder != null && !string.IsNullOrEmpty(config.recorder.storagePath))
            {
                storagePath = config.recorder.storagePath;
            }
            else
            {
                storagePath = UIRecordStorage.GetDefaultStoragePath();
            }
            
            // Capture screenshot immediately if in play mode
            if (Application.isPlaying)
            {
                CaptureGameViewScreenshot();
            }
        }
        
        private void OnDisable()
        {
            // Clean up texture
            if (capturedScreenshot != null)
            {
                DestroyImmediate(capturedScreenshot);
                capturedScreenshot = null;
            }
        }
        
        private void CaptureGameViewScreenshot()
        {
            // Use ScreenCapture to capture the game view
            capturedScreenshot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            capturedScreenshot = ScreenCapture.CaptureScreenshotAsTexture();
        }
        
        private void OnGUI()
        {
            EditorGUILayout.LabelField("保存界面记录", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            versionInput = EditorGUILayout.TextField("版本号", versionInput);
            descriptionInput = EditorGUILayout.TextField("描述", descriptionInput);
            
            EditorGUILayout.Space();
            
            // Screenshot option
            GUILayout.BeginHorizontal();
            captureScreenshot = EditorGUILayout.ToggleLeft("保存截图", captureScreenshot);
            if (capturedScreenshot != null)
            {
                EditorGUILayout.LabelField($"({capturedScreenshot.width}x{capturedScreenshot.height})", EditorStyles.miniLabel);
            }
            else if (captureScreenshot)
            {
                EditorGUILayout.LabelField("(需在 Play Mode 下截图)", EditorStyles.miniLabel);
            }
            GUILayout.EndHorizontal();
            
            EditorGUILayout.Space();
            
            // Show current storage path
            EditorGUILayout.LabelField("存储路径:", storagePath, EditorStyles.miniLabel);
            EditorGUILayout.HelpBox("如需修改路径，请前往「设置」页签配置。", MessageType.Info);
            
            EditorGUILayout.Space();
            
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("保存"))
            {
                DoSave();
            }
            if (GUILayout.Button("取消"))
            {
                Close();
            }
            GUILayout.EndHorizontal();
        }
        
        private void DoSave()
        {
            Session.Version = versionInput;
            Session.Description = descriptionInput;
            
            Texture2D screenshot = (captureScreenshot && capturedScreenshot != null) ? capturedScreenshot : null;
            UIRecordStorage.SaveSession(Session, storagePath, screenshot);
            
            OnSaveComplete?.Invoke();
            Close();
        }
    }
}

