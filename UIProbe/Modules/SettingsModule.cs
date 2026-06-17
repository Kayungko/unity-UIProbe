namespace UIProbe
{
    internal sealed class SettingsModule : UIProbeModuleBase
    {
        public override string Id => "settings";
        public override string DisplayName => "设置";
        public override Tab Tab => Tab.Settings;
        public override bool IsVisible(UIProbeConfig config) => true; // 固定可见
        public override SidebarSection Section => SidebarSection.Bottom;
        public override void Draw() => Window.DrawSettingsTab_Bridge();
    }
}
