using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace UIProbe
{
    /// <summary>
    /// 栈式多级撤销管理器：支持最多 MaxStackSize 次生成操作的撤销
    /// 撤销栈持久化到磁盘，重启 Unity 后可恢复
    /// </summary>
    internal class RedGoldUndoManager
    {
        private const int MaxStackSize = 10;
        private const string UndoIndexFileName = "undo_index.json";

        private readonly Stack<RedGoldUndoSnapshot> undoStack = new Stack<RedGoldUndoSnapshot>();
        private readonly string undoRootDir;

        public bool HasUndo => undoStack.Count > 0;
        public string CurrentDescription => HasUndo ? undoStack.Peek().Description : "";
        public int EntryCount => HasUndo ? undoStack.Peek().Entries.Count : 0;
        public int StackDepth => undoStack.Count;

        public RedGoldUndoManager()
        {
            undoRootDir = Path.Combine(UIProbeStorage.GetMainFolderPath(), "RedGoldUndo");
        }

        /// <summary>
        /// 扫描磁盘上的已持久化撤销快照，重建撤销栈
        /// </summary>
        public void ScanForOrphanedBackups()
        {
            if (!Directory.Exists(undoRootDir)) return;

            var found = new List<(DateTime timestamp, RedGoldUndoSnapshot snapshot)>();

            foreach (string dir in Directory.GetDirectories(undoRootDir))
            {
                string indexPath = Path.Combine(dir, UndoIndexFileName);
                if (!File.Exists(indexPath)) continue; // 不完整的目录，跳过

                try
                {
                    string json = File.ReadAllText(indexPath);
                    var indexFile = JsonUtility.FromJson<UndoIndexFile>(json);
                    if (indexFile == null || indexFile.entries == null || indexFile.entries.Length == 0)
                    {
                        // 损坏的 index → 清理目录
                        TryDeleteDirectory(dir);
                        continue;
                    }

                    var entries = new List<RedGoldUndoEntry>();
                    foreach (var ie in indexFile.entries)
                    {
                        string backupPath = Path.Combine(dir, ie.backupFile);
                        entries.Add(new RedGoldUndoEntry
                        {
                            BackupFilePath = backupPath,
                            OriginalOutputPath = ie.originalPath,
                            TableRowIndex = ie.tableRowIndex,
                            OldIconPath = ie.oldIconPath,
                            OldCellGridLong = ie.oldCellGridLong,
                            OldCellGridWide = ie.oldCellGridWide,
                            OldCellGridCount = ie.oldCellGridCount,
                        });
                    }

                    var snapshot = new RedGoldUndoSnapshot
                    {
                        Entries = entries,
                        TableSnapshotPath = indexFile.tableSnapshotPath,
                        Description = indexFile.description,
                        BackupDirectory = dir,
                    };

                    // 从目录名解析时间戳
                    string dirName = Path.GetFileName(dir);
                    if (DateTime.TryParseExact(dirName, "yyyyMMdd_HHmmss", null,
                        System.Globalization.DateTimeStyles.None, out DateTime ts))
                    {
                        found.Add((ts, snapshot));
                    }
                    else
                    {
                        found.Add((DateTime.MinValue, snapshot));
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[UIProbe] 读取撤销索引失败，跳过: {dir} ({ex.Message})");
                    TryDeleteDirectory(dir);
                }
            }

            // 按时间戳升序入栈（最早的先入，最晚的在栈顶）
            found.Sort((a, b) => a.timestamp.CompareTo(b.timestamp));
            undoStack.Clear();
            foreach (var (_, snapshot) in found)
            {
                undoStack.Push(snapshot);
            }

            // 超出上限的清理最旧的
            while (undoStack.Count > MaxStackSize)
            {
                var arr = undoStack.ToArray();
                var oldest = arr[arr.Length - 1];
                undoStack.Clear();
                for (int i = arr.Length - 2; i >= 0; i--)
                    undoStack.Push(arr[i]);
                TryDeleteDirectory(oldest.BackupDirectory);
            }
        }

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

            // 持久化到磁盘
            WriteIndexToDisk(snapshot);

            // 超过上限时弹出最旧的快照并清理备份目录
            while (undoStack.Count > MaxStackSize)
            {
                var arr = undoStack.ToArray();
                var oldest = arr[arr.Length - 1];
                undoStack.Clear();
                for (int i = arr.Length - 2; i >= 0; i--)
                    undoStack.Push(arr[i]);
                TryDeleteDirectory(oldest.BackupDirectory);
            }
        }

        /// <summary>
        /// 撤销最近一次生成操作：恢复文件和表格数据
        /// </summary>
        public UndoResult TryUndo(RedGoldTableData tableData, bool overrideGrid)
        {
            if (!HasUndo)
                return new UndoResult { Success = false, Error = "没有可撤销的操作。" };

            var snapshot = undoStack.Pop();
            int restoredFileCount = 0;
            int restoredTableCount = 0;

            try
            {
                // 先删 index（标记"正在撤销"），防止崩溃后残留
                string indexPath = Path.Combine(snapshot.BackupDirectory, UndoIndexFileName);
                TryDeleteFile(indexPath);

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
                TryDeleteDirectory(snapshot.BackupDirectory);

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
        /// 清空撤销栈并清理所有备份目录
        /// </summary>
        public void Clear()
        {
            foreach (var snapshot in undoStack)
            {
                TryDeleteDirectory(snapshot.BackupDirectory);
            }
            undoStack.Clear();
        }

        private void WriteIndexToDisk(RedGoldUndoSnapshot snapshot)
        {
            if (string.IsNullOrEmpty(snapshot.BackupDirectory) || !Directory.Exists(snapshot.BackupDirectory))
                return;

            var indexFile = new UndoIndexFile
            {
                version = 1,
                description = snapshot.Description,
                tableSnapshotPath = snapshot.TableSnapshotPath,
                entries = snapshot.Entries.Select(e => new UndoIndexEntry
                {
                    backupFile = Path.GetFileName(e.BackupFilePath),
                    originalPath = e.OriginalOutputPath,
                    tableRowIndex = e.TableRowIndex,
                    oldIconPath = e.OldIconPath,
                    oldCellGridLong = e.OldCellGridLong,
                    oldCellGridWide = e.OldCellGridWide,
                    oldCellGridCount = e.OldCellGridCount,
                }).ToArray()
            };

            string indexPath = Path.Combine(snapshot.BackupDirectory, UndoIndexFileName);
            string json = JsonUtility.ToJson(indexFile, true);
            File.WriteAllText(indexPath, json);
        }

        private static void TryDeleteDirectory(string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                try { Directory.Delete(path, recursive: true); } catch { }
            }
        }

        private static void TryDeleteFile(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try { File.Delete(path); } catch { }
            }
        }

        // ---- 序列化辅助类 ----

        [Serializable]
        private class UndoIndexFile
        {
            public int version = 1;
            public string description;
            public string tableSnapshotPath;
            public UndoIndexEntry[] entries;
        }

        [Serializable]
        private class UndoIndexEntry
        {
            public string backupFile;
            public string originalPath;
            public int tableRowIndex;
            public string oldIconPath;
            public string oldCellGridLong;
            public string oldCellGridWide;
            public string oldCellGridCount;
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
