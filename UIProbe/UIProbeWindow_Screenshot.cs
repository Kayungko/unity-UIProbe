using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Reflection;
using UnityEngine.Rendering;

namespace UIProbe
{
    partial class UIProbeWindow
    {
        // 截屏标签页状态
        private Vector2 screenshotScrollPos;
        private int screenshotSuperSize = 1; // 超采样倍数 (1-4)
        private bool screenshotTransparent = false; // 是否透明背景
        private bool autoFrameContent = false; // 自动对焦内容
        private bool screenshotUIOnly = false; // 是否仅截 UI 层
        private int screenshotWidth = 1920;
        private int screenshotHeight = 1080;
        private bool useCustomResolution = false;
        private string lastScreenshotPath = "";
        
        // ---- UI 层异步截图状态 ----
        private bool            _uiCaptureInProgress  = false;
        private Camera          _uiCaptureCam         = null;
        private RenderTexture   _uiCaptureRT          = null;
        private string          _uiCapturePath        = null;
        private RenderTexture   _uiCaptureOrigTarget  = null;
        private CameraClearFlags _uiCaptureOrigFlags  = CameraClearFlags.Skybox;
        private Color           _uiCaptureOrigBg      = Color.black;
        private Component       _uiCaptureUrpData     = null;
        private PropertyInfo    _uiCaptureRenderProp  = null;
        private bool            _uiCaptureWasOverlay  = false;
        private int             _uiCaptureWidth       = 0;
        private int             _uiCaptureHeight      = 0;
        // 双背景差值合成状态
        private int             _uiCapturePhase       = 0; // 1=黑底 2=白底 3=透明单帧
        private Texture2D       _uiCaptureBlackTex    = null;
        private bool            _screenshotUIComposite = true; // 是否启用双背景合成
        
