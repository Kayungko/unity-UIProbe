namespace UIProbe
{
    internal sealed partial class BrowserModule : UIProbeModuleBase
    {
        public override string Id => "browser";
        public override string DisplayName => "历史浏览";
        public override Tab Tab => Tab.Browser;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showBrowser;
        public override void Draw() => DrawBrowserTab();
        public override void Apply() => RefreshSessionList();
    }
}
