# UIProbe 工作台化与自有 MCP 重构 会话日志

> 由 scaffold_gen 生成。写入此文件请通过 `@write-session-log` skill，不要直接追加。硬限 100 行。

## 待办 (Pending)

<!-- 未完成 / 阻塞 / 下一步。永不压缩。 -->

- [ ] Next: T1-3 — 定义 Adapter 接缝接口(IAssetGateway/IFileSystem/IEditorPrefs)供 Service 注入,ToolContext 回填 Adapter 引用
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
