# UIProbe MCP 替代方案规划

> 分支：`plan/workbench-refactor`  
> 范围：记录 UIProbe 自有 MCP 的总体方向、替代目标、架构边界与分阶段能力。  
> 状态：草案。本文档用于后续讨论 MCP 方向，暂不代表最终实现承诺。

---

## 1. 目标定位

UIProbe MCP 的目标不是再增加一个分散的 Unity MCP，也不是简单代理项目里已有的其他 MCP。

目标是：

```text
UIProbe 成为项目内统一的 Unity MCP 实现，逐步替代其他零散 MCP / Editor 自动化入口。
```

安装 UIProbe 后，团队希望通过 UIProbe 获得一套统一、可维护、可治理、可扩展的 MCP 能力，而不是继续维护多个各自独立的 MCP server、bridge、tool schema 和权限策略。

### 1.1 要解决的问题

当前 Unity 项目中常见问题：

- MCP / Editor 自动化工具来源分散。
- 每个 MCP 都有自己的连接方式、端口、工具命名和错误格式。
- Domain Reload / Assembly Reload 后连接稳定性不一致。
- 工具权限和危险操作缺少统一治理。
- AI 端看到太多低层工具，选择成本高。
- 项目扩展想接入 AI 时，需要重复实现 MCP 协议层。
- 日志、报告、预览、执行、撤销没有统一模型。

UIProbe MCP 要提供统一替代层：

- 统一 MCP Server / Orchestrator。
- 统一 Unity Bridge。
- 统一 ToolRegistry。
- 统一工具命名、分类、参数 schema、结果结构。
- 统一安全等级和 preview / execute 规则。
- 统一 Domain Reload 恢复策略。
- 统一运行历史、报告与错误追踪。

---

## 2. 非目标范围

为了避免 UIProbe 变成无限制的 Unity 全能执行器，以下不作为默认目标：

- 默认开放任意 C# 执行。
- 默认开放任意反射调用。
- 默认开放任意文件系统写入。
- 默认开放任意菜单命令执行。
- 默认开放任意外部进程执行。
- 默认开放任意网络请求。
- 默认提供 PSD 解析 / PSD 导入 / PSD → Prefab 自动生成。
- 默认提供通用 Prefab 自动生成或通用 Prefab 更新流水线。

这些能力如果后续确实需要，只能作为显式启用的高级工具或其他项目能力，不进入 UIProbe MCP 默认能力集。

---

## 3. 总体架构

推荐架构：

```text
AI Client / MCP Host
        ↓
UIProbe MCP Server / Orchestrator（外部进程）
        ↓
UIProbe Unity Bridge（Unity Editor 内）
        ↓
MainThread Dispatcher
        ↓
UIProbe ToolRegistry
        ↓
UIProbe Core Services
        ↓
Unity Editor API / Project Tools
```

### 3.1 外部 MCP Server / Orchestrator

职责：

- 实现 MCP 协议。
- 管理 AI client 会话。
- 管理 Unity 连接状态。
- 心跳检测。
- Domain Reload 等待与恢复。
- 请求队列、超时、取消、重试。
- 工具列表缓存与 capabilities 刷新。
- 将高层 MCP tool call 路由到 Unity Bridge。

### 3.2 Unity Bridge

职责：

- 在 Unity Editor 内启动本地 HTTP / WebSocket JSON-RPC bridge。
- 暴露 `/health`、`/rpc`、`/tools/list`、`/tools/describe` 等本地接口。
- 将请求投递到 Unity 主线程执行。
- 调用 UIProbe ToolRegistry。
- 返回结构化结果。
- Domain Reload 后自动重建 bridge 并重新上报 capabilities。

### 3.3 ToolRegistry

职责：

- 注册 UIProbe 内置工具。
- 注册项目扩展工具。
- 维护工具 descriptor、参数 schema、安全等级、来源、启用状态。
- 统一执行入口。
- 统一 preview / execute 协议。
- 统一 ToolResult。

### 3.4 Core Services

MCP 不直接操作 UI 面板，所有能力应通过 Core Services：

