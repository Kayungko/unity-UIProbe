namespace UIProbe.Infrastructure.UnityAdapters
{
    /// <summary>
    /// IAssetGateway.CollectReferences 的中性返回记录。
    /// 落在 Infrastructure(而非 Core.Services 的 AssetRef),避免接口反向依赖上层程序集成环;
    /// 由 PrefabIndexService 映射为 Core.Services 的 AssetRef。
    /// Kind 取 "Image"/"RawImage"/"Prefab"/"Material"/"Font"(对应遗留 AssetReferenceType)。
    /// </summary>
    public sealed class AssetReferenceRecord
    {
        public string AssetPath;
        public string Guid;
        public string NodePath;
        public string AssetName;
        public string Kind;
        public string ExtraInfo;
    }
}
