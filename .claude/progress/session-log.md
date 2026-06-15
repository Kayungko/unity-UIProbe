# UIProbe 工作台化与自有 MCP 重构 会话日志

> 由 scaffold_gen 生成。写入此文件请通过 `@write-session-log` skill，不要直接追加。硬限 100 行。

## 待办 (Pending)

<!-- 未完成 / 阻塞 / 下一步。永不压缩。 -->

- [ ] 编译/测试基建(机器相关,暂不入库):临时宿主 `E:\uiprobe-compile-host`(junction 挂 UIProbe→Assets/UIProbe;manifest 含 com.unity.test-framework 1.1.33 + com.unity.testtools.codecoverage 1.2.6)。编译:`Unity.exe -batchmode -quit -nographics -projectPath <host> -logFile <log>`;测试:加 `-runTests -testPlatform EditMode -testResults <xml>`(去掉 -quit);覆盖率:再加 `-enableCodeCoverage -coverageResultsPath <dir> -coverageOptions "generateAdditionalMetrics;generateHtmlReport;assemblyFilters:+<asm>" -debugCodeOptimization`
- [ ] Next: M2/T2-1 — 抽离 PrefabIndexService(/plan 已定稿,待 /build)。已决:① 给 `UIProbe/Data/` 建运行时 asmdef `UIProbe.Data`(引用 Contract),新数据类型(PrefabIndex/PrefabIndexItem/PrefabIndexBuildOptions)落 Data/,同时解锁 T2-2/T2-3;② 同步 + IProgress<float> 形态,jobId/Dispatcher 留 M3;③ 不包装 IUIProbeTool/不进 Registry(留 M4);④ 不复用 ResourceScanner(直调静态 API,在 IAssetGateway 接缝上写薄 BuildIndex)。接口:BuildIndex/LoadCache/SaveCache/Search/GetPrefabDetail(缺失→TOOL_NOT_FOUND)。TDD spec 已落可执行用例

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

#### T2-1: 抽离 PrefabIndexService (NOT_STARTED — /plan 已定稿)
- plan: 2026-06-16 — 范围/架构/接口/验收→验证映射已锁定(详见 Pending → Next)。prefab-index.spec.md 已从模板替换为可执行用例(NUnit:BuildIndex/LoadCache/SaveCache/Search/GetPrefabDetail + golden 回归)。

#### T2-2: 抽离 AssetReferenceService (NOT_STARTED) — 依赖 T2-1
#### T2-3: 抽离 UICheckService (NOT_STARTED) — 依赖 T2-1
