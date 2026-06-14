# UIProbe 工作台化与自有 MCP 重构 会话日志

> 由 scaffold_gen 生成。写入此文件请通过 `@write-session-log` skill，不要直接追加。硬限 100 行。

## 待办 (Pending)

<!-- 未完成 / 阻塞 / 下一步。永不压缩。 -->

- [ ] M1 五任务(T1-1..T1-5)全部 DONE;coverage_gate 仍 pending(单任务 /quick 未跑覆盖率,留 milestone 收尾)。Next: M2/T2-1 — 只读 Service 抽离(PrefabIndex,经接缝 + 黄金样本回归)
- [ ] 编译/测试基建就绪:临时宿主 `E:\uiprobe-compile-host`(junction 挂 UIProbe→Assets/UIProbe,manifest 含 com.unity.test-framework 1.1.33)。编译:`Unity.exe -batchmode -quit -nographics -projectPath <host> -logFile <log>`;测试:加 `-runTests -testPlatform EditMode -testResults <xml>`。绝对路径机器相关,暂不入库

## 归档 (Archive)

_尚无已完成的里程碑。_

## 近期活动 (Recent Activity)

_尚无快速任务或调试记录。_

## 当前里程碑 (Current Milestone)

### M1 Service 化底座

#### T1-1: 冻结 ToolContract 核心类型(代码落地) (DONE)
- build: 2026-06-14 — 在 `UIProbe/Core/Contract/` 落地 8 个文件:ToolDescriptor / ToolRequest / ToolResult / Change / Issue / ToolError / IUIProbeTool / ToolContext。字段、枚举、错误码逐字对齐 `Docs/ToolContract.md`(单一来源)。
- 关键取舍:① `Params`/`ParamsSchema` 用字符串(原始 JSON / JSON-Schema 文本),契约层零外部依赖,解析推迟到 T1-4;② `ToolContext` 不引用 Adapter(遵循文档 §9,且 T1-3 接口未定义),Adapter 注入待 T1-3;③ `CapabilityProfile` 枚举定义在契约层,authorization(M5)消费策略;④ `ToolResult` 取文档全字段 + 任务必需的 `JobId`/`Data`;⑤ 忽略验收里"几何断言"模板残留。
- verify: PASSED — Unity 2022.3.23f1 batchmode 编译通过(8 文件全部导入,Csc 生成 Assembly-CSharp-Editor.dll,0 error CS,返回码 0);结构自检 gate high=0/medium=0。
- 编译命令:`Unity.exe -batchmode -quit -nographics -projectPath E:\uiprobe-compile-host -logFile compile.log`(D:\unity\Unity2022\Unity2022\Editor\Unity.exe)。

#### T1-2: 建立 asmdef 三件套(运行时/编辑器/测试工程) (DONE)
- build: 2026-06-14 — 落地 3 个 asmdef + 1 个 smoke 测试:① `UIProbe/Core/Contract/UIProbe.Contract.asmdef`(运行时,noEngineReferences 纯托管,仅含 8 个契约文件);② `UIProbe/UIProbe.Editor.asmdef`(includePlatforms Editor,原地包裹全部遗留代码,引用 Contract+UnityEngine.UI+UnityEditor.UI+Unity.TextMeshPro+Editor);③ `UIProbe/Tests/Editor/UIProbe.Tests.Editor.asmdef`(Editor-only,引用 Contract+Editor+nunit.framework+UnityEngine/UnityEditor.TestRunner,UNITY_INCLUDE_TESTS);+ `ContractSmokeTests.cs`(2 NUnit 用例)。
- 关键取舍:最小风险落地——Contract 作运行时程序集,遗留代码原地保留在 Editor 程序集(遵循 task"先留 Editor 后续下沉"),零文件移动、零 GUID 变动、零跨引用断裂。宿主 manifest 加 `com.unity.test-framework` 1.1.33,junction 重挂到 `Assets/UIProbe`(避开 Editor 路径文件夹歧义)。
- verify: PASSED — batchmode 编译 0 error CS,生成 UIProbe.Contract.dll/UIProbe.Editor.dll/UIProbe.Tests.Editor.dll;EditMode 测试 ContractSmokeTests 2/2 Passed;结构自检 gate high=0/medium=0。

