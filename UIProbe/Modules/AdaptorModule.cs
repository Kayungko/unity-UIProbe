namespace UIProbe
{
    internal sealed partial class AdaptorModule : UIProbeModuleBase
    {
        public override string Id => "adaptor";
        public override string DisplayName => "预制体助手";
        public override Tab Tab => Tab.Adaptor;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showAdaptor;
        public override void Draw() => DrawAdaptorTab();
        public override void Apply() => ApplyHelperConfig();
        public override void Collect() => CollectHelperConfig();

        private readonly ConfigService _configService;
        private readonly NavigationService _navService;
        private UIProbeConfig config => _configService?.Config;

        public AdaptorModule(ConfigService configService, NavigationService navService)
        {
            _configService = configService;
            _navService = navService;
        }
    }
}
