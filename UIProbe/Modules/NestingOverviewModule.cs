using System.Collections.Generic;

namespace UIProbe
{
    internal sealed partial class NestingOverviewModule : UIProbeModuleBase
    {
        public override string Id => "nestingOverview";
        public override string DisplayName => "嵌套总览";
        public override Tab Tab => Tab.NestingOverview;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showNestingOverview;
        public override void Draw() => DrawNestingOverviewTab();

        private readonly PrefabIndexService _indexService;

        // 共享索引经 PrefabIndexService 持有，以同名 shim 引用使迁移方法体不变。
        private List<PrefabIndexItem> allPrefabs => _indexService.AllPrefabs;
        private bool isIndexBuilt => _indexService.IsIndexBuilt;

        public NestingOverviewModule(PrefabIndexService indexService)
        {
            _indexService = indexService;
        }
    }
}
