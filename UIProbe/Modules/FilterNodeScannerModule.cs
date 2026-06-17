namespace UIProbe
{
    internal sealed class FilterNodeScannerModule : UIProbeModuleBase
    {
        public override string Id => "filterNodeScanner";
        public override string DisplayName => "Filter排查";
        public override Tab Tab => Tab.FilterNodeScanner;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showFilterNodeScanner;
        public override void Draw() => Window.DrawFilterNodeScannerTab_Bridge();
    }
}
