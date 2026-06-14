# UIProbe Tool 层契约（单一来源）

> 分支：`plan/workbench-refactor`
> 角色：UIProbe 工具层的**唯一权威契约**。UI Toolkit 工作台、MCP Server、内部 Flow 全部构造 `ToolRequest`、消费 `ToolResult`，差异仅在传输层（进程内调用 vs JSON-RPC）。
> 仲裁规则：凡涉及 ToolDescriptor / ToolRequest / ToolResult / Change / Issue / Preview-Execute / 错误码的描述，以本文档为准；其他文档不得另行定义，只能引用。
> 状态：草案，待冻结后作为实现基线。

---

## 1. 设计目标

- **一套契约，两处消费**：UI 与 MCP 不各写一套结果模型，消除双重定义。
- **写操作两阶段**：Preview 产出计划，Execute 凭令牌落地。
- **AI 可决策**：统一错误码 + 高质量工具描述，让 AI 能据此选工具、判重试。
- **可演进**：契约自带 schema 版本，升级时 AI client 缓存可失效。

---

## 2. ToolDescriptor

描述一个工具"是什么、多危险、需要什么档位"。`describe_tool` / `list_tools` 直接序列化它。

```csharp
public sealed class ToolDescriptor
{
    public string Id;                 // "ui_probe.search_prefabs"，命名空间前缀决定 Source
    public string Name;               // 人类可读名
    public string Description;        // 面向 AI 的说明，须达到“AI 据此能选对工具”的质量标准（见 §8）
    public string Category;           // "UIProbe/Index"
    public ToolSource Source;         // builtin | project | experimental（无 legacy）
    public ToolSafety Safety;         // 见 §5
    public CapabilityProfile MinProfile;  // 该工具要求的最低能力档位
    public bool EnabledByDefault;
    public bool SupportsPreview;
    public bool SupportsUndo;
    public bool RequiresConfirmation; // 即使自动放行模式也强制确认一次
    public bool AuditRequired;
    public bool LongRunning;          // true → 走 jobId + 进度/取消通道
    public bool ReloadSafe;           // Domain Reload 中断后是否可自动重试（幂等只读为 true）
    public ToolSchema ParamsSchema;   // 参数 JSON-Schema，供 MCP tools/list 直出
    public string ContractVersion;    // 该工具契约 schema 版本（SemVer）
}

public enum ToolSource { Builtin, Project, Experimental }
```

命名空间：`ui_probe.*`（内置）/ `project.*`（项目扩展）/ `experimental.*`（实验）。**不再有 `legacy.*`**。

---

## 3. ToolRequest

```csharp
public sealed class ToolRequest
{
    public string ToolId;
    public ToolPhase Phase;           // Describe | Preview | Execute
    public JsonObject Params;         // 按 ParamsSchema 校验
    public string OperationId;        // Execute 阶段回传 Preview 产出的 id
    public string ConfirmationToken;  // Execute 阶段的批准令牌
    public string SessionId;          // 权限判定 + 审计串联
    public string CorrelationId;      // 全链路 trace id（Node → Bridge → Service）
}

public enum ToolPhase { Describe, Preview, Execute }
```

---

## 4. ToolResult

```csharp
public sealed class ToolResult
{
    public ToolStatus Status;         // 替代裸 bool，见下
    public string Message;
    public ToolError Error;           // 含错误码，见 §6
    public string OperationId;        // Preview 产出，Execute 回传
    public string UndoId;             // 可撤销操作的回退句柄
    public string ReportPath;
    public bool RequiresConfirmation; // Preview 时提示是否需确认
    public bool CanUndo;
    public List<Issue> Issues;
    public List<Change> PlannedChanges;   // Preview 阶段填充
    public List<Change> AppliedChanges;   // Execute 阶段填充
    public List<Risk> Risks;
    public List<LogEntry> ProgressLog;
}

public enum ToolStatus { Success, Cancelled, Interrupted, Failed }
```

