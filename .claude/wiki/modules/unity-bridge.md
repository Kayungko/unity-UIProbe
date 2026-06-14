# unity-bridge 模块文档

> 此文件由 wiki_gen 生成。手动编辑将在重新生成时保留。



> 关联文件: `UIProbe/Infrastructure/Bridge`

## 职责

Unity Editor 内的本地 HTTP JSON-RPC bridge(v0.1 仅 HTTP + loopback,WebSocket 留 v0.2)。暴露 /health、/rpc、/tools/list、/tools/describe;经 MainThread Dispatcher 把请求投递到 Unity 主线程执行;调用 ToolRegistry 返回结构化结果;Domain Reload 后自动重建并重新上报 capabilities。把 Domain Reload 当成常态而非异常。

## 所属路径

- UIProbe/Infrastructure/Bridge

## 实体

### HealthStatus (model)

/health 返回,用于握手校验与 Domain Reload 检测。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `status` | string | ok |
| `serverId` | guid | 进程内新建绝不持久化复用;变化即判定发生 Domain Reload |
| `pid` | int | 僵尸监听靠 pid 存活校验剔除 |
| `projectPath` | string | Orchestrator 按此路由多实例 |
| `uiProbeVersion` | string | 版本握手 |
| `contractVersion` | string | 握手校验 major |
| `isCompiling` | bool | 编译中暂缓写操作 |
| `isUpdating` | bool |  |
| `isPlaying` | bool |  |

**不变量**:

- serverId 每次 afterAssemblyReload 重新生成,绝不持久化

### DispatchJob (model)

投递到主线程的执行单元。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `JobId` | string | LongRunning 立即返回供轮询 |
| `Action` | Action/TaskCompletionSource | 入并发队列 |
| `Status` | JobStatus | Running/Done/Interrupted |

**不变量**:

- beforeAssemblyReload 把进行中 job 标 Interrupted


## 接口

### GET /health `[入站]`

返回 HealthStatus,供握手与 Domain Reload 检测。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  ()
  ```
- **输出**:
  ```ts
  HealthStatus
  ```

### POST /rpc `[入站]`

JSON-RPC 调用工具,经 Dispatcher 主线程执行。请求头携带 session token。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  ToolRequest
  ```
- **输出**:
  ```ts
  ToolResult
  ```
- **错误码**: `UNITY_BUSY`, `MAIN_THREAD_TIMEOUT`, `DOMAIN_RELOAD_INTERRUPTED`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_

### GET /tools/list, /tools/describe `[入站]`

代理 ToolRegistry 的发现接口。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  { profile }
  ```
- **输出**:
  ```ts
  List<ToolDescriptor>
  ```

### MainThreadDispatcher `[出站]`

EditorApplication.update 回调逐帧 drain 并发队列、主线程执行、结果写回 TCS。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  Action 队列
  ```
- **错误码**: `MAIN_THREAD_TIMEOUT`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_


## 场景

### 短任务主线程执行

1. HTTP 后台线程收 /rpc
2. Action 入并发队列
3. EditorApplication.update 主线程 drain 执行
4. HTTP 线程 await TCS(默认 30s 超时)

**边界情况**:
- 超时 -> MAIN_THREAD_TIMEOUT 并尽量取消
- isCompiling/isUpdating -> 暂缓 drain 写操作

### 长任务 jobId 轮询

1. LongRunning 工具立即返回 jobId
2. 主线程后台分帧执行,结果存 job 表
3. 进度经 ToolContext.Progress 节流上报
4. Orchestrator 轮询拉取

### Domain Reload 恢复

1. beforeAssemblyReload 标 job Interrupted/关 HttpListener/持久化必要状态
2. afterAssemblyReload 生成新 serverId/重建 Bridge/重报 capabilities
3. Orchestrator 发现 serverId 变化知道重载发生

**边界情况**:
- ReloadSafe=true 幂等只读自动重发
- 写操作 -> DOMAIN_RELOAD_INTERRUPTED 交人确认
- reload 期间端口短暂不可用 -> Orchestrator 退避重试,勿当离线


## 约束

### Correctness

- 所有 Editor API 调用必须经 Dispatcher,Service 层不在监听线程直接碰 Unity API
- 执行单元短小、可在 Stage 边界检查取消
- 放行写操作前等待 isCompiling=false 稳定窗口

### Security

- 仅监听 127.0.0.1(loopback)
- Bridge 启动生成一次性 session token 写仅当前用户可读文件,/rpc 校验,防同机恶意进程直连


## 设计决策

- **v0.1 仅 HTTP loopback + jobId 轮询**
  - 理由: 最成熟稳妥,WebSocket 进度推送留 v0.2
  - 备选方案: 一开始上 WebSocket(增加 v0.1 不确定性)
- **serverId 变化作为 Domain Reload 信号**
  - 理由: 无需额外心跳协议,Orchestrator 轮询 /health 即可检测
  - 备选方案: 持久化 serverId(无法区分重载,被否决)
- **MainThread Dispatcher 经 EditorApplication.update**
  - 理由: HTTP 监听在后台线程,Unity API 必须主线程,需统一投递边界
  - 备选方案: 在监听线程直接调 Unity API(崩溃,被否决)

## 相关任务

| ID | 标题 | 阶段 | 状态 | 里程碑 |
|---|---|---|---|---|
| T3-1 | MainThreadDispatcher:后台线程到主线程的执行边界 | exec | NOT_STARTED | M3 |
| T3-2 | HTTP Bridge:/health + /rpc + /tools 端点(loopback + token) | exec | NOT_STARTED | M3 |
| T3-3 | Domain Reload 恢复:serverId 信号 + job 中断 + capabilities 重报 | exec | NOT_STARTED | M3 |

## 错误处理

- 方式: 统一 ToolError 错误码体系(见 ToolContract.md §6)。C# 异常 / Node 协议错误 / 审计错误共用一套码,每个错误带 Retriable 标志。
- 重试策略: AI 据 ToolError.Retriable 决定是否重试或换工具。Domain Reload 中断按工具 ReloadSafe 决定 -- 幂等只读(ReloadSafe=true)可自动重发,写操作返回 DOMAIN_RELOAD_INTERRUPTED 默认交人确认不自动重试。
- 降级行为: 写操作两阶段(Preview 产计划 / Execute 凭令牌落地)。失败返回结构化 ToolResult{Status,Error},绝不静默崩溃;配置迁移失败回退默认 + 显著告警 + 写审计。



## 相关依赖模块

- [mcp-server](mcp-server.md) — Node/TypeScript 实现的 MCP Server(Orchestrator),对外讲标准 MCP 协议给 A

## 相关社区

_(无图社区)_
