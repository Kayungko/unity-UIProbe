using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using System.Collections.Generic;
// Conditional compilation for TMP if needed, but assuming it exists based on context
using TMPro;

namespace UIProbe
{
    public partial class UIProbeWindow
    {
        // 助手状态 (Helper State)
        private Vector2 helperScrollPos;
        private int selectedLayoutType = 0; // 0: Full, 1: Window, 2: Side
        private string[] layoutTypes = new string[] { "全屏面板 (Full Panel)", "居中窗口 (Center Window)", "侧边栏 (Side Widget)" };
        
        // 布局参数
        private float adaptorPaddingLeft = 0;
        private float adaptorPaddingRight = 0;
        private float adaptorPaddingTop = 0;
        private float adaptorPaddingBottom = 0;
        private float adaptorWidth = 800;
        private float adaptorHeight = 600;
        private int adaptorSideAlignment = 0; // 0: Left, 1: Right, 2: Top, 3: Bottom
        private string[] sideAlignments = new string[] { "靠左 (Left)", "靠右 (Right)", "靠上 (Top)", "靠下 (Bottom)" };
        private bool adaptorIsStretch = false; // 是否开启拉伸模式
        
        // 快速创建状态
        private bool defaultNoRaycastImage = true;
        private bool defaultNoRaycastText = true;
        private int selectedFontIndex = 0;

