namespace UIProbe
{
    internal sealed class RecorderModule : UIProbeModuleBase
    {
        public override string Id => "recorder";
        public override string DisplayName => "界面记录";
        public override Tab Tab => Tab.Recorder;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showRecorder;
        public override void Draw() => Window.DrawRecorderTab_Bridge();
    }
}
