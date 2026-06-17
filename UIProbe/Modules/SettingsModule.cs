using System.Collections.Generic;

namespace UIProbe
{
    /// <summary>
    /// 设置模块。包含主设置页（UIProbeWindow_Settings.cs）与重名检测子标签
    /// （UIProbeWindow_DuplicateSettings.cs）两个 partial。最后迁移，是配置编辑器：
    /// 通过 ConfigService 读写同一 config 实例，其他模块从同一服务读取，无模块间直连。
    /// </summary>
    internal sealed partial class SettingsModule : UIProbeModuleBase
    {
        public override string Id => "settings";
        public override string DisplayName => "设置";
        public override Tab Tab => Tab.Settings;
        public override bool IsVisible(UIProbeConfig config) => true; // 固定可见
        public override SidebarSection Section => SidebarSection.Bottom;
        public override void Draw() => DrawSettingsTab();
        public override void Apply() => LoadSettingsData();
        public override void Collect() => CollectSettingsData();

        private readonly ConfigService _configService;
        private UIProbeConfig config => _configService?.Config;

        // duplicateSettings 实例归本模块持有（字段在 UIProbeWindow_Settings.cs），
        // 经此 internal 属性暴露给 DuplicateCheckerModule 与批量检测桥接读写同一实例。
        internal DuplicateDetectionSettings DuplicateSettings
        {
            get => duplicateSettings;
            set => duplicateSettings = value;
        }

        // 收藏夹/搜索历史归 IndexerModule 持有，经壳层 internal 桥接读取同一实例。
        private List<string> searchHistory => Window.IndexerSearchHistory;
        private List<string> bookmarks => Window.IndexerBookmarks;

        public SettingsModule(ConfigService configService)
        {
            _configService = configService;
        }
    }
}
