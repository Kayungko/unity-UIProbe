using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace UIProbe
{
    public partial class UIProbeWindow
    {
        // Picker State
        private bool isPickerActive = false;
        private bool autoPickerMode = false;  // Auto-enable when entering Play mode
        private GameObject currentSelection;
        private Vector2 pickerScrollPosition;

        private void InitPickerAutoMode()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            
            // Load saved preference
            ApplyPickerConfig();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (autoPickerMode)
            {
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    isPickerActive = true;
                    Repaint();
                }
                else if (state == PlayModeStateChange.ExitingPlayMode)
                {
                    isPickerActive = false;
                    currentSelection = null;
                    Repaint();
                }
            }
        }

        private void DrawPickerTab()
        {
            EditorGUILayout.LabelField("运行时拾取 (Runtime Picker)", EditorStyles.boldLabel);
            
            // Main status/toggle button
            GUI.backgroundColor = isPickerActive ? Color.green : Color.gray;
            string statusText = isPickerActive ? "● 拾取模式: 已启用" : "○ 拾取模式: 已禁用";
            if (GUILayout.Button(statusText, GUILayout.Height(35)))
            {
                isPickerActive = !isPickerActive;
            }
            GUI.backgroundColor = Color.white;
            
            // Options row
            GUILayout.BeginHorizontal();
            
            // Auto-mode checkbox
            bool newAutoMode = EditorGUILayout.ToggleLeft("进入 Play Mode 时自动启用", autoPickerMode);
            if (newAutoMode != autoPickerMode)
            {
                autoPickerMode = newAutoMode;
                // Config will be saved on disable
                if (config != null) config.picker.autoMode = autoPickerMode;
                
                if (autoPickerMode && Application.isPlaying)
                {
                    isPickerActive = true;
                }
            }
            
            GUILayout.FlexibleSpace();
            
            // 显示当前拾取方式
            string inputModeText = GetPickerInputModeText();
            EditorGUILayout.LabelField($"拾取方式: {inputModeText}", EditorStyles.miniLabel, GUILayout.Width(150));
            EditorGUILayout.LabelField("快捷键: F1", EditorStyles.miniLabel, GUILayout.Width(70));
            GUILayout.EndHorizontal();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("请在运行游戏 (Play Mode) 下使用此功能。", MessageType.Info);
                return;
            }

            pickerScrollPosition = EditorGUILayout.BeginScrollView(pickerScrollPosition);

            if (currentSelection != null)
            {
                DrawSelectionInfo(currentSelection);
            }
            else
            {
                EditorGUILayout.HelpBox("点击 Game 视图中的 UI 元素以查看信息。", MessageType.None);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSelectionInfo(GameObject selection)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("当前选中:", selection.name, EditorStyles.boldLabel);
            
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("路径:", GetFullPath(selection.transform));
            if (GUILayout.Button("复制", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                EditorGUIUtility.systemCopyBuffer = GetFullPath(selection.transform);
                Debug.Log($"路径已复制: {EditorGUIUtility.systemCopyBuffer}");
            }
            GUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();

            DrawPrefabInfo(selection);
            DrawComponentInfo(selection);
            DrawProblems(selection);
        }

        private void DrawProblems(GameObject selection)
        {
            var problems = UIProbeChecker.CheckSingle(selection);
            
            if (problems.Count == 0) return;
            
            EditorGUILayout.Space();
            
            // Header with count
            GUI.backgroundColor = new Color(0.9f, 0.5f, 0.3f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = Color.white;
            
            EditorGUILayout.LabelField($"▼ 问题检测 ({problems.Count})", EditorStyles.boldLabel);
            
            foreach (var problem in problems)
            {
                EditorGUILayout.BeginHorizontal();
                
                // Icon and color
                GUI.backgroundColor = problem.GetColor();
                GUILayout.Label(problem.GetIcon(), EditorStyles.miniButton, GUILayout.Width(20));
                GUI.backgroundColor = Color.white;
                
                // Description
                EditorGUILayout.LabelField(problem.Description, EditorStyles.wordWrappedLabel);
                
                // Locate button if target is different from current selection
                if (problem.Target != selection && GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(35)))
                {
                    Selection.activeGameObject = problem.Target;
                    EditorGUIUtility.PingObject(problem.Target);
                }
                
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.EndVertical();
        }

        private void DrawPrefabInfo(GameObject selection)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("▼ 预制体层级 (Prefab Chain)", EditorStyles.boldLabel);

            // Find the nearest prefab root for the selection
            var prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(selection);
            if (prefabRoot == null)
            {
                EditorGUILayout.HelpBox("此对象不是预制体实例的一部分。", MessageType.Info);
                return;
            }

            var current = prefabRoot;
            while (current != null)
            {
                // Get the source asset (the .prefab file)
                var source = PrefabUtility.GetCorrespondingObjectFromSource(current);
                if (source == null) break;

                string assetPath = AssetDatabase.GetAssetPath(source);
                
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"📦 {source.name}", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("打开", GUILayout.Width(45))) AssetDatabase.OpenAsset(source);
                if (GUILayout.Button("定位", GUILayout.Width(45))) EditorGUIUtility.PingObject(source);
                GUILayout.EndHorizontal();
                
                EditorGUILayout.LabelField(assetPath, EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();

                var parentTransform = current.transform.parent;
                if (parentTransform == null) break;
                
                var nextRoot = PrefabUtility.GetNearestPrefabInstanceRoot(parentTransform.gameObject);
                if (nextRoot == current) 
                {
                    current = FindNextDifferentPrefabRoot(parentTransform);
                }
                else
                {
                    current = nextRoot;
                }
            }
        }

        private GameObject FindNextDifferentPrefabRoot(Transform startNode)
        {
            var current = startNode;
            GameObject lastRoot = PrefabUtility.GetNearestPrefabInstanceRoot(startNode.gameObject);
            
            while (current != null)
            {
                var root = PrefabUtility.GetNearestPrefabInstanceRoot(current.gameObject);
                if (root != lastRoot && root != null)
                {
                    return root;
                }
                current = current.parent;
            }
            return null;
        }

        private void DrawComponentInfo(GameObject selection)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("▼ 组件信息 (Components)", EditorStyles.boldLabel);

            var image = selection.GetComponent<UnityEngine.UI.Image>();
            if (image != null)
            {
                DrawImageInfo(image);
            }

            var text = selection.GetComponent<UnityEngine.UI.Text>();
            if (text != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("📝 Text", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Content: {text.text}");
                EditorGUILayout.LabelField($"Font: {text.font?.name ?? "None"}");
                EditorGUILayout.LabelField($"Size: {text.fontSize}");
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawImageInfo(UnityEngine.UI.Image image)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("🖼️ Image", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (image.sprite != null && GUILayout.Button("定位资源", GUILayout.Width(60)))
            {
                EditorGUIUtility.PingObject(image.sprite);
            }
            GUILayout.EndHorizontal();

            EditorGUILayout.LabelField($"Sprite: {image.sprite?.name ?? "None"}");
            EditorGUILayout.EndVertical();
        }

        private string GetFullPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        private void HandlePickerInput()
        {
            // 获取配置的输入方式
            PickerInputMode inputMode = GetConfiguredPickerInputMode();
            
            bool shouldPick = false;
            
            // 根据配置的方式检测输入
            switch (inputMode)
            {
                case PickerInputMode.RightClick:
                    shouldPick = Input.GetMouseButtonDown(1); // 右键
                    break;
                    
                case PickerInputMode.CtrlLeftClick:
                    shouldPick = Input.GetMouseButtonDown(0) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
                    break;
                    
                case PickerInputMode.MiddleClick:
                    shouldPick = Input.GetMouseButtonDown(2); // 中键
                    break;
                    
                case PickerInputMode.AltLeftClick:
                    shouldPick = Input.GetMouseButtonDown(0) && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));
                    break;
            }
            
            if (shouldPick)
            {
                PickUIElement(Input.mousePosition);
            }
            
            // 支持触摸输入（Device Simulator），但不要绕过用户配置的拾取方式。
            if (!shouldPick && ShouldHandleTouchInput(inputMode) && Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Began)
                {
                    PickUIElement(touch.position);
                }
            }
        }

        private void PickUIElement(Vector2 screenPosition)
        {
            if (UnityEngine.EventSystems.EventSystem.current == null) return;

            var pointerData = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
            {
                position = screenPosition
            };

            var results = new List<UnityEngine.EventSystems.RaycastResult>();
            UnityEngine.EventSystems.EventSystem.current.RaycastAll(pointerData, results);

            if (results.Count > 0)
            {
                var detected = results[0].gameObject;
                SelectObject(detected);
            }
        }

        private void SelectObject(GameObject obj)
        {
            currentSelection = obj;
            Selection.activeGameObject = obj;
            EditorGUIUtility.PingObject(obj);
            Repaint();
        }
        
        private void ApplyPickerConfig()
        {
            if (config != null && config.picker != null)
            {
                autoPickerMode = config.picker.autoMode;
            }
        }

        private PickerInputMode GetConfiguredPickerInputMode()
        {
            if (config == null || config.picker == null)
            {
                return PickerInputMode.RightClick;
            }

            if (!System.Enum.IsDefined(typeof(PickerInputMode), config.picker.inputMode))
            {
                return PickerInputMode.RightClick;
            }

            return (PickerInputMode)config.picker.inputMode;
        }

        private bool ShouldHandleTouchInput(PickerInputMode inputMode)
        {
            switch (inputMode)
            {
                case PickerInputMode.CtrlLeftClick:
                    return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                case PickerInputMode.AltLeftClick:
                    return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                default:
                    return false;
            }
        }
        
        /// <summary>
        /// 获取拾取方式的显示文本
        /// </summary>
        private string GetPickerInputModeText()
        {
            PickerInputMode mode = GetConfiguredPickerInputMode();
            switch (mode)
            {
                case PickerInputMode.RightClick:
                    return "右键";
                case PickerInputMode.CtrlLeftClick:
                    return "Ctrl+左键";
                case PickerInputMode.MiddleClick:
                    return "中键";
                case PickerInputMode.AltLeftClick:
                    return "Alt+左键";
                default:
                    return "右键";
            }
        }
        
        private void CollectPickerConfig()
        {
            if (config != null)
            {
                if (config.picker == null) config.picker = new PickerConfig();
                config.picker.autoMode = autoPickerMode;
            }
        }
    }
}
