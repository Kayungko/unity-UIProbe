using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace UIProbe
{
    /// <summary>
    /// 栈式多级撤销管理器：支持最多 MaxStackSize 次生成操作的撤销
    /// </summary>
    internal class RedGoldUndoManager
    {
        private const int MaxStackSize = 10;

        private readonly Stack<RedGoldUndoSnapshot> undoStack = new Stack<RedGoldUndoSnapshot>();

        public bool HasUndo => undoStack.Count > 0;
        public string CurrentDescription => HasUndo ? undoStack.Peek().Description : "";
        public int EntryCount => HasUndo ? undoStack.Peek().Entries.Count : 0;
        public int StackDepth => undoStack.Count;

        /// <summary>
        /// 压入一次生成操作的快照。超过栈容量时自动清理最旧的备份目录。
        /// </summary>
        public void PushSnapshot(List<RedGoldUndoEntry> entries, string tableSnapshotPath, string description)
        {
            var validEntries = entries.Where(e => !string.IsNullOrEmpty(e.BackupFilePath)).ToList();
            if (validEntries.Count == 0) return;

            var snapshot = new RedGoldUndoSnapshot
            {
                Entries = validEntries,
                TableSnapshotPath = tableSnapshotPath,
                Description = description,
                BackupDirectory = validEntries.Count > 0
                    ? Path.GetDirectoryName(validEntries[0].BackupFilePath)
                    : ""
            };

            undoStack.Push(snapshot);

            // 超过上限时弹出最旧的快照并清理备份目录
            while (undoStack.Count > MaxStackSize)
            {
                var arr = undoStack.ToArray();
                // Stack.ToArray 返回的顺序是 top→bottom，最后一个是最旧的
                var oldest = arr[arr.Length - 1];

                // 重建栈（去掉最旧的）
                undoStack.Clear();
                for (int i = arr.Length - 2; i >= 0; i--)
                    undoStack.Push(arr[i]);

                // 清理最旧快照的备份目录
                if (!string.IsNullOrEmpty(oldest.BackupDirectory) && Directory.Exists(oldest.BackupDirectory))
                {
                    try { Directory.Delete(oldest.BackupDirectory, recursive: true); } catch { }
                }
            }
        }

        /// <summary>
        /// 撤销最近一次生成操作：恢复文件和表格数据
        /// </summary>
        /// <returns>撤销结果（成功时包含恢复计数），失败时 Success = false</returns>
        public UndoResult TryUndo(RedGoldTableData tableData, bool overrideGrid)
        {
            if (!HasUndo)
                return new UndoResult { Success = false, Error = "没有可撤销的操作。" };

            var snapshot = undoStack.Pop();
            int restoredFileCount = 0;
            int restoredTableCount = 0;

            try
            {
                // 1. 恢复文件
                foreach (var entry in snapshot.Entries)
                {
                    if (!string.IsNullOrEmpty(entry.BackupFilePath) && File.Exists(entry.BackupFilePath)
                        && !string.IsNullOrEmpty(entry.OriginalOutputPath))
                    {
                        File.Copy(entry.BackupFilePath, entry.OriginalOutputPath, overwrite: true);
                        restoredFileCount++;
                    }
                }

                // 2. 恢复表格数据
                if (!string.IsNullOrEmpty(snapshot.TableSnapshotPath) && File.Exists(snapshot.TableSnapshotPath)
                    && tableData != null)
                {
                    foreach (var entry in snapshot.Entries)
                    {
                        if (entry.TableRowIndex < 0 || entry.TableRowIndex >= tableData.Rows.Count) continue;
                        var tRow = tableData.Rows[entry.TableRowIndex];
                        RedGoldTableData.SetCell(tRow, tableData.IconPathColumn, entry.OldIconPath);
                        if (overrideGrid)
                        {
                            RedGoldTableData.SetCell(tRow, tableData.GridLongColumn, entry.OldCellGridLong);
                            RedGoldTableData.SetCell(tRow, tableData.GridWideColumn, entry.OldCellGridWide);
                            if (tableData.GridCountColumn >= 0)
                                RedGoldTableData.SetCell(tRow, tableData.GridCountColumn, entry.OldCellGridCount);
                        }
                        restoredTableCount++;
                    }

                    DelimitedFileParser.WriteTable(snapshot.TableSnapshotPath, tableData);
                }

                // 3. 刷新编辑器
                AssetDatabase.Refresh();

                // 4. 删除备份目录
                if (!string.IsNullOrEmpty(snapshot.BackupDirectory) && Directory.Exists(snapshot.BackupDirectory))
                {
                    try { Directory.Delete(snapshot.BackupDirectory, recursive: true); } catch { }
                }

                return new UndoResult
                {
                    Success = true,
                    RestoredFileCount = restoredFileCount,
                    RestoredTableCount = restoredTableCount
                };
            }
            catch (Exception e)
            {
                return new UndoResult
                {
                    Success = false,
                    RestoredFileCount = restoredFileCount,
                    RestoredTableCount = restoredTableCount,
                    Error = $"撤销过程中发生错误：\n{e.Message}\n\n部分文件可能已恢复，部分可能未恢复。请手动检查。"
                };
            }
        }

        /// <summary>
        /// 清空撤销栈（不清理备份目录）
        /// </summary>
        public void Clear()
        {
            undoStack.Clear();
        }

        internal class UndoResult
        {
            public bool Success;
            public int RestoredFileCount;
            public int RestoredTableCount;
            public string Error;
        }
    }

    /// <summary>
    /// 单次生成操作的撤销快照
    /// </summary>
    internal class RedGoldUndoSnapshot
    {
        public List<RedGoldUndoEntry> Entries;
        public string TableSnapshotPath;
        public string Description;
        public string BackupDirectory;
    }
}
