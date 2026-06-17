namespace UIProbe
{
    internal sealed class ResourceDetectorModule : UIProbeModuleBase
    {
        public override string Id => "resourceDetector";
        public override string DisplayName => "资源使用检测";
        public override Tab Tab => Tab.ResourceDetector;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showResourceDetector;
        public override void Draw() => Window.DrawResourceDetectorTab_Bridge();
    }
}