        /// <summary>
        /// 绘制截屏标签页
        /// </summary>
        private void DrawScreenshotTab()
        {
            screenshotScrollPos = EditorGUILayout.BeginScrollView(screenshotScrollPos);
            
            EditorGUILayout.LabelField("游戏截屏 (Screenshot)", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            // ==========================================
            // 通用设置 (分辨率 & 超采样 & 透明)
            // ==========================================
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("通用设置", EditorStyles.boldLabel);
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
                
                if (GUILayout.Button("Game View"))
                {
                    var size = UnityEditor.Handles.GetMainGameViewSize();
                    screenshotWidth = (int)size.x;
                    screenshotHeight = (int)size.y;
                    GUI.FocusControl(null);
                }
                
                if (GUILayout.Button("1080p")) { screenshotWidth = 1920; screenshotHeight = 1080; }
                if (GUILayout.Button("720p")) { screenshotWidth = 1280; screenshotHeight = 720; }
                if (GUILayout.Button("2K")) { screenshotWidth = 2560; screenshotHeight = 1440; }
                if (GUILayout.Button("4K")) { screenshotWidth = 3840; screenshotHeight = 2160; }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                // 显示当前上下文分辨率
                string resolutionInfo = "自动匹配";
                if (Application.isPlaying) 
                    resolutionInfo = $"Game View: {Screen.width} x {Screen.height}"; // 注意：Screen.width在EditorWindow会返回Window尺寸，需谨慎
                else
                    resolutionInfo = "Scene View: (所见即所得)";
                
                EditorGUILayout.HelpBox(resolutionInfo, MessageType.None);
            }
            
            EditorGUILayout.Space(5);
            
            // 超采样设置
            screenshotSuperSize = EditorGUILayout.IntSlider("超采样倍数", screenshotSuperSize, 1, 4);
            
            EditorGUILayout.Space(5);
            
            // 透明背景（仅适用于某些渲染模式）
            screenshotTransparent = EditorGUILayout.Toggle("透明背景 (实验性)", screenshotTransparent);
            if (screenshotTransparent)
            {
                EditorGUILayout.HelpBox("透明背景移除了天空盒/Grid/Gizmos，适合抠图。", MessageType.Info);
            }
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            // ==========================================
            // 1. 场景/预制体截图区域
            // ==========================================
            DrawSceneScreenshotSection();
            
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.Space(10);
            
            EditorGUILayout.LabelField("运行时截图 (Game View)", EditorStyles.boldLabel);
            
            // 2. Play 模式检测
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("请进入 Play 模式后使用运行时截屏功能。", MessageType.Info);
                
                EditorGUILayout.Space(5);
                if (GUILayout.Button("▶ 进入 Play 模式", GUILayout.Height(30)))
                {
                    EditorApplication.isPlaying = true;
                }
            }
            else
            {
                // 运行时截屏按钮
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                // 主截屏按钮
                GUI.backgroundColor = Color.green;
                if (GUILayout.Button("📸 截屏并保存 (Game)", GUILayout.Height(50)))
                {
                    CaptureScreenshot();
                }
                GUI.backgroundColor = Color.white;
                
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox("快捷键: F12 - 快速截屏", MessageType.None);
                
                EditorGUILayout.Space(5);
                
                // UI 层表1：设置区
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("仅截 UI 层设置", EditorStyles.boldLabel);
                _screenshotUIComposite = EditorGUILayout.Toggle(
                    new GUIContent("处理特效（双背景合成）",
                        "启用时用黑底+白底两帧渲染差値合成，能正确处理任意 Shader 输出的黑色块。\n不启用时单帧透明渲染，速度较快。"),
                    _screenshotUIComposite);
                if (_screenshotUIComposite)
                {
                    EditorGUILayout.HelpBox("双背景合成：连续渲染两帧（黑底+白底），耗时2帧时间，自动处理黑色块和半透明特效。", MessageType.None);
                }
                else
                {
                    EditorGUILayout.HelpBox("单帧透明渲染：速度快，但 Shader 未正确写入 Alpha 的特效可能出现黑色块。\n→ 建议截图前先手动隐藏 VFX / UIParticle 节点。", MessageType.Warning);
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(5);

                // UI 层表2：截图按钮
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                // UI 专属截图按钮
                GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
                string uiBtnLabel = _screenshotUIComposite
                    ? "🖼 仅截 UI 层（双背景合成）"
                    : "🖼 仅截 UI 层（单帧透明）";
                if (GUILayout.Button(uiBtnLabel, GUILayout.Height(40)))
                {
                    CaptureUIOnlyScreenshot();
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.HelpBox("通过 Tag=\"UICamera\" 找到专用 UI 相机，仅渲染 UI 层，背景完全透明，输出 PNG（含 Alpha 通道）。", MessageType.None);
                EditorGUILayout.EndVertical();
            }
            
            EditorGUILayout.Space(10);
            
            // 3. 通用信息 (最近截屏 & 路径)
            if (!string.IsNullOrEmpty(lastScreenshotPath))
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("最近的截屏", EditorStyles.boldLabel);
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField($"📁 {lastScreenshotPath}", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("打开文件夹", GUILayout.Height(30))) { EditorUtility.RevealInFinder(Path.GetDirectoryName(lastScreenshotPath)); }
                if (GUILayout.Button("打开图片", GUILayout.Height(30))) { Application.OpenURL("file:///" + lastScreenshotPath); }
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
            if (GUILayout.Button("打开截屏文件夹", GUILayout.Height(30))) { EditorUtility.RevealInFinder(screenshotsPath); }
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.EndScrollView();
        }
        
        /// <summary>
        /// 获取实际截图宽度
        /// </summary>
        private int GetActualWidth(bool isScene = false)
        {
            int baseWidth = 0;
            if (useCustomResolution)
            {
                baseWidth = screenshotWidth;
            }
            else
            {
                if (isScene)
                {
                    var view = SceneView.lastActiveSceneView;
                    baseWidth = view != null ? (int)view.position.width : Screen.width;
                }
                else
                {
                     // Runtime: Use Screen.width (which might be GameView size if focused, or Window size)
                     // Best practice: Handles.GetMainGameViewSize() but internal.
                     // Fallback: Screen.width
                     baseWidth = Screen.width;
                }
            }
            return baseWidth * screenshotSuperSize;
        }
        
        /// <summary>
        /// 获取实际截图高度
        /// </summary>
        private int GetActualHeight(bool isScene = false)
        {
            int baseHeight = 0;
            if (useCustomResolution)
            {
                baseHeight = screenshotHeight;
            }
            else
            {
                if (isScene)
                {
                    var view = SceneView.lastActiveSceneView;
                    baseHeight = view != null ? (int)view.position.height : Screen.height;
                }
                else
                {
                     baseHeight = Screen.height;
                }
            }
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
        /// 触发仅截 UI 层截图（异步入口）。
        /// 不再手动调用 camera.Render()，改为订阅 RenderPipelineManager.endCameraRendering，
        /// 等 Unity 自然渲染完整一帧（含粒子特效）后再读取像素。
        /// </summary>
        private void CaptureUIOnlyScreenshot()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("错误", "请在 Play 模式下使用截屏功能。", "确定");
                return;
            }
            
            if (_uiCaptureInProgress)
            {
                EditorUtility.DisplayDialog("提示", "UI 专属截图正在进行中，请稍候...", "确定");
                return;
            }
            
            try
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName  = $"Screenshot_UIOnly_{timestamp}.png";
                string folder    = UIProbeStorage.GetScreenshotsPath();
                lastScreenshotPath = Path.Combine(folder, fileName);
                
                StartUIOnlyCaptureAsync(lastScreenshotPath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIProbe] UI 专属截图启动失败: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("截屏失败", $"截屏时发生错误:\n{ex.Message}", "确定");
            }
        }
        
