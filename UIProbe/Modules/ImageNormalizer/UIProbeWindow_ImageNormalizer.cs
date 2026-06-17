using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UIProbe
{
    // ─── 图片规范化列表条目（扫描时缓存，避免每帧重复读文件）─────────────────
    internal class NormalizerImageItem
    {
        public string Path;       // 完整路径
        public string FileName;   // 仅文件名（含扩展名）
        public bool   IsSelected; // 是否勾选
        public int    Width;      // 原始宽度
        public int    Height;     // 原始高度
        /// <summary>缓存的尺寸标签，避免 OnGUI 里拼字符串</summary>
        public string SizeLabel;
    }

    // ─── 目标尺寸计算方式 ────────────────────────────────────────────────────
    internal enum NormalizerSizeMode
    {
        Custom,      // 固定尺寸：直接输入 W × H
        Percentage,  // 百分比：按原图等比缩放
        LockWidth,   // 锁定宽度：高度自动等比计算
        LockHeight,  // 锁定高度：宽度自动等比计算
    }

    // ─── 分辨率分组（扫描后按 WxH 聚合）────────────────────────────────────
    internal class NormalizerSizeGroup
    {
        public int    Width;
        public int    Height;
        /// <summary>分组标题，如 "512 × 512"</summary>
        public string SizeLabel;
        public List<NormalizerImageItem> Items = new List<NormalizerImageItem>();
        /// <summary>折叠状态（UI）</summary>
        public bool IsFoldout = true;
    }

    internal sealed partial class ImageNormalizerModule
    {
        // ─── 图片工具子标签 ──────────────────────────────────────────────
        private enum ImageToolSubTab { Normalizer, BatchRename, RedGoldImporter }
        private ImageToolSubTab imageToolSubTab = ImageToolSubTab.Normalizer;

        // 图片规范化标签页状态
        private string normalizerSourceFolder = "";
        private bool normalizerIncludeSubfolders = true;
        private int normalizerTargetWidth = 512;
        private int normalizerTargetHeight = 512;
        private bool normalizerForceSquare = true;
        private ResizeMode normalizerResizeMode = ResizeMode.Expand;
        private ContentAlignment normalizerAlignment = ContentAlignment.Center;
        private bool normalizerOverwrite = true;
        private string normalizerNamingSuffix = "_normalized";
        private Vector2 normalizerScrollPos;

        private List<NormalizerImageItem>  normalizerImageItems  = new List<NormalizerImageItem>();
        private List<NormalizerSizeGroup>  normalizerImageGroups = new List<NormalizerSizeGroup>();
        private bool  normalizerProcessing = false;
        private float normalizerProgress   = 0f;

        // ── 新增：尺寸计算方式 ──────────────────────────────────────────────
        private NormalizerSizeMode normalizerSizeMode     = NormalizerSizeMode.Custom;
        private float              normalizerScalePercent = 100f;   // 百分比模式
        private int                normalizerLockWidth    = 512;    // 锁宽模式
        private int                normalizerLockHeight   = 512;    // 锁高模式
        private bool               normalizerNoUpscale    = false;  // 仅缩小
        private int                normalizerMaxDimension = 0;      // 最大边长（0=不限）
        
        /// <summary>
        /// 绘制图片工具标签页（含子标签：规范化 / 批量命名）
        /// </summary>
        private void DrawImageNormalizerTab()
        {
            EditorGUILayout.LabelField("图片工具 (Image Tools)", EditorStyles.boldLabel);
            EditorGUILayout.Space(3);

            if (!IsImageToolSubTabVisible(imageToolSubTab))
            {
                imageToolSubTab = ImageToolSubTab.Normalizer;
            }

            // 子标签栏
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            DrawImageSubTabButton(ImageToolSubTab.Normalizer,  "📐 图片规范化");
            DrawImageSubTabButton(ImageToolSubTab.BatchRename, "✏️ 批量命名");
            if (IsImageToolSubTabVisible(ImageToolSubTab.RedGoldImporter))
            {
                DrawImageSubTabButton(ImageToolSubTab.RedGoldImporter, "大红大金资源修改导入", 170);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            switch (imageToolSubTab)
            {
                case ImageToolSubTab.Normalizer:  DrawImageNormalizerContent(); break;
                case ImageToolSubTab.BatchRename: DrawImageRenamerContent();    break;
                case ImageToolSubTab.RedGoldImporter: DrawRedGoldResourceImporterContent(); break;
            }
        }

        /// <summary>子标签按钮辅助</summary>
        private void DrawImageSubTabButton(ImageToolSubTab tab, string label, int width = 110)
        {
            GUI.backgroundColor = imageToolSubTab == tab ? Color.cyan : Color.white;
            if (GUILayout.Button(label, EditorStyles.toolbarButton, GUILayout.Width(width)))
                imageToolSubTab = tab;
            GUI.backgroundColor = Color.white;
        }

        private bool IsImageToolSubTabVisible(ImageToolSubTab tab)
        {
            if (config == null || config.modulesVisibility == null)
            {
                return true;
            }

            switch (tab)
            {
                case ImageToolSubTab.RedGoldImporter:
                    return config.modulesVisibility.showRedGoldResourceImporter;
                default:
                    return true;
            }
        }

        /// <summary>
        /// 绘制图片规范化内容（原 DrawImageNormalizerTab 主体）
        /// </summary>
        private void DrawImageNormalizerContent()
        {
            EditorGUILayout.HelpBox("将不同尺寸的图片统一到相同尺寸，保持非透明内容不变形。", MessageType.Info);
            EditorGUILayout.Space(5);
            
            // 源文件夹选择
            GUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("源文件设置", EditorStyles.boldLabel);
            
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("源文件夹:", GUILayout.Width(80));
            EditorGUI.BeginDisabledGroup(normalizerProcessing);
            normalizerSourceFolder = EditorGUILayout.TextField(normalizerSourceFolder);
            if (GUILayout.Button("📁 浏览", GUILayout.Width(60)))
            {
                string selected = EditorUtility.OpenFolderPanel("选择图片文件夹", normalizerSourceFolder, "");
                if (!string.IsNullOrEmpty(selected))
                {
                    normalizerSourceFolder = selected;
                }
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();
            
            normalizerIncludeSubfolders = EditorGUILayout.Toggle("包含子文件夹", normalizerIncludeSubfolders);
            GUILayout.EndVertical();
            
            EditorGUILayout.Space(5);
            
            // ── 目标尺寸设置 ──────────────────────────────────────────────
            GUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("目标尺寸设置", EditorStyles.boldLabel);
            EditorGUI.BeginDisabledGroup(normalizerProcessing);

            // 缩放方式选择
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("缩放方式:", GUILayout.Width(65));
            normalizerSizeMode = (NormalizerSizeMode)GUILayout.Toolbar(
                (int)normalizerSizeMode,
                new[] { "固定尺寸", "百分比", "锁定宽", "锁定高" });
            GUILayout.EndHorizontal();
            EditorGUILayout.Space(5);

            // 根据缩放方式显示对应输入控件
            NormalizerImageItem _sample = normalizerImageItems.FirstOrDefault(x => x.Width > 0);
            switch (normalizerSizeMode)
            {
                case NormalizerSizeMode.Custom:
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("目标尺寸:", GUILayout.Width(65));
                    normalizerTargetWidth  = Mathf.Max(1, EditorGUILayout.IntField(normalizerTargetWidth,  GUILayout.Width(58)));
                    EditorGUILayout.LabelField("x", GUILayout.Width(14));
                    EditorGUI.BeginDisabledGroup(normalizerForceSquare);
                    normalizerTargetHeight = Mathf.Max(1, EditorGUILayout.IntField(normalizerTargetHeight, GUILayout.Width(58)));
                    EditorGUI.EndDisabledGroup();
                    normalizerForceSquare  = EditorGUILayout.ToggleLeft("正方形", normalizerForceSquare, GUILayout.Width(65));
                    if (normalizerForceSquare) normalizerTargetHeight = normalizerTargetWidth;
                    GUILayout.EndHorizontal();
                    break;

                case NormalizerSizeMode.Percentage:
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("缩放比例:", GUILayout.Width(65));
                    normalizerScalePercent = EditorGUILayout.Slider(normalizerScalePercent, 1f, 400f);
                    EditorGUILayout.LabelField($"{normalizerScalePercent:F0}%", GUILayout.Width(42));
                    GUILayout.EndHorizontal();
                    if (_sample != null)
                    {
                        int pw = Mathf.Max(1, Mathf.RoundToInt(_sample.Width  * normalizerScalePercent / 100f));
                        int ph = Mathf.Max(1, Mathf.RoundToInt(_sample.Height * normalizerScalePercent / 100f));
                        EditorGUILayout.HelpBox($"示例（{_sample.Width}×{_sample.Height}）  →  {pw}×{ph}", MessageType.None);
                    }
                    break;

                case NormalizerSizeMode.LockWidth:
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("目标宽度:", GUILayout.Width(65));
                    normalizerLockWidth = Mathf.Max(1, EditorGUILayout.IntField(normalizerLockWidth, GUILayout.Width(70)));
                    EditorGUILayout.LabelField("px  （高度按原图比例自动计算）", EditorStyles.miniLabel);
                    GUILayout.EndHorizontal();
                    if (_sample != null && _sample.Width > 0)
                    {
                        int ph = Mathf.Max(1, Mathf.RoundToInt((float)_sample.Height / _sample.Width * normalizerLockWidth));
                        EditorGUILayout.HelpBox($"示例（{_sample.Width}×{_sample.Height}）  →  {normalizerLockWidth}×{ph}", MessageType.None);
                    }
                    break;

                case NormalizerSizeMode.LockHeight:
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("目标高度:", GUILayout.Width(65));
                    normalizerLockHeight = Mathf.Max(1, EditorGUILayout.IntField(normalizerLockHeight, GUILayout.Width(70)));
                    EditorGUILayout.LabelField("px  （宽度按原图比例自动计算）", EditorStyles.miniLabel);
                    GUILayout.EndHorizontal();
                    if (_sample != null && _sample.Height > 0)
                    {
                        int pw = Mathf.Max(1, Mathf.RoundToInt((float)_sample.Width / _sample.Height * normalizerLockHeight));
                        EditorGUILayout.HelpBox($"示例（{_sample.Width}×{_sample.Height}）  →  {pw}×{normalizerLockHeight}", MessageType.None);
                    }
                    break;
            }

            EditorGUILayout.Space(4);

            // 通用选项：仅缩小 + 最大边长
            GUILayout.BeginHorizontal();
            normalizerNoUpscale = EditorGUILayout.ToggleLeft("仅缩小，不放大", normalizerNoUpscale, GUILayout.Width(120));
            GUILayout.Space(10);
            EditorGUILayout.LabelField("最大边长:", GUILayout.Width(58));
            normalizerMaxDimension = Mathf.Max(0, EditorGUILayout.IntField(normalizerMaxDimension, GUILayout.Width(55)));
            EditorGUILayout.LabelField("px（0 = 不限）", EditorStyles.miniLabel);
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // 缩放模式（ResizeMode）
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("缩放模式:", GUILayout.Width(65));
            normalizerResizeMode = (ResizeMode)EditorGUILayout.EnumPopup(normalizerResizeMode);
            GUILayout.EndHorizontal();

            string resizeModeHint;
            if      (normalizerResizeMode == ResizeMode.Expand)           resizeModeHint = "仅扩展画布，内容不缩放，多余区域填透明";
            else if (normalizerResizeMode == ResizeMode.ProportionalFit)  resizeModeHint = "等比缩放内容以完整适应目标尺寸，多余区域填透明";
            else if (normalizerResizeMode == ResizeMode.ProportionalFill) resizeModeHint = "等比缩放内容以铺满目标尺寸，超出部分从中心裁切";
            else if (normalizerResizeMode == ResizeMode.Stretch)          resizeModeHint = "将内容强制拉伸到目标尺寸，不保持比例";
            else                                                           resizeModeHint = "";
            if (!string.IsNullOrEmpty(resizeModeHint))
                EditorGUILayout.HelpBox(resizeModeHint, MessageType.None);

            // 对齐方式（仅 Expand 模式有意义）
            EditorGUI.BeginDisabledGroup(normalizerResizeMode != ResizeMode.Expand);
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("对齐方式:", GUILayout.Width(65));
            normalizerAlignment = (ContentAlignment)EditorGUILayout.EnumPopup(normalizerAlignment);
            GUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();

            EditorGUI.EndDisabledGroup();
            GUILayout.EndVertical();
            
            EditorGUILayout.Space(5);
            
            // 处理模式设置
            GUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("处理模式", EditorStyles.boldLabel);
            
            EditorGUI.BeginDisabledGroup(normalizerProcessing);
            normalizerOverwrite = EditorGUILayout.Toggle("覆盖原文件", normalizerOverwrite);
            
            EditorGUI.BeginDisabledGroup(normalizerOverwrite);
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("文件名后缀:", GUILayout.Width(80));
            normalizerNamingSuffix = EditorGUILayout.TextField(normalizerNamingSuffix);
            GUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
            EditorGUI.EndDisabledGroup();
            
            GUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            // 扫描和处理按钮
            GUILayout.BeginHorizontal();
            
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(normalizerSourceFolder) || normalizerProcessing);
            if (GUILayout.Button("🔍 扫描图片", GUILayout.Height(30)))
            {
                ScanImagesForNormalizer();
            }
            EditorGUI.EndDisabledGroup();
            
            int selectedCount = normalizerImageItems.Count(x => x.IsSelected);
            EditorGUI.BeginDisabledGroup(selectedCount == 0 || normalizerProcessing);
            if (GUILayout.Button($"开始处理 ({selectedCount} 张)", GUILayout.Height(30)))
            {
                StartNormalizerProcessing();
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.EndHorizontal();

            // ── 分辨率分类列表 ────────────────────────────────────────────
            if (normalizerImageGroups.Count > 0)
            {
                EditorGUILayout.Space(5);

                // ── 全局标题 + 快捷按钮 ──
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(
                    $"找到 {normalizerImageItems.Count} 张  ·  {normalizerImageGroups.Count} 种分辨率  ·  已选 {selectedCount} 张",
                    EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("全选",   EditorStyles.miniButton, GUILayout.Width(40)))
                    foreach (var it in normalizerImageItems) it.IsSelected = true;
                if (GUILayout.Button("全不选", EditorStyles.miniButton, GUILayout.Width(50)))
                    foreach (var it in normalizerImageItems) it.IsSelected = false;
                if (GUILayout.Button("反选",   EditorStyles.miniButton, GUILayout.Width(40)))
                    foreach (var it in normalizerImageItems) it.IsSelected = !it.IsSelected;
                GUILayout.EndHorizontal();

                EditorGUILayout.Space(3);

                // ── 分组滚动列表 ──
                normalizerScrollPos = EditorGUILayout.BeginScrollView(normalizerScrollPos, GUILayout.Height(240));

                foreach (var group in normalizerImageGroups)
                {
                    int groupSel = group.Items.Count(x => x.IsSelected);

                    // ─ 分组标题行（toolbar 风格）─
                    GUILayout.BeginHorizontal(EditorStyles.toolbar);

                    // ① 折叠箭头：固定 16px，与分辨率文字分离，避免 Foldout 自动拉伸
                    Rect foldRect = GUILayoutUtility.GetRect(
                        16f, 16f, GUILayout.Width(16f), GUILayout.ExpandWidth(false));
                    group.IsFoldout = EditorGUI.Foldout(foldRect, group.IsFoldout, GUIContent.none, true);

                    // ② 分辨率标签：用 CalcSize 测量实际文字宽度，确保宽度刚好容下文字
                    var sizeContent = new GUIContent(group.SizeLabel);
                    float sizeW = EditorStyles.boldLabel.CalcSize(sizeContent).x + 6f;
                    EditorGUILayout.LabelField(group.SizeLabel, EditorStyles.boldLabel, GUILayout.Width(sizeW));

                    // ③ 数量标签：紧随分辨率之后，颜色标记选中状态
                    var countStyle = new GUIStyle(EditorStyles.miniLabel);
                    countStyle.normal.textColor = groupSel == group.Items.Count
                        ? new Color(0.3f, 0.8f, 0.3f)        // 全选：绿
                        : groupSel == 0
                            ? new Color(0.55f, 0.55f, 0.55f)  // 全不选：灰
                            : new Color(1f,   0.75f, 0.2f);   // 部分：橙
                    var countContent = new GUIContent($"{groupSel} / {group.Items.Count} 张");
                    float countW = EditorStyles.miniLabel.CalcSize(countContent).x + 4f;
                    EditorGUILayout.LabelField(countContent, countStyle, GUILayout.Width(countW));

                    // ⑤ 目标尺寸箭头（紧随数量，CalcSize 自适应宽度）
                    var (gTargetW, gTargetH) = ComputeTarget(group.Width, group.Height);
                    var targetArrowStyle = new GUIStyle(EditorStyles.miniLabel);
                    targetArrowStyle.normal.textColor = new Color(0.4f, 0.75f, 1f);
                    var targetArrowContent = new GUIContent($"→ {gTargetW}×{gTargetH}");
                    float targetArrowW = EditorStyles.miniLabel.CalcSize(targetArrowContent).x + 4f;
                    EditorGUILayout.LabelField(targetArrowContent, targetArrowStyle, GUILayout.Width(targetArrowW));

                    GUILayout.FlexibleSpace();

                    // ④ 组级快捷按钮（靠右）
                    if (GUILayout.Button("全选组",   EditorStyles.toolbarButton, GUILayout.Width(50)))
                        foreach (var it in group.Items) it.IsSelected = true;
                    if (GUILayout.Button("全不选组", EditorStyles.toolbarButton, GUILayout.Width(60)))
                        foreach (var it in group.Items) it.IsSelected = false;

                    GUILayout.EndHorizontal();

                    // ─ 展开时显示组内文件 ─
                    if (group.IsFoldout)
                    {
                        foreach (var item in group.Items)
                        {
                            Rect rowRect = EditorGUILayout.BeginHorizontal();
                            if (item.IsSelected && Event.current.type == EventType.Repaint)
                                EditorGUI.DrawRect(rowRect, new Color(0.3f, 0.6f, 1f, 0.10f));

                            GUILayout.Space(16); // 缩进
                            item.IsSelected = EditorGUILayout.Toggle(item.IsSelected, GUILayout.Width(18));

                            // 文件名可点击：项目内路径 → Ping 到 Project 窗口；项目外 → 打开文件所在目录
                            if (GUILayout.Button(
                                    new GUIContent($"📄 {item.FileName}", "点击在 Project 窗口中定位"),
                                    EditorStyles.label,
                                    GUILayout.MinWidth(160)))
                                PingNormalizerAsset(item.Path);

                            // 勾选项显示该图实际计算目标（不同缩放方式下各图结果不同）
                            if (item.IsSelected)
                            {
                                var (itw, ith) = ComputeTarget(item.Width, item.Height);
                                var arrowStyle = new GUIStyle(EditorStyles.miniLabel);
                                arrowStyle.normal.textColor = new Color(0.4f, 0.7f, 1f);
                                EditorGUILayout.LabelField($"→ {itw}×{ith}", arrowStyle, GUILayout.Width(90));
                            }
                            else
                            {
                                EditorGUILayout.LabelField("", GUILayout.Width(90));
                            }

                            EditorGUILayout.EndHorizontal();
                        }

                        EditorGUILayout.Space(2);
                    }
                }

                EditorGUILayout.EndScrollView();
            }
            
            // 显示处理进度
            if (normalizerProcessing)
            {
                EditorGUILayout.Space(5);
                EditorGUI.ProgressBar(
                    EditorGUILayout.GetControlRect(GUILayout.Height(20)),
                    normalizerProgress,
                    $"处理中... {(int)(normalizerProgress * 100)}%"
                );
            }
        }
        
        /// <summary>
        /// 扫描图片文件，并一次性读取每张图片的尺寸缓存到列表项中
        /// （避免原来每帧 LoadTexture 的性能问题）
        /// </summary>
        private void ScanImagesForNormalizer()
        {
            normalizerImageItems.Clear();
            normalizerImageGroups.Clear();

            if (string.IsNullOrEmpty(normalizerSourceFolder) || !Directory.Exists(normalizerSourceFolder))
            {
                EditorUtility.DisplayDialog("错误", "请选择有效的文件夹", "确定");
                return;
            }

            SearchOption searchOption = normalizerIncludeSubfolders
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            try
            {
                var files = new List<string>();
                files.AddRange(Directory.GetFiles(normalizerSourceFolder, "*.png", searchOption));
                files.AddRange(Directory.GetFiles(normalizerSourceFolder, "*.jpg", searchOption));
                files.Sort(System.StringComparer.OrdinalIgnoreCase);

                if (files.Count == 0)
                {
                    EditorUtility.DisplayDialog("提示", "未找到 PNG 或 JPG 图片文件", "确定");
                    return;
                }

                // 读取尺寸 —— 仅在扫描时执行一次，不在 OnGUI 里重复 IO
                int total = files.Count;
                for (int i = 0; i < total; i++)
                {
                    string filePath = files[i];

                    // 显示进度（大量图片时避免卡死感）
                    if (i % 20 == 0)
                        EditorUtility.DisplayProgressBar("扫描中", Path.GetFileName(filePath), (float)i / total);

                    int w = 0, h = 0;
                    Texture2D tex = ImageNormalizer.LoadTexture(filePath);
                    if (tex != null)
                    {
                        w = tex.width;
                        h = tex.height;
                        UnityEngine.Object.DestroyImmediate(tex);
                    }

                    normalizerImageItems.Add(new NormalizerImageItem
                    {
                        Path       = filePath,
                        FileName   = Path.GetFileName(filePath),
                        IsSelected = true,                              // 默认全选
                        Width      = w,
                        Height     = h,
                        SizeLabel  = w > 0 ? $"({w}x{h})" : "(未知)"
                    });
                }

                EditorUtility.ClearProgressBar();

                // 扫描完成后按分辨率分组
                BuildNormalizerGroups();
            }
            catch (System.Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("错误", $"扫描失败:\n{e.Message}", "确定");
            }
        }

        /// <summary>
        /// 根据当前缩放方式计算某张图片的目标输出尺寸（含仅缩小 / 最大边长保护）
        /// </summary>
        private (int w, int h) ComputeTarget(int srcW, int srcH)
        {
            // 原始尺寸未知时回退到固定尺寸
            if (srcW <= 0 || srcH <= 0)
                return (normalizerTargetWidth, normalizerTargetHeight);

            int outW, outH;
            switch (normalizerSizeMode)
            {
                case NormalizerSizeMode.Percentage:
                    outW = Mathf.Max(1, Mathf.RoundToInt(srcW * normalizerScalePercent / 100f));
                    outH = Mathf.Max(1, Mathf.RoundToInt(srcH * normalizerScalePercent / 100f));
                    break;

                case NormalizerSizeMode.LockWidth:
                    outW = normalizerLockWidth;
                    outH = Mathf.Max(1, Mathf.RoundToInt((float)srcH / srcW * normalizerLockWidth));
                    break;

                case NormalizerSizeMode.LockHeight:
                    outH = normalizerLockHeight;
                    outW = Mathf.Max(1, Mathf.RoundToInt((float)srcW / srcH * normalizerLockHeight));
                    break;

                default: // Custom
                    outW = normalizerTargetWidth;
                    outH = normalizerTargetHeight;
                    break;
            }

            // 仅缩小：若计算结果大于原始尺寸则保持原尺寸
            if (normalizerNoUpscale && (outW > srcW || outH > srcH))
            {
                outW = srcW;
                outH = srcH;
            }

            // 最大边长限制（等比裁剪到上限）
            if (normalizerMaxDimension > 0)
            {
                int maxEdge = Mathf.Max(outW, outH);
                if (maxEdge > normalizerMaxDimension)
                {
                    float capScale = (float)normalizerMaxDimension / maxEdge;
                    outW = Mathf.Max(1, Mathf.RoundToInt(outW * capScale));
                    outH = Mathf.Max(1, Mathf.RoundToInt(outH * capScale));
                }
            }

            return (outW, outH);
        }

        /// <summary>
        /// 点击文件名时的跳转逻辑：
        ///   • 项目内路径 → 在 Project 窗口高亮并选中对应资源
        ///   • 项目外路径 → 在系统文件管理器中打开所在目录
        /// </summary>
        private void PingNormalizerAsset(string absolutePath)
        {
            string dataPath      = Application.dataPath.Replace('\\', '/');
            string normalizedAbs = absolutePath.Replace('\\', '/');

            if (normalizedAbs.StartsWith(dataPath, System.StringComparison.OrdinalIgnoreCase))
            {
                // 转换为 Assets/ 相对路径
                string relativePath = "Assets" + normalizedAbs.Substring(dataPath.Length);
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(relativePath);
                if (obj != null)
                {
                    EditorGUIUtility.PingObject(obj);   // Project 窗口闪烁定位
                    Selection.activeObject = obj;        // 同时选中，方便 Inspector 查看
                    return;
                }
            }

            // 项目外或资源未导入 → 用系统文件管理器打开所在目录
            EditorUtility.RevealInFinder(absolutePath);
        }

        /// <summary>
        /// 将扫描结果按分辨率分组，并按像素数从大到小排列
        /// </summary>
        private void BuildNormalizerGroups()
        {
            normalizerImageGroups.Clear();

            var dict = new Dictionary<string, NormalizerSizeGroup>();
            foreach (var item in normalizerImageItems)
            {
                // 以 "W_H" 作为分组键，"未知" 统一归入 0_0 组
                string key = $"{item.Width}_{item.Height}";
                if (!dict.ContainsKey(key))
                {
                    dict[key] = new NormalizerSizeGroup
                    {
                        Width     = item.Width,
                        Height    = item.Height,
                        SizeLabel = item.Width > 0
                            ? $"{item.Width} × {item.Height}"
                            : "未知尺寸",
                        IsFoldout = true
                    };
                }
                dict[key].Items.Add(item);
            }

            // 按像素数降序（大图优先）；像素数相同时按数量降序
            normalizerImageGroups = dict.Values
                .OrderByDescending(g => (long)g.Width * g.Height)
                .ThenByDescending(g => g.Items.Count)
                .ToList();
        }
        
        /// <summary>
        /// 开始批量处理（只处理列表中勾选的图片）
        /// </summary>
        private void StartNormalizerProcessing()
        {
            // 只取勾选的条目
            var selectedItems = normalizerImageItems.Where(x => x.IsSelected).ToList();
            if (selectedItems.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请至少勾选一张图片。", "确定");
                return;
            }

            // 构建确认对话框中显示的尺寸描述
            string targetSizeDesc;
            if      (normalizerSizeMode == NormalizerSizeMode.Custom)     targetSizeDesc = $"{normalizerTargetWidth}×{normalizerTargetHeight}（固定）";
            else if (normalizerSizeMode == NormalizerSizeMode.Percentage) targetSizeDesc = $"{normalizerScalePercent:F0}%（各图独立计算）";
            else if (normalizerSizeMode == NormalizerSizeMode.LockWidth)  targetSizeDesc = $"宽 {normalizerLockWidth}px（高自动等比）";
            else                                                           targetSizeDesc = $"高 {normalizerLockHeight}px（宽自动等比）";

            string resizeModeLabel;
            if      (normalizerResizeMode == ResizeMode.Expand)           resizeModeLabel = "仅扩展画布";
            else if (normalizerResizeMode == ResizeMode.ProportionalFit)  resizeModeLabel = "等比适应";
            else if (normalizerResizeMode == ResizeMode.ProportionalFill) resizeModeLabel = "等比填充";
            else                                                           resizeModeLabel = "强制拉伸";

            string noUpscaleNote = normalizerNoUpscale ? "  ·  仅缩小不放大" : "";
            string maxDimNote    = normalizerMaxDimension > 0 ? $"  ·  最大边长 {normalizerMaxDimension}px" : "";

            bool confirmed = EditorUtility.DisplayDialog(
                "确认处理",
                $"即将处理 {selectedItems.Count} / {normalizerImageItems.Count} 张图片\n" +
                $"目标尺寸：{targetSizeDesc}{noUpscaleNote}{maxDimNote}\n" +
                $"缩放模式：{resizeModeLabel}\n" +
                $"文件操作：{(normalizerOverwrite ? "覆盖原文件" : "生成新文件（后缀 " + normalizerNamingSuffix + "）")}\n\n" +
                "确认开始处理？",
                "开始", "取消"
            );

            if (!confirmed) return;

            normalizerProcessing = true;
            normalizerProgress   = 0f;

            // 逐图按各自计算目标处理（支持百分比、锁宽/锁高等差异化尺寸）
            int successCount = 0;
            for (int _i = 0; _i < selectedItems.Count; _i++)
            {
                var _item = selectedItems[_i];
                var (_tw, _th) = ComputeTarget(_item.Width, _item.Height);

                string outputPath = normalizerOverwrite
                    ? _item.Path
                    : Path.Combine(
                        Path.GetDirectoryName(_item.Path),
                        Path.GetFileNameWithoutExtension(_item.Path) + normalizerNamingSuffix + Path.GetExtension(_item.Path));

                Texture2D _src = ImageNormalizer.LoadTexture(_item.Path);
                if (_src != null)
                {
                    Texture2D _result = ImageNormalizer.Normalize(_src, _tw, _th, normalizerAlignment, normalizerResizeMode);
                    UnityEngine.Object.DestroyImmediate(_src);
                    if (_result != null)
                    {
                        if (ImageNormalizer.SaveTexture(_result, outputPath)) successCount++;
                        UnityEngine.Object.DestroyImmediate(_result);
                    }
                }

                normalizerProgress = (float)(_i + 1) / selectedItems.Count;
                Repaint();
            }

            normalizerProcessing = false;
            normalizerProgress   = 0f;

            EditorUtility.DisplayDialog(
                "完成",
                $"处理完成！\n成功：{successCount} / {selectedItems.Count}",
                "确定"
            );

            // 如果处理的是项目内文件，刷新 AssetDatabase
            if (normalizerSourceFolder.StartsWith(Application.dataPath))
                UnityEditor.AssetDatabase.Refresh();
        }
        
        /// <summary>
        /// 从配置应用到UI（规范化 + 批量命名）
        /// </summary>
        private void ApplyImageNormalizerConfig()
        {
            if (config == null) return;

            // 图片规范化
            if (config.imageNormalizer != null)
            {
                normalizerSourceFolder      = config.imageNormalizer.lastSourceFolder;
                normalizerIncludeSubfolders = config.imageNormalizer.includeSubfolders;
                normalizerTargetWidth       = config.imageNormalizer.targetWidth;
                normalizerTargetHeight      = config.imageNormalizer.targetHeight;
                normalizerForceSquare       = config.imageNormalizer.forceSquare;

                if (config.imageNormalizer.alignment == "Center")
                    normalizerAlignment = ContentAlignment.Center;
                else if (config.imageNormalizer.alignment == "KeepOriginal")
                    normalizerAlignment = ContentAlignment.KeepOriginal;

                normalizerOverwrite    = config.imageNormalizer.overwrite;
                normalizerNamingSuffix = config.imageNormalizer.namingSuffix;

                try { normalizerResizeMode = (ResizeMode)System.Enum.Parse(typeof(ResizeMode), config.imageNormalizer.resizeMode); }
                catch { normalizerResizeMode = ResizeMode.Expand; }

                try { normalizerSizeMode = (NormalizerSizeMode)System.Enum.Parse(typeof(NormalizerSizeMode), config.imageNormalizer.sizeMode); }
                catch { normalizerSizeMode = NormalizerSizeMode.Custom; }

                normalizerScalePercent  = config.imageNormalizer.scalePercent > 0 ? config.imageNormalizer.scalePercent : 100f;
                normalizerLockWidth     = config.imageNormalizer.lockWidth  > 0 ? config.imageNormalizer.lockWidth  : 512;
                normalizerLockHeight    = config.imageNormalizer.lockHeight > 0 ? config.imageNormalizer.lockHeight : 512;
                normalizerNoUpscale     = config.imageNormalizer.noUpscale;
                normalizerMaxDimension  = config.imageNormalizer.maxDimension;
            }

            // 批量命名
            ApplyBatchRenameConfig();

            // 大红大金资源修改导入
            ApplyRedGoldResourceImporterConfig();
        }

        /// <summary>
        /// 从UI收集到配置（规范化 + 批量命名）
        /// </summary>
        private void CollectImageNormalizerConfig()
        {
            if (config == null) return;

            // 图片规范化
            if (config.imageNormalizer == null)
                config.imageNormalizer = new ImageNormalizerConfig();

            config.imageNormalizer.lastSourceFolder  = normalizerSourceFolder;
            config.imageNormalizer.includeSubfolders = normalizerIncludeSubfolders;
            config.imageNormalizer.targetWidth       = normalizerTargetWidth;
            config.imageNormalizer.targetHeight      = normalizerTargetHeight;
            config.imageNormalizer.forceSquare       = normalizerForceSquare;
            config.imageNormalizer.alignment         = normalizerAlignment.ToString();
            config.imageNormalizer.overwrite         = normalizerOverwrite;
            config.imageNormalizer.namingSuffix      = normalizerNamingSuffix;
            config.imageNormalizer.resizeMode        = normalizerResizeMode.ToString();
            config.imageNormalizer.sizeMode          = normalizerSizeMode.ToString();
            config.imageNormalizer.scalePercent      = normalizerScalePercent;
            config.imageNormalizer.lockWidth         = normalizerLockWidth;
            config.imageNormalizer.lockHeight        = normalizerLockHeight;
            config.imageNormalizer.noUpscale         = normalizerNoUpscale;
            config.imageNormalizer.maxDimension      = normalizerMaxDimension;

            // 批量命名
            CollectBatchRenameConfig();

            // 大红大金资源修改导入
            CollectRedGoldResourceImporterConfig();
        }
    }
}
