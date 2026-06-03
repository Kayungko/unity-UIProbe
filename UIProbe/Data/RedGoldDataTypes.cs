using System;
using System.Collections.Generic;

namespace UIProbe
{
    /// <summary>
    /// 品质配置条目：关键字匹配、输出路径、命名模板
    /// </summary>
    [Serializable]
    internal class QualityConfigEntry
    {
        public string keyword = "";         // 匹配品质列值的关键字，如 "红"
        public string displayName = "";     // UI 显示名，如 "红色品质"
        public string outputFolder = "";    // 输出路径
        public string namingTemplate = "";  // 命名模板，如 "T_Icon_Red_{Pinyin}.png"
        public bool usePinyin = false;      // 是否使用拼音转换（旧红品质特殊行为）
    }

    internal enum ModificationStatus
    {
        New,        // 新增 - 输出文件尚未生成
        Modified,   // 已修改 - 源文件比现有输出文件更新
        Unchanged,  // 无变化 - 源文件与现有输出文件相同或更早
        Unknown     // 未知
    }

    internal class RedGoldUndoEntry
    {
        public string BackupFilePath;       // 备份文件的完整路径
        public string OriginalOutputPath;   // 原始输出文件路径（还原时恢复到此路径）
        public int TableRowIndex;           // 表格行索引
        public string OldIconPath;          // 表格中旧的图标路径值（还原时写回）
        public string OldCellGridLong;      // 旧的格数：长（还原时写回）
        public string OldCellGridWide;      // 旧的格数：宽（还原时写回）
        public string OldCellGridCount;     // 旧的总格数（还原时写回）
    }

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
        public bool UserEdited; // 用户手动编辑过行内字段

        // ▼ 新增字段：源文件信息 + 修改检测 + 撤销备份
        public string SourceFileName;           // 源文件名（含扩展名，如 "weapon_01.png"）
        public string SourceRelativePath;       // 相对于源根目录的路径（如 "v2/weapon_01.png"）
        public string SourceModifiedTime;       // 源文件最后修改时间（格式化字符串）
        public bool OutputExists;               // 目标输出文件是否已存在
        public string OutputModifiedTime;       // 输出文件最后修改时间（格式化字符串）
        public ModificationStatus ModStatus;    // 新增/已修改/无变化
        public string BackupFilePath;           // 备份文件路径（生成前设置，供撤销使用）
        public bool UseExistingOutput;          // true = 使用已有输出文件替代源文件（手动切换）
        public string OutputFileNameOverride;   // 手动覆盖的输出文件名（含扩展名，如 "my_icon.png"），用于自定义命名
    }

    /// <summary>
    /// 源文件夹中未匹配到表格行的文件信息
    /// </summary>
    internal class UnmatchedSourceInfo
    {
        public string FilePath;
        public string FileName;
        public string ModifiedTime;
        public long FileSize;
        public bool IsSelected = true;
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

        public static string GetCell(List<string> row, int index)
        {
            if (index < 0 || index >= row.Count) return "";
            return row[index] ?? "";
        }

        public static void SetCell(List<string> row, int index, string value)
        {
            while (row.Count <= index) row.Add("");
            row[index] = value ?? "";
        }
    }
}
