# tool-contract 模块文档

> 此文件由 wiki_gen 生成。手动编辑将在重新生成时保留。



> 关联文件: `UIProbe/Core/Tools/Models`

## 职责

UIProbe 工具层的唯一权威契约。UI Toolkit 工作台、MCP Server、内部 Flow 全部构造 ToolRequest、消费 ToolResult,差异仅在传输层(进程内调用 vs JSON-RPC)。凡涉及 ToolDescriptor/ToolRequest/ToolResult/Change/Issue/Preview-Execute/错误码的描述,以本契约为准,其他模块只能引用不得另定义。

## 所属路径

- UIProbe/Core/Tools/Models

## 实体

### ToolDescriptor (model)

描述一个工具是什么、多危险、需要什么档位。describe_tool / list_tools 直接序列化它。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `Id` | string | 命名空间前缀决定 Source,如 ui_probe.search_prefabs |
| `Name` | string | 人类可读名 |
| `Description` | string | 面向 AI 的说明,须达到 AI 据此能选对工具的质量标准 |
| `Category` | string | 如 UIProbe/Index |
| `Source` | ToolSource | builtin \| project \| experimental,无 legacy |
| `Safety` | ToolSafety | 见安全等级枚举 |
| `MinProfile` | CapabilityProfile | 该工具要求的最低能力档位 |
| `EnabledByDefault` | bool |  |
| `SupportsPreview` | bool |  |
| `SupportsUndo` | bool |  |
| `RequiresConfirmation` | bool | 即使自动放行模式也强制确认一次 |
| `AuditRequired` | bool |  |
| `LongRunning` | bool | true 走 jobId + 进度/取消通道 |
| `ReloadSafe` | bool | Domain Reload 中断后是否可自动重试,幂等只读为 true |
| `ParamsSchema` | ToolSchema | 参数 JSON-Schema,供 MCP tools/list 直出 |
| `ContractVersion` | string | 该工具契约 schema 版本 SemVer |

**不变量**:

- Source 只能取 builtin/project/experimental
- ContractVersion 与契约文档版本同步

### ToolRequest (model)

工具调用请求,按阶段携带不同字段。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `ToolId` | string |  |
| `Phase` | ToolPhase | Describe \| Preview \| Execute |
| `Params` | JsonObject | 按 ParamsSchema 校验 |
| `OperationId` | string | Execute 阶段回传 Preview 产出的 id |
| `ConfirmationToken` | string | Execute 阶段的批准令牌 |
| `SessionId` | string | 权限判定 + 审计串联 |
| `CorrelationId` | string | 全链路 trace id(Node -> Bridge -> Service) |

**不变量**:

- Execute 阶段必须带有效 OperationId 与 ConfirmationToken(非只读工具)

### ToolResult (model)

统一工具结果。UI 与 MCP 不各写一套,消除双重定义。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `Status` | ToolStatus | Success \| Cancelled \| Interrupted \| Failed,替代裸 bool |
| `Message` | string |  |
| `Error` | ToolError | 含错误码 |
| `OperationId` | string | Preview 产出,Execute 回传 |
| `UndoId` | string | 可撤销操作的回退句柄 |
| `ReportPath` | string |  |
| `RequiresConfirmation` | bool | Preview 时提示是否需确认 |
| `CanUndo` | bool |  |
| `Issues` | List<Issue> |  |
| `PlannedChanges` | List<Change> | Preview 阶段填充 |
| `AppliedChanges` | List<Change> | Execute 阶段填充 |
| `Risks` | List<Risk> |  |
| `ProgressLog` | List<LogEntry> |  |

**不变量**:

- Interrupted 表示 Domain Reload 等外部中断,可否重试由 ReloadSafe 决定
- Cancelled 返回已完成的 AppliedChanges + UndoId

### ToolError (model)

统一错误对象,C# 异常 / Node 协议错误 / 审计错误共用。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `Code` | string | 见统一错误码表 |
| `Message` | string | 人类可读 |
| `Detail` | string | 可选,调试用 |
| `Retriable` | bool | AI 是否值得重试 |

### Change (model)

一次变更描述,带撤销能力分级。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `Type` | ChangeType | create \| update \| delete \| rename \| move \| import \| export |
| `AssetPath` | string |  |
| `NodePath` | string |  |
| `OldValue` | string |  |
| `NewValue` | string |  |
| `Undo` | UndoCapability | None \| UnityUndo \| FileBackup \| MultiLevelStack |
| `BackupPath` | string |  |

**不变量**:

- 不假设单一 backupPath 通吃 -- prefab 改名=UnityUndo,图片覆盖=FileBackup,RedGold 导入=MultiLevelStack

### Issue (model)

检测问题项,驱动 preview/apply fix。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `Severity` | Severity | Error \| Warning \| Info |
| `RuleId` | string |  |
| `PrefabPath` | string |  |
| `NodePath` | string |  |
| `ComponentType` | string |  |
| `Message` | string |  |
| `SuggestedFixId` | string |  |
| `CanAutoFix` | bool |  |

### ToolContext (model)

