using UnityEngine;
using UnityEditor;

namespace UIProbe
{
    /// <summary>
    /// UIProbe 预制体创建扩展模块
    /// </summary>
    public partial class UIProbeWindow
    {
        // 预制体创建模块状态
        private int radialMenuItemCount = 12;
        private float radialMenuOuterRadius = 400f;
        private float radialMenuRingThickness = 150f;
        
        /// <summary>
        /// 绘制预制体创建标签页
        /// </summary>
        private void DrawPrefabCreatorTab()
        {
            GUILayout.Label("预制体创建工具", EditorStyles.boldLabel);
            GUILayout.Space(10);
            
            EditorGUILayout.HelpBox("在这里创建径向菜单预制体，使用 Image.Filled 原生方案", MessageType.Info);
            GUILayout.Space(10);
            
            // 径向菜单区域
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("径向菜单预制体", EditorStyles.boldLabel);
            GUILayout.Space(5);
            
            radialMenuItemCount = EditorGUILayout.IntSlider("菜单项数量", radialMenuItemCount, 2, 16);
            radialMenuOuterRadius = EditorGUILayout.Slider("外半径", radialMenuOuterRadius, 200f, 600f);
            radialMenuRingThickness = EditorGUILayout.Slider("环形宽度", radialMenuRingThickness, 50f, 300f);
            
            GUILayout.Space(10);
            
            if (GUILayout.Button("创建径向菜单预制体", GUILayout.Height(40)))
            {
                CreateRadialMenuPrefabFromUI();
            }
            
            GUILayout.Space(5);
            
            if (GUILayout.Button("创建菜单项预制体", GUILayout.Height(40)))
            {
                CreateMenuItemPrefabFromUI();
            }
            
            GUILayout.Space(5);
            EditorGUILayout.HelpBox(
                "预制体保存路径:\n" +
                "• Assets/UI/Prefabs/UI_Battle/Battle_RadialMenu.prefab\n" +
                "• Assets/UI/Prefabs/UI_Battle/Battle_RadialMenuItem.prefab", 
                MessageType.None);
            
            GUILayout.EndVertical();
        }
        
        /// <summary>
        /// 从 UI 创建径向菜单预制体
        /// </summary>
        private void CreateRadialMenuPrefabFromUI()
        {
            RadialMenuPrefabCreator.CreateRadialMenuPrefabStatic();
            EditorUtility.DisplayDialog("成功", "径向菜单预制体已创建！\n路径: Assets/UI/Prefabs/UI_Battle/Battle_RadialMenu.prefab", "确定");
        }
        
        /// <summary>
        /// 从 UI 创建菜单项预制体
        /// </summary>
        private void CreateMenuItemPrefabFromUI()
        {
            RadialMenuPrefabCreator.CreateMenuItemPrefabStatic();
            EditorUtility.DisplayDialog("成功", "菜单项预制体已创建！\n路径: Assets/UI/Prefabs/UI_Battle/Battle_RadialMenuItem.prefab", "确定");
        }
    }
}
