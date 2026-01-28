using System;
using System.Collections.Generic;

namespace UIProbe
{
    /// <summary>
    /// 资源引用类型
    /// </summary>
    public enum AssetReferenceType
    {
        Image,      // Sprite from Image component
        RawImage,   // Texture from RawImage component
        Prefab,     // Nested prefab reference
        Material,   // Material reference
        Font        // Font reference
    }

    /// <summary>
    /// 可序列化的资源引用
    /// </summary>
    [Serializable]
    public class SerializableAssetReference
    {
        public string AssetPath;        // 资源路径
        public string NodePath;         // 使用该资源的节点路径
        public string AssetName;        // 资源文件名
        public int Type;                // 引用类型 (AssetReferenceType as int for serialization)
        public string ExtraInfo;        // 额外信息（如组件类型、预制体GUID等）
    }

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
        public List<SerializableAssetReference> AssetReferences = new List<SerializableAssetReference>();
    }

    [Serializable]
    public class SerializableFolderNode
    {
        public string Name;
        public string FullPath;
        public string ParentPath; // Added for flat hierarchy reconstruction
        public bool IsExpanded;
        public int TotalPrefabCount;
        // Flattened: SubFolders removed, hierarchy maintained via ParentPath
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
        public List<SerializableFolderNode> AllFolders = new List<SerializableFolderNode>(); // Flattened list
    }
}
