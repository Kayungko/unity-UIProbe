namespace UIProbe
{
    internal sealed partial class FilterNodeScannerModule : UIProbeModuleBase
    {
        public override string Id => "filterNodeScanner";
        public override string DisplayName => "Filter排查";
        public override Tab Tab => Tab.FilterNodeScanner;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showFilterNodeScanner;
        public override void Draw() => DrawFilterNodeScannerTab();

        private readonly PrefabIndexService _indexService;

        // 共享索引根路径归 PrefabIndexService，FilterNodeScanner 只读取作为默认扫描根。
        private string indexRootPath => _indexService.IndexRootPath;
        private void Repaint() => Window.Repaint();

        public FilterNodeScannerModule(PrefabIndexService indexService)
        {
            _indexService = indexService;
        }
    }
}
