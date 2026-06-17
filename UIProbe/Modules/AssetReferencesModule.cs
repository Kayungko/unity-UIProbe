namespace UIProbe
{
    internal sealed class AssetReferencesModule : UIProbeModuleBase
    {
        public override string Id => "assetReferences";
        public override string DisplayName => "资源引用";
        public override Tab Tab => Tab.AssetReferences;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showAssetReferences;
        public override void Draw() => Window.DrawAssetReferencesTab_Bridge();
    }
}
