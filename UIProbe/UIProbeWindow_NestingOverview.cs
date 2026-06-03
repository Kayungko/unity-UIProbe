using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;

namespace UIProbe
{
    partial class UIProbeWindow
    {
        // 嵌套关系总览状态
        private Vector2 nestingOverviewScrollPos;
        private NestingViewMode currentNestingView = NestingViewMode.Forward; // 当前视图模式
        private string nestingFolderFilter = ""; // 文件夹筛选
        private int nestingCountFilter = 0; // 嵌套数量筛选 (0=全部, 1=有嵌套, 2=特定数量)
        private int nestingCountMin = 1; // 最小嵌套数量
        private bool showMultiLevelNesting = true; // 显示多层嵌套
        private List<PrefabNestingInfo> nestingInfoList = new List<PrefabNestingInfo>();
        private Dictionary<string, List<ParentPrefabInfo>> reverseNestingIndex = new Dictionary<string, List<ParentPrefabInfo>>();
        private List<PrefabOverrideInfo> overrideInfoList = new List<PrefabOverrideInfo>();
        
        // 视图模式枚举
        private enum NestingViewMode
        {
            Forward,   // 正向视图
            Reverse,   // 反向视图
            Override   // Override检测视图
        }
        
        /// <summary>
        /// 预制体嵌套信息
        /// </summary>
        private class PrefabNestingInfo
        {
            public string PrefabName;
            public string PrefabPath;
            public string FolderPath;
            public List<NestedPrefabReference> NestedPrefabs = new List<NestedPrefabReference>();
            public int TotalInstanceCount; // 总实例数
            public bool IsExpanded = false; // 展开状态
        }
        
        /// <summary>
        /// 嵌套的预制体引用
        /// </summary>
        private class NestedPrefabReference
        {
            public string PrefabName;
            public string PrefabPath;
            public int InstanceCount; // 该预制体在父预制体中出现的次数
            public List<string> NodePaths = new List<string>(); // 节点路径列表
            public bool HasDeepNesting; // 是否存在多层嵌套
            public List<string> DeepNestedPrefabs = new List<string>(); // 多层嵌套的预制体名称
            public bool IsLocationExpanded; // 多实例路径是否展开
        }
        
        /// <summary>
        /// 父预制体信息 (用于反向视图)
        /// </summary>
        private class ParentPrefabInfo
        {
            public string PrefabName;
            public string PrefabPath;
            public int InstanceCount;
        }
        
        /// <summary>
        /// 预制体Override信息
        /// </summary>
        private class PrefabOverrideInfo
        {
            public string ParentPrefabName;
            public string ParentPrefabPath;
            public string ParentFolderPath;
            public List<NestedOverrideInstance> OverrideInstances = new List<NestedOverrideInstance>();
            public int TotalOverrideCount; // 总修改数量
            public bool IsExpanded = false;
        }
        
        /// <summary>
        /// 嵌套实例的Override信息
        /// </summary>
        private class NestedOverrideInstance
        {
            public string NestedPrefabName;
            public string NestedPrefabPath;
            public string InstancePath; // 实例在父预制体中的路径
            public List<string> PropertyModifications = new List<string>(); // 属性修改
            public List<string> AddedComponents = new List<string>(); // 新增组件
            public List<string> RemovedComponents = new List<string>(); // 删除组件
            public int TotalModCount => PropertyModifications.Count + AddedComponents.Count + RemovedComponents.Count;
        }
        
        /// <summary>
        /// 绘制嵌套关系总览标签页
        /// </summary>
        private void DrawNestingOverviewTab()
        {
            EditorGUILayout.LabelField("预制体嵌套关系总览 (Nesting Overview)", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            if (!isIndexBuilt)
            {
                EditorGUILayout.HelpBox("请先在「预制体索引」标签页点击「刷新」按钮建立索引。", MessageType.Info);
                return;
            }
            
            // 工具栏
            DrawNestingOverviewToolbar();
            
            EditorGUILayout.Space(5);
            
            // 筛选器
            DrawNestingFilters();
            
            EditorGUILayout.Space(10);
            
            // 视图切换按钮
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(currentNestingView == NestingViewMode.Forward, "正向视图 (预制体嵌套哪些)", EditorStyles.toolbarButton))
            {
                if (currentNestingView != NestingViewMode.Forward)
                {
                    currentNestingView = NestingViewMode.Forward;
                    BuildForwardNestingData();
                }
            }
            if (GUILayout.Toggle(currentNestingView == NestingViewMode.Reverse, "反向视图 (预制体被哪些引用)", EditorStyles.toolbarButton))
            {
                if (currentNestingView != NestingViewMode.Reverse)
                {
                    currentNestingView = NestingViewMode.Reverse;
                    BuildReverseNestingData();
                }
            }
            if (GUILayout.Toggle(currentNestingView == NestingViewMode.Override, "Override检测 (嵌套预制体被修改)", EditorStyles.toolbarButton))
            {
                if (currentNestingView != NestingViewMode.Override)
                {
                    currentNestingView = NestingViewMode.Override;
                    BuildOverrideData();
                }
            }
            GUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            // 显示内容
            switch (currentNestingView)
            {
                case NestingViewMode.Forward:
                    DrawForwardNestingView();
                    break;
                case NestingViewMode.Reverse:
                    DrawReverseNestingView();
                    break;
                case NestingViewMode.Override:
                    DrawOverrideView();
                    break;
            }
        }
        
