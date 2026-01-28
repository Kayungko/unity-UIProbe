using UnityEngine;
using UnityEditor;
using System;
using System.IO;

namespace UIProbe
{
    partial class UIProbeWindow
    {
        // 截屏标签页状态
        private Vector2 screenshotScrollPos;
        private int screenshotSuperSize = 1; // 超采样倍数 (1-4)
        private bool screenshotTransparent = false; // 是否透明背景
        private int screenshotWidth = 1920;
        private int screenshotHeight = 1080;
        private bool useCustomResolution = false;
        private string lastScreenshotPath = "";
        
        /// <summary>
        /// 绘制截屏标签页
        /// </summary>
        private void DrawScreenshotTab()
        {
            screenshotScrollPos = EditorGUILayout.BeginScrollView(screenshotScrollPos);
            
            EditorGUILayout.LabelField("游戏截屏 (Screenshot)", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            // Play 模式检测
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("请进入 Play 模式后使用此功能。", MessageType.Info);
                
                EditorGUILayout.Space(10);
                if (GUILayout.Button("▶ 进入 Play 模式", GUILayout.Height(40)))
                {
                    EditorApplication.isPlaying = true;
                }
                
                EditorGUILayout.EndScrollView();
                return;
            }
            
            // 截屏设置
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("截屏设置", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            // 分辨率设置
            useCustomResolution = EditorGUILayout.Toggle("使用自定义分辨率", useCustomResolution);
            
            if (useCustomResolution)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("宽度:", GUILayout.Width(60));
                screenshotWidth = EditorGUILayout.IntField(screenshotWidth, GUILayout.Width(100));
                EditorGUILayout.LabelField("高度:", GUILayout.Width(60));
                screenshotHeight = EditorGUILayout.IntField(screenshotHeight, GUILayout.Width(100));
                EditorGUILayout.EndHorizontal();
                
                // 快捷分辨率按钮
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("快捷:", GUILayout.Width(60));
                if (GUILayout.Button("1920x1080"))
                {
                    screenshotWidth = 1920;
                    screenshotHeight = 1080;
                }
                if (GUILayout.Button("1280x720"))
                {
                    screenshotWidth = 1280;
                    screenshotHeight = 720;
                }
                if (GUILayout.Button("2560x1440"))
                {
                    screenshotWidth = 2560;
                    screenshotHeight = 1440;
                }
                if (GUILayout.Button("3840x2160"))
                {
                    screenshotWidth = 3840;
                    screenshotHeight = 2160;
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox($"当前游戏分辨率: {Screen.width} x {Screen.height}", MessageType.None);
            }
            
            EditorGUILayout.Space(5);
            
            // 超采样设置
            screenshotSuperSize = EditorGUILayout.IntSlider("超采样倍数", screenshotSuperSize, 1, 4);
            EditorGUILayout.HelpBox($"实际截图分辨率: {GetActualWidth()} x {GetActualHeight()}", MessageType.None);
            
            EditorGUILayout.Space(5);
            
            // 透明背景（仅适用于某些渲染模式）
            screenshotTransparent = EditorGUILayout.Toggle("透明背景（实验性）", screenshotTransparent);
            if (screenshotTransparent)
            {
                EditorGUILayout.HelpBox("透明背景仅在某些相机设置下有效（Clear Flags = Solid Color, Alpha = 0）", MessageType.Warning);
            }
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            // 截屏按钮
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("执行截屏", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            // 主截屏按钮
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("📸 截屏并保存", GUILayout.Height(50)))
            {
                CaptureScreenshot();
            }
            GUI.backgroundColor = Color.white;
            
            EditorGUILayout.Space(5);
            
            // 快捷键提示
            EditorGUILayout.HelpBox("快捷键: F12 - 快速截屏", MessageType.None);
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            // 最近截屏
            if (!string.IsNullOrEmpty(lastScreenshotPath))
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("最近的截屏", EditorStyles.boldLabel);
                EditorGUILayout.Space(3);
                
                EditorGUILayout.LabelField($"📁 {lastScreenshotPath}", EditorStyles.wordWrappedMiniLabel);
                
                EditorGUILayout.BeginHorizontal();
                
                if (GUILayout.Button("打开文件夹", GUILayout.Height(30)))
                {
                    string folder = Path.GetDirectoryName(lastScreenshotPath);
                    EditorUtility.RevealInFinder(folder);
                }
                
                if (GUILayout.Button("打开图片", GUILayout.Height(30)))
                {
                    Application.OpenURL("file:///" + lastScreenshotPath);
                }
                
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.EndVertical();
            }
            
            EditorGUILayout.Space(10);
            
            // 存储路径信息
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("存储路径", EditorStyles.boldLabel);
            EditorGUILayout.Space(3);
            
            string screenshotsPath = UIProbeStorage.GetScreenshotsPath();
            EditorGUILayout.LabelField(screenshotsPath, EditorStyles.wordWrappedMiniLabel);
            
            if (GUILayout.Button("打开截屏文件夹", GUILayout.Height(30)))
            {
                EditorUtility.RevealInFinder(screenshotsPath);
            }
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.EndScrollView();
        }
        