        /// <summary>
        /// 设置异步捕获环境：找相机、切换 Base、延迟赋値 RT，然后订阅 endCameraRendering。
        /// </summary>
        private void StartUIOnlyCaptureAsync(string path)
        {
            // ---- 1. 查找 UI 专用相机 ----
            Camera uiCamera = null;
            foreach (var cam in Camera.allCameras)
            {
                if (cam.CompareTag("UICamera")) { uiCamera = cam; break; }
            }
            if (uiCamera == null)
            {
                int uiOnlyMask = 1 << LayerMask.NameToLayer("UI");
                foreach (var cam in Camera.allCameras)
                {
                    if (cam.cullingMask == uiOnlyMask) { uiCamera = cam; break; }
                }
            }
            if (uiCamera == null)
                throw new Exception("未找到 UI 专用相机。\n请确保场景中有 Tag=\"UICamera\" 的相机，或 Culling Mask 仅含 UI 层的相机。");
            
            // ---- 2. 保存相机原始设置 ----
            _uiCaptureCam        = uiCamera;
            _uiCapturePath       = path;
            _uiCaptureWidth      = GetActualWidth();
            _uiCaptureHeight     = GetActualHeight();
            _uiCaptureOrigTarget = uiCamera.targetTexture;
            _uiCaptureOrigFlags  = uiCamera.clearFlags;
            _uiCaptureOrigBg     = uiCamera.backgroundColor;
            
            // ---- 3. URP：临时将 Overlay 改为 Base（反射）----
            _uiCaptureWasOverlay = false;
            _uiCaptureUrpData    = uiCamera.GetComponent("UniversalAdditionalCameraData");
            _uiCaptureRenderProp = _uiCaptureUrpData?.GetType().GetProperty("renderType");
            if (_uiCaptureRenderProp != null)
            {
                int currentType = (int)_uiCaptureRenderProp.GetValue(_uiCaptureUrpData);
                if (currentType == 1) // Overlay
                {
                    _uiCaptureWasOverlay = true;
                    _uiCaptureRenderProp.SetValue(_uiCaptureUrpData, 0); // Base
                }
            }
            
            // ---- 4. 创建透明 RT 并明赋给相机 ----
            //   将 RT 赋给相机后，Unity 下一帧自然渲染到此 RT。
            _uiCaptureRT = new RenderTexture(_uiCaptureWidth, _uiCaptureHeight, 32, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1
            };
            _uiCaptureRT.Create();
            
            // ---- 5. 根据勾选项选择渲染模式 ----
            uiCamera.clearFlags    = CameraClearFlags.SolidColor;
            uiCamera.targetTexture = _uiCaptureRT;

            if (_screenshotUIComposite)
            {
                // 双背景合成：黑底开始
                uiCamera.backgroundColor = new Color(0f, 0f, 0f, 1f);
                _uiCapturePhase = 1;
                Debug.Log("[UIProbe] UI 层截图（双背景合成）已启动，第1帧：黑底渲染...");
            }
            else
            {
                // 单帧透明渲染
                uiCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _uiCapturePhase = 3;
                Debug.Log("[UIProbe] UI 层截图（单帧透明）已启动...");
            }

            _uiCaptureInProgress = true;
            RenderPipelineManager.endCameraRendering += OnUICameraPostRender;
        }
        