        /// <summary>
        /// 绘制工具栏
        /// </summary>
        private void DrawNestingOverviewToolbar()
        {
            GUILayout.BeginHorizontal();
            
            if (GUILayout.Button("🔄 刷新数据", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                switch (currentNestingView)
                {
                    case NestingViewMode.Forward:
                        BuildForwardNestingData();
                        break;
                    case NestingViewMode.Reverse:
                        BuildReverseNestingData();
                        break;
                    case NestingViewMode.Override:
                        BuildOverrideData();
                        break;
                }
            }
            
            if (GUILayout.Button("📊 导出CSV", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                ExportNestingToCSV();
            }
            
            GUILayout.FlexibleSpace();
            
            // 多层嵌套显示切换
            showMultiLevelNesting = GUILayout.Toggle(showMultiLevelNesting, "显示多层嵌套", EditorStyles.toolbarButton, GUILayout.Width(100));
            
            GUILayout.EndHorizontal();
        }
        
        /// <summary>
        /// 绘制筛选器
        /// </summary>
        private void DrawNestingFilters()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("筛选器", EditorStyles.boldLabel);
            
            // 文件夹筛选
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("文件夹:", GUILayout.Width(60));
            EditorGUI.BeginChangeCheck();
            nestingFolderFilter = EditorGUILayout.TextField(nestingFolderFilter, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck())
            {
                // 自动刷新
                switch (currentNestingView)
                {
                    case NestingViewMode.Forward:
                        BuildForwardNestingData();
                        break;
                    case NestingViewMode.Reverse:
                        BuildReverseNestingData();
                        break;
                    case NestingViewMode.Override:
                        BuildOverrideData();
                        break;
                }
            }
            
            if (!string.IsNullOrEmpty(nestingFolderFilter))
            {
                if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(25)))
                {
                    nestingFolderFilter = "";
                    switch (currentNestingView)
                    {
                        case NestingViewMode.Forward:
                            BuildForwardNestingData();
                            break;
                        case NestingViewMode.Reverse:
                            BuildReverseNestingData();
                            break;
                        case NestingViewMode.Override:
                            BuildOverrideData();
                            break;
                    }
                }
            }
            GUILayout.EndHorizontal();
            
            // 嵌套数量筛选
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("嵌套数量:", GUILayout.Width(70));
            
            EditorGUI.BeginChangeCheck();
            string[] filterOptions = { "全部", "有嵌套", $"≥{nestingCountMin}" };
            nestingCountFilter = GUILayout.SelectionGrid(nestingCountFilter, filterOptions, 3);
            
            if (nestingCountFilter == 2)
            {
                nestingCountMin = EditorGUILayout.IntField(nestingCountMin, GUILayout.Width(50));
                nestingCountMin = Mathf.Max(1, nestingCountMin);
            }
            
            if (EditorGUI.EndChangeCheck())
            {
                // 自动刷新
                switch (currentNestingView)
                {
                    case NestingViewMode.Forward:
                        BuildForwardNestingData();
                        break;
                    case NestingViewMode.Reverse:
                        BuildReverseNestingData();
                        break;
                    case NestingViewMode.Override:
                        BuildOverrideData();
                        break;
                }
            }
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
        }
        
        /// <summary>
        /// 绘制正向嵌套视图
        /// </summary>
        private void DrawForwardNestingView()
        {
            if (nestingInfoList.Count == 0)
            {
                EditorGUILayout.HelpBox("没有找到符合条件的预制体。", MessageType.Info);
                return;
            }
            
            EditorGUILayout.LabelField($"共 {nestingInfoList.Count} 个预制体符合筛选条件:", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            nestingOverviewScrollPos = EditorGUILayout.BeginScrollView(nestingOverviewScrollPos);
            
            foreach (var info in nestingInfoList)
            {
                DrawForwardNestingCard(info);
            }
            
            EditorGUILayout.EndScrollView();
        }
        
        /// <summary>
        /// 绘制正向嵌套卡片
        /// </summary>
        private void DrawForwardNestingCard(PrefabNestingInfo info)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            
            // 标题栏
            GUILayout.BeginHorizontal();
            
            // 展开/折叠按钮
            string expandIcon = info.IsExpanded ? "▼" : "▶";
            if (GUILayout.Button(expandIcon, GUILayout.Width(20)))
            {
                info.IsExpanded = !info.IsExpanded;
            }
            
            // 预制体名称
            EditorGUILayout.LabelField($"📦 {info.PrefabName}", EditorStyles.boldLabel, GUILayout.MinWidth(100), GUILayout.ExpandWidth(true));

            // 统计信息
            EditorGUILayout.LabelField($"(嵌套 {info.NestedPrefabs.Count} 种预制体，共 {info.TotalInstanceCount} 个实例)",
                EditorStyles.miniLabel, GUILayout.MinWidth(80), GUILayout.ExpandWidth(true));
            
            GUILayout.FlexibleSpace();
            
            // 操作按钮
            if (GUILayout.Button("打开", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                var obj = AssetDatabase.LoadAssetAtPath<GameObject>(info.PrefabPath);
                if (obj != null) AssetDatabase.OpenAsset(obj);
            }
            
            if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                var obj = AssetDatabase.LoadAssetAtPath<GameObject>(info.PrefabPath);
                if (obj != null) EditorGUIUtility.PingObject(obj);
            }
            
            GUILayout.EndHorizontal();
            
            // 文件夹路径
            EditorGUILayout.LabelField(info.FolderPath, EditorStyles.miniLabel);
            
            // 展开详情
            if (info.IsExpanded && info.NestedPrefabs.Count > 0)
            {
                EditorGUILayout.Space(3);
                
                foreach (var nested in info.NestedPrefabs)
                {
                    DrawNestedPrefabItem(nested, info.PrefabPath);
                }
            }
            
            GUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }
        