        /// <summary>
        /// 获取实际截图宽度
        /// </summary>
        private int GetActualWidth()
        {
            int baseWidth = useCustomResolution ? screenshotWidth : Screen.width;
            return baseWidth * screenshotSuperSize;
        }
        
        /// <summary>
        /// 获取实际截图高度
        /// </summary>
        private int GetActualHeight()
        {
            int baseHeight = useCustomResolution ? screenshotHeight : Screen.height;
            return baseHeight * screenshotSuperSize;
        }
        
        /// <summary>
        /// 执行截屏
        /// </summary>
        private void CaptureScreenshot()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("错误", "请在 Play 模式下使用截屏功能。", "确定");
                return;
            }
            
            try
            {
                // 生成文件名
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"Screenshot_{timestamp}.png";
                
                // 获取存储路径
                string screenshotsPath = UIProbeStorage.GetScreenshotsPath();
                lastScreenshotPath = Path.Combine(screenshotsPath, fileName);
                
                // 执行截屏
                if (screenshotTransparent)
                {
                    // 使用 RenderTexture 进行透明背景截屏（实验性）
                    CaptureTransparentScreenshot(lastScreenshotPath);
                }
                else
                {
                    // 使用 Unity 内置截屏 API
                    ScreenCapture.CaptureScreenshot(lastScreenshotPath, screenshotSuperSize);
                }
                
                Debug.Log($"[UIProbe] 截屏已保存: {lastScreenshotPath}");
                EditorUtility.DisplayDialog("截屏成功", $"截屏已保存到:\n{lastScreenshotPath}", "确定");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIProbe] 截屏失败: {ex.Message}");
                EditorUtility.DisplayDialog("截屏失败", $"截屏时发生错误:\n{ex.Message}", "确定");
            }
        }
        
        /// <summary>
        /// 透明背景截屏（实验性）
        /// </summary>
        private void CaptureTransparentScreenshot(string path)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                Debug.LogWarning("[UIProbe] 未找到主相机，使用标准截屏方式");
                ScreenCapture.CaptureScreenshot(path, screenshotSuperSize);
                return;
            }
            
            int width = GetActualWidth();
            int height = GetActualHeight();
            
            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            
            Texture2D screenShot = new Texture2D(width, height, TextureFormat.ARGB32, false);
            camera.Render();
            RenderTexture.active = rt;
            screenShot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            screenShot.Apply();
            
            camera.targetTexture = null;
            RenderTexture.active = null;
            DestroyImmediate(rt);
            
            byte[] bytes = screenShot.EncodeToPNG();
            File.WriteAllBytes(path, bytes);
            
            DestroyImmediate(screenShot);
        }
        
        /// <summary>
        /// Update 中检测快捷键
        /// </summary>
        private void HandleScreenshotInput()
        {
            // F12 快速截屏
            if (Event.current != null && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F12)
            {
                CaptureScreenshot();
                Event.current.Use();
            }
        }
    }
}
