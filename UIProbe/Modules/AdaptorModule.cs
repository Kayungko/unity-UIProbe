namespace UIProbe
{
    internal sealed class AdaptorModule : UIProbeModuleBase
    {
        public override string Id => "adaptor";
        public override string DisplayName => "预制体助手";
        public override Tab Tab => Tab.Adaptor;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showAdaptor;
        public override void Draw() => Window.DrawAdaptorTab_Bridge();
    }
}