        /// <summary>
        /// 绘制嵌套预制体条目
        /// </summary>
        private void DrawNestedPrefabItem(NestedPrefabReference nested, string parentPrefabPath)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            
            // 预制体图标和名称
            EditorGUILayout.LabelField("└─", GUILayout.Width(20));
            EditorGUILayout.LabelField(nested.PrefabName, EditorStyles.label, GUILayout.Width(200));
            
            // 实例数量
            EditorGUILayout.LabelField($"× {nested.InstanceCount}", EditorStyles.miniLabel, GUILayout.Width(50));
            
            GUILayout.FlexibleSpace();
            
            // 【新增】根据实例数量显示不同的按钮
            if (nested.InstanceCount == 1)
            {
                // 只有1个实例：直接显示定位按钮
                if (GUILayout.Button("📍 定位", EditorStyles.miniButton, GUILayout.Width(60)))
                {
                    LocateNestedInPrefab(parentPrefabPath, nested.NodePaths[0]);
                }
            }
            else
            {
                // 多个实例：显示展开按钮
                string expandIcon = nested.IsLocationExpanded ? "▼" : "▶";
                if (GUILayout.Button($"{expandIcon} 位置 ({nested.InstanceCount})", EditorStyles.miniButton, GUILayout.Width(85)))
                {
                    nested.IsLocationExpanded = !nested.IsLocationExpanded;
                }
            }
            
            // 定位到Project按钮
            if (GUILayout.Button("📁 Project", EditorStyles.miniButton, GUILayout.Width(70)))
            {
                var obj = AssetDatabase.LoadAssetAtPath<GameObject>(nested.PrefabPath);
                if (obj != null)
                {
                    EditorGUIUtility.PingObject(obj);
                    Selection.activeObject = obj;
                }
            }
            
            GUILayout.EndHorizontal();
            
            // 【新增】展开后显示所有实例路径
            if (nested.InstanceCount > 1 && nested.IsLocationExpanded)
            {
                EditorGUILayout.Space(2);
                
                for (int i = 0; i < nested.NodePaths.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(60);
                    
                    // 路径显示
                    EditorGUILayout.LabelField($"📍 {nested.NodePaths[i]}", EditorStyles.miniLabel);
                    
                    GUILayout.FlexibleSpace();
                    
                    // 每个路径的定位按钮
                    if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(40)))
                    {
                        LocateNestedInPrefab(parentPrefabPath, nested.NodePaths[i]);
                    }
                    