        /// <summary>
        /// 绘制预制体助手标签页
        /// </summary>
        private void DrawAdaptorTab()
        {
            EditorGUILayout.LabelField("预制体助手 (Prefab Helper)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("快速创建 UI 节点与自动布局工具。", MessageType.Info);
            EditorGUILayout.Space(5);

            helperScrollPos = EditorGUILayout.BeginScrollView(helperScrollPos);

            // 检查是否有选中的 UI 元素
            GameObject selectedGo = Selection.activeGameObject;
            RectTransform selectedRect = selectedGo != null ? selectedGo.GetComponent<RectTransform>() : null;

            // 1. 快速创建 (Quick Create) - 即使没有选中也可以尝试创建（默认作为根节点或Canvas子节点）
            DrawQuickCreateSection(selectedGo);

            EditorGUILayout.Space(10);
            
            if (selectedRect == null)
            {
                EditorGUILayout.HelpBox("选中一个 UI 节点以使用布局工具。", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField($"当前编辑: {selectedGo.name}", EditorStyles.boldLabel);
                EditorGUILayout.Space(5);

                // 2. 快速锚点 (Quick Anchors)
                DrawQuickAnchorsSection(selectedRect);
                
                EditorGUILayout.Space(10);

                // 3. 智能布局 (Smart Layout)
                DrawSmartLayoutSection(selectedRect);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawQuickCreateSection(GameObject parent)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("✨ 快速创建 (Quick Create)", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            GUILayout.BeginHorizontal();
            
            // Create Empty
            if (GUILayout.Button("Empty", GUILayout.Height(30)))
            {
                CreateGameObject("GameObject", parent);
            }
            
            // Create Image
            if (GUILayout.Button("Image", GUILayout.Height(30)))
            {
                GameObject go = CreateGameObject("Image", parent);
                Image img = go.AddComponent<Image>();
                img.raycastTarget = !defaultNoRaycastImage;
                // Assign a default sprite if possible, or leave white
            }
            // Image Raycast Toggle
            defaultNoRaycastImage = EditorGUILayout.ToggleLeft("No Raycast", defaultNoRaycastImage, GUILayout.Width(90));
            
            GUILayout.EndHorizontal();
            GUILayout.Space(5);
            
            GUILayout.BeginHorizontal();
            
            // Create TMP
            if (GUILayout.Button("TMP Text", GUILayout.Height(30)))
            {
                CreateTMPObject(parent);
            }
            
            // TMP Raycast Toggle
            defaultNoRaycastText = EditorGUILayout.ToggleLeft("No Raycast", defaultNoRaycastText, GUILayout.Width(90));
            
            GUILayout.EndHorizontal();
            
            // TMP Font Selector
            if (config != null && config.helper != null)
            {
                var fontList = config.helper.tmpFontGuids;
                if (fontList.Count > 0)
                {
                    string[] fontNames = GetFontNames(fontList);
                    selectedFontIndex = EditorGUILayout.Popup("默认字体:", selectedFontIndex, fontNames);
                }
                else
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("默认字体:", GUILayout.Width(60));
                    EditorGUILayout.LabelField("请在 [设置] 中添加字体", EditorStyles.miniLabel);
                    if (GUILayout.Button("去设置", EditorStyles.miniButton))
                    {
                        currentTab = Tab.Settings; // Jump to settings
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            
            EditorGUILayout.EndVertical();
        }

        private GameObject CreateGameObject(string name, GameObject parent)
        {
            GameObject go = new GameObject(name);
            if (parent != null)
            {
                go.transform.SetParent(parent.transform, false);
                go.layer = parent.layer;
            }
            else
            {
                // Try to find a Canvas
                Canvas canvas = Object.FindObjectOfType<Canvas>();
                if (canvas != null)
                {
                    go.transform.SetParent(canvas.transform, false);
                    go.layer = canvas.gameObject.layer;
                }
            }
            
            go.AddComponent<RectTransform>();
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            Selection.activeGameObject = go;
            return go;
        }

        private void CreateTMPObject(GameObject parent)
        {
            GameObject go = CreateGameObject("Text (TMP)", parent);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.raycastTarget = !defaultNoRaycastText;
            tmp.text = "New Text";
            
            // Apply Font
            if (config != null && config.helper.tmpFontGuids.Count > 0 && selectedFontIndex >= 0 && selectedFontIndex < config.helper.tmpFontGuids.Count)
            {
                string guid = config.helper.tmpFontGuids[selectedFontIndex];
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                {
                    TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                    if (font != null)
                    {
                        tmp.font = font;
                    }
                }
            }
        }
        
        private string[] GetFontNames(List<string> guids)
        {
            string[] names = new string[guids.Count];
            for (int i = 0; i < guids.Count; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrEmpty(path))
                {
                    names[i] = System.IO.Path.GetFileNameWithoutExtension(path);
                }
                else
                {
                    names[i] = "<Missing Font>";
                }
            }
            return names;
        }

        // --- Previously Implemented Adaptor Functions ---
        
        /// <summary>
        /// 绘制快速锚点部分
        /// </summary>
        private void DrawQuickAnchorsSection(RectTransform rect)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("⚓ 快速锚点 (Quick Anchors)", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // 九宫格布局
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            GUILayout.BeginVertical();
            
            // Top Row
            DrawAnchorRow(rect, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(1, 1));
            // Middle Row
            DrawAnchorRow(rect, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(1, 0.5f));
            // Bottom Row
            DrawAnchorRow(rect, new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(1, 0));

            GUILayout.EndVertical();
            
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // 拉伸按钮
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("↔ 水平拉伸", GUILayout.Height(25)))
            {
                Undo.RecordObject(rect, "Stretch Horizontal");
                rect.anchorMin = new Vector2(0, rect.anchorMin.y);
                rect.anchorMax = new Vector2(1, rect.anchorMax.y);
                rect.offsetMin = new Vector2(0, rect.offsetMin.y);
                rect.offsetMax = new Vector2(0, rect.offsetMax.y);
                rect.pivot = new Vector2(0.5f, rect.pivot.y);
            }
            if (GUILayout.Button("↕ 垂直拉伸", GUILayout.Height(25)))
            {
                Undo.RecordObject(rect, "Stretch Vertical");
                rect.anchorMin = new Vector2(rect.anchorMin.x, 0);
                rect.anchorMax = new Vector2(rect.anchorMax.x, 1);
                rect.offsetMin = new Vector2(rect.offsetMin.x, 0);
                rect.offsetMax = new Vector2(rect.offsetMax.x, 0);
                rect.pivot = new Vector2(rect.pivot.x, 0.5f);
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("✛ 全屏拉伸 (Stretch All)", GUILayout.Height(30)))
            {
                Undo.RecordObject(rect, "Stretch All");
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.pivot = new Vector2(0.5f, 0.5f);
            }
            
            // 归零按钮
            if (GUILayout.Button("◎ 位置归零 (Reset Position)", GUILayout.Height(25)))
            {
                Undo.RecordObject(rect, "Reset Position");
                rect.anchoredPosition = Vector2.zero;
            }

            // 当前参数显示
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("📊 当前参数 (Current Parameters)", EditorStyles.miniBoldLabel);
            
            GUIStyle paramStyle = new GUIStyle(EditorStyles.miniLabel);
            paramStyle.fontSize = 9;
            paramStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"Anchor: ({rect.anchorMin.x:F2}, {rect.anchorMin.y:F2}) ~ ({rect.anchorMax.x:F2}, {rect.anchorMax.y:F2})", paramStyle);
            EditorGUILayout.LabelField($"Offset: ({rect.offsetMin.x:F1}, {rect.offsetMin.y:F1}) ~ ({rect.offsetMax.x:F1}, {rect.offsetMax.y:F1})", paramStyle);
            EditorGUILayout.LabelField($"Size: {rect.sizeDelta.x:F1} × {rect.sizeDelta.y:F1}", paramStyle);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndVertical();
        }

        private void DrawAnchorRow(RectTransform rect, Vector2 left, Vector2 center, Vector2 right)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◤", GUILayout.Width(30), GUILayout.Height(30))) SetAnchor(rect, left);
            if (GUILayout.Button("▲", GUILayout.Width(30), GUILayout.Height(30))) SetAnchor(rect, center);
            if (GUILayout.Button("◥", GUILayout.Width(30), GUILayout.Height(30))) SetAnchor(rect, right);
            GUILayout.EndHorizontal();
        }

        private void SetAnchor(RectTransform rect, Vector2 anchor)
        {
            Undo.RecordObject(rect, "Set Anchor");
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor; // 通常设置锚点时也希望pivot跟随
            rect.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// 从RectTransform推断布局类型和参数
        /// </summary>
        private (int layoutType, bool isStretch, string hint) InferLayoutFromRect(RectTransform rect)
        {
            Vector2 anchorMin = rect.anchorMin;
            Vector2 anchorMax = rect.anchorMax;
            
            // 判断是否全屏拉伸
            if (Mathf.Approximately(anchorMin.x, 0) && Mathf.Approximately(anchorMin.y, 0) &&
                Mathf.Approximately(anchorMax.x, 1) && Mathf.Approximately(anchorMax.y, 1))
            {
                return (0, false, "检测到全屏面板布局");
            }
            
            // 判断是否居中
            if (Mathf.Approximately(anchorMin.x, 0.5f) && Mathf.Approximately(anchorMin.y, 0.5f) &&
                Mathf.Approximately(anchorMax.x, 0.5f) && Mathf.Approximately(anchorMax.y, 0.5f))
            {
                return (1, false, "检测到居中窗口布局");
            }
            
            // 判断侧边栏布局
            bool isLeftAligned = Mathf.Approximately(anchorMin.x, 0) && Mathf.Approximately(anchorMax.x, 0);
            bool isRightAligned = Mathf.Approximately(anchorMin.x, 1) && Mathf.Approximately(anchorMax.x, 1);
            bool isTopAligned = Mathf.Approximately(anchorMin.y, 1) && Mathf.Approximately(anchorMax.y, 1);
            bool isBottomAligned = Mathf.Approximately(anchorMin.y, 0) && Mathf.Approximately(anchorMax.y, 0);
            
            bool isVerticalStretch = Mathf.Approximately(anchorMin.y, 0) && Mathf.Approximately(anchorMax.y, 1);
            bool isHorizontalStretch = Mathf.Approximately(anchorMin.x, 0) && Mathf.Approximately(anchorMax.x, 1);
            
            if (isLeftAligned)
            {
                if (isVerticalStretch)
                    return (2, true, "检测到左侧侧边栏 (垂直拉伸)");
                else
                    return (2, false, "检测到左侧侧边栏 (固定高度)");
            }
            else if (isRightAligned)
            {
                if (isVerticalStretch)
                    return (2, true, "检测到右侧侧边栏 (垂直拉伸)");
                else
                    return (2, false, "检测到右侧侧边栏 (固定高度)");
            }
            else if (isTopAligned)
            {
                if (isHorizontalStretch)
                    return (2, true, "检测到顶部侧边栏 (水平拉伸)");
                else
                    return (2, false, "检测到顶部侧边栏 (固定宽度)");
            }
            else if (isBottomAligned)
            {
                if (isHorizontalStretch)
                    return (2, true, "检测到底部侧边栏 (水平拉伸)");
                else
                    return (2, false, "检测到底部侧边栏 (固定宽度)");
            }
            
            return (-1, false, "自定义锚点配置");
        }


        /// <summary>
        /// 绘制智能布局部分
        /// </summary>
        private void DrawSmartLayoutSection(RectTransform rect)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("📐 智能布局 (Smart Layout)", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // 智能状态检测
            var (inferredType, inferredStretch, hint) = InferLayoutFromRect(rect);
            
            if (inferredType >= 0)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField("💡 " + hint, EditorStyles.miniLabel);
                if (GUILayout.Button("应用当前状态", GUILayout.Width(100)))
                {
                    selectedLayoutType = inferredType;
                    adaptorIsStretch = inferredStretch;
                    
                    // 同步参数值
                    if (inferredType == 0) // Full Panel
                    {
                        adaptorPaddingLeft = rect.offsetMin.x;
                        adaptorPaddingBottom = rect.offsetMin.y;
                        adaptorPaddingRight = -rect.offsetMax.x;
                        adaptorPaddingTop = -rect.offsetMax.y;
                    }
                    else if (inferredType == 1) // Center Window
                    {
                        adaptorWidth = rect.sizeDelta.x;
                        adaptorHeight = rect.sizeDelta.y;
                    }
                    else if (inferredType == 2) // Side Widget
                    {
                        // 根据锚点判断对齐方向
                        if (Mathf.Approximately(rect.anchorMin.x, 0) && Mathf.Approximately(rect.anchorMax.x, 0))
                            adaptorSideAlignment = 0; // Left
                        else if (Mathf.Approximately(rect.anchorMin.x, 1) && Mathf.Approximately(rect.anchorMax.x, 1))
                            adaptorSideAlignment = 1; // Right
                        else if (Mathf.Approximately(rect.anchorMin.y, 1) && Mathf.Approximately(rect.anchorMax.y, 1))
                            adaptorSideAlignment = 2; // Top
                        else if (Mathf.Approximately(rect.anchorMin.y, 0) && Mathf.Approximately(rect.anchorMax.y, 0))
                            adaptorSideAlignment = 3; // Bottom
                        
                        // 同步尺寸和边距
                        adaptorWidth = rect.sizeDelta.x;
                        adaptorHeight = rect.sizeDelta.y;
                        adaptorPaddingLeft = rect.offsetMin.x;
                        adaptorPaddingBottom = rect.offsetMin.y;
                        adaptorPaddingRight = -rect.offsetMax.x;
                        adaptorPaddingTop = -rect.offsetMax.y;
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(3);
            }

            // 布局类型选择
            EditorGUI.BeginChangeCheck();
            selectedLayoutType = EditorGUILayout.Popup("布局类型", selectedLayoutType, layoutTypes);
            if (EditorGUI.EndChangeCheck())
            {
                // 可以保存最后一次选择
            }

            EditorGUILayout.Space(5);

            switch (selectedLayoutType)
            {
                case 0: // Full Panel
                    DrawFullPanelSettings(rect);
                    break;
                case 1: // Center Window
                    DrawCenterWindowSettings(rect);
                    break;
                case 2: // Side Widget
                    DrawSideWidgetSettings(rect);
                    break;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawFullPanelSettings(RectTransform rect)
        {
            EditorGUILayout.HelpBox("全屏拉伸，可设置内边距 (Padding)。", MessageType.None);
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("左边距:", GUILayout.Width(50));
            adaptorPaddingLeft = EditorGUILayout.FloatField(adaptorPaddingLeft);
            EditorGUILayout.LabelField("右边距:", GUILayout.Width(50));
            adaptorPaddingRight = EditorGUILayout.FloatField(adaptorPaddingRight);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("上边距:", GUILayout.Width(50));
            adaptorPaddingTop = EditorGUILayout.FloatField(adaptorPaddingTop);
            EditorGUILayout.LabelField("下边距:", GUILayout.Width(50));
            adaptorPaddingBottom = EditorGUILayout.FloatField(adaptorPaddingBottom);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);
            if (GUILayout.Button("应用布局 (Apply)", GUILayout.Height(30)))
            {
                Undo.RecordObject(rect, "Apply Full Panel Layout");
                
                // 设置为全屏拉伸
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                
                // 设置边距
                // offsetMin.x = Left, offsetMin.y = Bottom
                // offsetMax.x = -Right, offsetMax.y = -Top
                rect.offsetMin = new Vector2(adaptorPaddingLeft, adaptorPaddingBottom);
                rect.offsetMax = new Vector2(-adaptorPaddingRight, -adaptorPaddingTop);
            }
        }

        private void DrawCenterWindowSettings(RectTransform rect)
        {
            EditorGUILayout.HelpBox("居中显示，固定大小。", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("宽度:", GUILayout.Width(40));
            adaptorWidth = EditorGUILayout.FloatField(adaptorWidth);
            EditorGUILayout.LabelField("高度:", GUILayout.Width(40));
            adaptorHeight = EditorGUILayout.FloatField(adaptorHeight);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);
            if (GUILayout.Button("应用布局 (Apply)", GUILayout.Height(30)))
            {
                Undo.RecordObject(rect, "Apply Center Window Layout");
                
                // 设置为居中
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                
                // 设置大小
                rect.sizeDelta = new Vector2(adaptorWidth, adaptorHeight);
                rect.anchoredPosition = Vector2.zero;
            }
        }

        private void DrawSideWidgetSettings(RectTransform rect)
        {
            EditorGUILayout.HelpBox("吸附到指定边缘。", MessageType.None);

            adaptorSideAlignment = EditorGUILayout.Popup("对齐方向", adaptorSideAlignment, sideAlignments);

            // Determine if we show Width or Height based on alignment and stretch mode
            bool isVerticalAlign = (adaptorSideAlignment == 0 || adaptorSideAlignment == 1); // Left or Right
            
            string stretchLabel = isVerticalAlign ? "垂直拉伸 (Stretch Vertical)" : "水平拉伸 (Stretch Horizontal)";
            adaptorIsStretch = EditorGUILayout.Toggle(stretchLabel, adaptorIsStretch);

            if (adaptorIsStretch)
            {
                // Stretch Mode: Show Margins
                if (isVerticalAlign)
                {
                    // Left/Right aligned -> Stretch Vertical -> Show Top/Bottom margins
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("上边距:", GUILayout.Width(50));
                    adaptorPaddingTop = EditorGUILayout.FloatField(adaptorPaddingTop);
                    EditorGUILayout.LabelField("下边距:", GUILayout.Width(50));
                    adaptorPaddingBottom = EditorGUILayout.FloatField(adaptorPaddingBottom);
                    EditorGUILayout.EndHorizontal();
                    
                    // Show Width only
                    adaptorWidth = EditorGUILayout.FloatField("宽度:", adaptorWidth);
                }
                else
                {
                    // Top/Bottom aligned -> Stretch Horizontal -> Show Left/Right margins
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("左边距:", GUILayout.Width(50));
                    adaptorPaddingLeft = EditorGUILayout.FloatField(adaptorPaddingLeft);
                    EditorGUILayout.LabelField("右边距:", GUILayout.Width(50));
                    adaptorPaddingRight = EditorGUILayout.FloatField(adaptorPaddingRight);
                    EditorGUILayout.EndHorizontal();

                    // Show Height only
                    adaptorHeight = EditorGUILayout.FloatField("高度:", adaptorHeight);
                }
            }
            else
            {
                // Fixed Size Mode
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("宽度:", GUILayout.Width(40));
                adaptorWidth = EditorGUILayout.FloatField(adaptorWidth);
                EditorGUILayout.LabelField("高度:", GUILayout.Width(40));
                adaptorHeight = EditorGUILayout.FloatField(adaptorHeight);
                EditorGUILayout.EndHorizontal();
            }

            // Alignment specific margin (Sidebar position)
            float margin = 0f;
            if (adaptorSideAlignment == 0) // Left
            {
                adaptorPaddingLeft = EditorGUILayout.FloatField("左边距 (X):", adaptorPaddingLeft);
            }
            else if (adaptorSideAlignment == 1) // Right
            {
                adaptorPaddingRight = EditorGUILayout.FloatField("右边距 (X):", adaptorPaddingRight);
            }
            else if (adaptorSideAlignment == 2) // Top
            {
                adaptorPaddingTop = EditorGUILayout.FloatField("上边距 (Y):", adaptorPaddingTop);
            }
            else if (adaptorSideAlignment == 3) // Bottom
            {
                adaptorPaddingBottom = EditorGUILayout.FloatField("下边距 (Y):", adaptorPaddingBottom);
            }

            EditorGUILayout.Space(10);
            if (GUILayout.Button("应用布局 (Apply)", GUILayout.Height(30)))
            {
                Undo.RecordObject(rect, "Apply Side Widget Layout");
                
                rect.anchoredPosition = Vector2.zero;

                if (adaptorSideAlignment == 0) // Left
                {
                    if (adaptorIsStretch)
                    {
                        // Stretch Vertical
                        rect.anchorMin = new Vector2(0, 0);
                        rect.anchorMax = new Vector2(0, 1);
                        rect.pivot = new Vector2(0, 0.5f);
                        rect.sizeDelta = new Vector2(adaptorWidth, -(adaptorPaddingTop + adaptorPaddingBottom));
                        rect.anchoredPosition = new Vector2(adaptorPaddingLeft, (adaptorPaddingBottom - adaptorPaddingTop) * 0.5f);
                    }
                    else
                    {
                        rect.anchorMin = new Vector2(0, 0.5f);
                        rect.anchorMax = new Vector2(0, 0.5f);
                        rect.pivot = new Vector2(0, 0.5f);
                        rect.sizeDelta = new Vector2(adaptorWidth, adaptorHeight);
                        rect.anchoredPosition = new Vector2(adaptorPaddingLeft, 0);
                    }
                }
                else if (adaptorSideAlignment == 1) // Right
                {
                    if (adaptorIsStretch)
                    {
                         // Stretch Vertical
                        rect.anchorMin = new Vector2(1, 0);
                        rect.anchorMax = new Vector2(1, 1);
                        rect.pivot = new Vector2(1, 0.5f);
                        rect.sizeDelta = new Vector2(adaptorWidth, -(adaptorPaddingTop + adaptorPaddingBottom));
                        rect.anchoredPosition = new Vector2(-adaptorPaddingRight, (adaptorPaddingBottom - adaptorPaddingTop) * 0.5f);
                    }
                    else
                    {
                        rect.anchorMin = new Vector2(1, 0.5f);
                        rect.anchorMax = new Vector2(1, 0.5f);
                        rect.pivot = new Vector2(1, 0.5f);
                        rect.sizeDelta = new Vector2(adaptorWidth, adaptorHeight);
                        rect.anchoredPosition = new Vector2(-adaptorPaddingRight, 0);
                    }
                }
                else if (adaptorSideAlignment == 2) // Top
                {
                    if (adaptorIsStretch)
                    {
                        // Stretch Horizontal
                        rect.anchorMin = new Vector2(0, 1);
                        rect.anchorMax = new Vector2(1, 1);
                        rect.pivot = new Vector2(0.5f, 1);
                        rect.sizeDelta = new Vector2(-(adaptorPaddingLeft + adaptorPaddingRight), adaptorHeight);
                        rect.anchoredPosition = new Vector2((adaptorPaddingLeft - adaptorPaddingRight) * 0.5f, -adaptorPaddingTop);
                    }
                    else
                    {
                        rect.anchorMin = new Vector2(0.5f, 1);
                        rect.anchorMax = new Vector2(0.5f, 1);
                        rect.pivot = new Vector2(0.5f, 1);
                        rect.sizeDelta = new Vector2(adaptorWidth, adaptorHeight);
                        rect.anchoredPosition = new Vector2(0, -adaptorPaddingTop);
                    }
                }
                else if (adaptorSideAlignment == 3) // Bottom
                {
                    if (adaptorIsStretch)
                    {
                        // Stretch Horizontal
                        rect.anchorMin = new Vector2(0, 0);
                        rect.anchorMax = new Vector2(1, 0);
                        rect.pivot = new Vector2(0.5f, 0);
                        rect.sizeDelta = new Vector2(-(adaptorPaddingLeft + adaptorPaddingRight), adaptorHeight);
                        rect.anchoredPosition = new Vector2((adaptorPaddingLeft - adaptorPaddingRight) * 0.5f, adaptorPaddingBottom);
                    }
                    else
                    {
                        rect.anchorMin = new Vector2(0.5f, 0);
                        rect.anchorMax = new Vector2(0.5f, 0);
                        rect.pivot = new Vector2(0.5f, 0);
                        rect.sizeDelta = new Vector2(adaptorWidth, adaptorHeight);
                        rect.anchoredPosition = new Vector2(0, adaptorPaddingBottom);
                    }
                }
            }
        }
        
        /// <summary>
        /// 从配置加载助手设置
        /// </summary>
        private void ApplyHelperConfig()
        {
            if (config == null || config.helper == null) return;
            
            selectedLayoutType = config.helper.lastLayoutType;
            adaptorPaddingLeft = config.helper.paddingLeft;
            adaptorPaddingRight = config.helper.paddingRight;
            adaptorPaddingTop = config.helper.paddingTop;
            adaptorPaddingBottom = config.helper.paddingBottom;
            adaptorWidth = config.helper.width;
            adaptorHeight = config.helper.height;
            adaptorSideAlignment = config.helper.sideAlignment;
            adaptorIsStretch = config.helper.isStretch;
            
            defaultNoRaycastImage = config.helper.defaultNoRaycastImage;
            defaultNoRaycastText = config.helper.defaultNoRaycastText;
            selectedFontIndex = config.helper.defaultFontIndex;
        }
        
        /// <summary>
        /// 收集助手配置
        /// </summary>
        private void CollectHelperConfig()
        {
            if (config == null) return;
            
            if (config.helper == null)
            {
                config.helper = new HelperConfig();
            }
            
            config.helper.lastLayoutType = selectedLayoutType;
            config.helper.paddingLeft = adaptorPaddingLeft;
            config.helper.paddingRight = adaptorPaddingRight;
            config.helper.paddingTop = adaptorPaddingTop;
            config.helper.paddingBottom = adaptorPaddingBottom;
            config.helper.width = adaptorWidth;
            config.helper.height = adaptorHeight;
            config.helper.sideAlignment = adaptorSideAlignment;
            config.helper.isStretch = adaptorIsStretch;
            
            config.helper.defaultNoRaycastImage = defaultNoRaycastImage;
            config.helper.defaultNoRaycastText = defaultNoRaycastText;
            config.helper.defaultFontIndex = selectedFontIndex;
        }
    }
}