- PrefabIndexService
- AssetReferenceService
- UICheckService
- DuplicateCheckService
- ImageProcessingService
- RedGoldImportService
- ScreenshotService
- ReportService
- ConfigService
- StorageService

---

## 4. 替代策略

UIProbe MCP 替代其他 MCP，不是一次性重写所有功能，而是分三步完成。

### 4.1 第一阶段：替代基础连接与状态能力

先替代各类 MCP 都会重复实现的基础能力：

```text
ui_probe.health
ui_probe.ping
ui_probe.get_editor_state
ui_probe.get_project_info
ui_probe.get_selected_object
ui_probe.get_console_summary
ui_probe.list_tools
ui_probe.describe_tool
```

目标：AI 只需要通过 UIProbe 判断 Unity 是否在线、是否编译中、是否 Play Mode、当前选中对象、当前项目和工具能力。

### 4.2 第二阶段：替代 UIProbe 领域能力

围绕 UI 工作台能力，提供高语义工具：

```text
ui_probe.build_prefab_index
ui_probe.get_prefab_index_status
ui_probe.search_prefabs
ui_probe.get_prefab_detail
ui_probe.find_asset_references
ui_probe.run_ui_checks
ui_probe.get_check_results
ui_probe.export_report
```

目标：让 UIProbe 自身能力成为 MCP 第一批稳定价值，不追求 Unity 通用全能操作。

### 4.3 第三阶段：替代常见 Unity 自动化能力

在安全可控前提下，逐步替代其他 MCP 常见基础工具：

```text
ui_probe.get_console_logs
ui_probe.clear_console
ui_probe.trigger_compile
ui_probe.get_compile_result
ui_probe.enter_play_mode
ui_probe.exit_play_mode
ui_probe.run_editmode_tests
ui_probe.run_playmode_tests
ui_probe.take_screenshot
```

这些工具应默认按安全等级控制，测试、编译、Play Mode 等操作需要考虑 Domain Reload / Play Mode 状态变化。

### 4.4 第四阶段：替代项目扩展接入方式

项目扩展不再单独实现 MCP，而是接入 UIProbe ToolRegistry：

```csharp
[UIProbeTool(
    id: "project.some_tool",
    name: "项目自定义工具",
    category: "Project/Custom",
    safety: UIProbeToolSafety.PreviewRequired
)]
public sealed class ProjectSomeTool : UIProbeTool<ProjectSomeToolParams>
{
    protected override UIProbeToolResult Execute(ProjectSomeToolParams parameters)
    {
        // 项目自定义逻辑
    }
}
```

目标：项目内扩展只写业务工具，不再重复实现 MCP 协议层、连接层和权限层。

---

## 5. 工具来源与命名规范

即使目标是替代其他 MCP，也需要清楚标记工具来源。

```text
builtin     UIProbe 内置工具
project     项目扩展注册工具
legacy      从旧 MCP / 旧扩展迁移来的兼容工具
experimental 实验工具
```

工具命名建议：

```text
ui_probe.*              UIProbe 官方内置工具
project.*               项目扩展工具
legacy.*                兼容旧工具名称，不推荐长期使用
experimental.*          实验能力
```

长期目标是把 `legacy.*` 能力迁移为 `ui_probe.*` 或 `project.*`。

---

## 6. 安全等级

每个工具必须声明安全等级。

```text
ReadOnly          只读，不修改项目
PreviewOnly       只生成计划，不执行写入
WriteSafe         写入但可控，通常可撤销
WriteDestructive  可能覆盖、删除、重命名、移动资源
CodeExecution     可执行代码，默认禁用
Reflection        可反射调用，默认禁用
ExternalProcess   可启动外部进程，默认禁用
ExternalNetwork   可访问外部网络，默认禁用
Experimental      实验能力，默认禁用或需确认
```

默认策略：

- `ReadOnly` 默认启用。
- `PreviewOnly` 默认启用。
- `WriteSafe` 需要用户授权。
- `WriteDestructive` 默认禁用。
- `CodeExecution`、`Reflection`、`ExternalProcess`、`ExternalNetwork` 默认禁用。
- 所有写操作必须支持 preview / execute 分离，除非明确标记为不可预览且需要强确认。

