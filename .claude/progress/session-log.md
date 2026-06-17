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

### M2 只读 Service 抽离 (DONE — 2026-06-17)

- 3/3 任务 DONE;coverage gate **passed 88.4%**(376/425,范围 +UIProbe.Core.Services,目标 80%),method 94.4%,54/54 EditMode 全过。PrefabIndexService 93.6% / AssetReferenceService 87.1% / UICheckService 89.7%。
- T2-1 PrefabIndexService(经 IAssetGateway/IFileSystem/IEditorPrefs 接缝,BuildIndex/LoadCache/Search/GetPrefabDetail;新数据类型落 `Core/Services/PrefabIndexData.cs` 因 `Data/` 编译进 Editor-only)。
- T2-2 AssetReferenceService(严格从 PrefabIndexService.Current 派生 AND-匹配;补 `IAssetGateway.CollectReferences` + 中立 DTO `AssetReferenceRecord`@Infrastructure;ExportCsv 经 IFileSystem)。
- T2-3 UICheckService(7 规则统一产契约 `Issue`→可读报告 DTO;补 `IAssetGateway.InspectPrefab` + 中立 `PrefabNodeRecord`@Infrastructure;`IsInteractable` 适配层判定)。三格式/CSV/JSON 黄金样本全绿。
- 共性偏差:UI 组件检测数据活在 Editor prefab 树,经中立 DTO 接缝从 Core.Services 跨到 Editor;Window 改造统一推后 M3/M4。

## 近期活动 (Recent Activity)

_尚无快速任务或调试记录。_

## 当前里程碑 (Current Milestone)

### M3 Unity Bridge:HTTP loopback + 主线程 Dispatcher + Domain Reload 恢复

#### T3-1: MainThreadDispatcher (DONE — 2026-06-17)
- build: MainThreadDispatcher 落 `UIProbe/Editor/Infrastructure/Bridge`。`Enqueue<T>(Func<CancellationToken,T>,kind,timeout)` 后台入并发队列返回可 await Task;`Pump()` 主线程 drain。超时经 `Task.WhenAny(tcs,Task.Delay)` 即便不 Pump 也触发→抛 `MainThreadTimeoutException(MAIN_THREAD_TIMEOUT)` 并 cts.Cancel。`IsCompiling/IsUpdating` 时 `JobKind.Write` 回填重入队、只读放行。`EnqueueLong` 即时返回 jobId + `GetJob` 轮询(Running/Done/Failed/Interrupted)。RED 62总/55过/7失(干净 NotImplementedException);GREEN 两跑 62/62;gate high=0。
- 偏差:可测接缝 `IEditorState`(注入编译/更新状态,生产 `EditorApplicationState` 包 EditorApplication,测试用假体)+ 公开 `Pump()` 测试直接驱动帧(绕 batchmode 下 update 不稳定);`EditorApplication.update` 订阅 + 编译稳定窗口细化留 T3-2。`IEditorState`/`DispatchJob`/`JobStatus`/`JobKind`/异常并入单文件(同 T2-1)。LongRunning v0.1 单帧跑完不做分帧切片、进度直写不节流(YAGNI,留 v0.2/MCP)。验收末条 geometric assertions 为模板伪影,忽略。