#### T1-3: 定义 Adapter 接缝接口 + Unity 实现 + 内存假体 (DONE)
- build: 2026-06-14 — 新建第 4 个程序集 `UIProbe.Infrastructure`(运行时/全平台,引擎引用)承载 3 接口:IAssetGateway(FindAssets/LoadAssetAtPath<T>/MoveAsset/GUID 互转,注释标明须主线程)、IFileSystem(读写/Exists/Backup/Restore 走 token 支撑 FileBackup 撤销)、IEditorPrefs(GetString/SetString/HasKey/DeleteKey)。`UIProbe.Editor` 落 3 个 Unity 生产实现(AssetDatabase/File/EditorPrefs);`UIProbe.Tests.Editor` 落 3 个内存假体(Dictionary 模拟,MaxEntries 可控规模 + Seed)+ AdapterFakesTests(4 用例)。Editor/Tests asmdef 各 +引用 UIProbe.Infrastructure。
- 关键取舍:接口须在运行时程序集而 Contract 是 noEngineReferences 纯托管(容不下需 UnityEngine.Object 的 IAssetGateway),故新建 UIProbe.Infrastructure,与 unity-adapters 模块所有权对齐、保持 Contract 纯净。不改任何现有业务调用点(迁移留到 Service 抽离里程碑)。
- verify: PASSED — 编译 0 error CS(首轮 InMemoryAssetGateway 因 System/UnityEngine 双 using 致 Object 二义 12 err,限定 UnityEngine.Object 后通过);EditMode 6/6 Passed;gate high=0/medium=0。

#### T1-4: ToolRegistry 骨架(注册/发现/只读调用,经 Adapter) (DONE)
- build: 2026-06-14 — 新建第 5 个运行时程序集 `UIProbe.Core.Services`(引用 Contract+Infrastructure)。`ToolRegistration.cs`:注册条目 + `OperationTicket`(写两阶段占位)+ `ToolRunnerBase<TParams>`(契约 §8 留白的 Run 接线:JsonUtility 反序列化→Validate 短路→按 Phase 分派 Describe/Preview/Execute)。`ToolRegistry.cs`:构造注入三 Adapter;Register(同 Id 去重抛异常)/ListTools(按 MinProfile 过滤)/DescribeTool(返回 ToolResult,缺失 TOOL_NOT_FOUND)/Invoke(查找→TOOL_NOT_FOUND→建 ToolContext→tool.Run)/RegisterFromAssembly(反射 [UIProbeTool] 按 ctor 签名注入 Adapter)。`ToolRegistryTests.cs` 7 用例(fake 只读工具真实调 AssetGateway 证明注入)。
- 关键取舍:① 经两个子 agent 独立评审后定案;② Adapter 经工具 **ctor 注入**而非 ToolContext(任务字面"转交给 ToolContext"与冻结契约冲突,ToolContext 在 noEngineReferences 的 Contract 层容不下 Adapter,以契约为准);③ Run 分派落 `ToolRunnerBase<TParams>`(Registry 只持非泛型 IUIProbeTool 看不见 TParams);④ DescribeTool 返回 ToolResult 以承载缺失码;⑤ ARCH-001 偏差:实际落点 `Core/Services` 与 CLAUDE.md 声明的 `Core/Tools` 不一致,以任务 write-path 为准,已记 tool-registry wiki。
- verify: PASSED — batchmode 编译 0 error CS;EditMode 13/13 Passed(7 ToolRegistry + 4 Adapter 假体 + 2 契约 smoke);gate high=0/medium=0。

#### T1-5: 黄金样本回归基线机制 (DONE)
- build: 2026-06-14 — 测试程序集内建立回归安全网,零生产代码改动。`Golden/GoldenSampleRunner.cs`:`AssertGolden(name, actual, GoldenFormat, baselineDir)` 支持 string/CSV/JSON 三格式(扩展名区分,核心为规范化文本逐行 diff 消除 CRLF 差异);刷新开关双保险(环境变量 `UIPROBE_UPDATE_GOLDEN=1` 或常量,默认 false 绝不误覆盖);基线目录经 `[CallerFilePath]` 自动定位 `GoldenSampleFixtures`,不耦合挂载点名;缺失/刷新时写入并通过,否则不一致 `Assert.Fail` 打印首处差异 + before/after 全文。`GoldenSampleFixtures/UIDuplicateFakeFixture.cs`:内存假体 seed 3 个受控 prefab + 确定性 fake 检测器(按 PrefabPath 排序产 Issue 列表)+ 三导出器。`GoldenSampleTests.cs` 5 用例:temp 目录 round-trip / 篡改失败含差异 / 三格式示例基线各一。录制基线 `ui_dup.txt|csv|json` 经 junction 落入真实仓库入库。
- 关键取舍:① 基线持久化用真实磁盘(经 junction 写入仓库),夹具输入用内存假体,两者解耦;② 机制自测用 temp 目录跑"首写→二次 diff→篡改失败",示例基线用默认目录,互不污染;③ 跑两轮 runTests 证明闭环——首轮录基线(首次通过),次轮走 diff 路径全绿;④ 仅建机制 + 一个示例基线,各模块基线由后续 Service 抽离任务录制;⑤ 忽略验收第 38 行"几何断言"模板残留。
- verify: PASSED — 两轮 batchmode runTests EditMode 各 18/18 Passed(13 旧 + 5 新),0 error CS;gate high=0/medium=0。
