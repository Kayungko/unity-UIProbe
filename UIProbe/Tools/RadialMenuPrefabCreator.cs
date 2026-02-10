using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using Muf.UI.RadialMenu;
using TMPro;

/// <summary>
/// 径向菜单预制体创建工具
/// </summary>
public class RadialMenuPrefabCreator : EditorWindow
{
    private int itemCount = 12;
    private float outerRadius = 400f;
    private float ringThickness = 150f;
    private bool createMenuItem = true;
    private bool createFullMenu = true;
    
    // 右键菜单入口已移除，请使用 UIProbe 工具创建预制体
    // 原有入口: GameObject/UI/Radial Menu/Create Radial Menu Prefab
    // 原有入口: GameObject/UI/Radial Menu/Create Menu Item Prefab
    
    // Tools 菜单入口已移除，请使用 UIProbe 工具创建预制体
    // 原有入口: Tools/Radial Menu/Prefab Creator Window
    
    void OnGUI()
    {
        GUILayout.Label("径向菜单预制体创建工具", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        
        itemCount = EditorGUILayout.IntSlider("菜单项数量", itemCount, 2, 16);
        outerRadius = EditorGUILayout.Slider("外半径", outerRadius, 200f, 800f);
        ringThickness = EditorGUILayout.Slider("环形宽度", ringThickness, 80f, 300f);
        
        EditorGUILayout.Space();
        
        createMenuItem = EditorGUILayout.Toggle("创建 MenuItem 预制体", createMenuItem);
        createFullMenu = EditorGUILayout.Toggle("创建完整菜单预制体", createFullMenu);
        
        EditorGUILayout.Space();
        
        if (GUILayout.Button("创建预制体", GUILayout.Height(30)))
        {
            if (createMenuItem)
                CreateMenuItemPrefab();
            
            if (createFullMenu)
                CreateRadialMenuPrefab();
        }
    }
    
    /// <summary>
    /// 创建完整的径向菜单预制体（供外部调用）
    /// </summary>
    public static void CreateRadialMenuPrefabStatic()
    {
        CreateRadialMenuPrefab();
    }
    
    /// <summary>
    /// 创建完整的径向菜单预制体
    /// </summary>
    static void CreateRadialMenuPrefab()
    {
        // 创建根对象
        GameObject menuRoot = new GameObject("RadialMenu");
        RectTransform rootRt = menuRoot.AddComponent<RectTransform>();
        rootRt.sizeDelta = new Vector2(800, 800);
        
        // 添加 CanvasRenderer（如果父对象是 Canvas）
        menuRoot.AddComponent<CanvasRenderer>();

        // 创建背景 (底板)
        GameObject bgObj = new GameObject("MenuBackground");
        bgObj.transform.SetParent(menuRoot.transform, false);
        RectTransform bgRt = bgObj.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.sizeDelta = Vector2.zero;
        bgRt.anchoredPosition = Vector2.zero;
        
        Image bgImage = bgObj.AddComponent<Image>();
        bgImage.color = new Color(0, 0, 0, 0.4f); // 半透明黑色背景
        bgImage.raycastTarget = true; // 可以阻挡点击

        // 添加控制器
        Muf.UI.RadialMenu.RadialMenuController controller = menuRoot.AddComponent<Muf.UI.RadialMenu.RadialMenuController>();
        
        // 创建容器
        GameObject containerObj = new GameObject("MenuItems");
        containerObj.transform.SetParent(menuRoot.transform, false);
        RectTransform containerRt = containerObj.AddComponent<RectTransform>();
        containerRt.anchorMin = Vector2.zero;
        containerRt.anchorMax = Vector2.one;
        containerRt.sizeDelta = Vector2.zero;
        containerRt.anchoredPosition = Vector2.zero;
        
        // 创建中心信息面板
        GameObject centerPanel = CreateCenterInfoPanel();
        centerPanel.transform.SetParent(menuRoot.transform, false);
        
        // 使用反射设置私有字段（或者直接在 Inspector 中手动拖拽）
        SerializedObject so = new SerializedObject(controller);
        so.FindProperty("menuItemsContainer").objectReferenceValue = containerObj.transform;
        so.FindProperty("centerInfoPanel").objectReferenceValue = centerPanel;
        so.FindProperty("centerIcon").objectReferenceValue = centerPanel.transform.Find("Icon").GetComponent<Image>();
        so.FindProperty("centerTitle").objectReferenceValue = centerPanel.transform.Find("Title").GetComponent<TextMeshProUGUI>();
        so.FindProperty("centerDescription").objectReferenceValue = centerPanel.transform.Find("Description").GetComponent<TextMeshProUGUI>();
        so.ApplyModifiedProperties();
        
        // 保存为预制体
        string path = "Assets/UI/Prefabs/UI_Battle/Battle_RadialMenu.prefab";
        string directory = System.IO.Path.GetDirectoryName(path);
        if (!System.IO.Directory.Exists(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }
        
        PrefabUtility.SaveAsPrefabAsset(menuRoot, path);
        
        Debug.Log($"[RadialMenu] 预制体已创建: {path}");
        
        // 清理场景对象
        DestroyImmediate(menuRoot);
        
        // 选中并高亮预制体
        Object prefab = AssetDatabase.LoadAssetAtPath<Object>(path);
        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
    }
    
    
    /// <summary>
    /// 创建菜单项预制体（供外部调用）
    /// </summary>
    public static void CreateMenuItemPrefabStatic()
    {
        CreateMenuItemPrefab();
    }
    
    /// <summary>
    /// 创建菜单项预制体
    /// </summary>
    static void CreateMenuItemPrefab()
    {
        // 创建根对象
        GameObject item = new GameObject("RadialMenuItem");
        RectTransform rt = item.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.one * 0.5f;
        rt.anchorMax = Vector2.one * 0.5f;
        rt.pivot = Vector2.one * 0.5f;
        rt.sizeDelta = new Vector2(800, 800);
        
        // 添加 RadialMenuItem 组件
        RadialMenuItem menuItem = item.AddComponent<RadialMenuItem>();
        
        // 创建背景 Image (Filled)
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(item.transform, false);
        RectTransform bgRt = bgObj.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.one * 0.5f;
        bgRt.anchorMax = Vector2.one * 0.5f;
        bgRt.pivot = Vector2.one * 0.5f;
        bgRt.anchoredPosition = Vector2.zero;
        bgRt.sizeDelta = new Vector2(800, 800);
        Image bgImage = bgObj.AddComponent<Image>();
        bgImage.color = new Color(0, 0, 0, 0.6f);
        bgImage.type = Image.Type.Filled;
        bgImage.fillMethod = Image.FillMethod.Radial360;
        bgImage.fillOrigin = (int)Image.Origin360.Top;
        
        // 创建品质条 Image (Filled)
        GameObject qualityBarObj = new GameObject("QualityBar");
        qualityBarObj.transform.SetParent(item.transform, false);
        RectTransform qualityBarRt = qualityBarObj.AddComponent<RectTransform>();
        qualityBarRt.anchorMin = Vector2.one * 0.5f;
        qualityBarRt.anchorMax = Vector2.one * 0.5f;
        qualityBarRt.pivot = Vector2.one * 0.5f;
        qualityBarRt.anchoredPosition = Vector2.zero;
        qualityBarRt.sizeDelta = new Vector2(800, 800);
        Image qualityBar = qualityBarObj.AddComponent<Image>();
        qualityBar.color = Color.white;
        qualityBar.type = Image.Type.Filled;
        qualityBar.fillMethod = Image.FillMethod.Radial360;
        qualityBar.fillOrigin = (int)Image.Origin360.Top;
        
        // 创建品质渐变 Image (Filled)
        GameObject qualityGradientObj = new GameObject("QualityGradient");
        qualityGradientObj.transform.SetParent(item.transform, false);
        RectTransform qualityGradientRt = qualityGradientObj.AddComponent<RectTransform>();
        qualityGradientRt.anchorMin = Vector2.one * 0.5f;
        qualityGradientRt.anchorMax = Vector2.one * 0.5f;
        qualityGradientRt.pivot = Vector2.one * 0.5f;
        qualityGradientRt.anchoredPosition = Vector2.zero;
        qualityGradientRt.sizeDelta = new Vector2(800, 800);
        Image qualityGradient = qualityGradientObj.AddComponent<Image>();
        qualityGradient.color = new Color(1f, 1f, 1f, 0.3f);
        qualityGradient.type = Image.Type.Filled;
        qualityGradient.fillMethod = Image.FillMethod.Radial360;
        qualityGradient.fillOrigin = (int)Image.Origin360.Top;
        
        // 创建高亮 Image (Filled)
        GameObject hlObj = new GameObject("Highlight");
        hlObj.transform.SetParent(item.transform, false);
        RectTransform hlRt = hlObj.AddComponent<RectTransform>();
        hlRt.anchorMin = Vector2.one * 0.5f;
        hlRt.anchorMax = Vector2.one * 0.5f;
        hlRt.pivot = Vector2.one * 0.5f;
        hlRt.anchoredPosition = Vector2.zero;
        hlRt.sizeDelta = new Vector2(800, 800);
        Image hlImage = hlObj.AddComponent<Image>();
        hlImage.color = new Color(1f, 0.6f, 0.2f, 0f);
        hlImage.type = Image.Type.Filled;
        hlImage.fillMethod = Image.FillMethod.Radial360;
        hlImage.fillOrigin = (int)Image.Origin360.Top;
        
        // 创建 Item_Container（普通物品容器）
        GameObject itemContainerObj = new GameObject("Item_Container");
        itemContainerObj.transform.SetParent(item.transform, false);
        RectTransform itemContainerRt = itemContainerObj.AddComponent<RectTransform>();
        itemContainerRt.anchorMin = Vector2.one * 0.5f;
        itemContainerRt.anchorMax = Vector2.one * 0.5f;
        itemContainerRt.pivot = Vector2.one * 0.5f;
        itemContainerRt.anchoredPosition = Vector2.zero;
        
        // 创建图标（作为 Item_Container 的子节点）
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(itemContainerObj.transform, false);
        RectTransform iconRt = iconObj.AddComponent<RectTransform>();
        iconRt.anchorMin = Vector2.one * 0.5f;
        iconRt.anchorMax = Vector2.one * 0.5f;
        iconRt.pivot = Vector2.one * 0.5f;
        iconRt.sizeDelta = new Vector2(60, 60);
        Image icon = iconObj.AddComponent<Image>();
        icon.raycastTarget = false;
        
        // 创建数量文本（作为 Item_Container 的子节点）
        GameObject countObj = new GameObject("Count");
        countObj.transform.SetParent(itemContainerObj.transform, false);
        RectTransform countRt = countObj.AddComponent<RectTransform>();
        countRt.anchorMin = Vector2.one * 0.5f;
        countRt.anchorMax = Vector2.one * 0.5f;
        countRt.pivot = Vector2.one * 0.5f;
        countRt.anchoredPosition = new Vector2(0, -30); // 在图标下方
        countRt.sizeDelta = new Vector2(50, 30);
        TextMeshProUGUI countText = countObj.AddComponent<TextMeshProUGUI>();
        countText.alignment = TextAlignmentOptions.Center;
        countText.fontSize = 18;
        countText.color = Color.white;
        countText.raycastTarget = false;
        countText.text = "1";
        
        // 创建 CloseButton_Container（关闭按钮容器）
        GameObject closeButtonContainerObj = new GameObject("CloseButton_Container");
        closeButtonContainerObj.transform.SetParent(item.transform, false);
        RectTransform closeButtonContainerRt = closeButtonContainerObj.AddComponent<RectTransform>();
        closeButtonContainerRt.anchorMin = Vector2.one * 0.5f;
        closeButtonContainerRt.anchorMax = Vector2.one * 0.5f;
        closeButtonContainerRt.pivot = Vector2.one * 0.5f;
        closeButtonContainerRt.anchoredPosition = Vector2.zero;
        
        // 创建关闭按钮图标（作为 CloseButton_Container 的子节点）
        GameObject closeIconObj = new GameObject("CloseIcon");
        closeIconObj.transform.SetParent(closeButtonContainerObj.transform, false);
        RectTransform closeIconRt = closeIconObj.AddComponent<RectTransform>();
        closeIconRt.anchorMin = Vector2.one * 0.5f;
        closeIconRt.anchorMax = Vector2.one * 0.5f;
        closeIconRt.pivot = Vector2.one * 0.5f;
        closeIconRt.sizeDelta = new Vector2(60, 60);
        Image closeIcon = closeIconObj.AddComponent<Image>();
        closeIcon.raycastTarget = false;
        
        // 创建选择指向标
        GameObject indicatorObj = new GameObject("SelectionIndicator");
        indicatorObj.transform.SetParent(item.transform, false);
        RectTransform indicatorRt = indicatorObj.AddComponent<RectTransform>();
        indicatorRt.anchorMin = Vector2.one * 0.5f;
        indicatorRt.anchorMax = Vector2.one * 0.5f;
        indicatorRt.pivot = Vector2.one * 0.5f;
        indicatorRt.sizeDelta = new Vector2(20, 20);
        Image indicatorImg = indicatorObj.AddComponent<Image>();
        indicatorImg.color = Color.white;
        indicatorImg.raycastTarget = false;
        indicatorObj.SetActive(false); // 初始隐藏
        
        // 使用反射设置私有字段
        SerializedObject so = new SerializedObject(menuItem);
        so.FindProperty("backgroundImage").objectReferenceValue = bgImage;
        so.FindProperty("m_Image_Highlight").objectReferenceValue = hlImage;
        
        // 容器引用
        so.FindProperty("itemContainer").objectReferenceValue = itemContainerRt;
        so.FindProperty("closeButtonContainer").objectReferenceValue = closeButtonContainerRt;
        
        // 图标和文本
        so.FindProperty("m_Icon_Item").objectReferenceValue = icon;
        so.FindProperty("m_Icon_CloseMenu").objectReferenceValue = closeIcon;
        so.FindProperty("m_Text_Count").objectReferenceValue = countText;
        
        // 品质系统组件
        so.FindProperty("m_Image_QualityBar").objectReferenceValue = qualityBar;
        so.FindProperty("m_Image_QualityGradient").objectReferenceValue = qualityGradient;
        
        // 选择状态组件
        so.FindProperty("selectionIndicator").objectReferenceValue = indicatorObj;
        so.FindProperty("indicatorImage").objectReferenceValue = indicatorImg;
        
        so.ApplyModifiedProperties();
        
        // 保存为预制体
        string path = "Assets/UI/Prefabs/UI_Battle/Battle_RadialMenuItem.prefab";
        string directory = System.IO.Path.GetDirectoryName(path);
        if (!System.IO.Directory.Exists(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }
        
        PrefabUtility.SaveAsPrefabAsset(item, path);
        
        Debug.Log($"[RadialMenu] MenuItem 预制体已创建: {path}");
        
        // 清理场景对象
        DestroyImmediate(item);
        
        // 选中并高亮预制体
        Object prefab = AssetDatabase.LoadAssetAtPath<Object>(path);
        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
    }
    
    /// <summary>
    /// 创建中心信息面板
    /// </summary>
    static GameObject CreateCenterInfoPanel()
    {
        GameObject panel = new GameObject("CenterInfo");
        RectTransform panelRt = panel.AddComponent<RectTransform>();
        panelRt.anchorMin = Vector2.one * 0.5f;
        panelRt.anchorMax = Vector2.one * 0.5f;
        panelRt.pivot = Vector2.one * 0.5f;
        panelRt.sizeDelta = new Vector2(200, 200);
        panelRt.anchoredPosition = Vector2.zero;
        
        // 背景
        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.8f);
        
        // 图标
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(panel.transform, false);
        RectTransform iconRt = iconObj.AddComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0.5f, 0.7f);
        iconRt.anchorMax = new Vector2(0.5f, 0.7f);
        iconRt.pivot = Vector2.one * 0.5f;
        iconRt.sizeDelta = new Vector2(80, 80);
        Image icon = iconObj.AddComponent<Image>();
        icon.raycastTarget = false;
        
        // 标题
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(panel.transform, false);
        RectTransform titleRt = titleObj.AddComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0f, 0.4f);
        titleRt.anchorMax = new Vector2(1f, 0.6f);
        titleRt.pivot = Vector2.one * 0.5f;
        titleRt.offsetMin = new Vector2(10, 0);
        titleRt.offsetMax = new Vector2(-10, 0);
        TextMeshProUGUI title = titleObj.AddComponent<TextMeshProUGUI>();
        title.text = "物品名称";
        title.alignment = TextAlignmentOptions.Center;
        title.fontSize = 20;
        title.color = Color.white;
        title.raycastTarget = false;
        
        // 描述
        GameObject descObj = new GameObject("Description");
        descObj.transform.SetParent(panel.transform, false);
        RectTransform descRt = descObj.AddComponent<RectTransform>();
        descRt.anchorMin = new Vector2(0f, 0f);
        descRt.anchorMax = new Vector2(1f, 0.4f);
        descRt.pivot = Vector2.one * 0.5f;
        descRt.offsetMin = new Vector2(10, 10);
        descRt.offsetMax = new Vector2(-10, 0);
        TextMeshProUGUI desc = descObj.AddComponent<TextMeshProUGUI>();
        desc.text = "物品描述";
        desc.alignment = TextAlignmentOptions.Top;
        desc.fontSize = 14;
        desc.color = new Color(0.9f, 0.9f, 0.9f);
        desc.raycastTarget = false;
        
        panel.SetActive(false);
        
        return panel;
    }
}
