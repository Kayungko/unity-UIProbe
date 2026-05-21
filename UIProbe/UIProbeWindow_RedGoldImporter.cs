using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UIProbe
{
    internal class RedGoldImportRow
    {
        public int RowIndex;
        public string Name;
        public string Quality;
        public int GridLong;
        public int GridWide;
        public int OutputWidth;
        public int OutputHeight;
        public string SourceImagePath;
        public string OutputFolder;
        public string PlannedOutputPath;
        public string Status;
        public bool IsSelected = true;
        public bool HasError;
    }

    internal class RedGoldTableData
    {
        public char Delimiter;
        public List<List<string>> Rows = new List<List<string>>();
        public int NameColumn = -1;
        public int QualityColumn = -1;
        public int GridLongColumn = -1;
        public int GridWideColumn = -1;
        public int GridCountColumn = -1;
        public int IconPathColumn = -1;
    }

    internal class RedGoldNamingState
    {
        private string prefix = "";
        private string suffix = "";
        private int nextNumber = 1;
        private int digits = 3;
        private bool hasPattern;
        private readonly HashSet<string> reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> allocatedPreferredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static RedGoldNamingState Create(string folder)
        {
            var state = new RedGoldNamingState();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return state;

            int bestNumber = -1;
            var prefixCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(file);
                state.reservedNames.Add(fileName);

                string nameNoExt = Path.GetFileNameWithoutExtension(file);
                int lastUnderline = nameNoExt.LastIndexOf('_');
                if (lastUnderline >= 0)
                {
                    string commonPrefix = nameNoExt.Substring(0, lastUnderline + 1);
                    if (!prefixCounts.ContainsKey(commonPrefix))
                        prefixCounts[commonPrefix] = 0;
                    prefixCounts[commonPrefix]++;
                }

                MatchCollection matches = Regex.Matches(nameNoExt, "\\d+");
                if (matches.Count == 0) continue;

                Match numberMatch = matches[matches.Count - 1];
                if (!int.TryParse(numberMatch.Value, out int number)) continue;
                if (number <= bestNumber) continue;

                bestNumber = number;
                state.prefix = nameNoExt.Substring(0, numberMatch.Index);
                state.suffix = nameNoExt.Substring(numberMatch.Index + numberMatch.Length);
                state.digits = numberMatch.Length;
                state.nextNumber = number + 1;
                state.hasPattern = true;
            }

            if (!state.hasPattern && prefixCounts.Count > 0)
            {
                var bestPrefix = prefixCounts
                    .OrderByDescending(x => x.Value)
                    .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .First();
                if (bestPrefix.Value >= 2)
                {
                    state.prefix = bestPrefix.Key;
                    state.suffix = "";
                    state.digits = 3;
                    state.nextNumber = bestPrefix.Value + 1;
                    state.hasPattern = true;
                }
            }

            return state;
        }

        public string Allocate(string folder, string fallbackNameNoExt)
        {
            if (hasPattern)
            {
                while (true)
                {
                    string name = $"{prefix}{nextNumber.ToString("D" + digits)}{suffix}.png";
                    nextNumber++;
                    if (reservedNames.Add(name))
                        return Path.Combine(folder, name);
                }
            }

            string safeName = string.IsNullOrEmpty(fallbackNameNoExt) ? "image" : fallbackNameNoExt;
            string candidate = safeName + ".png";
            int index = 1;
            while (!reservedNames.Add(candidate))
            {
                candidate = $"{safeName}_{index}.png";
                index++;
            }

            return Path.Combine(folder, candidate);
        }

        public void ReserveFileName(string fileName)
        {
            if (!string.IsNullOrEmpty(fileName))
                reservedNames.Add(fileName);
        }

        public string ReservePreferred(string folder, string preferredFileName)
        {
            if (string.IsNullOrEmpty(preferredFileName))
                return "";

            string extension = Path.GetExtension(preferredFileName);
            string nameNoExt = Path.GetFileNameWithoutExtension(preferredFileName);
            if (string.IsNullOrEmpty(extension))
                extension = ".png";

            string candidate = nameNoExt + extension;
            if (allocatedPreferredNames.Add(candidate))
            {
                reservedNames.Add(candidate);
                return Path.Combine(folder, candidate);
            }

            int index = 2;
            while (true)
            {
                candidate = $"{nameNoExt}_{index}{extension}";
                if (allocatedPreferredNames.Add(candidate) && reservedNames.Add(candidate))
                    return Path.Combine(folder, candidate);
                index++;
            }
        }
    }

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

        private string redGoldRedOutputFolder = "";
        private string redGoldPurpleOutputFolder = "";
        private string redGoldGoldOutputFolder = "";
        private bool redGoldOverwriteTable = false;
        private string redGoldOutputTablePath = "";

        private RedGoldTableData redGoldTableData;
        private readonly List<RedGoldImportRow> redGoldPreviewRows = new List<RedGoldImportRow>();
        private Vector2 redGoldScrollPos;
        private bool redGoldProcessing;
        private float redGoldProgress;

        private bool redGoldFoldSource = true;
        private bool redGoldFoldColumns = true;
        private bool redGoldFoldRules = true;
        private bool redGoldFoldPreview = true;

        private static readonly Dictionary<char, string> RedGoldPinyinMap = new Dictionary<char, string>
        {
            { '阿', "A" }, { '白', "Bai" }, { '宝', "Bao" }, { '爆', "Bao" }, { '兵', "Bing" },
            { '裁', "Cai" }, { '茶', "Cha" }, { '辰', "Chen" }, { '处', "Chu" }, { '傩', "Nuo" },
            { '地', "Di" }, { '赌', "Du" }, { '发', "Fa" }, { '骨', "Gu" }, { '锅', "Guo" },
            { '红', "Hong" }, { '虹', "Hong" }, { '火', "Huo" }, { '画', "Hua" }, { '匠', "Jiang" },
            { '祭', "Ji" }, { '甲', "Jia" }, { '金', "Jin" }, { '晶', "Jing" }, { '巨', "Ju" },
            { '骼', "Ge" }, { '怪', "Guai" }, { '猎', "Lie" }, { '炉', "Lu" }, { '满', "Man" },
            { '蒙', "Meng" }, { '霓', "Ni" }, { '球', "Qiu" }, { '神', "Shen" }, { '生', "Sheng" },
            { '森', "Sen" }, { '坛', "Tan" }, { '堂', "Tang" }, { '特', "Te" }, { '天', "Tian" },
            { '铁', "Tie" }, { '外', "Wai" }, { '文', "Wen" }, { '西', "Xi" }, { '戏', "Xi" },
            { '先', "Xian" }, { '像', "Xiang" }, { '星', "Xing" }, { '型', "Xing" }, { '鸭', "Ya" },
            { '源', "Yuan" }, { '之', "Zhi" }, { '桌', "Zhuo" }, { '死', "Si" },
            { 'Ⅰ', "1" }, { 'Ⅱ', "2" }, { 'Ⅲ', "3" }, { 'Ⅳ', "4" }
        };

        private static readonly Dictionary<string, string> RedGoldSemanticNameMap = new Dictionary<string, string>
        {
            { "星辰画匠文森特", "XingChenHuaJiang" },
            { "铁球先生", "TieQiuXianSheng" },
            { "白死神西蒙", "BaiSiShen" },
            { "傩戏外骨骼", "NuoXiWaiGuGe" },
            { "霓虹茶桌", "NiHongChaZhuo" },
            { "赌神祭坛", "DuShenJiTan" },
            { "火锅神像", "HuoGuoShenXiang" },
            { "满堂红天锅", "ManTangHongTianGuo" }
        };

        private void DrawRedGoldResourceImporterContent()
        {
            EditorGUILayout.HelpBox(
                "读取 CSV/TSV 表格，根据名称匹配图片，按品质输出到红 / 紫 / 金路径；画布按格数比例调整，内容等比适配不裁切，并把新图标路径写回表格。",
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

            if (redGoldPreviewRows.Count > 0)
            {
                int selected = redGoldPreviewRows.Count(x => x.IsSelected && !x.HasError);
                int errors = redGoldPreviewRows.Count(x => x.HasError);
                string header = errors > 0
                    ? $"④ 预览列表 ({redGoldPreviewRows.Count} 行，{selected} 可生成，{errors} 个问题)"
                    : $"④ 预览列表 ({redGoldPreviewRows.Count} 行，{selected} 可生成)";

                EditorGUILayout.Space(5);
                redGoldFoldPreview = EditorGUILayout.Foldout(redGoldFoldPreview, header, true, EditorStyles.foldoutHeader);
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
                string p = EditorUtility.OpenFilePanel("选择 CSV/TSV 表格", RedGoldGetExistingDirectory(redGoldTablePath), "");
                if (!string.IsNullOrEmpty(p)) redGoldTablePath = p;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("图片文件夹:", GUILayout.Width(75));
            redGoldImageSourceFolder = EditorGUILayout.TextField(redGoldImageSourceFolder);
            if (GUILayout.Button("📁", GUILayout.Width(28)))
            {
                string p = EditorUtility.OpenFolderPanel("选择待修改图片文件夹", RedGoldToAbsolutePath(redGoldImageSourceFolder), "");
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
            EditorGUILayout.LabelField("方形1-4统一:", GUILayout.Width(85));
            redGoldMaxOutputEdge = Mathf.Max(0, EditorGUILayout.IntField(redGoldMaxOutputEdge, GUILayout.Width(60)));
            EditorGUILayout.LabelField("px（仅 1:1 / 2:2 / 3:3 / 4:4 生效）", EditorStyles.miniLabel);
            GUILayout.EndHorizontal();

            EditorGUILayout.HelpBox("尺寸计算：1:1、2:2、3:3、4:4 统一输出 512×512；其它比例按“格数 × 格子基准像素”输出，默认每格 100px，如 2:3 输出 200×300。", MessageType.None);

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
                string defaultDir = RedGoldGetExistingDirectory(redGoldTablePath);
                string p = EditorUtility.SaveFilePanel("保存回写后的表格", defaultDir, RedGoldGetDefaultOutputTableName(), RedGoldGetTableExtension());
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
            GUILayout.EndHorizontal();
        }

        private void DrawRedGoldPreviewList()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("全选", EditorStyles.miniButton, GUILayout.Width(40)))
                foreach (var row in redGoldPreviewRows.Where(x => !x.HasError)) row.IsSelected = true;
            if (GUILayout.Button("全不选", EditorStyles.miniButton, GUILayout.Width(50)))
                foreach (var row in redGoldPreviewRows) row.IsSelected = false;
            if (GUILayout.Button("反选", EditorStyles.miniButton, GUILayout.Width(40)))
                foreach (var row in redGoldPreviewRows.Where(x => !x.HasError)) row.IsSelected = !row.IsSelected;
            GUILayout.EndHorizontal();

            redGoldScrollPos = EditorGUILayout.BeginScrollView(redGoldScrollPos, GUILayout.Height(260));
            foreach (var row in redGoldPreviewRows)
            {
                Rect rowRect = EditorGUILayout.BeginHorizontal();
                if (Event.current.type == EventType.Repaint)
                {
                    if (row.HasError) EditorGUI.DrawRect(rowRect, new Color(1f, 0.25f, 0.2f, 0.10f));
                    else if (row.IsSelected) EditorGUI.DrawRect(rowRect, new Color(0.3f, 0.6f, 1f, 0.08f));
                }

                EditorGUI.BeginDisabledGroup(row.HasError);
                row.IsSelected = EditorGUILayout.Toggle(row.IsSelected, GUILayout.Width(18));
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.LabelField($"#{row.RowIndex + 1}", GUILayout.Width(34));
                if (GUILayout.Button(row.Name, EditorStyles.label, GUILayout.Width(140)))
                    RedGoldPingPath(row.SourceImagePath);
                EditorGUILayout.LabelField(row.Quality, GUILayout.Width(38));
                EditorGUILayout.LabelField($"{row.GridLong}×{row.GridWide}", GUILayout.Width(48));
                EditorGUILayout.LabelField($"→ {row.OutputWidth}×{row.OutputHeight}", GUILayout.Width(82));
                EditorGUILayout.LabelField(row.Status, EditorStyles.miniLabel);

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            GUILayout.EndVertical();
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
                string p = EditorUtility.OpenFolderPanel("选择生成资源路径", RedGoldToAbsolutePath(value), "");
                if (!string.IsNullOrEmpty(p)) value = p;
            }
            GUILayout.EndHorizontal();
        }

        private void RedGoldLoadPreview()
        {
            redGoldPreviewRows.Clear();
            redGoldTableData = null;

            string tablePath = RedGoldToAbsolutePath(redGoldTablePath);
            string imageFolder = RedGoldToAbsolutePath(redGoldImageSourceFolder);
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
                redGoldTableData = RedGoldReadTable(tablePath);
                if (redGoldTableData.Rows.Count < 2)
                {
                    EditorUtility.DisplayDialog("提示", "表格没有可处理的数据行。", "确定");
                    return;
                }

                if (!RedGoldResolveColumns(redGoldTableData))
                    return;

                Dictionary<string, string> imageMap = RedGoldBuildImageMap(imageFolder);
                Dictionary<string, RedGoldNamingState> namingStates = new Dictionary<string, RedGoldNamingState>(StringComparer.OrdinalIgnoreCase);

                for (int i = 1; i < redGoldTableData.Rows.Count; i++)
                {
                    List<string> tableRow = redGoldTableData.Rows[i];
                    string name = RedGoldGetCell(tableRow, redGoldTableData.NameColumn).Trim();
                    string quality = RedGoldGetCell(tableRow, redGoldTableData.QualityColumn).Trim();
                    int gridLong = redGoldOverrideGrid ? redGoldOverrideGridLong : RedGoldParseInt(RedGoldGetCell(tableRow, redGoldTableData.GridLongColumn));
                    int gridWide = redGoldOverrideGrid ? redGoldOverrideGridWide : RedGoldParseInt(RedGoldGetCell(tableRow, redGoldTableData.GridWideColumn));
                    string outputFolder = RedGoldGetQualityFolder(quality);
                    string iconPath = RedGoldGetCell(tableRow, redGoldTableData.IconPathColumn).Trim();
                    string sourcePath = RedGoldFindSourceImage(imageMap, name, iconPath);
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
                        string iconOutputFileName = RedGoldGetOutputFileNameFromIconPath(iconPath);
                        string redPreferredName = RedGoldBuildRedOutputFileName(name);
                        if (RedGoldIsRedQuality(quality) && !string.IsNullOrEmpty(redPreferredName))
                        {
                            if (!namingStates.TryGetValue(outputFolder, out RedGoldNamingState state))
                            {
                                state = RedGoldNamingState.Create(outputFolder);
                                namingStates[outputFolder] = state;
                            }

                            previewRow.PlannedOutputPath = state.ReservePreferred(outputFolder, redPreferredName);
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
                        previewRow.Status = RedGoldToTablePath(previewRow.PlannedOutputPath);
                    }

                    redGoldPreviewRows.Add(previewRow);
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
                ? RedGoldToAbsolutePath(redGoldTablePath)
                : RedGoldToAbsolutePath(redGoldOutputTablePath);
            if (string.IsNullOrEmpty(tableOutputPath))
                tableOutputPath = RedGoldGetDefaultOutputTablePath();

            bool confirmed = EditorUtility.DisplayDialog(
                "确认生成",
                $"即将生成 {selectedRows.Count} 张图片，并写回表格:\n{tableOutputPath}\n\n内容将等比适配目标画布，不会裁切。",
                "开始",
                "取消");
            if (!confirmed) return;

            redGoldProcessing = true;
            redGoldProgress = 0f;
            int successCount = 0;

            try
            {
                for (int i = 0; i < selectedRows.Count; i++)
                {
                    var row = selectedRows[i];
                    EditorUtility.DisplayProgressBar("生成资源", row.Name, (float)i / selectedRows.Count);

                    Directory.CreateDirectory(row.OutputFolder);
                    Texture2D source = ImageNormalizer.LoadTexture(row.SourceImagePath);
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

                    bool saved = ImageNormalizer.SaveTexture(result, row.PlannedOutputPath);
                    UnityEngine.Object.DestroyImmediate(result);

                    if (saved)
                    {
                        RedGoldWriteBackRow(row);
                        row.Status = RedGoldToTablePath(row.PlannedOutputPath);
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

                RedGoldWriteTable(tableOutputPath, redGoldTableData);
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

            EditorUtility.DisplayDialog("完成", $"生成完成：{successCount} / {selectedRows.Count}\n表格已写入：\n{tableOutputPath}", "确定");
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

        private RedGoldTableData RedGoldReadTable(string path)
        {
            string text = RedGoldReadAllText(path);
            char delimiter = RedGoldChooseDelimiter(path, text);
            return new RedGoldTableData
            {
                Delimiter = delimiter,
                Rows = RedGoldParseDelimited(text, delimiter)
            };
        }

        private string RedGoldReadAllText(string path)
        {
            byte[] bytes;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length > int.MaxValue)
                    throw new IOException("表格文件过大，无法读取。");

                bytes = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read == 0) break;
                    offset += read;
                }
            }

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.Default.GetString(bytes);
            }
        }

        private char RedGoldChooseDelimiter(string path, string text)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".tsv") return '\t';

            string firstLine = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None).FirstOrDefault() ?? "";
            int commaCount = firstLine.Count(c => c == ',');
            int tabCount = firstLine.Count(c => c == '\t');
            return tabCount > commaCount ? '\t' : ',';
        }

        private List<List<string>> RedGoldParseDelimited(string text, char delimiter)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == delimiter && !inQuotes)
                {
                    row.Add(field.ToString());
                    field.Length = 0;
                }
                else if ((c == '\r' || c == '\n') && !inQuotes)
                {
                    row.Add(field.ToString());
                    field.Length = 0;
                    rows.Add(row);
                    row = new List<string>();
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                }
                else
                {
                    field.Append(c);
                }
            }

            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }

            return rows.Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        }

        private void RedGoldWriteTable(string path, RedGoldTableData table)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            foreach (List<string> row in table.Rows)
            {
                for (int i = 0; i < row.Count; i++)
                {
                    if (i > 0) sb.Append(table.Delimiter);
                    sb.Append(RedGoldEscapeCell(row[i], table.Delimiter));
                }
                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private string RedGoldEscapeCell(string cell, char delimiter)
        {
            cell = cell ?? "";
            if (cell.IndexOfAny(new[] { delimiter, '"', '\r', '\n' }) < 0)
                return cell;

            return "\"" + cell.Replace("\"", "\"\"") + "\"";
        }

        private Dictionary<string, string> RedGoldBuildImageMap(string folder)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            SearchOption option = redGoldIncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            string[] extensions = { "*.png", "*.jpg", "*.jpeg" };
            foreach (string pattern in extensions)
            {
                foreach (string file in Directory.GetFiles(folder, pattern, option))
                {
                    string key = Path.GetFileNameWithoutExtension(file);
                    if (!result.ContainsKey(key))
                        result.Add(key, file);
                }
            }
            return result;
        }

        private string RedGoldFindSourceImage(Dictionary<string, string> imageMap, string name, string iconPath)
        {
            if (!string.IsNullOrEmpty(iconPath))
            {
                string iconName = Path.GetFileNameWithoutExtension(iconPath);
                if (!string.IsNullOrEmpty(iconName) && imageMap.TryGetValue(iconName, out string iconMatchPath))
                    return iconMatchPath;

                string absoluteIconPath = RedGoldToAbsolutePath(iconPath);
                if (!string.IsNullOrEmpty(absoluteIconPath) && File.Exists(absoluteIconPath))
                    return absoluteIconPath;
            }

            if (string.IsNullOrEmpty(name)) return "";
            if (imageMap.TryGetValue(name, out string exactPath)) return exactPath;

            string normalizedName = RedGoldNormalizeFileKey(name);
            foreach (var pair in imageMap)
            {
                if (RedGoldNormalizeFileKey(pair.Key) == normalizedName)
                    return pair.Value;
            }

            return "";
        }

        private string RedGoldGetOutputFileNameFromIconPath(string iconPath)
        {
            if (string.IsNullOrEmpty(iconPath)) return "";

            string fileName = Path.GetFileName(iconPath.Replace('\\', '/'));
            if (string.IsNullOrEmpty(fileName)) return "";

            string extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension))
                fileName += ".png";
            else if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
                fileName = Path.GetFileNameWithoutExtension(fileName) + ".png";

            return fileName;
        }

        private string RedGoldBuildRedOutputFileName(string displayName)
        {
            string pinyin = RedGoldGetSemanticPinyin(displayName);
            return string.IsNullOrEmpty(pinyin) ? "" : $"T_Icon_Red_{pinyin}.png";
        }

        private string RedGoldGetSemanticPinyin(string displayName)
        {
            string key = RedGoldNormalizeSemanticName(displayName);
            if (!string.IsNullOrEmpty(key) && RedGoldSemanticNameMap.TryGetValue(key, out string semanticName))
                return semanticName;

            return RedGoldToShortPinyin(displayName);
        }

        private string RedGoldNormalizeSemanticName(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return "";

            var builder = new StringBuilder();
            foreach (char c in displayName)
            {
                if (RedGoldIsCjk(c) || char.IsLetterOrDigit(c))
                    builder.Append(c);
            }

            return builder.ToString();
        }

        private string RedGoldToShortPinyin(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return "";

            var builder = new StringBuilder();
            bool capitalizeNextAscii = true;
            foreach (char c in displayName)
            {
                if (RedGoldPinyinMap.TryGetValue(c, out string pinyin))
                {
                    builder.Append(pinyin);
                    capitalizeNextAscii = true;
                }
                else if (c >= 'a' && c <= 'z')
                {
                    builder.Append(capitalizeNextAscii ? char.ToUpperInvariant(c) : c);
                    capitalizeNextAscii = false;
                }
                else if (c >= 'A' && c <= 'Z')
                {
                    builder.Append(c);
                    capitalizeNextAscii = false;
                }
                else if (c >= '0' && c <= '9')
                {
                    builder.Append(c);
                    capitalizeNextAscii = false;
                }
                else if (RedGoldIsCjk(c))
                {
                    builder.Append("X");
                    capitalizeNextAscii = true;
                }
                else
                {
                    capitalizeNextAscii = true;
                }
            }

            return builder.ToString();
        }

        private bool RedGoldIsCjk(char c)
        {
            return c >= 0x4E00 && c <= 0x9FFF;
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

        private string RedGoldNormalizeFileKey(string value)
        {
            return (value ?? "").Trim().Replace(" ", "").ToLowerInvariant();
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
            string q = (quality ?? "").Trim().ToLowerInvariant();
            if (RedGoldIsRedQuality(quality)) return RedGoldToAbsolutePath(redGoldRedOutputFolder);
            if (q.Contains("紫") || q.Contains("purple")) return RedGoldToAbsolutePath(redGoldPurpleOutputFolder);
            if (q.Contains("金") || q.Contains("gold")) return RedGoldToAbsolutePath(redGoldGoldOutputFolder);
            return "";
        }

        private bool RedGoldIsRedQuality(string quality)
        {
            string q = (quality ?? "").Trim().ToLowerInvariant();
            return q.Contains("红") || q.Contains("red");
        }

        private (int width, int height) RedGoldComputeOutputSize(int gridLong, int gridWide)
        {
            int longCount = Mathf.Max(1, gridLong);
            int wideCount = Mathf.Max(1, gridWide);

            if (longCount == wideCount && longCount >= 1 && longCount <= 4 && redGoldMaxOutputEdge > 0)
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

        private string RedGoldGetCell(List<string> row, int index)
        {
            if (index < 0 || index >= row.Count) return "";
            return row[index] ?? "";
        }

        private void RedGoldSetCell(List<string> row, int index, string value)
        {
            while (row.Count <= index) row.Add("");
            row[index] = value ?? "";
        }

        private void RedGoldWriteBackRow(RedGoldImportRow row)
        {
            List<string> tableRow = redGoldTableData.Rows[row.RowIndex];
            if (redGoldOverrideGrid)
            {
                RedGoldSetCell(tableRow, redGoldTableData.GridLongColumn, row.GridLong.ToString());
                RedGoldSetCell(tableRow, redGoldTableData.GridWideColumn, row.GridWide.ToString());
                if (redGoldTableData.GridCountColumn >= 0)
                    RedGoldSetCell(tableRow, redGoldTableData.GridCountColumn, (row.GridLong * row.GridWide).ToString());
            }
            RedGoldSetCell(tableRow, redGoldTableData.IconPathColumn, RedGoldToTablePath(row.PlannedOutputPath));
        }

        private string RedGoldToTablePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return "";

            string full = Path.GetFullPath(absolutePath).Replace('\\', '/');
            string dataPath = Application.dataPath.Replace('\\', '/');
            if (full.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                return "Assets" + full.Substring(dataPath.Length);

            return full;
        }

        private string RedGoldToAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";

            string normalized = path.Replace('\\', '/').Trim();
            if (Path.IsPathRooted(normalized))
                return normalized;

            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || normalized == "Assets")
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                return Path.Combine(projectRoot, normalized);
            }

            return Path.GetFullPath(normalized);
        }

        private string RedGoldGetDefaultOutputTablePath()
        {
            string tablePath = RedGoldToAbsolutePath(redGoldTablePath);
            if (string.IsNullOrEmpty(tablePath)) return "";
            string dir = Path.GetDirectoryName(tablePath);
            string name = Path.GetFileNameWithoutExtension(tablePath);
            string ext = Path.GetExtension(tablePath);
            return Path.Combine(dir, $"{name}_导入结果{ext}");
        }

        private string RedGoldGetDefaultOutputTableName()
        {
            string tablePath = RedGoldToAbsolutePath(redGoldTablePath);
            if (string.IsNullOrEmpty(tablePath)) return "导入结果" + RedGoldGetTableExtension();
            return Path.GetFileNameWithoutExtension(tablePath) + "_导入结果";
        }

        private string RedGoldGetTableExtension()
        {
            string ext = Path.GetExtension(redGoldTablePath);
            if (string.IsNullOrEmpty(ext)) return "csv";
            return ext.TrimStart('.');
        }

        private string RedGoldGetExistingDirectory(string path)
        {
            string absolute = RedGoldToAbsolutePath(path);
            if (string.IsNullOrEmpty(absolute)) return "";
            if (Directory.Exists(absolute)) return absolute;

            string directory = Path.GetDirectoryName(absolute);
            return !string.IsNullOrEmpty(directory) && Directory.Exists(directory) ? directory : "";
        }

        private void RedGoldPingPath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return;
            string assetPath = RedGoldToTablePath(absolutePath);
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
            redGoldRedOutputFolder = cfg.redOutputFolder;
            redGoldPurpleOutputFolder = cfg.purpleOutputFolder;
            redGoldGoldOutputFolder = cfg.goldOutputFolder;
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
            cfg.redOutputFolder = redGoldRedOutputFolder;
            cfg.purpleOutputFolder = redGoldPurpleOutputFolder;
            cfg.goldOutputFolder = redGoldGoldOutputFolder;
            cfg.overwriteTable = redGoldOverwriteTable;
            cfg.outputTablePath = redGoldOutputTablePath;
        }
    }
}