        /// <summary>
        /// RenderPipelineManager.endCameraRendering 回调（双背景两阶段）。
        /// Phase1：黑底渲染完成 → 读像素 → 换白底 → 重新订阅。
        /// Phase2：白底渲染完成 → 读像素 → 差值合成 → 保存 PNG → 恢复相机。
        /// </summary>
        private void OnUICameraPostRender(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _uiCaptureCam) return;

            RenderPipelineManager.endCameraRendering -= OnUICameraPostRender;

            try
            {
                if (_uiCapturePhase == 3)
                {
                    // ===== 单帧透明模式：直接读取并保存 =====
                    RenderTexture.active = _uiCaptureRT;
                    var singleShot = new Texture2D(_uiCaptureWidth, _uiCaptureHeight, TextureFormat.ARGB32, false);
                    singleShot.ReadPixels(new Rect(0, 0, _uiCaptureWidth, _uiCaptureHeight), 0, 0);
                    singleShot.Apply();
                    RenderTexture.active = null;

                    _uiCaptureCam.targetTexture   = _uiCaptureOrigTarget;
                    _uiCaptureCam.clearFlags      = _uiCaptureOrigFlags;
                    _uiCaptureCam.backgroundColor = _uiCaptureOrigBg;
                    if (_uiCaptureWasOverlay && _uiCaptureUrpData != null && _uiCaptureRenderProp != null)
                        _uiCaptureRenderProp.SetValue(_uiCaptureUrpData, 1);

                    DestroyImmediate(_uiCaptureRT); _uiCaptureRT = null;

                    byte[] singleBytes = singleShot.EncodeToPNG();
                    File.WriteAllBytes(_uiCapturePath, singleBytes);
                    DestroyImmediate(singleShot);

                    _uiCaptureInProgress = false;
                    _uiCapturePhase      = 0;

                    Debug.Log($"[UIProbe] UI 层截图（单帧透明）已保存: {_uiCapturePath}");
                    EditorApplication.delayCall += () =>
                        EditorUtility.DisplayDialog("截屏成功", $"UI 层截图已保存到:\n{_uiCapturePath}", "确定");
                }
                else if (_uiCapturePhase == 1)
                {
                    // ----- Phase 1：黑底完成，读像素 -----
                    RenderTexture.active = _uiCaptureRT;
                    _uiCaptureBlackTex = new Texture2D(_uiCaptureWidth, _uiCaptureHeight, TextureFormat.ARGB32, false);
                    _uiCaptureBlackTex.ReadPixels(new Rect(0, 0, _uiCaptureWidth, _uiCaptureHeight), 0, 0);
                    _uiCaptureBlackTex.Apply();
                    RenderTexture.active = null;

                    // 换白底，下一帧重新渲染
                    _uiCapturePhase = 2;
                    _uiCaptureCam.backgroundColor = new Color(1f, 1f, 1f, 1f);
                    RenderPipelineManager.endCameraRendering += OnUICameraPostRender;
                    Debug.Log("[UIProbe] Phase2：白底渲染...");
                }
                else if (_uiCapturePhase == 2)
                {
                    // ----- Phase 2：白底完成，读像素并合成 -----
                    RenderTexture.active = _uiCaptureRT;
                    var whiteTex = new Texture2D(_uiCaptureWidth, _uiCaptureHeight, TextureFormat.ARGB32, false);
                    whiteTex.ReadPixels(new Rect(0, 0, _uiCaptureWidth, _uiCaptureHeight), 0, 0);
                    whiteTex.Apply();
                    RenderTexture.active = null;

                    // 恢复相机
                    _uiCaptureCam.targetTexture   = _uiCaptureOrigTarget;
                    _uiCaptureCam.clearFlags      = _uiCaptureOrigFlags;
                    _uiCaptureCam.backgroundColor = _uiCaptureOrigBg;
                    if (_uiCaptureWasOverlay && _uiCaptureUrpData != null && _uiCaptureRenderProp != null)
                        _uiCaptureRenderProp.SetValue(_uiCaptureUrpData, 1);

                    DestroyImmediate(_uiCaptureRT);
                    _uiCaptureRT = null;

                    // 差值合成
                    Texture2D result = CompositeFromBlackAndWhite(_uiCaptureBlackTex, whiteTex);
                    DestroyImmediate(_uiCaptureBlackTex); _uiCaptureBlackTex = null;
                    DestroyImmediate(whiteTex);

                    // 保存 PNG
                    byte[] bytes = result.EncodeToPNG();
                    File.WriteAllBytes(_uiCapturePath, bytes);
                    DestroyImmediate(result);

                    _uiCaptureInProgress = false;
                    _uiCapturePhase      = 0;

                    Debug.Log($"[UIProbe] UI 层截图（双背景合成）已保存: {_uiCapturePath}");
                    EditorApplication.delayCall += () =>
                        EditorUtility.DisplayDialog("截屏成功", $"UI 层截图已保存到:\n{_uiCapturePath}", "确定");
                }
            }
            catch (Exception ex)
            {
                // 异常时恢复相机
                if (_uiCaptureCam != null)
                {
                    _uiCaptureCam.targetTexture   = _uiCaptureOrigTarget;
                    _uiCaptureCam.clearFlags      = _uiCaptureOrigFlags;
                    _uiCaptureCam.backgroundColor = _uiCaptureOrigBg;
                }
                if (_uiCaptureWasOverlay && _uiCaptureUrpData != null && _uiCaptureRenderProp != null)
                    _uiCaptureRenderProp.SetValue(_uiCaptureUrpData, 1);
                if (_uiCaptureRT != null)       { DestroyImmediate(_uiCaptureRT);       _uiCaptureRT       = null; }
                if (_uiCaptureBlackTex != null) { DestroyImmediate(_uiCaptureBlackTex); _uiCaptureBlackTex = null; }
                _uiCaptureInProgress = false;
                _uiCapturePhase      = 0;

                Debug.LogError($"[UIProbe] UI 层截图失败: {ex.Message}\n{ex.StackTrace}");
                EditorApplication.delayCall += () =>
                    EditorUtility.DisplayDialog("截屏失败", $"截屏时发生错误:\n{ex.Message}", "确定");
            }
        }

        /// <summary>
        /// 双背景差值合成。
        /// 数学推导（标准 Alpha 混合）：
        ///   黑底 B = C * A    白底 W = C * A + (1 - A)
        ///   => A = 1 - (W - B)  （三通道平均）
        ///   => C = B / A        （A > 0 时）
        /// 加法混合粒子：W - B ≈ 1，推算 A ≈ 0；保留 B 颜色，Alpha=0（表示纯加法贡献）。
        /// </summary>
        private Texture2D CompositeFromBlackAndWhite(Texture2D black, Texture2D white)
        {
            Color32[] bPix   = black.GetPixels32();
            Color32[] wPix   = white.GetPixels32();
            Color32[] output = new Color32[bPix.Length];

            for (int i = 0; i < bPix.Length; i++)
            {
                Color32 b = bPix[i];
                Color32 w = wPix[i];

                // 三通道差均值 (0-1 归一化)
                float dR = (w.r - b.r) / 255f;
                float dG = (w.g - b.g) / 255f;
                float dB = (w.b - b.b) / 255f;
                float avgDiff = (dR + dG + dB) / 3f;

                float alpha = Mathf.Clamp01(1f - avgDiff);
                byte  a8    = (byte)(alpha * 255f + 0.5f);

                byte r8, g8, b8;
                if (alpha > 0.001f)
                {
                    // 标准 Alpha：还原真实颜色 C = B / A
                    r8 = (byte)Mathf.Clamp(b.r / alpha, 0, 255);
                    g8 = (byte)Mathf.Clamp(b.g / alpha, 0, 255);
                    b8 = (byte)Mathf.Clamp(b.b / alpha, 0, 255);
                }
                else
                {
                    // 加法混合：Alpha=0，颜色取黑底值（作为加法贡献保留）
                    r8 = b.r;
                    g8 = b.g;
                    b8 = b.b;
                    a8 = 0;
                }

                output[i] = new Color32(r8, g8, b8, a8);
            }

            var result = new Texture2D(_uiCaptureWidth, _uiCaptureHeight, TextureFormat.ARGB32, false);
            result.SetPixels32(output);
            result.Apply();
            return result;
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

        // ==========================================
        // Scene / Prefab 截图功能
        // ==========================================

        private void DrawSceneScreenshotSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("场景/预制体截图 (Scene View)", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // 获取当前预制体环境信息
            var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            string contextInfo = prefabStage != null ? $"当前预制体: {prefabStage.prefabContentsRoot.name}" : "当前环境: Scene";
            EditorGUILayout.LabelField(contextInfo, EditorStyles.miniLabel);

            // 选项
            screenshotTransparent = EditorGUILayout.Toggle("透明背景 (无天空盒/网格)", screenshotTransparent);
            autoFrameContent = EditorGUILayout.Toggle("自动对焦内容 (Auto Frame)", autoFrameContent);

            
            EditorGUILayout.Space(5);

            // 截图按钮
            GUI.backgroundColor = new Color(0.2f, 0.8f, 1f); // 浅蓝色区分
            if (GUILayout.Button("📸 截取 Scene 视图", GUILayout.Height(40)))
            {
                CaptureSceneScreenshot();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 截取 Scene 视图
        /// </summary>
        private void CaptureSceneScreenshot()
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null)
            {
                EditorUtility.DisplayDialog("错误", "未找到活动的 Scene 视图，请先打开 Scene 窗口。", "确定");
                return;
            }

            try
            {
                // 1. 确定文件名
                string prefix = "Scene";
                var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
                if (prefabStage != null)
                {
                    prefix = prefabStage.prefabContentsRoot.name;
                }
                else
                {
                    prefix = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                    if (string.IsNullOrEmpty(prefix)) prefix = "Untitled";
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"{prefix}_{timestamp}.png";
                
                string screenshotsPath = UIProbeStorage.GetScreenshotsPath();
                string fullPath = Path.Combine(screenshotsPath, fileName);

                // 2. 创建临时相机以匹配 SceneView
                Camera tempCam = new GameObject("TempScreenshotCam").AddComponent<Camera>();
                tempCam.CopyFrom(view.camera); 
                
                // 3. 设置渲染属性 (去除背景/Grid/Gizmos)
                if (screenshotTransparent)
                {
                    tempCam.clearFlags = CameraClearFlags.SolidColor;
                    tempCam.backgroundColor = new Color(0, 0, 0, 0); // 透明
                }
                else
                {
                    // 如果不透明，使用简单的纯色背景而不是天空盒，以保持干净
                    tempCam.clearFlags = CameraClearFlags.SolidColor;
                    tempCam.backgroundColor = Color.gray; 
                }

                // 强制关闭 Gizmos (通过不调用 DrawGizmos，或者简单地因为 Render() 默认不画 Gizmos)
                // SceneView 的 Grid 是 SceneView 绘制的，Camera.Render() 不会包含它，所以天然就是干净的

                // 4. 渲染到 Texture
                int width = GetActualWidth(true);
                int height = GetActualHeight(true);
                
                // 调整相机 Aspect Ratio 以匹配输出分辨率
                tempCam.aspect = (float)width / height;
                
                // 自动对焦逻辑
                if (autoFrameContent)
                {
                    var targetObj = GetScreenshotTarget();
                    if (targetObj != null)
                        AutoFrameCamera(tempCam, targetObj);
                }
                


                RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
                tempCam.targetTexture = rt;
                tempCam.Render();

                // 5. 读取像素
                Texture2D screenShot = new Texture2D(width, height, TextureFormat.ARGB32, false);
                RenderTexture.active = rt;
                screenShot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                screenShot.Apply();

                // 6. 保存
                byte[] bytes = screenShot.EncodeToPNG();
                File.WriteAllBytes(fullPath, bytes);

                // 7. 清理
                RenderTexture.active = null;
                tempCam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(screenShot);
                UnityEngine.Object.DestroyImmediate(tempCam.gameObject);

                Debug.Log($"[UIProbe] Scene截图已保存: {fullPath}");
                lastScreenshotPath = fullPath; // 更新最近截图路径以便在UI显示
                
                // 提示
                EditorUtility.DisplayDialog("截图成功", $"预制体/场景截图已保存到:\n{fullPath}", "确定");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIProbe] Scene截图失败: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("截图失败", $"发生错误:\n{ex.Message}", "确定");
            }
        }
        private GameObject GetScreenshotTarget()
        {
            // 优先使用当前 Prefab Stage
            var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null) 
            {
                // 1. 尝试寻找 "Canvas (Environment)" (通常在 Prefab Mode 根节点)
                // 这代表了统一的设计分辨率/屏幕区域，比单纯对焦 Root 更准确
                var stageScene = prefabStage.scene;
                if (stageScene.IsValid())
                {
                    var roots = stageScene.GetRootGameObjects();
                    foreach (var r in roots)
                    {
                        if (r.name == "Canvas (Environment)") return r;
                    }
                }
                
                // 2. 回退到 Prefab 内容根节点
                return prefabStage.prefabContentsRoot;
            }
            
            // 其次使用选中物体
            if (Selection.activeGameObject != null) return Selection.activeGameObject;
            
            return null;
        }

        private void AutoFrameCamera(Camera cam, GameObject target)
        {
            Bounds bounds = CalculateBounds(target);
            
            // 移动相机中心对齐 Bounds 中心 (保持 Z 不变，或者在 3D 模式下调整)
            if (cam.orthographic)
            {
                cam.transform.position = new Vector3(bounds.center.x, bounds.center.y, cam.transform.position.z);
                
                // 计算 Orthographic Size
                // Size 是垂直可视高度的一半
                float targetSizeY = bounds.extents.y;
                float targetSizeX = bounds.extents.x / cam.aspect;
                
                cam.orthographicSize = Mathf.Max(targetSizeY, targetSizeX);
                // 稍微加一点 Padding (2%)
                cam.orthographicSize *= 1.02f; 
            }
            else
            {
                // 透视相机逻辑
                cam.transform.LookAt(bounds.center);
                float maxExtent = bounds.extents.magnitude;
                // 防止 divide by zero
                float fov = cam.fieldOfView;
                if (fov < 1) fov = 60;
                
                float dist = maxExtent / Mathf.Sin(Mathf.Deg2Rad * fov / 2.0f);
                cam.transform.position = bounds.center - cam.transform.forward * dist * 1.1f;
            }
        }

        private Bounds CalculateBounds(GameObject target)
        {
            // 1. UI 预制体 (优先使用根节点 RectTransform)
            // 只计算根节点的四个角，忽略子物体，确保对焦区域严格匹配设计分辨率
            var rectTrans = target.GetComponent<RectTransform>();
            if (rectTrans != null)
            {
                Vector3[] corners = new Vector3[4];
                rectTrans.GetWorldCorners(corners);
                
                Bounds b = new Bounds(corners[0], Vector3.zero);
                for (int i = 1; i < 4; i++)
                    b.Encapsulate(corners[i]);
                return b;
            }
            
            // 2. 处理 Renderer (3D)
            var renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    b.Encapsulate(renderers[i].bounds);
                return b;
            }
            
            // 3. Fallback
            return new Bounds(target.transform.position, Vector3.one);
        }



    }
}
