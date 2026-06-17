namespace UIProbe
{
    internal sealed class NestingOverviewModule : UIProbeModuleBase
    {
        public override string Id => "nestingOverview";
        public override string DisplayName => "嵌套总览";
        public override Tab Tab => Tab.NestingOverview;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showNestingOverview;
        public override void Draw() => Window.DrawNestingOverviewTab_Bridge();
    }
}
