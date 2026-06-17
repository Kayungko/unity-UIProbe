namespace UIProbe
{
    internal sealed partial class ImageNormalizerModule : UIProbeModuleBase
    {
        public override string Id => "imageNormalizer";
        public override string DisplayName => "图片规范化";
        public override Tab Tab => Tab.ImageNormalizer;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showImageNormalizer;
        public override void Draw() => DrawImageNormalizerTab();

        public override void Apply()
        {
            EnsureRedGoldUndoManager();
            ApplyImageNormalizerConfig();
        }

        public override void Collect() => CollectImageNormalizerConfig();

        private readonly ConfigService _configService;

        // 统一配置经 ConfigService 持有，以同名 shim 引用使迁移方法体不变。
        private UIProbeConfig config => _configService?.Config;
        private void Repaint() => Window.Repaint();

        public ImageNormalizerModule(ConfigService configService)
        {
            _configService = configService;
        }
    }
}