- `Interrupted`：Domain Reload 等外部中断；可否重试由 `ReloadSafe` 决定。
- `Cancelled`：用户/AI 协作式取消，返回已完成的 `AppliedChanges` + `UndoId`。

---

## 5. 安全等级（ToolSafety）

```text
ReadOnly          只读查询
PreviewOnly       只生成预览，不改项目
WriteSafe         可控写入，支持回滚或影响有限
WriteDestructive  批量覆盖 / 删除 / 重命名 / 改 prefab
MenuExecution     执行 Unity 菜单
CodeExecution     执行 C# / 动态脚本
Reflection        反射访问类型或方法
ExternalProcess   启动外部进程
ExternalNetwork   访问外部网络
Experimental      实验能力
```

安全等级只声明工具"有多危险"。"是否能跑"由安全等级 + 当前 Capability Profile + 授权模式共同决定，判定逻辑见 `MCPAuthorizationModel.md`。

---

## 6. 统一错误码

C# 异常、Node 协议错误、审计错误共用一套码，AI 可据码决策。

```csharp
public sealed class ToolError
{
    public string Code;       // 见下表
    public string Message;    // 人类可读
    public string Detail;     // 可选，调试用
    public bool Retriable;    // AI 是否值得重试
}
```

| Code | 含义 | Retriable |
|---|---|---|
| `OK` | 成功 | — |
| `INVALID_PARAMS` | 参数不合 schema 或语义校验失败 | 否 |
| `TOOL_NOT_FOUND` | 工具不存在 | 否 |
| `NOT_IN_PROFILE` | 当前 Capability Profile 不允许此工具 | 否 |
| `PERMISSION_DENIED` | 被 policy / 授权模式拒绝 | 否 |
| `CONFIRMATION_REQUIRED` | 需用户批准但未提供有效 token | 否（需走批准流程） |
| `OPERATION_EXPIRED` | OperationId/token 过期 | 否（需重新 Preview） |
| `UNITY_OFFLINE` | Bridge 未连接 | 是 |
| `UNITY_BUSY` | 编译中 / 更新中，暂不可执行 | 是 |
| `DOMAIN_RELOAD_INTERRUPTED` | 任务被 Domain Reload 中断 | 视 ReloadSafe |
| `MAIN_THREAD_TIMEOUT` | 主线程执行超时 | 是 |
| `EXECUTION_FAILED` | 工具内部执行错误 | 否 |
| `IO_ERROR` | 文件/资源读写错误 | 视情况 |

---

## 7. Preview / Execute 协议

危险（非只读）操作必须两阶段：

```text
Preview(ToolRequest{Phase=Preview})
  → ToolResult{ OperationId, PlannedChanges, Risks, RequiresConfirmation, CanUndo, ReportPath }

Execute(ToolRequest{Phase=Execute, OperationId, ConfirmationToken})
  → ToolResult{ Status, AppliedChanges, UndoId, ReportPath, Error }
```

规则：
- `OperationId` 有有效期；过期返回 `OPERATION_EXPIRED`，需重新 Preview。
- `ConfirmationToken` 由授权层在用户批准后签发；缺失或失配返回 `CONFIRMATION_REQUIRED` / `PERMISSION_DENIED`。
- 只读工具（ReadOnly/PreviewOnly）可跳过两阶段，直接 Execute 即查询。

---

## 8. 工具基类与扩展 API

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class UIProbeToolAttribute : Attribute
{
    public string Id;                 // 必填
    public string Name;
    public string Category;
    public ToolSafety Safety = ToolSafety.ReadOnly;
    public CapabilityProfile MinProfile = CapabilityProfile.SafeDefault;
    public bool SupportsPreview;
    public bool SupportsUndo;
    public bool RequiresConfirmation;
    public bool AuditRequired;
    public bool LongRunning;
    public bool ReloadSafe;
}

