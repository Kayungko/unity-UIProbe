# UIProbe MCP 替代方案规划

> 分支：`plan/workbench-refactor`  
> 范围：记录 UIProbe 自有 MCP 的总体方向、替代目标、架构边界与分阶段能力。  
> 上位文档：`RefactorRoadmap.md`（关键路径、MVP、决策总账）。工具契约见 `ToolContract.md`，授权治理见 `MCPAuthorizationModel.md`，分发版本见 `DistributionAndVersioning.md`。本文档不再重复定义工具结果模型与安全等级，一律引用 `ToolContract.md`。  
> 状态：方案已收敛，关键决策已锁定（见 §0）。

---

## 0. 已锁定决策（速查）

- MCP Server 语言：**Node / TypeScript**（官方 SDK 最成熟、npx 分发、与 AI client 契合）。
- Unity Bridge 传输：**v0.1 仅 HTTP（loopback）+ jobId 轮询**；WebSocket 进度推送留 v0.2。
- **不做独立 CLI**；Flow 仅作 Core 内部编排，需要命令行时复用 Bridge HTTP。
- **不做 legacy 兼容层**：UIProbe MCP 开发完成后直接替代其他 Unity MCP。工具来源只有 `builtin` / `project` / `experimental`。
- 首发 MVP 为 **AI 向**，v0.1 仅只读工具（约 17 个），突出 `run_ui_checks + 结构化报告`差异化能力；测试/编译/写操作推到 v0.2+。
- 授权模式与 Capability Profile **正交**（详见 `MCPAuthorizationModel.md`）。
- 目标 Unity：**2022.3 LTS+**。

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
- 统一能力档位（Capability Profile），允许团队按项目需要启用更高风险能力。
- 统一 Domain Reload 恢复策略。
- 统一运行历史、报告与错误追踪。

---

## 2. 非目标范围

为了避免 UIProbe 变成无限制的 Unity 全能执行器，以下不作为默认能力：

- 默认开放任意 C# 执行。
- 默认开放任意反射调用。
- 默认开放任意文件系统写入。
- 默认开放任意菜单命令执行。
- 默认开放任意外部进程执行。
- 默认开放任意网络请求。
- 默认提供 PSD 解析 / PSD 导入 / PSD → Prefab 自动生成。
- 默认提供通用 Prefab 自动生成或通用 Prefab 更新流水线。

但为了替代其他 Unity MCP，UIProbe MCP 可以提供这些高级能力的可选档位。原则是：

- 默认安装保持安全，只启用只读和低风险工具。
- 高风险能力通过 Capability Profile 显式启用。
- 高风险档位需要工作台 UI 中可见、可审计、可关闭。
- 写操作、代码执行、反射、外部进程、外部网络等能力必须有独立权限开关。
- 团队可以在项目级配置中锁定允许的最高档位。
- 个人本地可以临时升档，但需要明确提示风险并记录日志。

这些能力不是默认目标，但可以作为 `Trusted` / `Admin` / `Debug` 档位的一部分，用于替代其他 MCP 中的全能工具。

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

- 在 Unity Editor 内启动本地 HTTP JSON-RPC bridge（v0.1 仅 HTTP + loopback；WebSocket 留 v0.2）。
- 暴露 `/health`、`/rpc`、`/tools/list`、`/tools/describe` 等本地接口。
- 将请求经 MainThread Dispatcher 投递到 Unity 主线程执行（见 §3.2.1）。
- 调用 UIProbe ToolRegistry。
- 返回结构化结果。
- Domain Reload 后自动重建 bridge 并重新上报 capabilities（见 §8）。

端口与多实例：固定基址（如 58300）向上探测空闲端口写入 EditorPrefs；`/health` 返回 `serverId/pid/projectPath/uiProbeVersion/contractVersion`；Orchestrator 扫端口段并按 `projectPath` 路由，多实例必须显式选择目标。详见 `DistributionAndVersioning.md` §6。

