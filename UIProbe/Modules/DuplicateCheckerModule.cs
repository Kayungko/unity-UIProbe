namespace UIProbe
{
    internal sealed class DuplicateCheckerModule : UIProbeModuleBase
    {
        public override string Id => "duplicateChecker";
        public override string DisplayName => "预制体综合检测";
        public override Tab Tab => Tab.DuplicateChecker;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showDuplicateChecker;
        public override void Draw() => Window.DrawDuplicateCheckerTab_Bridge();
    }
}
