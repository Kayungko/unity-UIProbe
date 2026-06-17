namespace UIProbe
{
    internal sealed class PickerModule : UIProbeModuleBase
    {
        public override string Id => "picker";
        public override string DisplayName => "运行时拾取";
        public override Tab Tab => Tab.Picker;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showPicker;
        public override void Draw() => Window.DrawPickerTab_Bridge();
    }
}
