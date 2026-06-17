using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UIProbe
{
    internal sealed partial class ImageNormalizerModule
    {
        private string redGoldTablePath = "";
        private readonly List<string> redGoldImageSourceFolders = new List<string> { "" };
        private bool redGoldIncludeSubfolders = true;

        private string redGoldNameColumn = "名称";
        private string redGoldQualityColumn = "品质";
        private string redGoldGridLongColumn = "格数：长";
        private string redGoldGridWideColumn = "格数：宽";
        private string redGoldGridCountColumn = "格数";
        private string redGoldIconPathColumn = "图标路径";

        private bool redGoldOverrideGrid = false;
        private int redGoldOverrideGridLong = 2;
        private int redGoldOverrideGridWide = 3;
        private int redGoldCellPixelSize = 100;
        private int redGoldMaxOutputEdge = 512;

        // ▼ 品质列表 + 预设状态
        private List<QualityConfigEntry> redGoldQualityEntries = new List<QualityConfigEntry>
        {
            new QualityConfigEntry { keyword = "红", displayName = "红色品质", namingTemplate = "T_Icon_Red_{Pinyin}.png", usePinyin = true },
            new QualityConfigEntry { keyword = "紫", displayName = "紫色品质" },
            new QualityConfigEntry { keyword = "金", displayName = "金色品质" },
        };
        private bool redGoldFoldQuality = true;
        private string redGoldCurrentPreset = "";
        private string[] redGoldPresetNames = new string[0];
        private bool redGoldOverwriteTable = false;
        private string redGoldOutputTablePath = "";

        private RedGoldTableData redGoldTableData;
        private readonly List<RedGoldImportRow> redGoldPreviewRows = new List<RedGoldImportRow>();
        private readonly List<UnmatchedSourceInfo> redGoldUnmatchedSources = new List<UnmatchedSourceInfo>();
        private Vector2 redGoldScrollPos;
        private Vector2 redGoldScrollPosMissing;
        private Vector2 redGoldScrollPosUnmatched;
        private bool redGoldProcessing;
        private float redGoldProgress;

        // ▼ 撤销系统状态
        private RedGoldUndoManager redGoldUndoManager;

        // ▼ 缩略图缓存
        private readonly Dictionary<string, Texture2D> redGoldThumbnailCache = new Dictionary<string, Texture2D>();

        private bool redGoldFoldSource = true;
        private bool redGoldFoldColumns = true;
        private bool redGoldFoldRules = true;
        private bool redGoldFoldPreview = true;
        private bool redGoldFoldReplaceable = true;
        private bool redGoldFoldMissing = true;
        private bool redGoldFoldUnmatched = true;

        // ▼ 批量操作栏状态
        private bool redGoldFoldBatchOps = false;
        private string redGoldBatchPrefix = "";
        private string redGoldBatchSuffix = "";
        private string redGoldBatchFind = "";
        private string redGoldBatchReplace = "";
        private int redGoldBatchGridLong = 2;
        private int redGoldBatchGridWide = 2;
        private string redGoldBatchQuality = "";
        private string redGoldBatchOutputPath = "";

        // ▼ 后台生成状态
        private int redGoldProcessingIndex;
        private int redGoldProcessingTotal;
        private List<RedGoldImportRow> redGoldProcessingRows;
        private int redGoldProcessingSuccess;
        private List<RedGoldUndoEntry> redGoldProcessingUndoEntries;
        private string redGoldProcessingTableOutputPath;
        private string redGoldProcessingUndoDir;
        private bool redGoldProcessingCancelled;

        private RedGoldUndoManager EnsureRedGoldUndoManager()
        {
            if (redGoldUndoManager == null)
            {
                redGoldUndoManager = new RedGoldUndoManager();
            }

            return redGoldUndoManager;
        }

        private void DrawRedGoldResourceImporterContent()
        {
            EditorGUILayout.HelpBox(
                "读取 CSV/TSV 表格，根据名称匹配图片，按品质输出到配置目录；画布按格数比例调整，内容等比适配不裁切，并把新图标路径写回表格。",
                MessageType.Info);
            EditorGUILayout.Space(5);

            redGoldFoldSource = EditorGUILayout.Foldout(redGoldFoldSource, "① 表格与图片来源", true, EditorStyles.foldoutHeader);
            if (redGoldFoldSource) DrawRedGoldSourceSettings();

            EditorGUILayout.Space(3);
            redGoldFoldColumns = EditorGUILayout.Foldout(redGoldFoldColumns, "② 表格列名映射", true, EditorStyles.foldoutHeader);
            if (redGoldFoldColumns) DrawRedGoldColumnSettings();

            EditorGUILayout.Space(3);
            redGoldFoldRules = EditorGUILayout.Foldout(redGoldFoldRules, "③ 输出规则", true, EditorStyles.foldoutHeader);
            if (redGoldFoldRules) DrawRedGoldOutputSettings();

            EditorGUILayout.Space(6);
            DrawRedGoldActions();

            if (redGoldPreviewRows.Count > 0 || redGoldUnmatchedSources.Count > 0)
            {
                int replaceable = redGoldPreviewRows.Count(x => !x.HasError);
                int missing = redGoldPreviewRows.Count(x => x.HasError);
                int unmatched = redGoldUnmatchedSources.Count;
                string summary = $"④ 预览列表 ({replaceable} 可替换";
                if (missing > 0) summary += $" / {missing} 缺失";
                if (unmatched > 0) summary += $" / {unmatched} 未匹配";
                summary += ")";

                EditorGUILayout.Space(5);
                redGoldFoldPreview = EditorGUILayout.Foldout(redGoldFoldPreview, summary, true, EditorStyles.foldoutHeader);
                if (redGoldFoldPreview) DrawRedGoldPreviewList();
            }

            if (redGoldProcessing)
            {
                EditorGUILayout.Space(5);
                GUILayout.BeginHorizontal();
                EditorGUI.ProgressBar(
                    EditorGUILayout.GetControlRect(GUILayout.Height(20)),
                    redGoldProgress,
                    $"处理中... {(int)(redGoldProgress * 100)}%");
                if (GUILayout.Button("取消", GUILayout.Width(50), GUILayout.Height(20)))
                {
                    redGoldProcessingCancelled = true;
                }
                GUILayout.EndHorizontal();
            }
        }

        private void DrawRedGoldSourceSettings()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("表格文件:", GUILayout.MinWidth(60));
            redGoldTablePath = EditorGUILayout.TextField(redGoldTablePath, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("📄", GUILayout.Width(28)))
            {
                string p = EditorUtility.OpenFilePanel("选择 CSV/TSV/Excel 表格", RedGoldPathHelper.GetExistingDirectory(redGoldTablePath), "csv,tsv,xlsx");
                if (!string.IsNullOrEmpty(p)) redGoldTablePath = p;
            }
            GUILayout.EndHorizontal();

            EditorGUILayout.LabelField("图片文件夹（优先级从上到下）:", EditorStyles.miniLabel);
            for (int i = 0; i < redGoldImageSourceFolders.Count; i++)
            {
                int capturedIdx = i;
                GUILayout.BeginHorizontal();
                redGoldImageSourceFolders[i] = EditorGUILayout.TextField(redGoldImageSourceFolders[i], GUILayout.ExpandWidth(true));
                if (GUILayout.Button("📁", GUILayout.Width(28)))
                {
                    string p = EditorUtility.OpenFolderPanel("选择待修改图片文件夹", RedGoldPathHelper.ToAbsolutePath(redGoldImageSourceFolders[i]), "");
                    if (!string.IsNullOrEmpty(p)) redGoldImageSourceFolders[i] = p;
                }
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
                {
                    redGoldImageSourceFolders.RemoveAt(capturedIdx);
                    break;
                }
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ 新增源文件夹", EditorStyles.miniButton))
            {
                redGoldImageSourceFolders.Add("");
            }

            redGoldIncludeSubfolders = EditorGUILayout.Toggle("包含子文件夹", redGoldIncludeSubfolders);

            GUILayout.EndVertical();
        }

        private void DrawRedGoldColumnSettings()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            DrawRedGoldTextField("名称列:", ref redGoldNameColumn);
            DrawRedGoldTextField("品质列:", ref redGoldQualityColumn);
            DrawRedGoldTextField("格数：长列:", ref redGoldGridLongColumn);
            DrawRedGoldTextField("格数：宽列:", ref redGoldGridWideColumn);
            DrawRedGoldTextField("格数列:", ref redGoldGridCountColumn);
            DrawRedGoldTextField("图标路径列:", ref redGoldIconPathColumn);
            GUILayout.EndVertical();
        }

        private void DrawRedGoldPresetBar()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.BeginHorizontal();

            EditorGUILayout.LabelField("预设:", GUILayout.Width(40));

            // 刷新预设列表
            redGoldPresetNames = RedGoldPresetManager.ListPresets().Prepend("").ToArray();
            int selectedIdx = Mathf.Max(0, Array.IndexOf(redGoldPresetNames, redGoldCurrentPreset));
            int newIdx = EditorGUILayout.Popup(selectedIdx, redGoldPresetNames, GUILayout.Width(120));
            if (newIdx != selectedIdx)
            {
                string newName = newIdx > 0 ? redGoldPresetNames[newIdx] : "";
                if (!string.IsNullOrEmpty(newName))
                {
                    var preset = RedGoldPresetManager.LoadPreset(newName);
                    if (preset != null)
                    {
                        ApplyPreset(preset);
                        redGoldCurrentPreset = newName;
                    }
                }
                else
                {
                    redGoldCurrentPreset = "";
                }
            }

            if (GUILayout.Button("保存", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                if (string.IsNullOrEmpty(redGoldCurrentPreset))
                {
                    SavePresetDialog();
                }
                else
                {
                    var preset = BuildCurrentPreset();
                    preset.name = redGoldCurrentPreset;
                    RedGoldPresetManager.SavePreset(preset);
                }
            }

            if (GUILayout.Button("另存为...", EditorStyles.miniButton, GUILayout.Width(64)))
            {
                SavePresetDialog();
            }

            if (!string.IsNullOrEmpty(redGoldCurrentPreset) && GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                if (EditorUtility.DisplayDialog("删除预设", $"确认删除预设「{redGoldCurrentPreset}」？", "删除", "取消"))
                {
                    RedGoldPresetManager.DeletePreset(redGoldCurrentPreset);
                    redGoldCurrentPreset = "";
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(redGoldCurrentPreset))
            {
                EditorGUILayout.LabelField($"当前: {redGoldCurrentPreset}", EditorStyles.miniLabel);
            }
            GUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }

        private void SavePresetDialog()
        {
            string name = EditorUtility.SaveFilePanelInProject("保存预设", "my_preset", "json", "输入预设名称");
            if (string.IsNullOrEmpty(name)) return;

            string presetName = System.IO.Path.GetFileNameWithoutExtension(name);
            var preset = BuildCurrentPreset();
            preset.name = presetName;
            RedGoldPresetManager.SavePreset(preset);
            redGoldCurrentPreset = presetName;
        }

        private RedGoldPreset BuildCurrentPreset()
        {
            return new RedGoldPreset
            {
                name = "",
                description = "",
                qualityEntries = new List<QualityConfigEntry>(redGoldQualityEntries),
                cellPixelSize = redGoldCellPixelSize,
                maxOutputEdge = redGoldMaxOutputEdge,
                overrideGrid = redGoldOverrideGrid,
                overrideGridLong = redGoldOverrideGridLong,
                overrideGridWide = redGoldOverrideGridWide,
            };
        }

        private void ApplyPreset(RedGoldPreset preset)
        {
            if (preset.qualityEntries != null)
                redGoldQualityEntries = preset.qualityEntries;
            redGoldCellPixelSize = preset.cellPixelSize;
            redGoldMaxOutputEdge = preset.maxOutputEdge;
            redGoldOverrideGrid = preset.overrideGrid;
            redGoldOverrideGridLong = preset.overrideGridLong;
            redGoldOverrideGridWide = preset.overrideGridWide;
        }

        private void DrawRedGoldOutputSettings()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);

            // ──── 预设栏 ────
            DrawRedGoldPresetBar();

            // 品质列表（可配置）
            redGoldFoldQuality = EditorGUILayout.Foldout(redGoldFoldQuality, "品质列表", true, EditorStyles.foldoutHeader);
            if (redGoldFoldQuality)
            {
                for (int i = 0; i < redGoldQualityEntries.Count; i++)
                {
                    var entry = redGoldQualityEntries[i];
                    int capturedIndex = i;

                    GUILayout.BeginVertical(EditorStyles.helpBox);

                    // 第一行：关键字 + 显示名 + 删除按钮
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"#{i + 1}", GUILayout.Width(20));
                    entry.keyword = EditorGUILayout.TextField(entry.keyword, GUILayout.Width(40));
                    entry.displayName = EditorGUILayout.TextField(entry.displayName, GUILayout.Width(80));
                    if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20)))
                    {
                        redGoldQualityEntries.RemoveAt(capturedIndex);
                        break;
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    // 第二行：输出路径
                    DrawRedGoldFolderField("路径:", ref entry.outputFolder);
                    if (!string.IsNullOrEmpty(entry.outputFolder))
                    {
                        EditorGUILayout.LabelField(RedGoldPathHelper.ToTablePath(entry.outputFolder), EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                    }

                    // 第三行：命名模板 + 拼音开关
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("模板:", GUILayout.Width(35));
                    entry.namingTemplate = EditorGUILayout.TextField(entry.namingTemplate, GUILayout.Width(150));
                    entry.usePinyin = EditorGUILayout.ToggleLeft("拼音", entry.usePinyin, GUILayout.Width(50));
                    if (!string.IsNullOrEmpty(entry.namingTemplate))
                    {
                        string sample = RedGoldPreviewTemplate(entry);
                        EditorGUILayout.LabelField(sample, EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                    }
                    else
                    {
                        EditorGUILayout.LabelField("未设置模板 → 使用图标路径或自动编号", EditorStyles.miniLabel);
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();
                    EditorGUILayout.Space(2);
                }

                if (GUILayout.Button("+ 新增品质", EditorStyles.miniButton))
                {
                    redGoldQualityEntries.Add(new QualityConfigEntry { keyword = "新", displayName = "新品质" });
                }
                EditorGUILayout.Space(4);
            }

                    GUILayout.BeginHorizontal();
            redGoldOverrideGrid = EditorGUILayout.ToggleLeft("统一修改格数比例", redGoldOverrideGrid, GUILayout.Width(130));
            EditorGUI.BeginDisabledGroup(!redGoldOverrideGrid);
            EditorGUILayout.LabelField("长:", GUILayout.Width(22));
            redGoldOverrideGridLong = Mathf.Max(1, EditorGUILayout.IntField(redGoldOverrideGridLong, GUILayout.Width(45)));
            EditorGUILayout.LabelField("宽:", GUILayout.Width(22));
            redGoldOverrideGridWide = Mathf.Max(1, EditorGUILayout.IntField(redGoldOverrideGridWide, GUILayout.Width(45)));
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("格子基准像素:", GUILayout.Width(90));
            redGoldCellPixelSize = Mathf.Max(1, EditorGUILayout.IntField(redGoldCellPixelSize, GUILayout.Width(60)));
            EditorGUILayout.LabelField("px");
            GUILayout.Space(10);
            EditorGUILayout.LabelField("方形1-6统一:", GUILayout.Width(85));
            redGoldMaxOutputEdge = Mathf.Max(0, EditorGUILayout.IntField(redGoldMaxOutputEdge, GUILayout.Width(60)));
            EditorGUILayout.LabelField("px（仅 1:1 ~ 6:6 生效）", EditorStyles.miniLabel);
            GUILayout.EndHorizontal();

            EditorGUILayout.HelpBox("尺寸计算：1:1 ~ 6:6 统一输出 512×512；其它比例按“格数 × 格子基准像素”输出，默认每格 100px，如 2:3 输出 200×300。", MessageType.None);

            EditorGUILayout.Space(4);
            redGoldOverwriteTable = EditorGUILayout.ToggleLeft("直接覆盖原表格", redGoldOverwriteTable);
            EditorGUI.BeginDisabledGroup(redGoldOverwriteTable);
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("输出表格:", GUILayout.MinWidth(60));
            redGoldOutputTablePath = EditorGUILayout.TextField(redGoldOutputTablePath, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("📄", GUILayout.Width(28)))
            {
                string defaultDir = RedGoldPathHelper.GetExistingDirectory(redGoldTablePath);
                string p = EditorUtility.SaveFilePanel("保存回写后的表格", defaultDir, RedGoldPathHelper.GetDefaultOutputTableName(redGoldTablePath), RedGoldPathHelper.GetTableExtension(redGoldTablePath));
                if (!string.IsNullOrEmpty(p)) redGoldOutputTablePath = p;
            }
            GUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();

            GUILayout.EndVertical();
        }

        private void DrawRedGoldActions()
        {
            GUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(redGoldProcessing || string.IsNullOrEmpty(redGoldTablePath) || redGoldImageSourceFolders.All(string.IsNullOrEmpty));
            if (GUILayout.Button("读取表格并预览", GUILayout.Height(30)))
                RedGoldLoadPreview();
            EditorGUI.EndDisabledGroup();

            int validSelected = redGoldPreviewRows.Count(x => x.IsSelected && !x.HasError);
            string generateLabel = redGoldPreviewRows.Count > 0
                ? $"生成资源并写回表格 ({validSelected})"
                : "生成资源并写回表格";
            // 变更数量
            int changedCount = redGoldPreviewRows.Count(x => !x.HasError && x.ModStatus != ModificationStatus.Unchanged);
            string generateLabel2 = redGoldPreviewRows.Count > 0
                ? $"生成资源并写回表格 ({validSelected})" + (changedCount > 0 ? $" [变更 {changedCount}]" : "")
                : "生成资源并写回表格";
            EditorGUI.BeginDisabledGroup(redGoldProcessing || string.IsNullOrEmpty(redGoldTablePath) || redGoldImageSourceFolders.All(string.IsNullOrEmpty));
            if (GUILayout.Button(generateLabel2, GUILayout.Height(30)))
                RedGoldGenerateAndWriteTable();
            EditorGUI.EndDisabledGroup();

            // ▼ 导出报告按钮
            EditorGUI.BeginDisabledGroup(redGoldPreviewRows.Count == 0);
            if (GUILayout.Button("📊 导出报告", GUILayout.Height(30)))
            {
                int total = redGoldPreviewRows.Count(x => !x.HasError);
                RedGoldExportReport(
                    redGoldPreviewRows.Count(x => x.ModStatus != ModificationStatus.Unchanged && !x.HasError),
                    total);
            }
            EditorGUI.EndDisabledGroup();

            // ▼ 撤销按钮
            var undoManager = EnsureRedGoldUndoManager();
            if (undoManager.HasUndo)
            {
                GUI.backgroundColor = new Color(0.9f, 0.4f, 0.3f);
                string undoLabel = $"↩ 撤销 ({undoManager.StackDepth})";
                if (GUILayout.Button(undoLabel, GUILayout.Height(30), GUILayout.Width(100)))
                {
                    if (EditorUtility.DisplayDialog("确认撤销",
                        $"即将撤销最近一次生成操作（{undoManager.CurrentDescription}）\n\n将恢复 {undoManager.EntryCount} 个文件的旧版本并还原表格数据。"
                        + (undoManager.StackDepth > 1 ? $"\n\n当前还有 {undoManager.StackDepth} 次可撤销操作。" : ""),
                        "确认撤销", "取消"))
                    {
                        var result = undoManager.TryUndo(redGoldTableData, redGoldOverrideGrid);
                        if (result.Success)
                        {
                            RedGoldLoadPreview();
                            EditorUtility.DisplayDialog("撤销完成",
                                $"已恢复 {result.RestoredFileCount} 个文件，{result.RestoredTableCount} 行表格数据。\n请检查结果是否满足预期。",
                                "确定");
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("撤销失败", result.Error, "确定");
                        }
                    }
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawRedGoldPreviewList()
        {
            // ──────────── 统计栏 ────────────
            int swapCount = redGoldPreviewRows.Count(x => x.UseExistingOutput && !x.HasError);
            int newCount = redGoldPreviewRows.Count(x => x.ModStatus == ModificationStatus.New && !x.HasError);
            int modCount = redGoldPreviewRows.Count(x => x.ModStatus == ModificationStatus.Modified && !x.HasError);
            int unchangedCount = redGoldPreviewRows.Count(x => x.ModStatus == ModificationStatus.Unchanged && !x.HasError);

            GUILayout.BeginHorizontal();
            if (swapCount > 0) { GUI.backgroundColor = new Color(0.3f, 0.5f, 0.8f); GUILayout.Label($"  换 {swapCount}", EditorStyles.miniButton, GUILayout.Width(50)); }
            if (newCount > 0) { GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f); GUILayout.Label($"  新 {newCount}", EditorStyles.miniButton, GUILayout.Width(50)); }
            if (modCount > 0) { GUI.backgroundColor = new Color(1f, 0.6f, 0.1f); GUILayout.Label($"  改 {modCount}", EditorStyles.miniButton, GUILayout.Width(50)); }
            if (unchangedCount > 0) { GUI.backgroundColor = new Color(0.6f, 0.6f, 0.6f); GUILayout.Label($"  - {unchangedCount}", EditorStyles.miniButton, GUILayout.Width(50)); }
            GUI.backgroundColor = Color.white;
            GUILayout.FlexibleSpace();

            // 增量生成控制
            if (GUILayout.Button("☑ 仅变更行", EditorStyles.miniButton, GUILayout.Width(80)))
            {
                foreach (var row in redGoldPreviewRows)
                {
                    if (!row.HasError)
                        row.IsSelected = row.ModStatus != ModificationStatus.Unchanged;
                }
            }
            if (GUILayout.Button("✕ 清除未变更", EditorStyles.miniButton, GUILayout.Width(88)))
            {
                foreach (var row in redGoldPreviewRows)
                {
                    if (!row.HasError && row.ModStatus == ModificationStatus.Unchanged)
                        row.IsSelected = false;
                }
            }
            if (GUILayout.Button("全部展开", EditorStyles.miniButton, GUILayout.Width(60)))
            {
                redGoldFoldReplaceable = true;
                redGoldFoldMissing = true;
                redGoldFoldUnmatched = true;
            }
            if (GUILayout.Button("全部收起", EditorStyles.miniButton, GUILayout.Width(60)))
            {
                redGoldFoldReplaceable = false;
                redGoldFoldMissing = false;
                redGoldFoldUnmatched = false;
            }
            GUILayout.EndHorizontal();

            // ──── 冲突统计 + 批量操作栏 ────
            var conflictGroups = redGoldPreviewRows.Where(r => !r.HasError)
                .Where(r => !string.IsNullOrEmpty(r.SourceImagePath))
                .GroupBy(r => r.SourceImagePath, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();
            int conflictCount = conflictGroups.Sum(g => g.Count());
            var conflictSourcePaths = new HashSet<string>(conflictGroups.Select(g => g.Key), StringComparer.OrdinalIgnoreCase);
            var conflictOutputPathsSet = redGoldPreviewRows.Where(r => !r.HasError && !string.IsNullOrEmpty(r.PlannedOutputPath))
                .GroupBy(r => r.PlannedOutputPath, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .SelectMany(g => g)
                .Select(r => r.SourceImagePath)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            conflictSourcePaths.UnionWith(conflictOutputPathsSet);

            // ──── 批量操作栏 ────
            DrawRedGoldBatchOpsBar(conflictSourcePaths);

            EditorGUILayout.Space(3);

            // ────────── ① 可替换图片资源 ──────────
            var replaceableRows = redGoldPreviewRows.Where(x => !x.HasError).ToList();
            if (replaceableRows.Count > 0)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                string conflictSuffix = conflictSourcePaths.Count > 0 ? $" ⚠ 冲突 {conflictSourcePaths.Count}" : "";
                redGoldFoldReplaceable = EditorGUILayout.Foldout(redGoldFoldReplaceable,
                    $"📦 可替换图片资源（{replaceableRows.Count}）{conflictSuffix}", true, EditorStyles.foldoutHeader);

                if (redGoldFoldReplaceable)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("全选", EditorStyles.miniButton, GUILayout.Width(38)))
                        foreach (var r in replaceableRows) r.IsSelected = true;
                    if (GUILayout.Button("全不选", EditorStyles.miniButton, GUILayout.Width(48)))
                        foreach (var r in replaceableRows) r.IsSelected = false;
                    if (GUILayout.Button("反选", EditorStyles.miniButton, GUILayout.Width(38)))
                        foreach (var r in replaceableRows) r.IsSelected = !r.IsSelected;
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    redGoldScrollPos = EditorGUILayout.BeginScrollView(redGoldScrollPos, GUILayout.ExpandHeight(true));
                    foreach (var row in replaceableRows)
                    {
                        Rect rowRect = EditorGUILayout.BeginHorizontal();
                        bool hasConflict = conflictSourcePaths.Contains(row.SourceImagePath);
                        if (Event.current.type == EventType.Repaint)
                        {
                            // 冲突行背景（优先于其他状态）
                            if (hasConflict) EditorGUI.DrawRect(rowRect, new Color(1f, 0.8f, 0f, 0.15f));
                            else if (row.UserEdited) EditorGUI.DrawRect(rowRect, new Color(1f, 0.8f, 0.2f, 0.10f));
                            else if (row.UseExistingOutput) EditorGUI.DrawRect(rowRect, new Color(0.3f, 0.5f, 0.8f, 0.06f));
                            else if (row.ModStatus == ModificationStatus.New) EditorGUI.DrawRect(rowRect, new Color(0.2f, 0.8f, 0.2f, 0.06f));
                            else if (row.ModStatus == ModificationStatus.Modified) EditorGUI.DrawRect(rowRect, new Color(1f, 0.6f, 0.1f, 0.07f));
                            if (row.IsSelected) EditorGUI.DrawRect(rowRect, new Color(0.3f, 0.6f, 1f, 0.08f));
                        }

                        EditorGUI.BeginDisabledGroup(row.HasError);
                        row.IsSelected = EditorGUILayout.Toggle(row.IsSelected, GUILayout.Width(16));
                        EditorGUI.EndDisabledGroup();

                        // 缩略图
                        string thumbPath = !string.IsNullOrEmpty(row.SourceImagePath) ? row.SourceImagePath : row.PlannedOutputPath;
                        Texture2D thumb = GetRedGoldThumbnail(thumbPath);
                        if (thumb != null)
                        {
                            Rect thumbRect = EditorGUILayout.GetControlRect(GUILayout.Width(32), GUILayout.Height(32));
                            GUI.DrawTexture(thumbRect, thumb, ScaleMode.ScaleToFit);
                            if (Event.current.type == EventType.MouseDown && thumbRect.Contains(Event.current.mousePosition))
                            {
                                RedGoldPingPath(thumbPath);
                                Event.current.Use();
                            }
                        }
                        else
                        {
                            EditorGUILayout.LabelField("", GUILayout.Width(32), GUILayout.Height(32));
                        }

                        EditorGUILayout.LabelField($"#{row.RowIndex + 1}", GUILayout.Width(28));

                        // 名称（双击编辑）
                        string nameControlId = $"RG_Name_{row.RowIndex}";
                        if (row.UserEdited)
                        {
                            GUI.SetNextControlName(nameControlId);
                            string newName = EditorGUILayout.TextField(row.Name, EditorStyles.miniTextField, GUILayout.Width(80));
                            if (newName != row.Name)
                            {
                                row.Name = newName;
                                string iconOutputFileName = RedGoldNameConverter.GetOutputFileNameFromIconPath(row.SourceImagePath);
                                if (!string.IsNullOrEmpty(iconOutputFileName))
                                    row.PlannedOutputPath = Path.Combine(row.OutputFolder, iconOutputFileName);
                                row.Status = RedGoldPathHelper.ToTablePath(row.PlannedOutputPath);
                            }
                            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
                            {
                                row.UserEdited = false;
                                Event.current.Use();
                            }
                        }
                        else
                        {
                            Rect nameRect = EditorGUILayout.GetControlRect(GUILayout.Width(80), GUILayout.Height(18));
                            if (GUI.Button(nameRect, row.Name, EditorStyles.label))
                                RedGoldPingPath(row.SourceImagePath);
                            if (Event.current.type == EventType.MouseDown && Event.current.clickCount == 2 && nameRect.Contains(Event.current.mousePosition))
                            {
                                row.UserEdited = true;
                                Event.current.Use();
                            }
                        }

                        // 品质（下拉选择）
                        if (row.UserEdited)
                        {
                            string[] qualityKeywords = redGoldQualityEntries
                                .Select(e => e.keyword).Where(k => !string.IsNullOrEmpty(k)).ToArray();
                            int curQIdx = Array.IndexOf(qualityKeywords, row.Quality);
                            if (curQIdx < 0) curQIdx = 0;
                            int newQIdx = EditorGUILayout.Popup(curQIdx, qualityKeywords, GUILayout.Width(56));
                            if (newQIdx != curQIdx)
                            {
                                row.Quality = qualityKeywords[newQIdx];
                                row.OutputFolder = RedGoldGetQualityFolder(row.Quality);
                                RedGoldNameConverter.GetOutputFileNameFromIconPath(row.SourceImagePath);
                                string iconFileName = RedGoldNameConverter.GetOutputFileNameFromIconPath(row.SourceImagePath);
                                if (!string.IsNullOrEmpty(iconFileName))
                                    row.PlannedOutputPath = Path.Combine(row.OutputFolder, iconFileName);
                                row.Status = RedGoldPathHelper.ToTablePath(row.PlannedOutputPath);
                            }
                        }
                        else
                        {
                            EditorGUILayout.LabelField(row.Quality, GUILayout.Width(28));
                        }

                        // 格数（双击编辑）
                        if (row.UserEdited)
                        {
                            EditorGUILayout.LabelField("", GUILayout.Width(36)); // 格数在编辑模式下保持只读
                        }
                        else
                        {
                            EditorGUILayout.LabelField($"{row.GridLong}x{row.GridWide}", GUILayout.Width(36));
                        }

                        // 徽标
                        ModificationStatus effStatus = row.UseExistingOutput && row.OutputExists ? ModificationStatus.Unchanged : row.ModStatus;
                        if (!row.HasError)
                        {
                            Color bc; string bt;
                            switch (effStatus) {
                                case ModificationStatus.New: bc = new Color(0.2f, 0.7f, 0.2f); bt = "新"; break;
                                case ModificationStatus.Modified: bc = new Color(1f, 0.5f, 0f); bt = "改"; break;
                                default: bc = new Color(0.5f, 0.5f, 0.5f); bt = "-"; break;
                            }
                            GUI.backgroundColor = bc; GUILayout.Label(bt, EditorStyles.miniButton, GUILayout.Width(20)); GUI.backgroundColor = Color.white;
                        }
                        else { GUILayout.Label("", GUILayout.Width(20)); }

                        // 来源路径
                        string sd = !string.IsNullOrEmpty(row.SourceRelativePath)
                            ? (row.SourceRelativePath.Length > 12 ? "..." + row.SourceRelativePath.Substring(row.SourceRelativePath.Length - 9) : row.SourceRelativePath)
                            : "";
                        Color sbg = row.UseExistingOutput ? new Color(0.5f, 0.5f, 0.5f) : new Color(0.2f, 0.6f, 0.2f);
                        GUI.backgroundColor = sbg;
                        if (!string.IsNullOrEmpty(row.SourceImagePath) && !string.IsNullOrEmpty(sd))
                        { if (GUILayout.Button(new GUIContent(sd, "源: " + row.SourceImagePath), EditorStyles.miniButton, GUILayout.Width(80))) RedGoldPingPath(row.SourceImagePath); }
                        else { EditorGUILayout.LabelField(sd, EditorStyles.miniLabel, GUILayout.Width(80)); }
                        GUI.backgroundColor = Color.white;

                        // 替换按钮
                        if (row.OutputExists && !row.HasError)
                        { if (GUILayout.Button(new GUIContent(row.UseExistingOutput ? "←" : "↔", row.UseExistingOutput ? "切回源文件" : "切到输出文件"), EditorStyles.miniButton, GUILayout.Width(18))) row.UseExistingOutput = !row.UseExistingOutput; }
                        else { GUILayout.Label("", GUILayout.Width(18)); }

                        // 输出文件名（可编辑，直接修改即可覆盖自动命名）
                        string outFileName = !string.IsNullOrEmpty(row.PlannedOutputPath)
                            ? Path.GetFileName(row.PlannedOutputPath) : "";
                        string editName = !string.IsNullOrEmpty(row.OutputFileNameOverride)
                            ? row.OutputFileNameOverride
                            : outFileName;
                        string beforeEdit = editName;

                        // 可编辑文件名
                        if (!string.IsNullOrEmpty(row.PlannedOutputPath) && !row.HasError)
                        {
                            GUI.SetNextControlName($"RG_Rename_{row.RowIndex}");
                            string enteredName = EditorGUILayout.TextField(editName, EditorStyles.miniTextField, GUILayout.Width(90));
                            // 检测用户修改
                            if (enteredName != beforeEdit)
                            {
                                row.OutputFileNameOverride = enteredName;
                                string dir = Path.GetDirectoryName(row.PlannedOutputPath);
                                string ext = Path.GetExtension(outFileName);
                                if (string.IsNullOrEmpty(ext)) ext = ".png";
                                string finalName = enteredName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
                                    ? enteredName : enteredName + ext;
                                row.PlannedOutputPath = Path.Combine(dir, finalName);
                            }
                            // 小定位按钮
                            if (GUILayout.Button("📁", EditorStyles.miniButton, GUILayout.Width(22)))
                                RedGoldPingPath(row.PlannedOutputPath);
                        }
                        else
                        {
                            EditorGUILayout.LabelField("", EditorStyles.miniLabel, GUILayout.Width(112));
                        }

                        EditorGUILayout.LabelField(row.UseExistingOutput ? "保留" : row.Status, EditorStyles.miniLabel, GUILayout.MinWidth(40));
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndScrollView();
                }
                GUILayout.EndVertical();
                EditorGUILayout.Space(3);
            }

            // ────────── ② 未找到同名文件 ──────────
            var missingRows = redGoldPreviewRows.Where(x => x.HasError).ToList();
            if (missingRows.Count > 0)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                redGoldFoldMissing = EditorGUILayout.Foldout(redGoldFoldMissing,
                    $"⚠ 未找到同名图片（{missingRows.Count}）", true, EditorStyles.foldoutHeader);

                if (redGoldFoldMissing)
                {
                    redGoldScrollPosMissing = EditorGUILayout.BeginScrollView(redGoldScrollPosMissing, GUILayout.Height(100));
                    foreach (var row in missingRows)
                    {
                        Rect er = EditorGUILayout.BeginHorizontal();
                        if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(er, new Color(1f, 0.25f, 0.2f, 0.10f));

                        EditorGUILayout.LabelField($"#{row.RowIndex + 1}", GUILayout.Width(28));
                        EditorGUILayout.LabelField(row.Name, GUILayout.Width(80));
                        EditorGUILayout.LabelField(row.Quality, GUILayout.Width(28));

                        // Try to show what path was looked for
                        string lookupInfo = "未找到: " + row.Name;
                        EditorGUILayout.LabelField(lookupInfo, EditorStyles.miniLabel);

                        if (GUILayout.Button("📂", EditorStyles.miniButton, GUILayout.Width(24)))
                        {
                            string folder = RedGoldGetQualityFolder(row.Quality);
                            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                                EditorUtility.RevealInFinder(folder);
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndScrollView();
                }
                GUILayout.EndVertical();
                EditorGUILayout.Space(3);
            }

            // ────────── ③ 源文件未匹配表格 ──────────
            if (redGoldUnmatchedSources.Count > 0)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                redGoldFoldUnmatched = EditorGUILayout.Foldout(redGoldFoldUnmatched,
                    $"📁 源文件未匹配表格（{redGoldUnmatchedSources.Count}）", true, EditorStyles.foldoutHeader);

                if (redGoldFoldUnmatched)
                {
                    redGoldScrollPosUnmatched = EditorGUILayout.BeginScrollView(redGoldScrollPosUnmatched, GUILayout.Height(100));
                    foreach (var src in redGoldUnmatchedSources)
                    {
                        Rect sr = EditorGUILayout.BeginHorizontal();
                        if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(sr, new Color(0.2f, 0.5f, 0.8f, 0.06f));

                        if (GUILayout.Button(src.FileName, EditorStyles.label, GUILayout.Width(120)))
                            RedGoldPingPath(src.FilePath);
                        EditorGUILayout.LabelField(src.ModifiedTime, EditorStyles.miniLabel, GUILayout.Width(80));
                        if (src.FileSize > 0)
                        {
                            string sizeStr = src.FileSize > 1024 * 1024
                                ? $"{src.FileSize / (1024f * 1024f):F1} MB"
                                : $"{src.FileSize / 1024f:F0} KB";
                            EditorGUILayout.LabelField(sizeStr, EditorStyles.miniLabel, GUILayout.Width(50));
                        }
                        string relPath = RedGoldPathHelper.ToTablePath(src.FilePath);
                        relPath = relPath.Length > 50 ? "..." + relPath.Substring(relPath.Length - 47) : relPath;
                        EditorGUILayout.LabelField(relPath, EditorStyles.miniLabel);
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndScrollView();
                }
                GUILayout.EndVertical();
            }
        }
        private void RedGoldLoadPreview()
        {
            redGoldPreviewRows.Clear();
            redGoldTableData = null;
            redGoldThumbnailCache.Clear(); // 清空缩略图缓存

            // 收集所有非空源文件夹的绝对路径
            var imageFolders = redGoldImageSourceFolders
                .Select(f => RedGoldPathHelper.ToAbsolutePath(f))
                .Where(f => !string.IsNullOrEmpty(f) && Directory.Exists(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (imageFolders.Count == 0)
            {
                EditorUtility.DisplayDialog("错误", "请选择有效的图片文件夹。", "确定");
                return;
            }

            // ... existing code ...

            string tablePath = RedGoldPathHelper.ToAbsolutePath(redGoldTablePath);
            if (string.IsNullOrEmpty(tablePath) || !File.Exists(tablePath))
            {
                EditorUtility.DisplayDialog("错误", "请选择有效的 CSV/TSV 表格文件。", "确定");
                return;
            }

            try
            {
                // 按扩展名选择解析器
                string ext = Path.GetExtension(tablePath).ToLowerInvariant();
                if (ext == ".xlsx")
                {
                    redGoldTableData = ExcelFileParser.ReadTable(tablePath);
                }
                else
                {
                    redGoldTableData = DelimitedFileParser.ReadTable(tablePath);
                }
                if (redGoldTableData.Rows.Count < 2)
                {
                    EditorUtility.DisplayDialog("提示", "表格没有可处理的数据行。", "确定");
                    return;
                }

                if (!RedGoldResolveColumns(redGoldTableData))
                    return;

                Dictionary<string, string> imageMap = RedGoldImageMatcher.BuildImageMap(imageFolders, redGoldIncludeSubfolders, out List<string> duplicateWarnings);
                if (duplicateWarnings.Count > 0)
                {
                    string msg = "发现同名文件冲突，已自动选择最新版本的图片：\n\n"
                        + string.Join("\n", duplicateWarnings)
                        + "\n\n是否继续使用以上选择？";
                    bool proceed = EditorUtility.DisplayDialog(
                        "同名文件提示",
                        msg,
                        "继续",
                        "取消预览");
                    if (!proceed)
                    {
                        Debug.Log("[UIProbe] 用户取消预览（因同名文件冲突）");
                        return;
                    }
                }
                Dictionary<string, RedGoldNamingState> namingStates = new Dictionary<string, RedGoldNamingState>(StringComparer.OrdinalIgnoreCase);

                for (int i = 1; i < redGoldTableData.Rows.Count; i++)
                {
                    List<string> tableRow = redGoldTableData.Rows[i];
                    string name = RedGoldTableData.GetCell(tableRow, redGoldTableData.NameColumn)
                        .Replace("\r\n", "").Replace("\n", "").Replace("\r", "").Trim();
                    string quality = RedGoldTableData.GetCell(tableRow, redGoldTableData.QualityColumn)
                        .Replace("\r\n", "").Replace("\n", "").Replace("\r", "").Trim();
                    int gridLong = redGoldOverrideGrid ? redGoldOverrideGridLong : RedGoldParseInt(RedGoldTableData.GetCell(tableRow, redGoldTableData.GridLongColumn));
                    int gridWide = redGoldOverrideGrid ? redGoldOverrideGridWide : RedGoldParseInt(RedGoldTableData.GetCell(tableRow, redGoldTableData.GridWideColumn));
                    string outputFolder = RedGoldGetQualityFolder(quality);
                    string iconPath = RedGoldTableData.GetCell(tableRow, redGoldTableData.IconPathColumn)
                        .Replace("\r\n", "").Replace("\n", "").Replace("\r", "").Trim();
                    string sourcePath = RedGoldImageMatcher.FindSourceImage(imageMap, name, iconPath);
                    var (outW, outH) = RedGoldComputeOutputSize(gridLong, gridWide);

                    var previewRow = new RedGoldImportRow
                    {
                        RowIndex = i,
                        Name = name,
                        Quality = quality,
                        GridLong = gridLong,
                        GridWide = gridWide,
                        OutputWidth = outW,
                        OutputHeight = outH,
                        SourceImagePath = sourcePath,
                        OutputFolder = outputFolder
                    };

                    RedGoldValidatePreviewRow(previewRow);
                    if (!previewRow.HasError)
                    {
                        string iconOutputFileName = RedGoldNameConverter.GetOutputFileNameFromIconPath(iconPath);
                        string namedOutputFileName = RedGoldBuildNamedOutputFileName(quality, name);
                        if (!string.IsNullOrEmpty(namedOutputFileName))
                        {
                            // 有命名模板的品质（如红品质拼音命名）
                            if (!namingStates.TryGetValue(outputFolder, out RedGoldNamingState state))
                            {
                                state = RedGoldNamingState.Create(outputFolder);
                                namingStates[outputFolder] = state;
                            }
                            previewRow.PlannedOutputPath = state.ReservePreferred(outputFolder, namedOutputFileName);
                        }
                        else if (!string.IsNullOrEmpty(iconOutputFileName))
                        {
                            previewRow.PlannedOutputPath = Path.Combine(outputFolder, iconOutputFileName);
                            if (!namingStates.TryGetValue(outputFolder, out RedGoldNamingState state))
                            {
                                state = RedGoldNamingState.Create(outputFolder);
                                namingStates[outputFolder] = state;
                            }
                            state.ReserveFileName(iconOutputFileName);
                        }
                        else
                        {
                            if (!namingStates.TryGetValue(outputFolder, out RedGoldNamingState state))
                            {
                                state = RedGoldNamingState.Create(outputFolder);
                                namingStates[outputFolder] = state;
                            }

                            previewRow.PlannedOutputPath = state.Allocate(outputFolder, Path.GetFileNameWithoutExtension(sourcePath));
                        }
                        previewRow.Status = RedGoldPathHelper.ToTablePath(previewRow.PlannedOutputPath);

                        // ▼ 计算源文件信息与修改检测
                        string srcPath = previewRow.SourceImagePath;
                        if (!string.IsNullOrEmpty(srcPath) && File.Exists(srcPath))
                        {
                                previewRow.SourceFileName = Path.GetFileName(srcPath);

                                // 验证并规范路径，防止非法字符异常
                                string absImageFolder = RedGoldPathHelper.ToAbsolutePath(redGoldImageSourceFolders.FirstOrDefault(f => !string.IsNullOrEmpty(f)) ?? "");
                                string absSrc = Path.GetFullPath(srcPath);

                                if (!string.IsNullOrEmpty(absImageFolder) && absSrc.StartsWith(absImageFolder, StringComparison.OrdinalIgnoreCase))
                                {
                                    string rel = absSrc.Substring(absImageFolder.Length).TrimStart('/', '\\');
                                    previewRow.SourceRelativePath = rel;
                                }
                                else
                                {
                                    previewRow.SourceRelativePath = previewRow.SourceFileName;
                                }

                            DateTime srcTime = File.GetLastWriteTime(srcPath);
                            previewRow.SourceModifiedTime = srcTime.ToString("yyyy-MM-dd HH:mm");

                            string outPath = previewRow.PlannedOutputPath;
                            if (!string.IsNullOrEmpty(outPath) && File.Exists(outPath))
                            {
                                previewRow.OutputExists = true;
                                DateTime outTime = File.GetLastWriteTime(outPath);
                                previewRow.OutputModifiedTime = outTime.ToString("yyyy-MM-dd HH:mm");
                                previewRow.ModStatus = srcTime > outTime
                                    ? ModificationStatus.Modified
                                    : ModificationStatus.Unchanged;
                            }
                            else
                            {
                                previewRow.OutputExists = false;
                                previewRow.OutputModifiedTime = "";
                                previewRow.ModStatus = ModificationStatus.New;
                            }
                        }
                        else
                        {
                            previewRow.ModStatus = ModificationStatus.Unknown;
                        }
                    }

                    redGoldPreviewRows.Add(previewRow);
                }

                // ▼ 找出源文件夹中未匹配到任何表格行的文件
                try
                {
                    var matchedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var row in redGoldPreviewRows)
                    {
                        if (!string.IsNullOrEmpty(row.SourceImagePath) && File.Exists(row.SourceImagePath))
                            matchedSourcePaths.Add(Path.GetFullPath(row.SourceImagePath));
                    }

                    redGoldUnmatchedSources.Clear();
                    foreach (var kvp in imageMap)
                    {
                        if (string.IsNullOrEmpty(kvp.Value)) continue;
                        string fullPath = Path.GetFullPath(kvp.Value);
                        if (!matchedSourcePaths.Contains(fullPath))
                        {
                            var fi = new FileInfo(fullPath);
                            redGoldUnmatchedSources.Add(new UnmatchedSourceInfo
                            {
                                FilePath = kvp.Value,
                                FileName = Path.GetFileName(kvp.Value),
                                ModifiedTime = fi.Exists ? fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm") : "",
                                FileSize = fi.Exists ? fi.Length : 0
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[UIProbe] 未匹配源文件分析跳过（{ex.Message}）");
                }
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("错误", $"读取表格失败:\n{e.Message}", "确定");
            }
        }

        private void RedGoldGenerateAndWriteTable()
        {
            if (redGoldTableData == null || redGoldPreviewRows.Count == 0)
            {
                RedGoldLoadPreview();
                if (redGoldTableData == null || redGoldPreviewRows.Count == 0)
                    return;
            }

            var selectedRows = redGoldPreviewRows.Where(x => x.IsSelected && !x.HasError).ToList();
            if (selectedRows.Count == 0)
            {
                string problemSummary = RedGoldBuildPreviewProblemSummary();
                EditorUtility.DisplayDialog("提示", "没有可生成的选中行。" + problemSummary, "确定");
                return;
            }

            string tableOutputPath = redGoldOverwriteTable
                ? RedGoldPathHelper.ToAbsolutePath(redGoldTablePath)
                : RedGoldPathHelper.ToAbsolutePath(redGoldOutputTablePath);
            if (string.IsNullOrEmpty(tableOutputPath))
                tableOutputPath = RedGoldPathHelper.GetDefaultOutputTablePath(redGoldTablePath);

            // 构建修改摘要用于确认对话框
            int swapInBatch = selectedRows.Count(x => x.UseExistingOutput);
            int newInBatch = selectedRows.Count(x => x.ModStatus == ModificationStatus.New);
            int modInBatch = selectedRows.Count(x => x.ModStatus == ModificationStatus.Modified);
            string modSummary = $"共计 {selectedRows.Count} 张";
            if (newInBatch > 0) modSummary += $"，新增 {newInBatch} 张";
            if (modInBatch > 0) modSummary += $"，修改 {modInBatch} 张";
            if (swapInBatch > 0) modSummary += $"，保留 {swapInBatch} 张";
            string confirmMsg = $"即将 {modSummary}，并写回表格:\n{tableOutputPath}\n\n内容将等比适配目标画布，不会裁切。";
            if (modInBatch > 0)
                confirmMsg += "\n\n⚠ 有 " + modInBatch + " 个文件将覆盖现有输出，已自动备份旧文件以供撤销。";

            bool confirmed = EditorUtility.DisplayDialog(
                "确认生成",
                confirmMsg,
                "开始",
                "取消");
            if (!confirmed) return;

            // ▼ 初始化后台生成状态
            redGoldProcessingRows = selectedRows;
            redGoldProcessingTotal = selectedRows.Count;
            redGoldProcessingIndex = 0;
            redGoldProcessingSuccess = 0;
            redGoldProcessingCancelled = false;
            redGoldProcessingTableOutputPath = tableOutputPath;
            redGoldProcessingUndoDir = Path.Combine(
                UIProbeStorage.GetMainFolderPath(),
                "RedGoldUndo",
                DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            redGoldProcessingUndoEntries = new List<RedGoldUndoEntry>();
            redGoldProcessing = true;
            redGoldProgress = 0f;

            EditorApplication.update += RedGoldProcessNextFrame;
        }

        private void RedGoldProcessNextFrame()
        {
            if (redGoldProcessingCancelled || redGoldProcessingIndex >= redGoldProcessingTotal)
            {
                EditorApplication.update -= RedGoldProcessNextFrame;
                RedGoldFinishGenerate();
                return;
            }

            // 每帧处理 3 行
            int batchSize = 3;
            for (int b = 0; b < batchSize && redGoldProcessingIndex < redGoldProcessingTotal; b++, redGoldProcessingIndex++)
            {
                var row = redGoldProcessingRows[redGoldProcessingIndex];
                Directory.CreateDirectory(row.OutputFolder);

                string actualSourcePath = row.UseExistingOutput && !string.IsNullOrEmpty(row.PlannedOutputPath) && File.Exists(row.PlannedOutputPath)
                    ? row.PlannedOutputPath
                    : row.SourceImagePath;
                Texture2D source = ImageNormalizer.LoadTexture(actualSourcePath);
                if (source == null)
                {
                    row.Status = "图片读取失败";
                    row.HasError = true;
                    continue;
                }

                Texture2D result = ImageNormalizer.Normalize(
                    source, row.OutputWidth, row.OutputHeight,
                    ContentAlignment.Center, ResizeMode.ProportionalFit);
                UnityEngine.Object.DestroyImmediate(source);

                if (result == null)
                {
                    row.Status = "图片处理失败";
                    row.HasError = true;
                    continue;
                }

                // 备份
                string outPath = row.PlannedOutputPath;
                if (!string.IsNullOrEmpty(outPath) && File.Exists(outPath))
                {
                    string backupPath = Path.Combine(redGoldProcessingUndoDir,
                        $"{Path.GetFileNameWithoutExtension(outPath)}_{row.RowIndex}_old{Path.GetExtension(outPath)}");
                    Directory.CreateDirectory(redGoldProcessingUndoDir);
                    File.Copy(outPath, backupPath, overwrite: true);

                    string oldGridLong = "", oldGridWide = "", oldGridCount = "";
                    if (redGoldTableData != null && row.RowIndex < redGoldTableData.Rows.Count)
                    {
                        var tRow = redGoldTableData.Rows[row.RowIndex];
                        oldGridLong = RedGoldTableData.GetCell(tRow, redGoldTableData.GridLongColumn);
                        oldGridWide = RedGoldTableData.GetCell(tRow, redGoldTableData.GridWideColumn);
                        oldGridCount = redGoldTableData.GridCountColumn >= 0
                            ? RedGoldTableData.GetCell(tRow, redGoldTableData.GridCountColumn) : "";
                    }
                    string oldIconPath = "";
                    if (redGoldTableData != null && row.RowIndex < redGoldTableData.Rows.Count && redGoldTableData.IconPathColumn >= 0)
                        oldIconPath = RedGoldTableData.GetCell(redGoldTableData.Rows[row.RowIndex], redGoldTableData.IconPathColumn);

                    redGoldProcessingUndoEntries.Add(new RedGoldUndoEntry
                    {
                        BackupFilePath = backupPath,
                        OriginalOutputPath = outPath,
                        TableRowIndex = row.RowIndex,
                        OldIconPath = oldIconPath,
                        OldCellGridLong = oldGridLong,
                        OldCellGridWide = oldGridWide,
                        OldCellGridCount = oldGridCount
                    });
                }

                bool saved = ImageNormalizer.SaveTexture(result, row.PlannedOutputPath);
                UnityEngine.Object.DestroyImmediate(result);

                if (saved)
                {
                    RedGoldWriteBackRow(row);
                    row.Status = RedGoldPathHelper.ToTablePath(row.PlannedOutputPath);
                    redGoldProcessingSuccess++;
                }
                else
                {
                    row.Status = "保存失败";
                    row.HasError = true;
                }

                redGoldProgress = (float)(redGoldProcessingIndex) / redGoldProcessingTotal;
            }

            Repaint();
        }

        private void RedGoldFinishGenerate()
        {
            int successCount = redGoldProcessingSuccess;
            var selectedRows = redGoldProcessingRows;
            string tableOutputPath = redGoldProcessingTableOutputPath;
            var undoEntries = redGoldProcessingUndoEntries;
            string undoDir = redGoldProcessingUndoDir;

            try
            {
                if (!redGoldProcessingCancelled)
                {
                    DelimitedFileParser.WriteTable(tableOutputPath, redGoldTableData);
                    AssetDatabase.Refresh();

                    // 资源引用联动
                    if (successCount > 0)
                    {
                        var generatedAssetPaths = selectedRows
                            .Where(r => !r.HasError && !string.IsNullOrEmpty(r.PlannedOutputPath))
                            .Select(r => RedGoldPathHelper.ToTablePath(r.PlannedOutputPath))
                            .Where(p => p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        if (generatedAssetPaths.Count > 0)
                            EditorApplication.delayCall += () => RedGoldShowReferenceImpact(generatedAssetPaths, successCount);
                    }

                    // 差异报告
                    RedGoldExportReport(successCount, selectedRows.Count);
                }
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("错误", $"生成或写表失败:\n{e.Message}", "确定");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                redGoldProcessing = false;
                redGoldProgress = 0f;
                redGoldProcessingRows = null;
            }

            // 保存撤销状态
            if (!redGoldProcessingCancelled)
            {
                bool anyBackups = undoEntries.Count > 0 && undoEntries.Any(e => !string.IsNullOrEmpty(e.BackupFilePath));
                if (successCount > 0 && anyBackups)
                {
                    int modFiles = undoEntries.Count(e => !string.IsNullOrEmpty(e.BackupFilePath));
                    int newFiles = successCount - modFiles;
                    string description = $"{successCount} 个文件";
                    if (newFiles > 0) description += $"（新增 {newFiles}）";
                    if (modFiles > 0) description += $"（修改 {modFiles}）";
                    EnsureRedGoldUndoManager().PushSnapshot(undoEntries, tableOutputPath, description);
                }
                try { if (Directory.Exists(undoDir) && Directory.GetFiles(undoDir).Length == 0) Directory.Delete(undoDir); } catch { }

                string cancelSuffix = redGoldProcessingCancelled ? "（已取消）" : "";
                EditorUtility.DisplayDialog("完成",
                    $"生成完成：{successCount} / {selectedRows.Count}{cancelSuffix}\n表格已写入：\n{tableOutputPath}"
                    + (EnsureRedGoldUndoManager().HasUndo ? "\n\n如需恢复旧版本，请点击面板中的「撤销」按钮。" : ""),
                    "确定");
            }
            else
            {
                try { if (Directory.Exists(undoDir) && Directory.GetFiles(undoDir).Length == 0) Directory.Delete(undoDir); } catch { }
            }
        }

        private bool RedGoldResolveColumns(RedGoldTableData table)
        {
            List<string> header = table.Rows[0];
            table.NameColumn = RedGoldFindColumn(header, redGoldNameColumn, new[] { "名称", "名字", "Name", "name" });
            table.QualityColumn = RedGoldFindColumn(header, redGoldQualityColumn, new[] { "品质", "Quality", "quality" });
            table.GridLongColumn = RedGoldFindColumn(header, redGoldGridLongColumn, new[] { "格数：长", "格数:长", "格数长", "长" });
            table.GridWideColumn = RedGoldFindColumn(header, redGoldGridWideColumn, new[] { "格数：宽", "格数:宽", "格数宽", "宽" });
            table.GridCountColumn = RedGoldFindColumn(header, redGoldGridCountColumn, new[] { "格数", "总格数", "格子数", "Grid", "grid" });
            table.IconPathColumn = RedGoldFindColumn(header, redGoldIconPathColumn, new[] { "图标路径", "IconPath", "iconPath", "Icon" });

            var missing = new List<string>();
            if (table.NameColumn < 0) missing.Add(redGoldNameColumn);
            if (table.QualityColumn < 0) missing.Add(redGoldQualityColumn);
            if (table.GridLongColumn < 0) missing.Add(redGoldGridLongColumn);
            if (table.GridWideColumn < 0) missing.Add(redGoldGridWideColumn);
            if (table.IconPathColumn < 0) missing.Add(redGoldIconPathColumn);

            if (missing.Count == 0) return true;

            EditorUtility.DisplayDialog(
                "列名未找到",
                "表头中找不到以下列：\n" + string.Join("\n", missing) + "\n\n请在“表格列名映射”中填写真实列名。",
                "确定");
            return false;
        }

        private int RedGoldFindColumn(List<string> header, string preferredName, string[] fallbacks)
        {
            string preferred = RedGoldNormalizeHeader(preferredName);
            for (int i = 0; i < header.Count; i++)
            {
                if (RedGoldNormalizeHeader(header[i]) == preferred)
                    return i;
            }

            foreach (string fallback in fallbacks)
            {
                string normalized = RedGoldNormalizeHeader(fallback);
                for (int i = 0; i < header.Count; i++)
                {
                    if (RedGoldNormalizeHeader(header[i]) == normalized)
                        return i;
                }
            }

            return -1;
        }

        private string RedGoldNormalizeHeader(string value)
        {
            return (value ?? "")
                .Replace("：", ":")
                .Replace(" ", "")
                .Replace("\t", "")
                .Trim()
                .ToLowerInvariant();
        }

        private string RedGoldBuildPreviewProblemSummary()
        {
            if (redGoldPreviewRows.Count == 0)
                return "";

            var groups = redGoldPreviewRows
                .Where(x => x.HasError)
                .GroupBy(x => x.Status)
                .Select(g => $"{g.Key}: {g.Count()} 行")
                .ToList();

            if (groups.Count == 0)
                return "";

            return "\n\n问题汇总：\n" + string.Join("\n", groups);
        }

        private void RedGoldValidatePreviewRow(RedGoldImportRow row)
        {
            if (string.IsNullOrEmpty(row.Name))
            {
                row.HasError = true;
                row.Status = "名称为空";
            }
            else if (string.IsNullOrEmpty(row.SourceImagePath))
            {
                row.HasError = true;
                row.Status = "未找到同名图片";
            }
            else if (row.GridLong <= 0 || row.GridWide <= 0)
            {
                row.HasError = true;
                row.Status = "格数无效";
            }
            else if (string.IsNullOrEmpty(row.OutputFolder))
            {
                row.HasError = true;
                row.Status = "品质未配置路径";
            }
            else
            {
                row.Status = "待生成";
            }
        }

        private string RedGoldGetQualityFolder(string quality)
        {
            if (string.IsNullOrEmpty(quality)) return "";
            string q = quality.Trim();

            foreach (var entry in redGoldQualityEntries)
            {
                if (string.IsNullOrEmpty(entry.keyword)) continue;
                if (q.IndexOf(entry.keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string absPath = RedGoldPathHelper.ToAbsolutePath(entry.outputFolder);
                    return string.IsNullOrEmpty(absPath) ? "" : absPath;
                }
            }
            return "";
        }

        /// <summary>
        /// 查找品质配置中配置了命名模板的条目
        /// </summary>
        private QualityConfigEntry RedGoldFindNamedQuality(string quality)
        {
            if (string.IsNullOrEmpty(quality)) return null;
            string q = quality.Trim();

            foreach (var entry in redGoldQualityEntries)
            {
                if (string.IsNullOrEmpty(entry.keyword) || string.IsNullOrEmpty(entry.namingTemplate)) continue;
                if (q.IndexOf(entry.keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return entry;
            }
            return null;
        }

        /// <summary>
        /// 预览命名模板样例（用于 UI 展示）
        /// </summary>
        private string RedGoldPreviewTemplate(QualityConfigEntry entry)
        {
            if (string.IsNullOrEmpty(entry.namingTemplate)) return "";
            const string sampleName = "火锅神像";
            const string sampleQuality = "红";

            string result = entry.namingTemplate;
            result = result.Replace("{Name}", sampleName);
            result = result.Replace("{Quality}", sampleQuality);
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\{Seq(:\d+)?\}", m => {
                int digits = m.Groups[1].Success ? int.Parse(m.Groups[1].Value.TrimStart(':')) : 4;
                return 1.ToString("D" + digits);
            });
            if (result.Contains("{Pinyin}"))
            {
                string pinyin = RedGoldNameConverter.GetSemanticPinyin(sampleName);
                result = result.Replace("{Pinyin}", string.IsNullOrEmpty(pinyin) ? sampleName : pinyin);
            }
            if (!result.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                result += ".png";
            return $"→ {result}";
        }

        /// <summary>
        /// 根据品质配置和命名模板生成输出文件名
        /// </summary>
        private string RedGoldBuildNamedOutputFileName(string quality, string name, int rowIndex = 0)
        {
            var entry = RedGoldFindNamedQuality(quality);
            if (entry == null || string.IsNullOrEmpty(entry.namingTemplate)) return "";

            string result = entry.namingTemplate;

            // {Name} → 原始名称
            result = result.Replace("{Name}", name);

            // {Quality} → 品质关键字
            result = result.Replace("{Quality}", entry.keyword);

            // {Seq:3} → 三位序号（默认 4 位）
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\{Seq(:\d+)?\}", match => {
                int digits = match.Groups[1].Success ? int.Parse(match.Groups[1].Value.TrimStart(':')) : 4;
                return (rowIndex + 1).ToString("D" + digits);
            });

            // {Pinyin} → 拼音
            if (result.Contains("{Pinyin}"))
            {
                string pinyin = RedGoldNameConverter.GetSemanticPinyin(name);
                result = result.Replace("{Pinyin}", string.IsNullOrEmpty(pinyin) ? name : pinyin);
            }

            // 确保 .png 扩展名
            if (!result.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                result += ".png";

            return result;
        }

        private (int width, int height) RedGoldComputeOutputSize(int gridLong, int gridWide)
        {
            int longCount = Mathf.Max(1, gridLong);
            int wideCount = Mathf.Max(1, gridWide);

            if (longCount == wideCount && longCount >= 1 && longCount <= 6 && redGoldMaxOutputEdge > 0)
            {
                return (redGoldMaxOutputEdge, redGoldMaxOutputEdge);
            }

            int baseW = longCount * Mathf.Max(1, redGoldCellPixelSize);
            int baseH = wideCount * Mathf.Max(1, redGoldCellPixelSize);
            return (baseW, baseH);
        }

        private int RedGoldParseInt(string value)
        {
            if (int.TryParse((value ?? "").Trim(), out int intValue))
                return intValue;
            if (float.TryParse((value ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue))
                return Mathf.RoundToInt(floatValue);
            return 0;
        }


        private void RedGoldWriteBackRow(RedGoldImportRow row)
        {
            List<string> tableRow = redGoldTableData.Rows[row.RowIndex];
            if (redGoldOverrideGrid)
            {
                RedGoldTableData.SetCell(tableRow, redGoldTableData.GridLongColumn, row.GridLong.ToString());
                RedGoldTableData.SetCell(tableRow, redGoldTableData.GridWideColumn, row.GridWide.ToString());
                if (redGoldTableData.GridCountColumn >= 0)
                    RedGoldTableData.SetCell(tableRow, redGoldTableData.GridCountColumn, (row.GridLong * row.GridWide).ToString());
            }
            RedGoldTableData.SetCell(tableRow, redGoldTableData.IconPathColumn, RedGoldPathHelper.ToTablePath(row.PlannedOutputPath));
        }


        private void DrawRedGoldTextField(string label, ref string value)
        {
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.MinWidth(60), GUILayout.ExpandWidth(false));
            value = EditorGUILayout.TextField(value, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
        }

        private void DrawRedGoldFolderField(string label, ref string value)
        {
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.MinWidth(60), GUILayout.ExpandWidth(false));
            value = EditorGUILayout.TextField(value, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("📁", GUILayout.Width(28)))
            {
                string p = EditorUtility.OpenFolderPanel("选择生成资源路径", RedGoldPathHelper.ToAbsolutePath(value), "");
                if (!string.IsNullOrEmpty(p)) value = p;
            }
            GUILayout.EndHorizontal();
        }

        private void RedGoldExportReport(int successCount, int totalCount)
        {
            if (redGoldPreviewRows.Count == 0) return;

            var sb = new System.Text.StringBuilder();
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string tableName = System.IO.Path.GetFileName(redGoldTablePath);

            sb.AppendLine("# 大红大金资源导入差异报告");
            sb.AppendLine();
            sb.AppendLine($"生成时间：{now}");
            sb.AppendLine($"表格文件：{tableName}");
            sb.AppendLine();

            // 汇总
            int newCount = redGoldPreviewRows.Count(x => x.ModStatus == ModificationStatus.New && !x.HasError);
            int modCount = redGoldPreviewRows.Count(x => x.ModStatus == ModificationStatus.Modified && !x.HasError);
            int unchangedCount = redGoldPreviewRows.Count(x => x.ModStatus == ModificationStatus.Unchanged && !x.HasError);
            int errorCount = redGoldPreviewRows.Count(x => x.HasError);

            sb.AppendLine("## 汇总");
            sb.AppendLine();
            sb.AppendLine("| 状态 | 数量 |");
            sb.AppendLine("|------|------|");
            sb.AppendLine($"| 总计 | {totalCount} |");
            sb.AppendLine($"| 新增（🟢） | {newCount} |");
            sb.AppendLine($"| 修改（🟠） | {modCount} |");
            sb.AppendLine($"| 无变化（⚪） | {unchangedCount} |");
            sb.AppendLine($"| 失败（🔴） | {errorCount} |");
            sb.AppendLine();

            // 新增
            var newRows = redGoldPreviewRows.Where(x => x.ModStatus == ModificationStatus.New && !x.HasError).ToList();
            if (newRows.Count > 0)
            {
                sb.AppendLine("## 新增资源");
                sb.AppendLine();
                sb.AppendLine("| 文件名 | 输出路径 | 源图 |");
                sb.AppendLine("|--------|----------|------|");
                foreach (var row in newRows)
                {
                    string fileName = System.IO.Path.GetFileName(row.PlannedOutputPath);
                    string outFolder = !string.IsNullOrEmpty(row.OutputFolder) ? System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(row.OutputFolder.TrimEnd('/', '\\'))) : "-";
                    string src = !string.IsNullOrEmpty(row.SourceRelativePath) ? row.SourceRelativePath : "-";
                    sb.AppendLine($"| {fileName} | {outFolder} | {src} |");
                }
                sb.AppendLine();
            }

            // 修改
            var modRows = redGoldPreviewRows.Where(x => x.ModStatus == ModificationStatus.Modified && !x.HasError).ToList();
            if (modRows.Count > 0)
            {
                sb.AppendLine("## 已修改资源");
                sb.AppendLine();
                sb.AppendLine("| 文件名 | 输出路径 | 源图 | 变更说明 |");
                sb.AppendLine("|--------|----------|------|----------|");
                foreach (var row in modRows)
                {
                    string fileName = System.IO.Path.GetFileName(row.PlannedOutputPath);
                    string outFolder = !string.IsNullOrEmpty(row.OutputFolder) ? System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(row.OutputFolder.TrimEnd('/', '\\'))) : "-";
                    string src = !string.IsNullOrEmpty(row.SourceRelativePath) ? row.SourceRelativePath : "-";
                    sb.AppendLine($"| {fileName} | {outFolder} | {src} | 源图更新 |");
                }
                sb.AppendLine();
            }

            // 失败
            var errorRows = redGoldPreviewRows.Where(x => x.HasError).ToList();
            if (errorRows.Count > 0)
            {
                sb.AppendLine("## 失败项");
                sb.AppendLine();
                sb.AppendLine("| 名称 | 原因 |");
                sb.AppendLine("|------|------|");
                foreach (var row in errorRows)
                {
                    sb.AppendLine($"| {row.Name} | {row.Status} |");
                }
                sb.AppendLine();
            }

            // 写入
            string reportDir = System.IO.Path.Combine(UIProbeStorage.GetMainFolderPath(), "RedGoldReports");
            System.IO.Directory.CreateDirectory(reportDir);
            string path = System.IO.Path.Combine(reportDir, DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".md");
            System.IO.File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));

            EditorUtility.RevealInFinder(path);
            Debug.Log($"[UIProbe] 差异报告已导出: {path}");
        }

        /// <summary>
        /// 获取缩略图（32x32），优先从 AssetPreview 缓存读取，外部文件用 ImageNormalizer 缩略
        /// </summary>
        private Texture2D GetRedGoldThumbnail(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return null;
            if (redGoldThumbnailCache.TryGetValue(imagePath, out Texture2D cached))
                return cached;

            string assetPath = RedGoldPathHelper.ToTablePath(imagePath);
            if (assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                if (obj != null)
                {
                    Texture2D preview = AssetPreview.GetAssetPreview(obj) ?? AssetPreview.GetMiniThumbnail(obj);
                    if (preview != null)
                    {
                        redGoldThumbnailCache[imagePath] = preview;
                        return preview;
                    }
                }
            }

            // 项目外文件 → 用 ImageNormalizer 缩略
            Texture2D tex = ImageNormalizer.LoadTexture(imagePath);
            if (tex != null)
            {
                Texture2D thumb = ImageNormalizer.Normalize(tex, 32, 32,
                    ContentAlignment.Center, ResizeMode.ProportionalFit);
                UnityEngine.Object.DestroyImmediate(tex);
                if (thumb != null)
                {
                    redGoldThumbnailCache[imagePath] = thumb;
                    return thumb;
                }
            }

            return null;
        }

        /// <summary>
        /// 生成完成后延迟扫描输出图片的引用影响
        /// </summary>
        private void RedGoldShowReferenceImpact(List<string> generatedAssetPaths, int totalCount)
        {
            EditorApplication.delayCall -= () => RedGoldShowReferenceImpact(generatedAssetPaths, totalCount);

            try
            {
                var referrerMap = new Dictionary<string, HashSet<string>>();
                foreach (string assetPath in generatedAssetPaths)
                {
                    string[] deps = AssetDatabase.GetDependencies(assetPath, false);
                    foreach (string dep in deps)
                    {
                        if (dep == assetPath) continue;
                        if (!dep.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;

                        if (!referrerMap.ContainsKey(dep))
                            referrerMap[dep] = new HashSet<string>();
                        referrerMap[dep].Add(assetPath);
                    }
                }

                if (referrerMap.Count > 0)
                {
                    int prefabCount = referrerMap.Count;
                    string msg = $"本次生成了 {totalCount} 个图标文件，影响到 {prefabCount} 个预制体：\n\n";
                    int showCount = 0;
                    foreach (var kvp in referrerMap.OrderBy(k => k.Key))
                    {
                        if (showCount >= 20) { msg += $"\n... 以及其它 {prefabCount - 20} 个预制体"; break; }
                        string prefabName = Path.GetFileNameWithoutExtension(kvp.Key);
                        msg += $"📍 {prefabName}\n";
                        showCount++;
                    }
                    msg += $"\n可前往 Asset References 标签页查看完整引用详情。";

                    EditorUtility.DisplayDialog("引用影响", msg, "确定");

                    // 输出详细 JSON 到 Console
                    var detail = referrerMap.OrderBy(k => k.Key)
                        .Select(k => new { Prefab = k.Key, ReferencedAssets = k.Value.OrderBy(a => a).ToList() })
                        .ToList();
                    Debug.Log($"[UIProbe] 本次生成影响 {prefabCount} 个预制体:\n{UnityEngine.JsonUtility.ToJson(detail, true)}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UIProbe] 引用影响扫描跳过: {ex.Message}");
            }
        }

        private void RedGoldPingPath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return;
            string assetPath = RedGoldPathHelper.ToTablePath(absolutePath);
            if (assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                if (obj != null)
                {
                    Selection.activeObject = obj;
                    EditorGUIUtility.PingObject(obj);
                    return;
                }
            }

            EditorUtility.RevealInFinder(absolutePath);
        }

        private void DrawRedGoldBatchOpsBar(HashSet<string> conflictSourcePaths)
        {
            redGoldFoldBatchOps = EditorGUILayout.Foldout(redGoldFoldBatchOps, $"⚡ 批量操作 {(conflictSourcePaths.Count > 0 ? $"⚠ 冲突 {conflictSourcePaths.Count}" : "")}", true, EditorStyles.foldoutHeader);
            if (!redGoldFoldBatchOps) return;

            GUILayout.BeginVertical(EditorStyles.helpBox);

            // 第1行: 前缀/后缀/查找替换
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("前缀:", GUILayout.MinWidth(25));
            redGoldBatchPrefix = EditorGUILayout.TextField(redGoldBatchPrefix, GUILayout.MinWidth(40), GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField("后缀:", GUILayout.MinWidth(25));
            redGoldBatchSuffix = EditorGUILayout.TextField(redGoldBatchSuffix, GUILayout.MinWidth(40), GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField("替换:", GUILayout.MinWidth(25));
            redGoldBatchFind = EditorGUILayout.TextField(redGoldBatchFind, GUILayout.MinWidth(40), GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField("→", GUILayout.Width(14));
            redGoldBatchReplace = EditorGUILayout.TextField(redGoldBatchReplace, GUILayout.MinWidth(40), GUILayout.ExpandWidth(true));
            if (GUILayout.Button("应用命名", EditorStyles.miniButton, GUILayout.Width(64)))
            {
                var targets = redGoldPreviewRows.Where(r => r.IsSelected && !r.HasError).ToList();
                foreach (var row in targets)
                {
                    string oldName = Path.GetFileNameWithoutExtension(row.PlannedOutputPath ?? row.Name);
                    string newName = oldName;
                    if (!string.IsNullOrEmpty(redGoldBatchFind))
                        newName = newName.Replace(redGoldBatchFind, redGoldBatchReplace);
                    newName = redGoldBatchPrefix + newName + redGoldBatchSuffix;
                    string dir = Path.GetDirectoryName(row.PlannedOutputPath);
                    if (!string.IsNullOrEmpty(dir))
                        row.PlannedOutputPath = Path.Combine(dir, newName + ".png");
                    row.OutputFileNameOverride = newName + ".png";
                    row.UserEdited = true;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // 第2行: 统一格数 / 品质 / 输出路径
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("格数:", GUILayout.Width(25));
            redGoldBatchGridLong = EditorGUILayout.IntField(redGoldBatchGridLong, GUILayout.MinWidth(25), GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField("×", GUILayout.Width(12));
            redGoldBatchGridWide = EditorGUILayout.IntField(redGoldBatchGridWide, GUILayout.MinWidth(25), GUILayout.ExpandWidth(true));

            string[] qualityKeywords = redGoldQualityEntries.Select(e => e.keyword).Where(k => !string.IsNullOrEmpty(k)).ToArray();
            int curQIdx = Mathf.Max(0, Array.IndexOf(qualityKeywords, redGoldBatchQuality));
            int newQIdx = EditorGUILayout.Popup(curQIdx, qualityKeywords, GUILayout.MinWidth(50), GUILayout.ExpandWidth(true));
            if (newQIdx != curQIdx) redGoldBatchQuality = qualityKeywords[newQIdx];
            EditorGUILayout.LabelField("路径:", GUILayout.Width(25));
            redGoldBatchOutputPath = EditorGUILayout.TextField(redGoldBatchOutputPath, GUILayout.MinWidth(60), GUILayout.ExpandWidth(true));

            if (GUILayout.Button("应用设置", EditorStyles.miniButton, GUILayout.Width(64)))
            {
                var targets = redGoldPreviewRows.Where(r => r.IsSelected && !r.HasError).ToList();
                foreach (var row in targets)
                {
                    if (redGoldBatchGridLong > 0 && redGoldBatchGridWide > 0)
                    {
                        row.GridLong = redGoldBatchGridLong;
                        row.GridWide = redGoldBatchGridWide;
                        (row.OutputWidth, row.OutputHeight) = RedGoldComputeOutputSize(row.GridLong, row.GridWide);
                    }
                    if (!string.IsNullOrEmpty(redGoldBatchQuality))
                    {
                        row.Quality = redGoldBatchQuality;
                        row.OutputFolder = RedGoldGetQualityFolder(row.Quality);
                    }
                    if (!string.IsNullOrEmpty(redGoldBatchOutputPath))
                    {
                        row.OutputFolder = RedGoldPathHelper.ToAbsolutePath(redGoldBatchOutputPath);
                    }
                    row.UserEdited = true;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }

        private void ApplyRedGoldResourceImporterConfig()
        {
            if (config == null || config.redGoldResourceImporter == null) return;

            var cfg = config.redGoldResourceImporter;
            redGoldTablePath = cfg.tablePath;
            // 多源文件夹向后兼容
            if (cfg.imageSourceFolders != null && cfg.imageSourceFolders.Count > 0)
            {
                redGoldImageSourceFolders.Clear();
                redGoldImageSourceFolders.AddRange(cfg.imageSourceFolders);
            }
            else if (!string.IsNullOrEmpty(cfg.imageSourceFolder))
            {
                redGoldImageSourceFolders.Clear();
                redGoldImageSourceFolders.Add(cfg.imageSourceFolder);
            }
            else
            {
                if (redGoldImageSourceFolders.Count == 0 || redGoldImageSourceFolders.All(string.IsNullOrEmpty))
                    redGoldImageSourceFolders.Add("");
            }
            redGoldIncludeSubfolders = cfg.includeSubfolders;
            redGoldNameColumn = string.IsNullOrEmpty(cfg.nameColumn) ? "名称" : cfg.nameColumn;
            redGoldQualityColumn = string.IsNullOrEmpty(cfg.qualityColumn) ? "品质" : cfg.qualityColumn;
            redGoldGridLongColumn = string.IsNullOrEmpty(cfg.gridLongColumn) ? "格数：长" : cfg.gridLongColumn;
            redGoldGridWideColumn = string.IsNullOrEmpty(cfg.gridWideColumn) ? "格数：宽" : cfg.gridWideColumn;
            redGoldGridCountColumn = string.IsNullOrEmpty(cfg.gridCountColumn) ? "格数" : cfg.gridCountColumn;
            redGoldIconPathColumn = string.IsNullOrEmpty(cfg.iconPathColumn) ? "图标路径" : cfg.iconPathColumn;
            redGoldOverrideGrid = cfg.overrideGrid;
            redGoldOverrideGridLong = Mathf.Max(1, cfg.overrideGridLong);
            redGoldOverrideGridWide = Mathf.Max(1, cfg.overrideGridWide);
            redGoldCellPixelSize = cfg.cellPixelSize > 0 ? cfg.cellPixelSize : 100;
            redGoldMaxOutputEdge = Mathf.Max(0, cfg.maxOutputEdge);

            // ▼ 品质列表向后兼容：优先读取 qualityEntries
            if (cfg.qualityEntries != null && cfg.qualityEntries.Count > 0)
            {
                redGoldQualityEntries = cfg.qualityEntries;
            }
            else if (!string.IsNullOrEmpty(cfg.redOutputFolder) || !string.IsNullOrEmpty(cfg.purpleOutputFolder) || !string.IsNullOrEmpty(cfg.goldOutputFolder))
            {
                // 从旧字段迁移
                redGoldQualityEntries = new List<QualityConfigEntry>();
                if (!string.IsNullOrEmpty(cfg.redOutputFolder))
                    redGoldQualityEntries.Add(new QualityConfigEntry { keyword = "红", displayName = "红色品质", outputFolder = cfg.redOutputFolder, namingTemplate = "T_Icon_Red_{Pinyin}.png", usePinyin = true });
                if (!string.IsNullOrEmpty(cfg.purpleOutputFolder))
                    redGoldQualityEntries.Add(new QualityConfigEntry { keyword = "紫", displayName = "紫色品质", outputFolder = cfg.purpleOutputFolder });
                if (!string.IsNullOrEmpty(cfg.goldOutputFolder))
                    redGoldQualityEntries.Add(new QualityConfigEntry { keyword = "金", displayName = "金色品质", outputFolder = cfg.goldOutputFolder });
            }

            redGoldOverwriteTable = cfg.overwriteTable;
            redGoldOutputTablePath = cfg.outputTablePath;
        }

        private void CollectRedGoldResourceImporterConfig()
        {
            if (config == null) return;
            if (config.redGoldResourceImporter == null)
                config.redGoldResourceImporter = new RedGoldResourceImporterConfig();

            var cfg = config.redGoldResourceImporter;
            cfg.tablePath = redGoldTablePath;
            cfg.imageSourceFolders = new List<string>(redGoldImageSourceFolders.Where(f => !string.IsNullOrEmpty(f)));
            cfg.imageSourceFolder = cfg.imageSourceFolders.FirstOrDefault() ?? "";
            cfg.includeSubfolders = redGoldIncludeSubfolders;
            cfg.nameColumn = redGoldNameColumn;
            cfg.qualityColumn = redGoldQualityColumn;
            cfg.gridLongColumn = redGoldGridLongColumn;
            cfg.gridWideColumn = redGoldGridWideColumn;
            cfg.gridCountColumn = redGoldGridCountColumn;
            cfg.iconPathColumn = redGoldIconPathColumn;
            cfg.overrideGrid = redGoldOverrideGrid;
            cfg.overrideGridLong = redGoldOverrideGridLong;
            cfg.overrideGridWide = redGoldOverrideGridWide;
            cfg.cellPixelSize = redGoldCellPixelSize;
            cfg.maxOutputEdge = redGoldMaxOutputEdge;
            cfg.qualityEntries = redGoldQualityEntries;
            cfg.redOutputFolder = "";
            cfg.purpleOutputFolder = "";
            cfg.goldOutputFolder = "";
            cfg.overwriteTable = redGoldOverwriteTable;
            cfg.outputTablePath = redGoldOutputTablePath;
        }
    }
}
