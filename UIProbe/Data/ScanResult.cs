using System;
using System.Collections.Generic;

namespace UIProbe
{
    // 文件夹统计
    [Serializable]
    public class FolderStatistics
    {
        public string FolderPath;
        public int TotalCount;
        public int UsedCount;
        public int UnusedCount;
        public float UsageRate;
        public long TotalSize;
        public long UnusedSize;
    }

    // 扫描结果
    [Serializable]
    public class ScanResult
    {
        public List<ResourceInfo> Resources = new List<ResourceInfo>();
        public FolderStatistics Overall = new FolderStatistics();
        public List<FolderStatistics> FolderStats = new List<FolderStatistics>();
        public long ScanTimeTicks;
        
        public DateTime ScanTime
        {
            get => new DateTime(ScanTimeTicks);
            set => ScanTimeTicks = value.Ticks;
        }
    }
}
