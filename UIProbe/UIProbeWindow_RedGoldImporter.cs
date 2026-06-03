using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UIProbe
{
    public partial class UIProbeWindow
    {
        private string redGoldTablePath = "";
        private string redGoldImageSourceFolder = "";
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

        // ▼ 品质列表（取代原来的 redGoldRed/Purple/GoldOutputFolder）
        private List<QualityConfigEntry> redGoldQualityEntries = new List<QualityConfigEntry>
        {
            new QualityConfigEntry { keyword = "红", displayName = "红色品质", namingTemplate = "T_Icon_Red_{Pinyin}.png", usePinyin = true },
            new QualityConfigEntry { keyword = "紫", displayName = "紫色品质" },
            new QualityConfigEntry { keyword = "金", displayName = "金色品质" },
        };
        private bool redGoldFoldQuality = true;
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
        private readonly RedGoldUndoManager redGoldUndoManager = new RedGoldUndoManager();

        // ▼ 缩略图缓存
        private readonly Dictionary<string, Texture2D> redGoldThumbnailCache = new Dictionary<string, Texture2D>();

        private bool redGoldFoldSource = true;
        private bool redGoldFoldColumns = true;
        private bool redGoldFoldRules = true;
        private bool redGoldFoldPreview = true;
        private bool redGoldFoldReplaceable = true;
        private bool redGoldFoldMissing = true;
        private bool redGoldFoldUnmatched = true;

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
                EditorGUI.ProgressBar(
                    EditorGUILayout.GetControlRect(GUILayout.Height(20)),
                    redGoldProgress,
                    $"处理中... {(int)(redGoldProgress * 100)}%");
            }
        }

        private void DrawRedGoldSourceSettings()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("表格文件:", GUILayout.Width(75));
            redGoldTablePath = EditorGUILayout.TextField(redGoldTablePath);
            if (GUILayout.Button("📄", GUILayout.Width(28)))
            {
                string p = EditorUtility.OpenFilePanel("选择 CSV/TSV 表格", RedGoldPathHelper.GetExistingDirectory(redGoldTablePath), "");
                if (!string.IsNullOrEmpty(p)) redGoldTablePath = p;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("图片文件夹:", GUILayout.Width(75));
            redGoldImageSourceFolder = EditorGUILayout.TextField(redGoldImageSourceFolder);
            if (GUILayout.Button("📁", GUILayout.Width(28)))
            {
                string p = EditorUtility.OpenFolderPanel("选择待修改图片文件夹", RedGoldPathHelper.ToAbsolutePath(redGoldImageSourceFolder), "");
                if (!string.IsNullOrEmpty(p)) redGoldImageSourceFolder = p;
            }
            GUILayout.EndHorizontal();

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

        private void DrawRedGoldOutputSettings()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);

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

                    // 第三行：命名模板 + 拼音开关
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("模板:", GUILayout.Width(35));
                    entry.namingTemplate = EditorGUILayout.TextField(entry.namingTemplate, GUILayout.Width(150));
                    entry.usePinyin = EditorGUILayout.ToggleLeft("拼音", entry.usePinyin, GUILayout.Width(50));
                    if (!string.IsNullOrEmpty(entry.namingTemplate))
                    {
                        string sample = entry.namingTemplate
                            .Replace("{Pinyin}", "HuoGuoShenXiang")
                            .Replace("{Name}", "火锅神像");
                        EditorGUILayout.LabelField($"→ {sample}", EditorStyles.miniLabel);
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
            DrawRedGoldFolderField("红品质路径:", ref redGoldRedOutputFolder);
            DrawRedGoldFolderField("紫品质路径:", ref redGoldPurpleOutputFolder);
            DrawRedGoldFolderField("金品质路径:", ref redGoldGoldOutputFolder);

            EditorGUILayout.Space(4);
            redGoldOverwriteTable = EditorGUILayout.ToggleLeft("直接覆盖原表格", redGoldOverwriteTable);
            EditorGUI.BeginDisabledGroup(redGoldOverwriteTable);
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("输出表格:", GUILayout.Width(75));
            redGoldOutputTablePath = EditorGUILayout.TextField(redGoldOutputTablePath);
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
            EditorGUI.BeginDisabledGroup(redGoldProcessing || string.IsNullOrEmpty(redGoldTablePath) || string.IsNullOrEmpty(redGoldImageSourceFolder));
            if (GUILayout.Button("读取表格并预览", GUILayout.Height(30)))
                RedGoldLoadPreview();
            EditorGUI.EndDisabledGroup();

            int validSelected = redGoldPreviewRows.Count(x => x.IsSelected && !x.HasError);
            string generateLabel = redGoldPreviewRows.Count > 0
                ? $"生成资源并写回表格 ({validSelected})"
                : "生成资源并写回表格";
            EditorGUI.BeginDisabledGroup(redGoldProcessing || string.IsNullOrEmpty(redGoldTablePath) || string.IsNullOrEmpty(redGoldImageSourceFolder));
            if (GUILayout.Button(generateLabel, GUILayout.Height(30)))
                RedGoldGenerateAndWriteTable();
            EditorGUI.EndDisabledGroup();

            // ▼ 撤销按钮
            if (redGoldUndoManager.HasUndo)
            {
                GUI.backgroundColor = new Color(0.9f, 0.4f, 0.3f);
                string undoLabel = $"↩ 撤销 ({redGoldUndoManager.StackDepth})";
                if (GUILayout.Button(undoLabel, GUILayout.Height(30), GUILayout.Width(100)))
                {
                    if (EditorUtility.DisplayDialog("确认撤销",
                        $"即将撤销最近一次生成操作（{redGoldUndoManager.CurrentDescription}）\n\n将恢复 {redGoldUndoManager.EntryCount} 个文件的旧版本并还原表格数据。"
                        + (redGoldUndoManager.StackDepth > 1 ? $"\n\n当前还有 {redGoldUndoManager.StackDepth} 次可撤销操作。" : ""),
                        "确认撤销", "取消"))
                    {
                        var result = redGoldUndoManager.TryUndo(redGoldTableData, redGoldOverrideGrid);
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

            EditorGUILayout.Space(3);

            // ────────── ① 可替换图片资源 ──────────
            var replaceableRows = redGoldPreviewRows.Where(x => !x.HasError).ToList();
            if (replaceableRows.Count > 0)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                redGoldFoldReplaceable = EditorGUILayout.Foldout(redGoldFoldReplaceable,
                    $"📦 可替换图片资源（{replaceableRows.Count}）", true, EditorStyles.foldoutHeader);

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

                    redGoldScrollPos = EditorGUILayout.BeginScrollView(redGoldScrollPos, GUILayout.Height(200));
                    foreach (var row in replaceableRows)
                    {
                        Rect rowRect = EditorGUILayout.BeginHorizontal();
                        if (Event.current.type == EventType.Repaint)
                        {
                            if (row.UseExistingOutput) EditorGUI.DrawRect(rowRect, new Color(0.3f, 0.5f, 0.8f, 0.06f));
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
                        if (GUILayout.Button(row.Name, EditorStyles.label, GUILayout.Width(80)))
                            RedGoldPingPath(row.SourceImagePath);
                        EditorGUILayout.LabelField(row.Quality, GUILayout.Width(28));
                        EditorGUILayout.LabelField($"{row.GridLong}×{row.GridWide}", GUILayout.Width(36));

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
                    redGoldScrollPosMissing = EditorGUILayout.BeginScrollView(redGoldScrollPosMissing, GUILayout.Height(120));
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
                    redGoldScrollPosUnmatched = EditorGUILayout.BeginScrollView(redGoldScrollPosUnmatched, GUILayout.Height(120));
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

            string tablePath = RedGoldPathHelper.ToAbsolutePath(redGoldTablePath);
            string imageFolder = RedGoldPathHelper.ToAbsolutePath(redGoldImageSourceFolder);
            if (string.IsNullOrEmpty(tablePath) || !File.Exists(tablePath))
            {
                EditorUtility.DisplayDialog("错误", "请选择有效的 CSV/TSV 表格文件。", "确定");
                return;
            }

            if (string.IsNullOrEmpty(imageFolder) || !Directory.Exists(imageFolder))
            {
                EditorUtility.DisplayDialog("错误", "请选择有效的图片文件夹。", "确定");
                return;
            }

            try
            {
                redGoldTableData = DelimitedFileParser.ReadTable(tablePath);
                if (redGoldTableData.Rows.Count < 2)
                {
                    EditorUtility.DisplayDialog("提示", "表格没有可处理的数据行。", "确定");
                    return;
                }

                if (!RedGoldResolveColumns(redGoldTableData))
                    return;

                Dictionary<string, string> imageMap = RedGoldImageMatcher.BuildImageMap(imageFolder, redGoldIncludeSubfolders, out List<string> duplicateWarnings);
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
                                string absImageFolder = RedGoldPathHelper.ToAbsolutePath(redGoldImageSourceFolder);
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

            redGoldProcessing = true;
            redGoldProgress = 0f;
            int successCount = 0;

            // ▼ 初始化撤销系统
            string undoDir = Path.Combine(
                UIProbeStorage.GetMainFolderPath(),
                "RedGoldUndo",
                DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            var undoEntries = new List<RedGoldUndoEntry>();

            try
            {
                for (int i = 0; i < selectedRows.Count; i++)
                {
                    var row = selectedRows[i];
                    EditorUtility.DisplayProgressBar("生成资源", row.Name, (float)i / selectedRows.Count);

                    Directory.CreateDirectory(row.OutputFolder);
                    // 根据 UseExistingOutput 决定加载源文件还是已有输出文件
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
                        source,
                        row.OutputWidth,
                        row.OutputHeight,
                        ContentAlignment.Center,
                        ResizeMode.ProportionalFit);
                    UnityEngine.Object.DestroyImmediate(source);

                    if (result == null)
                    {
                        row.Status = "图片处理失败";
                        row.HasError = true;
                        continue;
                    }

                    // ▼ 备份：生成前如果输出文件已存在，复制到撤销目录
                    string outPath = row.PlannedOutputPath;
                    if (!string.IsNullOrEmpty(outPath) && File.Exists(outPath))
                    {
                        string backupPath = Path.Combine(undoDir,
                            $"{Path.GetFileNameWithoutExtension(outPath)}_{row.RowIndex}_old{Path.GetExtension(outPath)}");
                        Directory.CreateDirectory(undoDir);
                        File.Copy(outPath, backupPath, overwrite: true);

                        // 记录旧表格值，用于撤销时恢复
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

                        undoEntries.Add(new RedGoldUndoEntry
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
                        successCount++;
                    }
                    else
                    {
                        row.Status = "保存失败";
                        row.HasError = true;
                    }

                    redGoldProgress = (float)(i + 1) / selectedRows.Count;
                    Repaint();
                }

                DelimitedFileParser.WriteTable(tableOutputPath, redGoldTableData);
                AssetDatabase.Refresh();
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
            }

            // ▼ 保存撤销状态
            bool anyBackups = undoEntries.Count > 0 && undoEntries.Any(e => !string.IsNullOrEmpty(e.BackupFilePath));
            if (successCount > 0 && anyBackups)
            {
                int modFiles = undoEntries.Count(e => !string.IsNullOrEmpty(e.BackupFilePath));
                int newFiles = successCount - modFiles;
                string description = $"{successCount} 个文件";
                if (newFiles > 0) description += $"（新增 {newFiles}）";
                if (modFiles > 0) description += $"（修改 {modFiles}）";
                redGoldUndoManager.PushSnapshot(undoEntries, tableOutputPath, description);
            }

            // 清理空撤销目录
            try { if (Directory.Exists(undoDir) && Directory.GetFiles(undoDir).Length == 0) Directory.Delete(undoDir); } catch { }

            EditorUtility.DisplayDialog("完成",
                $"生成完成：{successCount} / {selectedRows.Count}\n表格已写入：\n{tableOutputPath}"
                + (redGoldUndoManager.HasUndo
                    ? "\n\n如需恢复旧版本，请点击面板中的「撤销」按钮。"
                    : ""),
                "确定");
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
        /// 查找品质配置中启用拼音命名的条目
        /// </summary>
        private QualityConfigEntry RedGoldFindPinyinQuality(string quality)
        {
            if (string.IsNullOrEmpty(quality)) return null;
            string q = quality.Trim();

            foreach (var entry in redGoldQualityEntries)
            {
                if (string.IsNullOrEmpty(entry.keyword)) continue;
                if (entry.usePinyin && !string.IsNullOrEmpty(entry.namingTemplate)
                    && q.IndexOf(entry.keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return entry;
            }
            return null;
        }

        /// <summary>
        /// 根据品质配置和命名模板生成输出文件名
        /// </summary>
        private string RedGoldBuildNamedOutputFileName(string quality, string name)
        {
            var entry = RedGoldFindPinyinQuality(quality);
            if (entry == null || string.IsNullOrEmpty(entry.namingTemplate)) return "";

            string pinyin = entry.usePinyin ? RedGoldNameConverter.BuildRedOutputFileName(name) : "";
            string result = entry.namingTemplate
                .Replace("{Pinyin}", string.IsNullOrEmpty(pinyin) ? name : pinyin.Replace("T_Icon_Red_", "").Replace(".png", ""))
                .Replace("{Name}", name);

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
            EditorGUILayout.LabelField(label, GUILayout.Width(90));
            value = EditorGUILayout.TextField(value);
            GUILayout.EndHorizontal();
        }

        private void DrawRedGoldFolderField(string label, ref string value)
        {
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(75));
            value = EditorGUILayout.TextField(value);
            if (GUILayout.Button("📁", GUILayout.Width(28)))
            {
                string p = EditorUtility.OpenFolderPanel("选择生成资源路径", RedGoldPathHelper.ToAbsolutePath(value), "");
                if (!string.IsNullOrEmpty(p)) value = p;
            }
            GUILayout.EndHorizontal();
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

        private void ApplyRedGoldResourceImporterConfig()
        {
            if (config == null || config.redGoldResourceImporter == null) return;

            var cfg = config.redGoldResourceImporter;
            redGoldTablePath = cfg.tablePath;
            redGoldImageSourceFolder = cfg.imageSourceFolder;
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
            cfg.imageSourceFolder = redGoldImageSourceFolder;
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