#### 3.2.1 MainThread Dispatcher（主线程执行边界）

Unity Editor API 必须在主线程调用。Bridge 的 HTTP 监听运行在后台线程，因此约定：

- **所有 Editor API 调用必须经 Dispatcher**，Service 层不在监听线程直接碰 Unity API。
- 监听线程收到请求后，把 `Action` / `TaskCompletionSource` 入并发队列；注册到 `EditorApplication.update` 的回调在主线程逐帧 drain 队列、执行、把结果写回 TCS。
- **短任务**：HTTP 线程 `await` TCS，默认 30s 超时，超时返回 `MAIN_THREAD_TIMEOUT` 并尽量取消。
- **长任务**（`LongRunning=true`）：立即返回 `jobId`，主线程后台分帧执行，结果存 job 表供轮询；进度经 `ToolContext.Progress` 节流上报（见 `ToolContract.md` §9）。
- **编译/更新中**（`isCompiling`/`isUpdating`）暂缓 drain 写操作，避免在不稳定状态执行。
- 执行单元应短小、可在 Stage 边界检查取消。

### 3.3 ToolRegistry

职责：

- 注册 UIProbe 内置工具。
- 注册项目扩展工具。
- 维护工具 descriptor、参数 schema、安全等级、来源、启用状态。
- 统一执行入口。
- 统一 preview / execute 协议。
- 统一 ToolResult。
- 根据当前 Capability Profile 过滤、禁用或要求确认特定工具。

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

### 4.5 第五阶段：可选替代高风险全能能力

为了替代其他 MCP 的全能能力，可以在高档位中提供：

```text
ui_probe.execute_menu_item
ui_probe.read_project_file
ui_probe.write_project_file_preview
ui_probe.write_project_file_execute
ui_probe.run_editor_script_preview
ui_probe.run_editor_script_execute
ui_probe.reflect_type_info
ui_probe.reflect_method_call
ui_probe.run_external_process
```

这些能力不进入默认档位，只能在 `TrustedProject` / `AdminDebug` 等能力档位中启用。

要求：

- 必须标记为 `CodeExecution`、`Reflection`、`ExternalProcess`、`WriteDestructive` 等安全等级。
- 默认不出现在普通 `list_tools` 结果里，除非当前 profile 允许。
- 必须记录调用者、时间、输入摘要、输出摘要和风险提示。
- 对文件写入必须限制在项目目录或显式 allowlist 目录内。
- 对菜单执行必须提供 allowlist，不允许默认任意菜单。
- 对反射和代码执行必须支持 dry-run / preview 或强确认。

---

## 5. 工具来源与命名规范

工具来源只有三类（**不设 legacy**，因为 UIProbe MCP 直接替代而非兼容旧 MCP）：

```text
builtin      UIProbe 内置工具
project      项目扩展注册工具
experimental 实验工具
```

工具命名：

```text
ui_probe.*              UIProbe 官方内置工具
project.*               项目扩展工具
experimental.*          实验能力
```

不再保留 `legacy.*` 命名空间，也不提供旧 MCP 工具名兼容映射。需要替代的其他 Unity MCP，其能力直接以 `ui_probe.*` 重新实现。

---

## 6. 安全等级与 Capability Profile

安全等级的**枚举定义以 `ToolContract.md` §5 为唯一来源**，本节不再重复，只说明默认放行策略与能力档位。

默认策略：

- `ReadOnly` / `PreviewOnly` 默认启用。
- `WriteSafe` 需要用户授权。
- `WriteDestructive` 默认禁用，或仅在受信任档位启用。
- `CodeExecution`、`Reflection`、`MenuExecution`、`ExternalProcess`、`ExternalNetwork` 默认禁用，但可在高级 Capability Profile 中显式启用。
- 所有写操作必须支持 preview / execute 分离（见 `ToolContract.md` §7），除非明确标记为不可预览且需要强确认。

