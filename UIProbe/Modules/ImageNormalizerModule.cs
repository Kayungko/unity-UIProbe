namespace UIProbe
{
    internal sealed class ImageNormalizerModule : UIProbeModuleBase
    {
        public override string Id => "imageNormalizer";
        public override string DisplayName => "图片规范化";
        public override Tab Tab => Tab.ImageNormalizer;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showImageNormalizer;
        public override void Draw() => Window.DrawImageNormalizerTab_Bridge();
    }
}
