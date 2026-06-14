# UIProbe 工作台化与自有 MCP 重构 会话日志

> 由 scaffold_gen 生成。写入此文件请通过 `@write-session-log` skill，不要直接追加。硬限 100 行。

## 待办 (Pending)

<!-- 未完成 / 阻塞 / 下一步。永不压缩。 -->

- [ ] Next: T1-2 — 建 `UIProbe.Tests.Editor.asmdef` + 引入 Unity Test Framework(M1 测试工程底座)
- [ ] 编译验证基建已就绪:临时宿主工程 `E:\uiprobe-compile-host`(junction 挂载 UIProbe 到 Assets/Editor),batchmode 编译命令见 Current Milestone T1-1;后续可回填 project.json verification_commands(注意绝对路径机器相关,暂不入库)

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
