using System;
using System.Collections.Generic;

namespace UIProbe.Core.Services
{
    /// <summary>
    /// prefab 引用到的单个资源（Image/RawImage/Prefab/Material/Font 等）。
    /// Kind 以字符串承载枚举名，保证 JsonUtility 序列化后可读。
    /// </summary>
    [Serializable]
    public sealed class AssetRef
    {
        public string AssetPath;
        public string NodePath;
        public string AssetName;
        public string Kind;
        public string ExtraInfo;
    }

    /// <summary>索引中的单个 prefab 条目。ReferencedAssets/ComponentSummary 为只读派生字段。</summary>
    [Serializable]
    public sealed class PrefabIndexItem
    {
        public string Guid;
        public string AssetPath;
        public string Name;
        public string FolderPath;
        public List<AssetRef> ReferencedAssets = new List<AssetRef>();
        public string ComponentSummary;
    }

    /// <summary>
    /// PrefabIndex 是后续只读能力（引用追踪/重复检测/嵌套总览/过滤扫描）的单一数据源。
    /// SchemaVersion 不符的缓存直接重建不迁移。folder 视图由 Items 的 FolderPath 派生，不另存树。
    /// </summary>
    [Serializable]
    public sealed class PrefabIndex
    {
        public int SchemaVersion;
        public string BuiltAt;
        public string RootPath;
        public List<PrefabIndexItem> Items = new List<PrefabIndexItem>();
    }

    /// <summary>BuildIndex 选项。RootFolders 为 null/空表示全工程；Incremental 复用上次索引未变更项。</summary>
    public sealed class PrefabIndexBuildOptions
    {
        public string[] RootFolders;
        public bool Incremental;
    }

    /// <summary>LoadCache 结果。Found=文件存在且可解析;SchemaValid=版本与当前一致(否则调用方应重建)。</summary>
    public sealed class LoadCacheResult
    {
        public bool Found;
        public bool SchemaValid;
        public PrefabIndex Index;
    }
}