> **授权模式与 Capability Profile 的关系**：二者**正交**——Capability Profile 决定"工具能否存在/可被启用"（能力天花板，团队项目级锁定），授权模式决定"已在天花板内的工具调用时是否自动放行还是弹窗"（批准策略，用户本地选）。完整判定流程见 `MCPAuthorizationModel.md` §1。本节描述的档位是底层能力维度，不与授权模式 UI 上的四个选项混为一谈。

### 6.1 Capability Profile 能力档位

UIProbe MCP 不采用“永远安全但能力不足”的单一策略，而是提供可选档位。

```text
SafeDefault       默认档位。只读 + 预览 + 少量安全写操作。
TeamAutomation   团队自动化档。允许编译、测试、Play Mode、截图、报告、受控文件读写。
TrustedProject   受信任项目档。允许项目扩展工具、受控菜单命令、受控文件写入、部分反射读取。
AdminDebug        管理员调试档。允许代码执行、反射调用、外部进程等高风险能力。
```

#### SafeDefault

默认安装后的档位。

允许：

- health / ping / editor state
- 工具发现
- Console 摘要
- 选择对象检查
- UIProbe Prefab Index / Asset Reference / UI Checks
- 报告导出
- preview-only 操作

不允许：

- 任意写文件
- 任意菜单执行
- 代码执行
- 反射调用
- 外部进程
- 外部网络

#### TeamAutomation

适合 CI、本地自动化、团队常规 AI 辅助。

在 SafeDefault 基础上允许：

- trigger compile
- get compile result
- run tests
- enter / exit play mode
- take screenshot
- 受控目录内读取文件
- 受控报告输出
- 支持 preview / execute 的安全写操作

仍然不允许：

- 任意 C# 执行
- 任意反射调用
- 任意菜单执行
- 任意外部进程

#### TrustedProject

适合项目内已经认可的自动化工具和扩展。

在 TeamAutomation 基础上允许：

- 项目扩展注册工具
- allowlist 菜单命令
- allowlist 文件写入
- 只读反射查询，例如类型、组件、字段结构
- 受控 batch operation

要求：

- 项目级配置显式启用。
- 所有写操作有日志。
- 高风险工具可以单独关闭。

#### AdminDebug

适合少数管理员或本地调试，不适合作为团队默认档位。

在 TrustedProject 基础上允许：

- 代码执行
- 反射调用方法
- 外部进程
- 外部网络
- 其他全能 MCP 才有的高风险工具（由 UIProbe 直接实现，非兼容层）

要求：

- 默认关闭。
- 每次启用需要明显提示。
- 可以设置临时有效期。
- 必须记录审计日志。
- 建议只绑定 loopback，不允许远程访问。

### 6.2 Profile 与 ToolDescriptor

ToolDescriptor 的**完整字段定义见 `ToolContract.md` §2**，本节不再重复。一个高风险工具示例：

```json
{
  "id": "ui_probe.run_editor_script_execute",
  "category": "Unity/Code",
  "safety": "CodeExecution",
  "minProfile": "AdminDebug",
  "enabledByDefault": false,
  "requiresConfirmation": true,
  "supportsPreview": true,
  "supportsUndo": false,
  "auditRequired": true
}
```

工具最终是否可用由以下条件共同决定：

```text
当前 Capability Profile
项目级 allow / deny policy
用户本地授权
工具自身 enabled 状态
MCP client / session 权限
```

### 6.3 client 鉴别与错误码

- **token 鉴权**：loopback 只挡远程，挡不住同机其他进程直连。Bridge 启动时生成一次性 session token 写入仅当前用户可读的本地文件；MCP Server 读取后在 `/rpc` 请求头携带，Bridge 校验。完全访问档位尤其依赖此机制防止任意本地进程触发高风险工具。
- **错误码**：所有工具错误统一走 `ToolContract.md` §6 的错误码体系，AI 据 `Retriable` 决定是否重试或换工具。
- **AI 工具描述质量**：`describe_tool` 的描述与参数 schema 须达到"AI 据此能选对工具"的质量标准，列为 v0.1 验收项（见 `RefactorRoadmap.md` §3.1、`ToolContract.md` §8）。

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

