namespace UIProbe
{
    internal sealed partial class RecorderModule : UIProbeModuleBase
    {
        public override string Id => "recorder";
        public override string DisplayName => "界面记录";
        public override Tab Tab => Tab.Recorder;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showRecorder;
        public override void Draw() => DrawRecorderTab();
        public override void OnDestroy() => RecorderOnDestroy();

        private readonly NavigationService _navService;
        private void Repaint() => Window.Repaint();

        public RecorderModule(NavigationService navService)
        {
            _navService = navService;
        }
    }
}
