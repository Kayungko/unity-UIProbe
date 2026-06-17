using System.Collections.Generic;

namespace UIProbe
{
    internal sealed partial class AssetReferencesModule : UIProbeModuleBase
    {
        public override string Id => "assetReferences";
        public override string DisplayName => "资源引用";
        public override Tab Tab => Tab.AssetReferences;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showAssetReferences;
        public override void Draw() => DrawAssetReferencesTab();

        private readonly PrefabIndexService _indexService;

        // 共享索引经 PrefabIndexService 持有，以同名 shim 引用使迁移方法体不变。
        private List<PrefabIndexItem> allPrefabs => _indexService.AllPrefabs;
        private bool isIndexBuilt => _indexService.IsIndexBuilt;
        private int prefabIndexVersion => _indexService.PrefabIndexVersion;

        // 资源类型图标归 Indexer，经壳层桥接复用同一映射。
        private string GetAssetTypeIcon(AssetReferenceType type) => Window.GetAssetTypeIcon_Bridge(type);

        public AssetReferencesModule(PrefabIndexService indexService)
        {
            _indexService = indexService;
        }
    }
}