长任务的取消 / 进度 / 日志上下文。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `Cancellation` | CancellationToken | 协作式取消,Stage 边界检查 |
| `Progress` | IProgress<ToolProgress> | 节流到约 200ms/次 |
| `Log` | IToolLogger | 结构化日志,带 CorrelationId |
| `CorrelationId` | string |  |


## 接口

### UIProbeTool<TParams> `[出站]`

工具基类。ToolRegistry 按 Phase 调 Preview 或 Execute。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  TParams(按 ParamsSchema 反序列化)
  ```
- **输出**:
  ```ts
  ToolResult
  ```
- **错误码**: `INVALID_PARAMS`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_

### DescribeParams `[出站]`

返回工具参数 JSON-Schema。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  ()
  ```
- **输出**:
  ```ts
  ToolSchema
  ```

### Validate `[出站]`

语义校验,失败转 ToolResult.Issues / INVALID_PARAMS,不抛异常穿透。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  TParams
  ```
- **输出**:
  ```ts
  ValidationResult
  ```
- **错误码**: `INVALID_PARAMS`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_

### Preview `[出站]`

SupportsPreview=true 时必须重写,产出 OperationId + PlannedChanges + Risks。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  TParams, ToolContext
  ```
- **输出**:
  ```ts
  ToolResult
  ```
- **错误码**: `INVALID_PARAMS`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_

### Execute `[出站]`

凭 OperationId + ConfirmationToken 落地变更,产出 AppliedChanges + UndoId。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  TParams, ToolContext
  ```
- **输出**:
  ```ts
  ToolResult
  ```
- **错误码**: `OPERATION_EXPIRED`, `CONFIRMATION_REQUIRED`, `EXECUTION_FAILED`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_


## 场景

### 只读工具直接执行

1. 构造 ToolRequest{Phase=Execute}
2. ReadOnly/PreviewOnly 工具跳过两阶段
3. 直接查询返回 ToolResult{Status=Success, Issues/数据}

**边界情况**:
- 参数不合 schema -> INVALID_PARAMS
- Unity 离线 -> UNITY_OFFLINE(Retriable)

### 写操作两阶段协议

1. Preview 返回 OperationId + PlannedChanges + RequiresConfirmation
2. 授权层签发 ConfirmationToken
3. Execute 携 OperationId + token 落地,返回 AppliedChanges + UndoId

**边界情况**:
- OperationId 过期 -> OPERATION_EXPIRED(需重新 Preview)
- token 缺失/失配 -> CONFIRMATION_REQUIRED / PERMISSION_DENIED

### 长任务取消与进度

1. LongRunning 工具立即返回 jobId
2. 主线程分帧执行,ToolContext.Progress 节流上报
3. Stage 边界检查 Cancellation

**边界情况**:
- 用户取消 -> Cancelled + 已做 AppliedChanges + UndoId
- Domain Reload -> Interrupted,按 ReloadSafe 决定重试


## 约束

### Correctness

- 参数反序列化与 schema 校验由 ToolRegistry 统一做
- Status 用 ToolStatus 枚举替代裸 bool
- 契约 break change(删字段/改语义)-> ContractVersion major +1,AI client 缓存须失效重拉

### Quality

- Description 与 ParamsSchema 字段描述是 v0.1 验收项 -- 一句话说清何时该用、参数无歧义、与相邻工具区别明确


## 设计决策

- **一套契约两处消费(UI + MCP)**
  - 理由: 消除双重结果模型定义,差异仅在传输层
  - 备选方案: UI 与 MCP 各自定义结果模型(被否决,会返工)
- **写操作强制 Preview/Execute 两阶段**
  - 理由: Preview 产计划让用户/AI 可审,Execute 凭令牌落地
  - 备选方案: 单阶段直接执行(危险操作不可接受)
- **撤销能力分级 UndoCapability**
  - 理由: 三类既有写流程映射不同撤销机制,不假设单一 backupPath 通吃
  - 备选方案: 统一 backupPath(无法表达 Unity Undo 栈与多级表格回滚)
- **统一错误码 + Retriable**
  - 理由: AI 可据码决策选工具/判重试
  - 备选方案: 自由文本错误(AI 无法机器决策)

## 相关任务

| ID | 标题 | 阶段 | 状态 | 里程碑 |
|---|---|---|---|---|
| T1-1 | 冻结 ToolContract 核心类型(代码落地) | prep | NOT_STARTED | M1 |

## 错误处理

- 方式: 统一 ToolError 错误码体系(见 ToolContract.md §6)。C# 异常 / Node 协议错误 / 审计错误共用一套码,每个错误带 Retriable 标志。
- 重试策略: AI 据 ToolError.Retriable 决定是否重试或换工具。Domain Reload 中断按工具 ReloadSafe 决定 -- 幂等只读(ReloadSafe=true)可自动重发,写操作返回 DOMAIN_RELOAD_INTERRUPTED 默认交人确认不自动重试。
- 降级行为: 写操作两阶段(Preview 产计划 / Execute 凭令牌落地)。失败返回结构化 ToolResult{Status,Error},绝不静默崩溃;配置迁移失败回退默认 + 显著告警 + 写审计。



## 相关依赖模块

- [mcp-server](mcp-server.md) — Node/TypeScript 实现的 MCP Server(Orchestrator),对外讲标准 MCP 协议给 A

## 相关社区

_(无图社区)_
