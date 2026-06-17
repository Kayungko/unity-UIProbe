namespace UIProbe
{
    internal sealed class RichTextGeneratorModule : UIProbeModuleBase
    {
        public override string Id => "richTextGenerator";
        public override string DisplayName => "富文本生成";
        public override Tab Tab => Tab.RichTextGenerator;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showRichTextGenerator;
        public override void Draw() => Window.DrawRichTextGeneratorTab_Bridge();
    }
}