                    GUILayout.EndHorizontal();
                }
            }
            else if (nested.InstanceCount == 1 && nested.NodePaths.Count > 0)
            {
                // 单个实例时，也显示路径（保持原有功能）
                GUILayout.BeginHorizontal();
                GUILayout.Space(40);
                EditorGUILayout.LabelField($"📍 {nested.NodePaths[0]}", EditorStyles.miniLabel);
                GUILayout.EndHorizontal();
            }
            
            // 多层嵌套信息
            if (showMultiLevelNesting && nested.HasDeepNesting && nested.DeepNestedPrefabs.Count > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(40);
                
                GUIStyle deepNestingStyle = new GUIStyle(EditorStyles.miniLabel);
                deepNestingStyle.normal.textColor = new Color(0.5f, 0.7f, 1f);
                
                string deepInfo = "  ↳ 深层嵌套: " + string.Join(", ", nested.DeepNestedPrefabs);
                EditorGUILayout.LabelField(deepInfo, deepNestingStyle);
                GUILayout.EndHorizontal();
            }
            
            GUILayout.EndVertical();
        }
        
        /// <summary>
        /// 在父预制体内定位嵌套实例
        /// </summary>
        private void LocateNestedInPrefab(string parentPrefabPath, string nodePath)
        {
            try
            {
                // 【修复】先检查并关闭当前打开的预制体舞台
                var currentStage = PrefabStageUtility.GetCurrentPrefabStage();
                if (currentStage != null)
                {
                    // 检查是否就是要打开的预制体
                    if (currentStage.assetPath == parentPrefabPath)
                    {
                        // 已经打开了目标预制体，直接定位
                        Transform currentRoot = currentStage.prefabContentsRoot.transform;
                        Transform currentTarget = FindTransformByPath(currentRoot, nodePath);
                        
                        if (currentTarget != null)
                        {
                            Selection.activeGameObject = currentTarget.gameObject;
                            EditorGUIUtility.PingObject(currentTarget.gameObject);
                            EditorApplication.delayCall += () =>
                            {
                                SceneView.lastActiveSceneView?.FrameSelected();
                            };
                        }
                        else
                        {
                            Debug.LogWarning($"未找到节点: {nodePath}");
                        }
                        return;
                    }
                    
                    // 关闭当前预制体舞台，回到主舞台（不保存更改）
                    StageUtility.GoToMainStage();
                }
                
                // 加载父预制体
                var parentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(parentPrefabPath);
                if (parentPrefab == null)
                {
                    Debug.LogWarning($"无法加载预制体: {parentPrefabPath}");
                    return;
                }
                
                // 打开预制体
                var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.OpenPrefab(parentPrefabPath);
                if (prefabStage == null)
                {
                    Debug.LogWarning($"无法打开预制体: {parentPrefabPath}");
                    return;
                }
                
                // 查找嵌套实例
                Transform root = prefabStage.prefabContentsRoot.transform;
                Transform target = FindTransformByPath(root, nodePath);
                
                if (target != null)
                {
                    // 选中并高亮显示
                    Selection.activeGameObject = target.gameObject;
                    EditorGUIUtility.PingObject(target.gameObject);
                    
                    // 展开Hierarchy中的父节点
                    EditorApplication.delayCall += () =>
                    {
                        SceneView.lastActiveSceneView?.FrameSelected();
                    };
                }
                else
                {
                    Debug.LogWarning($"未找到节点: {nodePath}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"定位失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 根据路径查找Transform
        /// </summary>
        private Transform FindTransformByPath(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root;
            
            string[] parts = path.Split('/');
            Transform current = root;
            
            foreach (string part in parts)
            {
                Transform found = null;
                foreach (Transform child in current)
                {
                    if (child.name == part)
                    {
                        found = child;
                        break;
                    }
                }
                
                if (found == null) return null;
                current = found;
            }
            
            return current;
        }
        
        /// <summary>
        /// 绘制反向嵌套视图
        /// </summary>
        private void DrawReverseNestingView()
        {
            if (reverseNestingIndex.Count == 0)
            {
                EditorGUILayout.HelpBox("没有找到符合条件的预制体。", MessageType.Info);
                return;
            }
            
            EditorGUILayout.LabelField($"共 {reverseNestingIndex.Count} 个预制体被其他预制体引用:", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            nestingOverviewScrollPos = EditorGUILayout.BeginScrollView(nestingOverviewScrollPos);
            
            foreach (var kvp in reverseNestingIndex.OrderByDescending(x => x.Value.Sum(p => p.InstanceCount)))
            {
                DrawReverseNestingCard(kvp.Key, kvp.Value);
            }
            
            EditorGUILayout.EndScrollView();
        }
        
        /// <summary>
        /// 绘制反向嵌套卡片
        /// </summary>
        private void DrawReverseNestingCard(string childPrefabPath, List<ParentPrefabInfo> parents)
        {
            string childName = Path.GetFileNameWithoutExtension(childPrefabPath);
            int totalInstances = parents.Sum(p => p.InstanceCount);
            
            GUILayout.BeginVertical(EditorStyles.helpBox);
            
            // 标题栏
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"📦 {childName}", EditorStyles.boldLabel, GUILayout.MinWidth(100), GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField($"(被 {parents.Count} 个预制体引用，共 {totalInstances} 个实例)",
                EditorStyles.miniLabel, GUILayout.MinWidth(80), GUILayout.ExpandWidth(true));
            
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                var obj = AssetDatabase.LoadAssetAtPath<GameObject>(childPrefabPath);
                if (obj != null) EditorGUIUtility.PingObject(obj);
            }
            
            GUILayout.EndHorizontal();
            
            EditorGUILayout.Space(3);
            
            // 父预制体列表
            foreach (var parent in parents.OrderByDescending(p => p.InstanceCount))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                
                EditorGUILayout.LabelField("←", GUILayout.Width(20));
                
                if (GUILayout.Button(parent.PrefabName, EditorStyles.linkLabel, GUILayout.Width(200)))
                {
                    var obj = AssetDatabase.LoadAssetAtPath<GameObject>(parent.PrefabPath);
                    if (obj != null)
                    {
                        EditorGUIUtility.PingObject(obj);
                        Selection.activeObject = obj;
                    }
                }
                
                EditorGUILayout.LabelField($"({parent.InstanceCount}次)", EditorStyles.miniLabel, GUILayout.Width(60));
                
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            
            GUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }
        
        /// <summary>
        /// 构建正向嵌套数据
        /// </summary>
        private void BuildForwardNestingData()
        {
            nestingInfoList.Clear();
            
            foreach (var prefab in allPrefabs)
            {
                // 文件夹筛选
                if (!string.IsNullOrEmpty(nestingFolderFilter) && 
                    !prefab.FolderPath.ToLower().Contains(nestingFolderFilter.ToLower()))
                {
                    continue;
                }
                
                // 获取预制体类型的引用
                var prefabRefs = prefab.GetReferencesByType(AssetReferenceType.Prefab);
                
                if (prefabRefs.Count == 0)
                {
                    // 嵌套数量筛选：如果选择了"有嵌套"，则跳过没有嵌套的预制体
                    if (nestingCountFilter >= 1)
                    {
                        continue;
                    }
                }
                
                // 【修复Bug】只统计直接嵌套的预制体（排除深层递归嵌套）
                // 算法：对于每个预制体引用，检查其NodePath的所有父路径是否也是预制体引用
                // 如果是，说明这是深层嵌套，应该排除
                var pathToAsset = new Dictionary<string, string>(); // NodePath -> AssetPath
                foreach (var r in prefabRefs)
                {
                    pathToAsset[r.NodePath] = r.AssetPath;
                }
                
                var directPrefabRefs = new List<AssetReference>();
                foreach (var r in prefabRefs)
                {
                    bool isDeepNested = false;
                    string[] pathParts = r.NodePath.Split('/');
                    
                    // 检查所有父路径
                    for (int i = 1; i < pathParts.Length; i++)
                    {
                        string parentPath = string.Join("/", pathParts.Take(i));
                        if (pathToAsset.ContainsKey(parentPath))
                        {
                            // 父路径也是预制体，说明这是深层嵌套
                            isDeepNested = true;
                            break;
                        }
                    }
                    
                    if (!isDeepNested)
                    {
                        directPrefabRefs.Add(r);
                    }
                }
                
                // 按预制体名称分组统计
                var groupedRefs = directPrefabRefs
                    .GroupBy(r => r.AssetPath)
                    .Select(g => new NestedPrefabReference
                    {
                        PrefabName = Path.GetFileNameWithoutExtension(g.Key),
                        PrefabPath = g.Key,
                        InstanceCount = g.Count(),
                        NodePaths = g.Select(r => r.NodePath).ToList(),
                        HasDeepNesting = false // 稍后检查
                    })
                    .OrderBy(n => n.PrefabName)
                    .ToList();
                
                int totalInstances = groupedRefs.Sum(n => n.InstanceCount);
                
                // 嵌套数量筛选
                if (nestingCountFilter == 2 && groupedRefs.Count < nestingCountMin)
                {
                    continue;
                }
                
                // 检查多层嵌套
                if (showMultiLevelNesting)
                {
                    CheckDeepNesting(groupedRefs);
                }
                
                var info = new PrefabNestingInfo
                {
                    PrefabName = prefab.Name,
                    PrefabPath = prefab.Path,
                    FolderPath = prefab.FolderPath,
                    NestedPrefabs = groupedRefs,
                    TotalInstanceCount = totalInstances
                };
                
                nestingInfoList.Add(info);
            }
            
            // 按嵌套数量降序排序
            nestingInfoList = nestingInfoList.OrderByDescending(n => n.NestedPrefabs.Count).ToList();
        }
        
        /// <summary>
        /// 检查多层嵌套
        /// </summary>
        private void CheckDeepNesting(List<NestedPrefabReference> nestedRefs)
        {
            foreach (var nested in nestedRefs)
            {
                // 查找该嵌套预制体是否也引用了其他预制体
                var deepPrefab = allPrefabs.FirstOrDefault(p => p.Path == nested.PrefabPath);
                if (deepPrefab != null)
                {
                    var deepRefs = deepPrefab.GetReferencesByType(AssetReferenceType.Prefab);
                    if (deepRefs.Count > 0)
                    {
                        nested.HasDeepNesting = true;
                        nested.DeepNestedPrefabs = deepRefs
                            .Select(r => Path.GetFileNameWithoutExtension(r.AssetPath))
                            .Distinct()
                            .Take(3) // 最多显示3个
                            .ToList();
                        
                        if (deepRefs.Select(r => r.AssetPath).Distinct().Count() > 3)
                        {
                            nested.DeepNestedPrefabs.Add($"...+{deepRefs.Select(r => r.AssetPath).Distinct().Count() - 3}");
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// 构建反向嵌套数据
        /// </summary>
        private void BuildReverseNestingData()
        {
            reverseNestingIndex.Clear();
            
            foreach (var prefab in allPrefabs)
            {
                // 文件夹筛选
                if (!string.IsNullOrEmpty(nestingFolderFilter) && 
                    !prefab.FolderPath.ToLower().Contains(nestingFolderFilter.ToLower()))
                {
                    continue;
                }
                
                var prefabRefs = prefab.GetReferencesByType(AssetReferenceType.Prefab);
                
                // 【修复Bug】只统计直接嵌套的预制体（排除深层递归嵌套）
                var pathToAsset = new Dictionary<string, string>();
                foreach (var r in prefabRefs)
                {
                    pathToAsset[r.NodePath] = r.AssetPath;
                }
                
                var directPrefabRefs = new List<AssetReference>();
                foreach (var r in prefabRefs)
                {
                    bool isDeepNested = false;
                    string[] pathParts = r.NodePath.Split('/');
                    
                    for (int i = 1; i < pathParts.Length; i++)
                    {
                        string parentPath = string.Join("/", pathParts.Take(i));
                        if (pathToAsset.ContainsKey(parentPath))
                        {
                            isDeepNested = true;
                            break;
                        }
                    }
                    
                    if (!isDeepNested)
                    {
                        directPrefabRefs.Add(r);
                    }
                }
                
                foreach (var assetRef in directPrefabRefs)
                {
                    if (!reverseNestingIndex.ContainsKey(assetRef.AssetPath))
                    {
                        reverseNestingIndex[assetRef.AssetPath] = new List<ParentPrefabInfo>();
                    }
                    
                    var existingParent = reverseNestingIndex[assetRef.AssetPath]
                        .FirstOrDefault(p => p.PrefabPath == prefab.Path);
                    
                    if (existingParent != null)
                    {
                        existingParent.InstanceCount++;
                    }
                    else
                    {
                        reverseNestingIndex[assetRef.AssetPath].Add(new ParentPrefabInfo
                        {
                            PrefabName = prefab.Name,
                            PrefabPath = prefab.Path,
                            InstanceCount = 1
                        });
                    }
                }
            }
            
            // 嵌套数量筛选
            if (nestingCountFilter >= 1)
            {
                var keysToRemove = reverseNestingIndex
                    .Where(kvp => kvp.Value.Count == 0 || (nestingCountFilter == 2 && kvp.Value.Count < nestingCountMin))
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var key in keysToRemove)
                {
                    reverseNestingIndex.Remove(key);
                }
            }
        }
        
        /// <summary>
        /// 导出嵌套关系到CSV
        /// </summary>
        private void ExportNestingToCSV()
        {
            try
            {
                string fileName = "";
                switch (currentNestingView)
                {
                    case NestingViewMode.Forward:
                        fileName = "NestingForward";
                        break;
                    case NestingViewMode.Reverse:
                        fileName = "NestingReverse";
                        break;
                    case NestingViewMode.Override:
                        fileName = "NestingOverride";
                        break;
                }
                string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filePath = Path.Combine(UIProbeStorage.GetMainFolderPath(), $"{fileName}_{timestamp}.csv");
                
                StringBuilder csv = new StringBuilder();
                
                int index = 1;
                
                if (currentNestingView == NestingViewMode.Forward)
                {
                    // 正向视图表头
                    csv.AppendLine("序号,预制体文件夹,预制体名称,嵌套的子预制体,实例数量");
                    
                    // 正向视图数据
                    foreach (var info in nestingInfoList)
                    {
                        if (info.NestedPrefabs.Count == 0)
                        {
                            csv.AppendLine($"{index},{info.FolderPath},{info.PrefabName},(无嵌套),0");
                            index++;
                        }
                        else
                        {
                            foreach (var nested in info.NestedPrefabs)
                            {
                                csv.AppendLine($"{index},{info.FolderPath},{info.PrefabName},{nested.PrefabName},{nested.InstanceCount}");
                                index++;
                            }
                        }
                    }
                }
                else if (currentNestingView == NestingViewMode.Reverse)
                {
                    // 反向视图表头
                    csv.AppendLine("序号,预制体文件夹,预制体名称,被谁引用,引用次数");
                    
                    // 反向视图数据
                    foreach (var kvp in reverseNestingIndex.OrderBy(x => Path.GetFileNameWithoutExtension(x.Key)))
                    {
                        string childName = Path.GetFileNameWithoutExtension(kvp.Key);
                        string childFolder = Path.GetDirectoryName(kvp.Key).Replace("\\", "/");
                        
                        foreach (var parent in kvp.Value.OrderBy(p => p.PrefabName))
                        {
                            csv.AppendLine($"{index},{childFolder},{childName},{parent.PrefabName},{parent.InstanceCount}");
                            index++;
                        }
                    }
                }
                else // Override视图
                {
                    // Override视图表头
                    csv.AppendLine("序号,父预制体文件夹,父预制体名称,嵌套预制体名称,实例路径,修改数量,属性修改,新增组件,删除组件");
                    
                    // Override视图数据
                    foreach (var info in overrideInfoList)
                    {
                        foreach (var instance in info.OverrideInstances)
                        {
                            string propMods = instance.PropertyModifications.Count > 0 
                                ? string.Join("; ", instance.PropertyModifications) 
                                : "-";
                            string addedComps = instance.AddedComponents.Count > 0 
                                ? string.Join("; ", instance.AddedComponents) 
                                : "-";
                            string removedComps = instance.RemovedComponents.Count > 0 
                                ? string.Join("; ", instance.RemovedComponents) 
                                : "-";
                                
                            csv.AppendLine($"{index},{info.ParentFolderPath},{info.ParentPrefabName},{instance.NestedPrefabName},{instance.InstancePath},{instance.TotalModCount},\"{propMods}\",\"{addedComps}\",\"{removedComps}\"");
                            index++;
                        }
                    }
                }
                
                File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
                
                EditorUtility.DisplayDialog("导出成功", 
                    $"嵌套关系已导出到:\n{filePath}\n\n共导出 {index - 1} 条记录。", "确定");
                
                EditorUtility.RevealInFinder(filePath);
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("导出失败", $"导出CSV时发生错误:\n{e.Message}", "确定");
            }
        }
        
        /// <summary>
        /// 构建Override检测数据
        /// </summary>
        private void BuildOverrideData()
        {
            overrideInfoList.Clear();
            
            EditorUtility.DisplayProgressBar("扫描Override", "正在检测预制体修改...", 0f);
            
            try
            {
                int totalPrefabs = allPrefabs.Count;
                int processedCount = 0;
                
                foreach (var prefab in allPrefabs)
                {
                    processedCount++;
                    if (processedCount % 10 == 0)
                    {
                        float progress = (float)processedCount / totalPrefabs;
                        EditorUtility.DisplayProgressBar("扫描Override", 
                            $"正在检测预制体修改...({processedCount}/{totalPrefabs})", progress);
                    }
                    
                    // 文件夹筛选
                    if (!string.IsNullOrEmpty(nestingFolderFilter) && 
                        !prefab.FolderPath.ToLower().Contains(nestingFolderFilter.ToLower()))
                    {
                        continue;
                    }
                    
                    // 加载预制体
                    GameObject prefabObj = AssetDatabase.LoadAssetAtPath<GameObject>(prefab.Path);
                    if (prefabObj == null) continue;
                    
                    // 检测嵌套预制体的Override
                    var overrideInstances = new List<NestedOverrideInstance>();
                    CheckPrefabOverrides(prefabObj.transform, overrideInstances);
                    
                    if (overrideInstances.Count > 0)
                    {
                        var info = new PrefabOverrideInfo
                        {
                            ParentPrefabName = prefab.Name,
                            ParentPrefabPath = prefab.Path,
                            ParentFolderPath = prefab.FolderPath,
                            OverrideInstances = overrideInstances,
                            TotalOverrideCount = overrideInstances.Sum(o => o.TotalModCount)
                        };
                        
                        overrideInfoList.Add(info);
                    }
                }
                
                // 按修改数量降序排序
                overrideInfoList = overrideInfoList.OrderByDescending(o => o.TotalOverrideCount).ToList();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
        
        /// <summary>
        /// 递归检查预制体Override
        /// </summary>
        private void CheckPrefabOverrides(Transform transform, List<NestedOverrideInstance> overrideInstances)
        {
            // 检查当前Transform是否是嵌套预制体实例
            GameObject go = transform.gameObject;
            
            if (PrefabUtility.IsPartOfPrefabInstance(go))
            {
                GameObject prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(go);
                if (prefabSource != null && PrefabUtility.GetPrefabAssetType(prefabSource) == PrefabAssetType.Regular)
                {
                    bool hasOverrides = PrefabUtility.HasPrefabInstanceAnyOverrides(go, false);
                    
                    if (hasOverrides)
                    {
                        string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                        string instancePath = GetGameObjectPath(transform);
                        
                        var overrideInstance = new NestedOverrideInstance
                        {
                            NestedPrefabName = prefabSource.name,
                            NestedPrefabPath = prefabPath,
                            InstancePath = instancePath
                        };
                        
                        // 获取属性修改
                        var propertyMods = PrefabUtility.GetPropertyModifications(go);
                        if (propertyMods != null)
                        {
                            foreach (var mod in propertyMods)
                            {
                                // 过滤掉Transform的position/rotation/scale（这些是预期的）
                                if (!mod.propertyPath.StartsWith("m_RootOrder") && 
                                    !mod.propertyPath.StartsWith("m_LocalPosition") &&
                                    !mod.propertyPath.StartsWith("m_LocalRotation") &&
                                    !mod.propertyPath.StartsWith("m_LocalScale"))
                                {
                                    string modStr = FormatPropertyModification(mod);
                                    if (!overrideInstance.PropertyModifications.Contains(modStr))
                                    {
                                        overrideInstance.PropertyModifications.Add(modStr);
                                    }
                                }
                            }
                        }
                        
                        // 获取添加的组件
                        var addedComponents = PrefabUtility.GetAddedComponents(go);
                        foreach (var comp in addedComponents)
                        {
                            if (comp != null && comp.instanceComponent != null)
                            {
                                overrideInstance.AddedComponents.Add(comp.instanceComponent.GetType().Name);
                            }
                        }
                        
                        // 获取删除的组件
                        var removedComponents = PrefabUtility.GetRemovedComponents(go);
                        foreach (var comp in removedComponents)
                        {
                            if (comp != null && comp.assetComponent != null)
                            {
                                overrideInstance.RemovedComponents.Add(comp.assetComponent.GetType().Name);
                            }
                        }
                        
                        overrideInstances.Add(overrideInstance);
                    }
                }
            }
            
            // 递归检查子对象
            foreach (Transform child in transform)
            {
                CheckPrefabOverrides(child, overrideInstances);
            }
        }
        
        /// <summary>
        /// 格式化属性修改信息
        /// </summary>
        private string FormatPropertyModification(PropertyModification mod)
        {
            string targetName = mod.target != null ? mod.target.name : "Unknown";
            return $"{targetName}.{mod.propertyPath}";
        }
        
        /// <summary>
        /// 获取GameObject的完整路径
        /// </summary>
        private string GetGameObjectPath(Transform transform)
        {
            string path = transform.name;
            Transform parent = transform.parent;
            
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            
            return path;
        }
        
        /// <summary>
        /// 绘制Override检测视图
        /// </summary>
        private void DrawOverrideView()
        {
            if (overrideInfoList.Count == 0)
            {
                EditorGUILayout.HelpBox("没有检测到嵌套预制体有Override修改。\n\n提示：点击上方「🔄 刷新数据」按钮开始检测。", MessageType.Info);
                return;
            }
            
            EditorGUILayout.LabelField($"共 {overrideInfoList.Count} 个预制体有Override修改:", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            nestingOverviewScrollPos = EditorGUILayout.BeginScrollView(nestingOverviewScrollPos);
            
            foreach (var info in overrideInfoList)
            {
                DrawOverrideCard(info);
            }
            
            EditorGUILayout.EndScrollView();
        }
        
        /// <summary>
        /// 绘制Override卡片
        /// </summary>
        private void DrawOverrideCard(PrefabOverrideInfo info)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            
            // 标题栏
            GUILayout.BeginHorizontal();
            
            // 展开/折叠按钮
            string expandIcon = info.IsExpanded ? "▼" : "▶";
            if (GUILayout.Button(expandIcon, GUILayout.Width(20)))
            {
                info.IsExpanded = !info.IsExpanded;
            }
            
            // 预制体名称
            EditorGUILayout.LabelField($"⚠️ {info.ParentPrefabName}", EditorStyles.boldLabel, GUILayout.MinWidth(100), GUILayout.ExpandWidth(true));

            // 统计信息
            EditorGUILayout.LabelField($"({info.OverrideInstances.Count} 个嵌套实例被修改，共 {info.TotalOverrideCount} 处修改)",
                EditorStyles.miniLabel, GUILayout.MinWidth(80), GUILayout.ExpandWidth(true));
            
            GUILayout.FlexibleSpace();
            
            // 操作按钮
            if (GUILayout.Button("打开", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                var obj = AssetDatabase.LoadAssetAtPath<GameObject>(info.ParentPrefabPath);
                if (obj != null) AssetDatabase.OpenAsset(obj);
            }
            
            if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                var obj = AssetDatabase.LoadAssetAtPath<GameObject>(info.ParentPrefabPath);
                if (obj != null) EditorGUIUtility.PingObject(obj);
            }
            
            GUILayout.EndHorizontal();
            
            // 文件夹路径
            EditorGUILayout.LabelField(info.ParentFolderPath, EditorStyles.miniLabel);
            
            // 展开详情
            if (info.IsExpanded && info.OverrideInstances.Count > 0)
            {
                EditorGUILayout.Space(3);
                
                foreach (var instance in info.OverrideInstances)
                {
                    DrawOverrideInstanceItem(instance);
                }
            }
            
            GUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }
        
        /// <summary>
        /// 绘制Override实例条目
        /// </summary>
        private void DrawOverrideInstanceItem(NestedOverrideInstance instance)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            
            // 嵌套预制体名称和修改数量
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            EditorGUILayout.LabelField("└─", GUILayout.Width(20));
            
            if (GUILayout.Button(instance.NestedPrefabName, EditorStyles.linkLabel, GUILayout.Width(180)))
            {
                var obj = AssetDatabase.LoadAssetAtPath<GameObject>(instance.NestedPrefabPath);
                if (obj != null)
                {
                    EditorGUIUtility.PingObject(obj);
                    Selection.activeObject = obj;
                }
            }
            
            GUIStyle warningStyle = new GUIStyle(EditorStyles.miniLabel);
            warningStyle.normal.textColor = new Color(1f, 0.5f, 0f);
            EditorGUILayout.LabelField($"({instance.TotalModCount} 处修改)", warningStyle, GUILayout.Width(80));
            
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            // 实例路径
            GUILayout.BeginHorizontal();
            GUILayout.Space(40);
            EditorGUILayout.LabelField($"📍 {instance.InstancePath}", EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
            
            // 修改详情
            if (instance.PropertyModifications.Count > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(40);
                EditorGUILayout.LabelField($"属性修改 ({instance.PropertyModifications.Count}):", EditorStyles.miniLabel, GUILayout.Width(100));
                
                // 显示前3个修改
                int showCount = Mathf.Min(3, instance.PropertyModifications.Count);
                string modsPreview = string.Join(", ", instance.PropertyModifications.Take(showCount));
                if (instance.PropertyModifications.Count > 3)
                {
                    modsPreview += $" ...+{instance.PropertyModifications.Count - 3}";
                }
                EditorGUILayout.LabelField(modsPreview, EditorStyles.miniLabel);
                GUILayout.EndHorizontal();
            }
            
            if (instance.AddedComponents.Count > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(40);
                EditorGUILayout.LabelField($"新增组件:", EditorStyles.miniLabel, GUILayout.Width(100));
                EditorGUILayout.LabelField(string.Join(", ", instance.AddedComponents), EditorStyles.miniLabel);
                GUILayout.EndHorizontal();
            }
            
            if (instance.RemovedComponents.Count > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(40);
                EditorGUILayout.LabelField($"删除组件:", EditorStyles.miniLabel, GUILayout.Width(100));
                EditorGUILayout.LabelField(string.Join(", ", instance.RemovedComponents), EditorStyles.miniLabel);
                GUILayout.EndHorizontal();
            }
            
            GUILayout.EndVertical();
        }
    }
}