public abstract class UIProbeTool<TParams> where TParams : class
{
    public abstract ToolSchema DescribeParams();
    protected virtual ValidationResult Validate(TParams p) => ValidationResult.Ok;
    protected virtual ToolResult Preview(TParams p, ToolContext ctx)
        => throw new NotSupportedException();   // SupportsPreview=true 时必须重写
    protected abstract ToolResult Execute(TParams p, ToolContext ctx);
}
```

- 参数反序列化与 schema 校验由 ToolRegistry 统一做；`Validate` 做语义校验，失败转 `ToolResult.Issues` / `INVALID_PARAMS`，不抛异常穿透。
- ToolRegistry 按 `Phase` 调 `Preview` 或 `Execute`。
- **工具描述质量**：`Description` 与 `ParamsSchema` 的字段描述是 v0.1 验收项。要求：一句话说清"何时该用这个工具"、参数含义无歧义、与相邻工具的区别明确。

---

## 9. ToolContext（长任务：取消 / 进度 / 日志）

```csharp
public sealed class ToolContext
{
    public CancellationToken Cancellation;
    public IProgress<ToolProgress> Progress;
    public IToolLogger Log;           // 结构化日志，带 CorrelationId
    public string CorrelationId;
}

public struct ToolProgress
{
    public float Fraction;   // 0..1，-1 表示不确定
    public string Stage;     // "scanning" / "resolving"
    public int Done, Total;
    public string Message;
}
```

约定：
- 进度回调节流到 ~200ms/次，经 Bridge 推到 Orchestrator（v0.1 用 jobId 轮询拉取，WebSocket 推送留 v0.2）。
- 取消是协作式：工具在 Stage 边界检查 `Cancellation`，取消后返回 `Cancelled` + 已做的 `AppliedChanges` + `UndoId`。
- 日志统一 `{ts, level, stage, message, correlationId}` JSON 行，与 ReportService 同通道。

---

## 10. Change 与 Issue 模型

```csharp
public sealed class Change
{
    public ChangeType Type;   // create | update | delete | rename | move | import | export
    public string AssetPath;
    public string NodePath;
    public string OldValue;
    public string NewValue;
    public UndoCapability Undo;   // 见“撤销能力分级”
    public string BackupPath;
}

public enum UndoCapability
{
    None,           // 不可撤销
    UnityUndo,      // 走 Unity Undo 栈（prefab 节点改名等）
    FileBackup,     // 文件级备份还原（图片覆盖等）
    MultiLevelStack // 多级栈 + 表格回滚（RedGold 导入）
}

public sealed class Issue
{
    public Severity Severity;     // Error | Warning | Info
    public string RuleId;
    public string PrefabPath;
    public string NodePath;
    public string ComponentType;
    public string Message;
    public string SuggestedFixId;
    public bool CanAutoFix;
}
```

**撤销能力分级**：不假设单一 `backupPath` 通吃。三类既有写流程映射到不同 `UndoCapability`：prefab 改名→`UnityUndo`；图片规范化覆盖→`FileBackup`；RedGold 导入→`MultiLevelStack`。UI 与 MCP 据此呈现不同的撤销入口。

---

## 11. Schema 版本与兼容

- `ToolDescriptor.ContractVersion` 与本文档版本号同步（SemVer）。
- 契约 break change（删字段、改语义）→ major +1，AI client 缓存的 tool schema 须失效重拉。
- Node Server 与 C# 包各自声明所支持的 ContractVersion 范围，握手时校验，详见 `DistributionAndVersioning.md`。

---

## 12. 与其他文档的关系

- 安全等级如何映射到放行决策：见 `MCPAuthorizationModel.md`。
- 工具按阶段（v0.1/v0.2/v0.3）的清单：见 `MCPReplacementPlan.md`。
- ToolResult / Change 如何驱动报告导出：见 `WorkbenchRefactorPlan.md` 的 ReportService。
- 版本握手与迁移：见 `DistributionAndVersioning.md`。
