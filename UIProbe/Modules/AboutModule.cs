namespace UIProbe
{
    internal sealed class AboutModule : UIProbeModuleBase
    {
        public override string Id => "about";
        public override string DisplayName => "关于";
        public override Tab Tab => Tab.About;
        public override bool IsVisible(UIProbeConfig config) => true; // 固定可见
        public override SidebarSection Section => SidebarSection.Bottom;
        public override void Draw() => Window.DrawAboutTab_Bridge();
    }
}