### 8.1 恢复工程细节

- **beforeAssemblyReload**：把进行中的 job 标记为 `Interrupted`（写入 job 表）、关闭 HttpListener、持久化必要状态（EditorPrefs / SessionState）。
- **afterAssemblyReload / InitializeOnLoad**：**生成新的 `serverId`（进程内新建，绝不持久化复用）**、重建 Bridge、重新上报 capabilities。
- **serverId 变化即判定发生 Domain Reload**：Orchestrator 轮询 `/health`，发现 `serverId` 变了就知道重载发生。
- **中断任务语义**：未完成请求按工具 `ReloadSafe` 决定——`ReloadSafe=true`（幂等只读）可自动重发；否则返回 `DOMAIN_RELOAD_INTERRUPTED`，写操作默认交人确认，不自动重试。
- **bridge 重建时机**：afterAssemblyReload 同步重建监听；但放行写操作前应等待 `isCompiling=false` 的稳定窗口。
- **Orchestrator 退避**：reload 期间端口短暂不可用，轮询需重试退避，不要把瞬时失败当作 Unity 离线。

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

### 9.5 高风险替代候选（非默认档位）

```text
ui_probe.execute_menu_item
ui_probe.read_project_file
ui_probe.write_project_file_preview
ui_probe.write_project_file_execute
ui_probe.run_editor_script_preview
ui_probe.run_editor_script_execute
ui_probe.reflect_type_info
ui_probe.reflect_method_call
ui_probe.run_external_process
```

这些工具用于覆盖其他 MCP 的高级能力，不进入默认安装档位，仅在高 Capability Profile 中显式启用。

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
9. Capability Profile 与高风险工具治理
```

原则：

- MCP 不直接调用 EditorWindow。
- MCP 不绕过 ToolRegistry。
- MCP 与 UI Toolkit 工作台共用 ToolDescriptor / ToolResult / ReportService。
- 项目扩展优先接入 UIProbe ToolRegistry，而不是继续新增独立 MCP。
- 高风险能力不是删除，而是放入更高 Capability Profile 并受策略治理。

---

## 11. 待讨论问题（结案状态）

已结案：

- 外部 MCP Server 语言 → **Node / TypeScript**（结案）。
- Unity Bridge 传输 → **v0.1 仅 HTTP loopback + jobId 轮询，WebSocket 留 v0.2**（结案）。
- 是否需要 CLI flow 层 → **不做独立 CLI，Flow 仅作 Core 内部编排**（结案）。
- 是否内置测试/编译/Console 工具 → **推到 v0.2+，v0.1 不含**（结案）。
- 是否提供旧 MCP 工具名兼容层 → **不提供**，直接替代（结案）。
- 项目扩展 Tool API 的 Attribute 结构 → 见 `ToolContract.md` §8（结案）。
- 权限配置保存位置 → `mcp.config.toml`（版控）+ `mcp.local.toml`（本地），见 `MCPAuthorizationModel.md` §2（结案）。
- 长任务取消/进度/日志格式 → 见 `ToolContract.md` §9（结案）。
- 多 Unity Editor 实例识别与选择 → 端口探测 + serverId 路由，见 `DistributionAndVersioning.md` §6（结案）。
- Capability Profile 保存/继承/锁定 → 见 `MCPAuthorizationModel.md` §2–§3（结案）。
- AdminDebug 是否允许远程访问 → **不允许，强制 loopback**（结案）。

仍待细化（非阻塞）：

- Tool Center / 权限 UI 的信息架构（待 UI Toolkit Shell 定型）。
- v0.2/v0.3 各工具 preview/execute 的具体设计。
- MCP SDK 版本锁定与升级节奏（见 `DistributionAndVersioning.md` §8）。
