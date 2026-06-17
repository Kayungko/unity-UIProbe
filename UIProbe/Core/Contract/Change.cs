using System;

namespace UIProbe.Core.Contract
{
    /// <summary>
    /// 单条变更描述。Preview 填 PlannedChanges，Execute 填 AppliedChanges。
    /// 字段以 Docs/ToolContract.md §10 为单一来源。
    /// </summary>
    [Serializable]
    public sealed class Change
    {
        public ChangeType Type;       // create | update | delete | rename | move | import | export
        public string AssetPath;
        public string NodePath;
        public string OldValue;
        public string NewValue;
        public UndoCapability Undo;
        public string BackupPath;
    }

    public enum ChangeType
    {
        Create,
        Update,
        Delete,
        Rename,
        Move,
        Import,
        Export
    }

    /// <summary>
    /// 撤销能力分级，不假设单一 backupPath 通吃。
    /// prefab 改名→UnityUndo；图片规范化覆盖→FileBackup；RedGold 导入→MultiLevelStack。
    /// </summary>
    public enum UndoCapability
    {
        None,            // 不可撤销
        UnityUndo,       // 走 Unity Undo 栈
        FileBackup,      // 文件级备份还原
        MultiLevelStack  // 多级栈 + 表格回滚
    }
}
