# UIProbe 工作台化与自有 MCP 重构 会话日志

> 由 scaffold_gen 生成。写入此文件请通过 `@write-session-log` skill，不要直接追加。硬限 100 行。

## 待办 (Pending)

<!-- 未完成 / 阻塞 / 下一步。永不压缩。 -->

- [ ] 编译/测试基建(机器相关,暂不入库):临时宿主 `E:\uiprobe-compile-host`(junction 挂 UIProbe→Assets/UIProbe;manifest 含 com.unity.test-framework 1.1.33 + com.unity.testtools.codecoverage 1.2.6)。编译:`Unity.exe -batchmode -quit -nographics -projectPath <host> -logFile <log>`;测试:加 `-runTests -testPlatform EditMode -testResults <xml>`(去掉 -quit);覆盖率:再加 `-enableCodeCoverage -coverageResultsPath <dir> -coverageOptions "generateAdditionalMetrics;generateHtmlReport;assemblyFilters:+<asm>" -debugCodeOptimization`
- [ ] Next: 进 M3(Unity Bridge:HTTP loopback + 主线程 Dispatcher + Domain Reload 恢复,T3-1/T3-2/T3-3),从 /plan 起步。

## 归档 (Archive)

### M1 Service 化底座 (DONE — 2026-06-16)

- 5/5 任务 DONE;coverage gate **passed 83.8%**(目标 80%,范围 +UIProbe.Core.Services;Contract=DTO、Infrastructure=接口不计入)。剩余未覆盖 21 行为 M5 写阶段桩(OperationTicket/Preview)。
- 关键决策:① ToolContract 冻结为单一来源,Contract 程序集 noEngineReferences 纯托管;② Adapter 接缝(IAssetGateway/IFileSystem/IEditorPrefs)经工具/Service ctor 注入而非 ToolContext,保 Contract 纯净;③ 因 Contract 容不下 UnityEngine.Object,新建 UIProbe.Infrastructure 承载 Adapter 接口;④ ToolRegistry 落 Core/Services(与 CLAUDE.md 声明的 Core/Tools 偏差,以任务 write-path 为准);⑤ 黄金样本回归机制 = 磁盘基线 + 内存假体输入,三格式(text/csv/json)逐行 diff。
- 5 个程序集(分层):Contract → Infrastructure → Core.Services → Editor(遗留+生产 Adapter 实现) → Tests.Editor。
- 结转:无阻塞。jobId/写两阶段为占位,真实落地在 M3/M5。

## 近期活动 (Recent Activity)

_尚无快速任务或调试记录。_

## 当前里程碑 (Current Milestone)

### M2 只读 Service 抽离:PrefabIndex + AssetReference + UICheck(经接缝 + 黄金样本回归)

#### T2-1: 抽离 PrefabIndexService (DONE — 2026-06-16)
- plan: 范围/架构/接口/验收→验证映射已锁定;prefab-index.spec.md 替换为可执行用例。
- build: PrefabIndexService 经 IAssetGateway/IFileSystem/IEditorPrefs 接缝注入,提供 BuildIndex(增量+IProgress)/LoadCache/SaveCache/Search/GetPrefabDetail(缺失→TOOL_NOT_FOUND)。RED 36总/21过/15失(干净);GREEN 两跑 36/36;三格式黄金样本 diff 全绿;结构 gate high=0。
- 偏差:新数据类型(PrefabIndex/Item/AssetRef/BuildOptions/LoadCacheResult)落 `Core/Services/PrefabIndexData.cs` 而非计划的 `Data/`——`UIProbe/Data/` 整体编译进 Editor-only 程序集(9/29 文件引用 UnityEditor),Core.Services(全平台)无法引用。未建第 6 个程序集(scope-discipline)。FolderTree 弃用(由 Items.FolderPath 派生,YAGNI)。Window 改造/jobId/Registry 包装按 /plan 推后 M3/M4。

#### T2-2: 抽离 AssetReferenceService (DONE — 2026-06-16)
- build: AssetReferenceService 严格从 PrefabIndexService.Current 派生(AND-匹配 AssetPath/AssetName/Guid/SpriteName/ReferenceType;无维度→INVALID_PARAMS;未构建→ExecutionFailed;无命中→Success+空列表);ExportCsv 经 IFileSystem 写受控目录返回 reportPath(失败→IO_ERROR)。RED 8/8 干净失败(NotImplementedException);GREEN 两跑 44/44;golden CSV diff 全绿;gate high=0。
- 完整路径偏差(跨 3 模块):补 unity-adapters 引用采集接缝——`IAssetGateway.CollectReferences` + 中立 DTO `AssetReferenceRecord` 定义在 Infrastructure(避 Infrastructure→Core.Services 循环),生产实现移植自遗留 `UIProbeWindow_Indexer` 的 Image/RawImage/Material/Prefab 采集;prefab-index 加 `AssetRef.Guid` 字段 + `PrefabIndexService.Current` 访问器 + `BuildIndex` 填充 `ReferencedAssets`。旧 prefab_index golden 不变(测试 prefab 无预置引用,RefCount 恒 0)。Window 改造/jobId/分页推后 M3/M4。
#### T2-3: 抽离 UICheckService (DONE — 2026-06-17)
- build: UICheckService 经 PrefabIndexService.Current 取目标,经新增节点检视接缝 `IAssetGateway.InspectPrefab`(中立 `PrefabNodeRecord` 置于 Infrastructure 避循环)在 Core.Services 跑 7 条规则(duplicate-name/missing-sprite/missing-font/unnecessary-raycast-target/bad-naming + opt-in empty-text + 关键字驱动 filter-node),统一产契约 `Issue`(单一来源)序列化为可读报告 DTO。索引未构建→ExecutionFailed;无问题→Success+空 Issues+非空 Summary。RED 54/44过/10失(干净 NotImplementedException);GREEN 两跑 54/54;golden JSON 录制→diff 全绿;gate high=0。
- 偏差:`Data/UIProbeChecker.cs` 不动(`UIProbe/Data/` 编译进 Editor-only 程序集,Core.Services 够不到 → 类型落 `Core/Services/UICheckService.cs`,同 T2-1);省 `MissingCanvasGroupRule`(不在 6 类内)与报告 `RanAt`(无时钟接缝,YAGNI);生产 `UnityAssetGateway.InspectPrefab` 遍历 prefab 树(`IsInteractable=Selectable||ScrollRect` 适配层判定)。Window 改造(DuplicateChecker/FilterNodeScanner 接统一 Service)推后 M3/M4。

#### M2 coverage gate (passed — 2026-06-17)
- 范围 +UIProbe.Core.Services:**line 88.4%**(376/425,目标 80%),method 94.4%(51/54),54/54 EditMode 全过。PrefabIndexService 93.6% / AssetReferenceService 87.1% / UICheckService 89.7% / ToolRegistry 80.6% / ToolRunnerBase 96.7%;未覆盖余量集中在 OperationTicket(写阶段占位 0%)与各 Service 错误分支。**M2 全部 DONE,下一步 M3。**
