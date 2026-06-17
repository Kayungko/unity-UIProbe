using System.Collections.Generic;

namespace UIProbe
{
    internal sealed partial class DuplicateCheckerModule : UIProbeModuleBase
    {
        public override string Id => "duplicateChecker";
        public override string DisplayName => "预制体综合检测";
        public override Tab Tab => Tab.DuplicateChecker;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showDuplicateChecker;
        public override void Draw() => DrawDuplicateCheckerTab();

        private readonly ConfigService _configService;
        private readonly PrefabIndexService _indexService;

        // 共享状态经服务持有，模块内以同名 shim 引用，使迁移的方法体保持不变。
        private UIProbeConfig config => _configService?.Config;
        private List<PrefabIndexItem> allPrefabs => _indexService.AllPrefabs;

        // duplicateSettings 实例仍归 Settings（最后迁移），经壳层桥接读写同一实例。
        private DuplicateDetectionSettings duplicateSettings
        {
            get => Window.DuplicateSettings;
            set => Window.DuplicateSettings = value;
        }

        private void Repaint() => Window.Repaint();

        public DuplicateCheckerModule(ConfigService configService, PrefabIndexService indexService)
        {
            _configService = configService;
            _indexService = indexService;
        }

        /// <summary>供 IndexerBridge 批量检测后加载结果到本模块（带 JSON 路径）。</summary>
        internal void LoadBatchResult(BatchDuplicateResult result, string jsonPath)
        {
            LoadBatchResultIntoChecker(result);
            currentBatchResultPath = jsonPath;
        }
    }
}
