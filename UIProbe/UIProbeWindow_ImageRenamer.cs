using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UIProbe
{
    // ─── 预览条目数据类（命名空间级，partial class 之外）──────────────────────
    internal class RenamePreviewItem
    {
        public string OriginalPath;        // 原始完整路径
        public string OriginalFileName;    // 原始文件名（含扩展名）
        public string OriginalNameNoExt;   // 原始文件名（不含扩展名）
        public string Extension;           // 扩展名，如 ".png"
        public string RuleGeneratedName;   // 规则生成的名称（不含扩展名）
        public string FinalNameNoExt;      // 最终名称（用户可覆盖）
        public bool   IsManualOverride;    // 是否被用户手动修改
        public bool   HasConflict;
        public string ConflictReason;

        /// <summary>最终文件名（含扩展名）</summary>
        public string FinalFileName => FinalNameNoExt + Extension;
    }

    // ─── 批量命名 UI + 逻辑（UIProbeWindow partial class）──────────────────
    public partial class UIProbeWindow
    {
        // ── 文件来源状态 ──────────────────────────────────────────────────────
        private string renamerSourceFolder      = "";
        private string renamerTargetFolder      = "";
        private bool   renamerIncludeSubfolders = false;
        /// <summary>true = 改名后保存在原位并删除原文件；false = 复制到目标路径</summary>
        private bool renamerOverwriteInPlace = false;

        // ── 命名规则状态 ──────────────────────────────────────────────────────
        private string renamerPrefix          = "";
        private bool   renamerKeepOriginalName = true;
        private bool   renamerEnableSequence  = false;
        private int    renamerSeqStart        = 1;
        private int    renamerSeqStep         = 1;
        private int    renamerSeqDigits       = 3;
        private string renamerSuffix          = "";

        // ── 安全选项 ──────────────────────────────────────────────────────────
        private bool renamerUpdateMeta = true;

        // ── 预览列表状态 ──────────────────────────────────────────────────────
        private List<RenamePreviewItem> renamePreviewList  = new List<RenamePreviewItem>();
        private Vector2                 renamePreviewScrollPos;
        private int                     renameConflictCount = 0;

        // ── 折叠状态 ──────────────────────────────────────────────────────────
        private bool renamerFoldSource  = true;
        private bool renamerFoldRules   = true;
        private bool renamerFoldPreview = true;

        // ─────────────────────────────────────────────────────────────────────
        // 主入口
        // ─────────────────────────────────────────────────────────────────────
        private void DrawImageRenamerContent()
        {
            EditorGUILayout.HelpBox(
                "批量修改图片文件名。支持前缀 / 后缀 / 序号规则，改名前可逐行预览并手动覆盖，执行后自动生成操作日志。",
                MessageType.Info);
            EditorGUILayout.Space(5);

            // ① 文件来源
            renamerFoldSource = EditorGUILayout.Foldout(renamerFoldSource, "① 文件来源", true, EditorStyles.foldoutHeader);
            if (renamerFoldSource) DrawRenamerFileSource();

            EditorGUILayout.Space(3);

            // ② 命名规则
            renamerFoldRules = EditorGUILayout.Foldout(renamerFoldRules, "② 命名规则构建器", true, EditorStyles.foldoutHeader);
            if (renamerFoldRules) DrawRenamerRules();

            EditorGUILayout.Space(3);

            // ③ 预览列表 + ④ 执行区域（仅在有文件时显示）
            if (renamePreviewList.Count > 0)
            {
                string previewHeader = renameConflictCount > 0
                    ? $"③ 预览列表 ({renamePreviewList.Count} 个文件  ⚠ {renameConflictCount} 个冲突)"
                    : $"③ 预览列表 ({renamePreviewList.Count} 个文件  ✓ 无冲突)";

                renamerFoldPreview = EditorGUILayout.Foldout(renamerFoldPreview, previewHeader, true, EditorStyles.foldoutHeader);
                if (renamerFoldPreview) DrawRenamerPreviewList();

                EditorGUILayout.Space(5);
                DrawRenamerExecuteArea();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // ① 文件来源
        // ─────────────────────────────────────────────────────────────────────
        private void DrawRenamerFileSource()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);

            // 源文件夹
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("源文件夹:", GUILayout.Width(75));
            renamerSourceFolder = EditorGUILayout.TextField(renamerSourceFolder);
            if (GUILayout.Button("📁", GUILayout.Width(28)))
            {
                string p = EditorUtility.OpenFolderPanel("选择源图片文件夹", renamerSourceFolder, "");
                if (!string.IsNullOrEmpty(p)) renamerSourceFolder = p;
            }
            GUILayout.EndHorizontal();

            renamerIncludeSubfolders = EditorGUILayout.Toggle("包含子文件夹", renamerIncludeSubfolders);

            EditorGUILayout.Space(5);

            // 覆盖模式开关
            EditorGUI.BeginChangeCheck();
            renamerOverwriteInPlace = EditorGUILayout.ToggleLeft(
                "原路径覆盖（改名后文件保存在原位置，删除原文件）", renamerOverwriteInPlace);
            if (EditorGUI.EndChangeCheck() && renamerOverwriteInPlace)
            {
                // 勾选时自动填入源路径，方便查看实际目标
                renamerTargetFolder = renamerSourceFolder;
            }

            // 目标文件夹（原路径覆盖时置灰）
            EditorGUI.BeginDisabledGroup(renamerOverwriteInPlace);
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("目标文件夹:", GUILayout.Width(75));
            renamerTargetFolder = EditorGUILayout.TextField(renamerTargetFolder);
            if (GUILayout.Button("📁", GUILayout.Width(28)))
            {
                string p = EditorUtility.OpenFolderPanel("选择目标文件夹", renamerTargetFolder, "");
                if (!string.IsNullOrEmpty(p)) renamerTargetFolder = p;
            }
            GUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(5);

            // 扫描按钮
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(renamerSourceFolder));
            if (GUILayout.Button("🔍 扫描图片", GUILayout.Height(28)))
                ScanRenamerImages();
            EditorGUI.EndDisabledGroup();

            GUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────────────────────────────
        // ② 命名规则构建器
        // ─────────────────────────────────────────────────────────────────────
        private void DrawRenamerRules()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUI.BeginChangeCheck();

            // 前缀
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("前缀:", GUILayout.Width(50));
            renamerPrefix = EditorGUILayout.TextField(renamerPrefix, GUILayout.Width(160));
            EditorGUILayout.LabelField("（不需要尾部下划线，自动添加分隔符）", EditorStyles.miniLabel);
            GUILayout.EndHorizontal();

            // 保留原文件名
            renamerKeepOriginalName = EditorGUILayout.ToggleLeft("保留原文件名", renamerKeepOriginalName);

            // 序号
            GUILayout.BeginHorizontal();
            renamerEnableSequence = EditorGUILayout.ToggleLeft("启用序号", renamerEnableSequence, GUILayout.Width(75));
            EditorGUI.BeginDisabledGroup(!renamerEnableSequence);
            EditorGUILayout.LabelField("起始:", GUILayout.Width(32));
            renamerSeqStart  = EditorGUILayout.IntField(renamerSeqStart,  GUILayout.Width(40));
            EditorGUILayout.LabelField("步长:", GUILayout.Width(32));
            renamerSeqStep   = Mathf.Max(1, EditorGUILayout.IntField(renamerSeqStep, GUILayout.Width(40)));
            EditorGUILayout.LabelField("位数:", GUILayout.Width(32));
            renamerSeqDigits = Mathf.Clamp(EditorGUILayout.IntField(renamerSeqDigits, GUILayout.Width(40)), 1, 8);
            EditorGUI.EndDisabledGroup();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // 后缀
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("后缀:", GUILayout.Width(50));
            renamerSuffix = EditorGUILayout.TextField(renamerSuffix, GUILayout.Width(160));
            GUILayout.EndHorizontal();

            // 实时预览示例
            EditorGUILayout.Space(5);
            string exampleNew = BuildRenamerName("icon_attack", renamerSeqStart);
            GUI.backgroundColor = new Color(0.9f, 0.95f, 1f);
            EditorGUILayout.HelpBox($"预览示例：icon_attack.png  →  {exampleNew}.png", MessageType.None);
            GUI.backgroundColor = Color.white;

            bool rulesChanged = EditorGUI.EndChangeCheck();

            // 规则变化时自动重新生成预览（保留手动覆盖项）
            if (rulesChanged && renamePreviewList.Count > 0)
                RegenerateRenamePreview();

            // 手动重新生成按钮
            if (renamePreviewList.Count > 0)
            {
                EditorGUILayout.Space(3);
                if (GUILayout.Button("↻ 重新生成预览（保留手动修改）", GUILayout.Width(210)))
                    RegenerateRenamePreview();
            }

            GUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────────────────────────────
        // ③ 预览列表
        // ─────────────────────────────────────────────────────────────────────
        private void DrawRenamerPreviewList()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);

            // 表头
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("原文件名",              EditorStyles.toolbarButton, GUILayout.Width(190));
            EditorGUILayout.LabelField("→",                                                GUILayout.Width(18));
            EditorGUILayout.LabelField("新文件名（可手动修改）", EditorStyles.toolbarButton, GUILayout.MinWidth(180));
            EditorGUILayout.LabelField("状态",                  EditorStyles.toolbarButton, GUILayout.Width(90));
            GUILayout.EndHorizontal();

            renamePreviewScrollPos = EditorGUILayout.BeginScrollView(renamePreviewScrollPos, GUILayout.Height(220));

            for (int i = 0; i < renamePreviewList.Count; i++)
            {
                var item = renamePreviewList[i];

                // 行底色
                Color rowBg = item.HasConflict     ? new Color(0.85f, 0.25f, 0.25f, 0.25f) :
                              item.IsManualOverride ? new Color(0.3f,  0.5f,  1.0f,  0.20f) :
                                                     Color.clear;

                Rect rowRect = EditorGUILayout.BeginHorizontal();
                // 只在 Repaint 事件绘制背景色，Layout 事件时 rect.height 为 0 会导致绘制错误
                if (rowBg != Color.clear && Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(rowRect, rowBg);

                // 原文件名（只读）
                EditorGUILayout.LabelField(item.OriginalFileName, GUILayout.Width(190));
                EditorGUILayout.LabelField("→", GUILayout.Width(18));

                // 可编辑的新名（不含扩展名）
                EditorGUI.BeginChangeCheck();
                string edited = EditorGUILayout.TextField(item.FinalNameNoExt, GUILayout.MinWidth(180));
                if (EditorGUI.EndChangeCheck())
                {
                    item.FinalNameNoExt   = edited;
                    item.IsManualOverride = !string.Equals(edited, item.RuleGeneratedName, StringComparison.Ordinal);
                    ValidateRenameConflicts();
                }

                // 状态标签
                if (item.HasConflict)
                {
                    var s = new GUIStyle(EditorStyles.miniLabel);
                    s.normal.textColor = new Color(0.9f, 0.2f, 0.2f);
                    EditorGUILayout.LabelField($"⚠ {item.ConflictReason}", s, GUILayout.Width(90));
                }
                else if (item.IsManualOverride)
                {
                    var s = new GUIStyle(EditorStyles.miniLabel);
                    s.normal.textColor = new Color(0.3f, 0.5f, 1f);
                    EditorGUILayout.LabelField("✎ 已修改", s, GUILayout.Width(90));
                }
                else
                {
                    EditorGUILayout.LabelField("✓ 正常", EditorStyles.miniLabel, GUILayout.Width(90));
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            // 底部快捷操作
            EditorGUILayout.Space(3);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("↻ 重置手动修改", EditorStyles.miniButton))
            {
                foreach (var item in renamePreviewList)
                {
                    if (item.IsManualOverride)
                    {
                        item.FinalNameNoExt   = item.RuleGeneratedName;
                        item.IsManualOverride = false;
                    }
                }
                ValidateRenameConflicts();
            }
            if (GUILayout.Button("✕ 清空列表", EditorStyles.miniButton))
            {
                renamePreviewList.Clear();
                renameConflictCount = 0;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────────────────────────────
        // ④ 执行区域
        // ─────────────────────────────────────────────────────────────────────
        private void DrawRenamerExecuteArea()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);

            // Meta 同步选项
            renamerUpdateMeta = EditorGUILayout.ToggleLeft(
                "同步更新 .meta 文件（Unity 项目内改名时建议开启）", renamerUpdateMeta);

            EditorGUILayout.Space(4);

            // 冲突警告
            if (renameConflictCount > 0)
            {
                EditorGUILayout.HelpBox(
                    $"存在 {renameConflictCount} 个冲突项（红色标记行），请在预览列表中修正后再执行。",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(3);

            // 执行按钮
            EditorGUI.BeginDisabledGroup(renameConflictCount > 0);
            GUI.backgroundColor = renameConflictCount > 0
                ? new Color(0.6f, 0.6f, 0.6f)
                : new Color(0.3f, 0.85f, 0.4f);

            string btnLabel = renameConflictCount > 0
                ? $"❌ 存在冲突，无法执行"
                : $"✅ 执行重命名（{renamePreviewList.Count} 个文件）";

            if (GUILayout.Button(btnLabel, GUILayout.Height(32)))
                ExecuteRename();

            GUI.backgroundColor = Color.white;
            EditorGUI.EndDisabledGroup();

            // 快速打开日志按钮
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("📂 打开重命名日志", EditorStyles.miniButton))
                EditorUtility.RevealInFinder(ImageRenameLogManager.GetLogsRootPath());
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────────────────────────────
        // 业务逻辑
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>扫描源文件夹中的图片文件，生成初始预览列表</summary>
        private void ScanRenamerImages()
        {
            renamePreviewList.Clear();
            renameConflictCount = 0;

            if (string.IsNullOrEmpty(renamerSourceFolder) || !Directory.Exists(renamerSourceFolder))
            {
                EditorUtility.DisplayDialog("错误", "请选择有效的源文件夹", "确定");
                return;
            }

            var searchOpt  = renamerIncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var extensions = new[] { "*.png", "*.jpg", "*.jpeg", "*.tga", "*.psd" };

            var files = new List<string>();
            foreach (var ext in extensions)
                files.AddRange(Directory.GetFiles(renamerSourceFolder, ext, searchOpt));

            if (files.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "未找到图片文件（支持 PNG / JPG / JPEG / TGA / PSD）", "确定");
                return;
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < files.Count; i++)
            {
                int seqValue = renamerSeqStart + i * renamerSeqStep;
                string path        = files[i];
                string nameNoExt   = Path.GetFileNameWithoutExtension(path);
                string ext2        = Path.GetExtension(path);
                string generated   = BuildRenamerName(nameNoExt, seqValue);

                renamePreviewList.Add(new RenamePreviewItem
                {
                    OriginalPath      = path,
                    OriginalFileName  = Path.GetFileName(path),
                    OriginalNameNoExt = nameNoExt,
                    Extension         = ext2,
                    RuleGeneratedName = generated,
                    FinalNameNoExt    = generated,
                    IsManualOverride  = false
                });
            }

            ValidateRenameConflicts();
        }

        /// <summary>
        /// 根据当前命名规则构建新名称（不含扩展名）
        /// </summary>
        /// <param name="originalNameNoExt">原文件名（不含扩展名）</param>
        /// <param name="seqValue">本条目对应的序号值（已计算 start + index * step）</param>
        private string BuildRenamerName(string originalNameNoExt, int seqValue)
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(renamerPrefix))
                parts.Add(renamerPrefix);

            if (renamerKeepOriginalName)
                parts.Add(originalNameNoExt);

            if (renamerEnableSequence)
                parts.Add(seqValue.ToString("D" + renamerSeqDigits));

            if (!string.IsNullOrEmpty(renamerSuffix))
                parts.Add(renamerSuffix);

            // 全部为空时回退到原名
            if (parts.Count == 0) return originalNameNoExt;

            return string.Join("_", parts);
        }

        /// <summary>按当前规则重新生成所有预览名（手动覆盖项保持不变）</summary>
        private void RegenerateRenamePreview()
        {
            int seqVal = renamerSeqStart;
            foreach (var item in renamePreviewList)
            {
                item.RuleGeneratedName = BuildRenamerName(item.OriginalNameNoExt, seqVal);
                seqVal += renamerSeqStep;

                // 只更新未手动覆盖的条目
                if (!item.IsManualOverride)
                    item.FinalNameNoExt = item.RuleGeneratedName;
            }
            ValidateRenameConflicts();
        }

        /// <summary>校验冲突：列表内重复 + 与目标目录已有文件冲突</summary>
        private void ValidateRenameConflicts()
        {
            renameConflictCount = 0;

            string effectiveTarget = renamerOverwriteInPlace ? renamerSourceFolder : renamerTargetFolder;

            // 统计列表内每个新文件名出现次数
            var nameCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in renamePreviewList)
            {
                string key = item.FinalFileName;
                nameCount[key] = nameCount.ContainsKey(key) ? nameCount[key] + 1 : 1;
            }

            foreach (var item in renamePreviewList)
            {
                item.HasConflict    = false;
                item.ConflictReason = "";

                string newFileName = item.FinalFileName;

                // ① 列表内部重复
                if (nameCount.ContainsKey(newFileName) && nameCount[newFileName] > 1)
                {
                    item.HasConflict    = true;
                    item.ConflictReason = "列表内重复";
                    renameConflictCount++;
                    continue;
                }

                // ② 与目标目录已有文件冲突（跳过文件自身）
                if (!string.IsNullOrEmpty(effectiveTarget) && Directory.Exists(effectiveTarget))
                {
                    string destPath = Path.Combine(effectiveTarget, newFileName);
                    bool isSelf = renamerOverwriteInPlace &&
                                  string.Equals(destPath, item.OriginalPath, StringComparison.OrdinalIgnoreCase);

                    if (!isSelf && File.Exists(destPath))
                    {
                        item.HasConflict    = true;
                        item.ConflictReason = "目标已存在";
                        renameConflictCount++;
                    }
                }
            }
        }

        /// <summary>执行批量重命名</summary>
        private void ExecuteRename()
        {
            string effectiveTarget = renamerOverwriteInPlace ? renamerSourceFolder : renamerTargetFolder;

            // 前置校验
            if (!renamerOverwriteInPlace && string.IsNullOrEmpty(renamerTargetFolder))
            {
                EditorUtility.DisplayDialog("错误", "未勾选「原路径覆盖」时，必须填写目标文件夹。", "确定");
                return;
            }

            int totalCount    = renamePreviewList.Count;
            int manualCount   = renamePreviewList.Count(x => x.IsManualOverride);
            string modeLabel  = renamerOverwriteInPlace ? "原路径覆盖（删除原文件）" : "复制到目标路径（保留原文件）";

            bool confirmed = EditorUtility.DisplayDialog(
                "确认执行重命名",
                $"即将重命名 {totalCount} 个文件\n" +
                $"操作模式：{modeLabel}\n" +
                $"目标路径：{effectiveTarget}\n" +
                $"手动覆盖：{manualCount} 项\n" +
                $"同步 .meta：{(renamerUpdateMeta ? "是" : "否")}\n\n" +
                "此操作不可撤销，确认执行？",
                "执行", "取消"
            );
            if (!confirmed) return;

            // 确保目标目录存在
            if (!Directory.Exists(effectiveTarget))
                Directory.CreateDirectory(effectiveTarget);

            // 判断目标是否在 Unity 项目内（需要刷新 AssetDatabase）
            bool isInsideProject = effectiveTarget.Replace('\\', '/').StartsWith(
                Application.dataPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

            int successCount = 0;
            var logItems     = new List<ImageRenameLogItem>();

            foreach (var item in renamePreviewList)
            {
                string destPath = Path.Combine(effectiveTarget, item.FinalFileName);

                try
                {
                    if (renamerOverwriteInPlace)
                    {
                        // 同名则跳过（已是目标名）
                        if (string.Equals(item.OriginalPath, destPath, StringComparison.OrdinalIgnoreCase))
                        {
                            successCount++;
                            continue;
                        }

                        // 复制为新名 → 删除原文件
                        File.Copy(item.OriginalPath, destPath, overwrite: true);
                        if (renamerUpdateMeta)
                        {
                            string srcMeta = item.OriginalPath + ".meta";
                            if (File.Exists(srcMeta))
                                File.Copy(srcMeta, destPath + ".meta", overwrite: true);
                        }
                        File.Delete(item.OriginalPath);
                        if (renamerUpdateMeta)
                        {
                            string srcMeta = item.OriginalPath + ".meta";
                            if (File.Exists(srcMeta)) File.Delete(srcMeta);
                        }
                    }
                    else
                    {
                        // 复制到目标路径，保留原文件
                        File.Copy(item.OriginalPath, destPath, overwrite: true);
                        if (renamerUpdateMeta)
                        {
                            string srcMeta = item.OriginalPath + ".meta";
                            if (File.Exists(srcMeta))
                                File.Copy(srcMeta, destPath + ".meta", overwrite: true);
                        }
                    }

                    successCount++;
                    logItems.Add(new ImageRenameLogItem
                    {
                        OriginalName  = item.OriginalFileName,
                        NewName       = item.FinalFileName,
                        OriginalPath  = item.OriginalPath,
                        TargetFolder  = effectiveTarget,
                        Mode          = modeLabel
                    });
                }
                catch (Exception e)
                {
                    Debug.LogError($"[UIProbe] 重命名失败: {item.OriginalFileName} → {item.FinalFileName}\n{e.Message}");
                }
            }

            // 刷新 Unity 资源数据库
            if (isInsideProject) AssetDatabase.Refresh();

            // 生成 CSV 日志
            string logPath = ImageRenameLogManager.GenerateLog(logItems);

            // 清空预览列表
            renamePreviewList.Clear();
            renameConflictCount = 0;

            // 结果弹窗
            string resultMsg = $"重命名完成！\n成功：{successCount} / {totalCount}";
            if (logPath != null) resultMsg += $"\n\n日志已生成：\n{logPath}";
            EditorUtility.DisplayDialog("完成", resultMsg, "确定");
        }

        // ─────────────────────────────────────────────────────────────────────
        // 配置持久化（由 ApplyImageNormalizerConfig / CollectImageNormalizerConfig 调用）
        // ─────────────────────────────────────────────────────────────────────

        private void ApplyBatchRenameConfig()
        {
            if (config == null || config.batchRename == null) return;
            var c = config.batchRename;

            renamerSourceFolder      = c.lastSourceFolder;
            renamerTargetFolder      = c.lastTargetFolder;
            renamerIncludeSubfolders = c.includeSubfolders;
            renamerOverwriteInPlace  = c.overwriteInPlace;
            renamerPrefix            = c.prefix;
            renamerKeepOriginalName  = c.keepOriginalName;
            renamerEnableSequence    = c.enableSequence;
            renamerSeqStart          = c.seqStart;
            renamerSeqStep           = c.seqStep;
            renamerSeqDigits         = c.seqDigits;
            renamerSuffix            = c.suffix;
            renamerUpdateMeta        = c.updateMeta;
        }

        private void CollectBatchRenameConfig()
        {
            if (config == null) return;
            if (config.batchRename == null) config.batchRename = new BatchRenameConfig();
            var c = config.batchRename;

            c.lastSourceFolder  = renamerSourceFolder;
            c.lastTargetFolder  = renamerTargetFolder;
            c.includeSubfolders = renamerIncludeSubfolders;
            c.overwriteInPlace  = renamerOverwriteInPlace;
            c.prefix            = renamerPrefix;
            c.keepOriginalName  = renamerKeepOriginalName;
            c.enableSequence    = renamerEnableSequence;
            c.seqStart          = renamerSeqStart;
            c.seqStep           = renamerSeqStep;
            c.seqDigits         = renamerSeqDigits;
            c.suffix            = renamerSuffix;
            c.updateMeta        = renamerUpdateMeta;
        }
    }
}
