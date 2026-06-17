namespace UIProbe
{
    internal sealed class ScreenshotModule : UIProbeModuleBase
    {
        public override string Id => "screenshot";
        public override string DisplayName => "游戏截屏";
        public override Tab Tab => Tab.Screenshot;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showScreenshot;
        public override void Draw() => Window.DrawScreenshotTab_Bridge();
    }
}