---

## 7. Preview / Execute 规则

危险操作必须拆分为：

```text
preview_xxx
execute_xxx
```

或统一：

```text
ui_probe.preview_operation
ui_probe.execute_operation
```

Preview 返回：

```text
operationId
summary
plannedChanges
risks
requiresConfirmation
canUndo
reportPath
```

Execute 输入：

```text
operationId
confirmationToken
```

Execute 返回：

```text
success
appliedChanges
undoId
reportPath
errors
```

---

## 8. Domain Reload 恢复要求

UIProbe MCP 必须把 Domain Reload 当成常态，而不是异常。

外部 Orchestrator：

- 轮询 Unity Bridge `/health`。
- 检测 `serverId` 变化。
- 等待 Unity Bridge 稳定。
- 刷新 tool capabilities。
- 对中断任务返回 `interrupted`，或按工具声明决定是否可重试。

Unity Bridge `/health` 建议返回：

```json
{
  "status": "ok",
  "serverId": "guid",
  "pid": 12345,
  "projectPath": "...",
  "unityVersion": "...",
  "uiProbeVersion": "...",
  "isCompiling": false,
  "isUpdating": false,
  "isPlaying": false
}
```

Unity 侧监听：

```csharp
AssemblyReloadEvents.beforeAssemblyReload
AssemblyReloadEvents.afterAssemblyReload
EditorApplication.playModeStateChanged
EditorApplication.quitting
InitializeOnLoad
```

---

## 9. UIProbe MCP 第一版建议工具清单

### 9.1 连接与工具发现

```text
ui_probe.ping
ui_probe.health
ui_probe.get_project_info
ui_probe.get_editor_state
ui_probe.list_tools
ui_probe.describe_tool
```

### 9.2 Editor 状态

```text
ui_probe.get_selected_object
ui_probe.inspect_game_object
ui_probe.get_console_summary
```

### 9.3 UIProbe 核心能力

```text
ui_probe.build_prefab_index
ui_probe.get_prefab_index_status
ui_probe.search_prefabs
ui_probe.get_prefab_detail
ui_probe.find_asset_references
ui_probe.run_ui_checks
ui_probe.get_check_results
ui_probe.export_report
```

### 9.4 基础 Unity 自动化替代候选

```text
ui_probe.get_console_logs
ui_probe.clear_console
ui_probe.trigger_compile
ui_probe.get_compile_result
ui_probe.enter_play_mode
ui_probe.exit_play_mode
ui_probe.take_screenshot
```

这些可以作为 v0.2 / v0.3 逐步加入，不必全部进入第一版。

---

## 10. 与现有工作台重构的关系

MCP 替代方案依赖非 MCP 工作台重构中的服务化结果。

推荐顺序：

```text
1. Workbench Services / ToolRegistry 基础结构
2. Unity Bridge / Dispatcher
3. 外部 MCP Server / Orchestrator
4. 内置只读工具
5. UI Toolkit 工具中心
6. 写操作 preview / execute
7. 项目扩展 Tool API
8. 旧 MCP 能力迁移与废弃
```

原则：

- MCP 不直接调用 EditorWindow。
- MCP 不绕过 ToolRegistry。
- MCP 与 UI Toolkit 工作台共用 ToolDescriptor / ToolResult / ReportService。
- 项目扩展优先接入 UIProbe ToolRegistry，而不是继续新增独立 MCP。

---

## 11. 后续待讨论问题

- 第一版外部 MCP Server 使用 Node 还是 Python。
- Unity Bridge 使用 HTTP、WebSocket，还是两者都支持。
- 是否需要 CLI flow 层。
- 是否内置测试 / 编译 / Console 工具。
- 是否提供旧 MCP 工具名兼容层。
- 项目扩展 Tool API 的 Attribute 结构。
- 权限配置保存位置。
- Tool Center UI 的信息架构。
- 长任务取消、进度和日志格式。
- 多 Unity Editor 实例如何识别和选择。
