namespace UIProbe
{
    internal sealed class IndexerModule : UIProbeModuleBase
    {
        public override string Id => "indexer";
        public override string DisplayName => "预制体索引";
        public override Tab Tab => Tab.Indexer;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showIndexer;
        public override void Draw() => Window.DrawIndexerTab_Bridge();
    }
}
