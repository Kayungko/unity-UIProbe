namespace UIProbe
{
    /// <summary>
    /// 过渡期桥接：把现有 private 绘制方法以 internal 单行包装暴露给模块适配器，
    /// 使 16 个模块 partial 文件在 Step 1 完全不动。
    /// Step 2 随模块状态外迁逐个删除这些桥接。
    /// </summary>
    public partial class UIProbeWindow
    {
        internal void DrawSidebarButtonPublic(Tab tab, string label) => DrawSidebarButton(tab, label);

        internal void DrawPickerTab_Bridge() => DrawPickerTab();
        internal void DrawIndexerTab_Bridge() => DrawIndexerTab();
        internal void DrawRecorderTab_Bridge() => DrawRecorderTab();
        internal void DrawBrowserTab_Bridge() => DrawBrowserTab();
        internal void DrawDuplicateCheckerTab_Bridge() => DrawDuplicateCheckerTab();
        internal void DrawAssetReferencesTab_Bridge() => DrawAssetReferencesTab();
        internal void DrawNestingOverviewTab_Bridge() => DrawNestingOverviewTab();
        internal void DrawImageNormalizerTab_Bridge() => DrawImageNormalizerTab();
        internal void DrawScreenshotTab_Bridge() => DrawScreenshotTab();
        internal void DrawAdaptorTab_Bridge() => DrawAdaptorTab();
        internal void DrawAnimationAutoRepairTab_Bridge() => DrawAnimationAutoRepairTab();
        internal void DrawFilterNodeScannerTab_Bridge() => DrawFilterNodeScannerTab();
        internal void DrawResourceDetectorTab_Bridge() => DrawResourceDetectorTab();
        internal void DrawSettingsTab_Bridge() => DrawSettingsTab();
        internal void DrawAboutTab_Bridge() => DrawAboutTab();

        internal void DrawAnimationAutoRepairSidebarButton_Bridge() => DrawAnimationAutoRepairSidebarButton();
    }
}
