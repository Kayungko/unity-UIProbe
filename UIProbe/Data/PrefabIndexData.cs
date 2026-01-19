using System;
using System.Collections.Generic;

namespace UIProbe
{
    /// <summary>
    /// 可序列化的预制体索引项
    /// </summary>
    [Serializable]
    public class SerializablePrefabIndexItem
    {
        public string Name;
        public string Path;
        public string Guid;
        public string FolderPath;
    }

    /// <summary>
    /// 可序列化的文件夹节点
    /// </summary>
    [Serializable]
    public class SerializableFolderNode
    {
        public string Name;
        public string FullPath;
        public bool IsExpanded;
        public int TotalPrefabCount;
        public List<SerializableFolderNode> SubFolders = new List<SerializableFolderNode>();
        public List<SerializablePrefabIndexItem> Prefabs = new List<SerializablePrefabIndexItem>();
    }

    /// <summary>
    /// 预制体索引缓存数据
    /// </summary>
    [Serializable]
    public class PrefabIndexCache
    {
        public string IndexRootPath;
        public string LastUpdateTime;
        public int TotalPrefabCount;
        public List<SerializablePrefabIndexItem> AllPrefabs = new List<SerializablePrefabIndexItem>();
        public List<SerializableFolderNode> RootFolders = new List<SerializableFolderNode>();
    }
}
