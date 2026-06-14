# mcp-server 模块文档

> 此文件由 wiki_gen 生成。手动编辑将在重新生成时保留。



> 关联文件: `mcp-server/src`

## 职责

Node/TypeScript 实现的 MCP Server(Orchestrator),对外讲标准 MCP 协议给 AI 客户端,对内通过 HTTP loopback 连 Unity Bridge。负责:连接/会话管理、版本与契约握手、工具发现缓存、把 MCP tool 调用翻译成 Bridge /rpc、Domain Reload 期间退避重试与恢复、把 Unity 端结构化错误透传成 MCP 错误。v0.1 只暴露只读工具,自身不直接碰 Unity API。多 Unity 实例时按 projectPath 路由。

## 所属路径

- mcp-server/src

## 实体

### BridgeConnection (entity)

到单个 Unity 实例的连接状态。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `projectPath` | string | 路由键,多实例区分 |
| `baseUrl` | string | 127.0.0.1:port |
| `sessionToken` | string | 从受控文件读,/rpc 携带 |
| `lastServerId` | guid | 与 /health 比对检测 Domain Reload |
| `state` | ConnectionState | Connecting/Ready/Reloading/Offline |

**不变量**:

- serverId 变化即判定 Domain Reload,标记 Reloading 并退避重试
- 端口短暂不可用时退避而非立即判 Offline

### ToolCacheEntry (model)

从 Bridge /tools/list 拉取后缓存的工具描述。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `descriptor` | ToolDescriptor | 见 tool-contract |
| `fetchedAtServerId` | guid | serverId 变化即失效重拉 |

**不变量**:

- Domain Reload 后缓存失效,重新 /tools/list

### HandshakeResult (model)

启动握手结果。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `contractVersionOk` | bool | major 不符直接拒绝连接 |
| `uiProbeVersion` | string | 记录用于诊断 |

**不变量**:

- contractVersion major 不一致 -> 拒绝,提示升级


## 接口

### ListTools (MCP) `[入站]`

MCP 客户端发现工具,代理 Bridge /tools/list 并走缓存。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  ()
  ```
- **输出**:
  ```ts
  List<McpTool>
  ```
- **错误码**: `UNITY_OFFLINE`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_

### CallTool (MCP) `[入站]`

MCP 客户端调用工具,翻译为 Bridge POST /rpc。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  { name, arguments }
  ```
- **输出**:
  ```ts
  ToolResult
  ```
- **错误码**: `UNITY_OFFLINE`, `UNITY_BUSY`, `DOMAIN_RELOAD_INTERRUPTED`, `MAIN_THREAD_TIMEOUT`, `TOOL_NOT_FOUND`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_

### Handshake `[出站]`

连接 Bridge 时校验 contractVersion/uiProbeVersion。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  ()
  ```
- **输出**:
  ```ts
  HandshakeResult
  ```
- **错误码**: `VERSION_MISMATCH`, `UNITY_OFFLINE`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_

### WaitForReload `[出站]`

Domain Reload 期间轮询 /health 等待新 serverId 稳定后恢复。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  ()
  ```
- **输出**:
  ```ts
  void
  ```
- **错误码**: `RELOAD_TIMEOUT`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_


## 场景

### AI 端到端只读调用

1. MCP 客户端 ListTools
2. Server 走缓存或 /tools/list
3. CallTool 翻译为 /rpc 携带 sessionToken
4. Bridge 主线程执行返回 ToolResult
5. Server 透传给 MCP 客户端

**边界情况**:
- Unity 未启动 -> UNITY_OFFLINE 友好提示
- 工具不存在 -> TOOL_NOT_FOUND

### Domain Reload 期间调用

1. 调用中 /health serverId 变化
2. 标记 Reloading,失效工具缓存
3. 退避重试轮询 /health 直到新 serverId 稳定
4. ReloadSafe 只读自动重发,否则回 DOMAIN_RELOAD_INTERRUPTED

**边界情况**:
- reload 超时 -> RELOAD_TIMEOUT 提示稍后重试
- 端口短暂不可用 -> 退避不判 Offline

### 多 Unity 实例路由

1. 发现多个 Bridge 实例(不同 projectPath)
2. 按调用上下文 projectPath 选 BridgeConnection
3. 各自维护 serverId/token/缓存

**边界情况**:
- projectPath 缺失 -> 默认单实例或要求显式指定


## 约束

### Correctness

- Server 不直接碰 Unity API,一切经 Bridge /rpc
- 工具缓存以 serverId 为失效边界,Domain Reload 后必重拉
- 错误码透传不吞,保留 Retriable 语义

### Security

- sessionToken 从仅当前用户可读文件读取,不写日志不外泄
- 仅连 127.0.0.1 loopback


## 设计决策

- **Node/TypeScript 实现 MCP Server**
  - 理由: MCP 生态成熟,AI 客户端对接成本低,与 Unity 端职责清晰分离
  - 备选方案: C# 内嵌 MCP(与 Editor 生命周期耦合,Domain Reload 风险高)
- **工具缓存以 serverId 为失效边界**
  - 理由: Domain Reload 后工具集可能变化,serverId 变化是天然失效信号
  - 备选方案: TTL 过期(可能用到过期工具集)
- **v0.1 只暴露只读工具**
  - 理由: 先验证端到端链路稳定性,写操作与授权治理留 v0.2
  - 备选方案: v0.1 直接上写操作(链路未验证即引入风险)

## 相关任务

| ID | 标题 | 阶段 | 状态 | 里程碑 |
|---|---|---|---|---|
| T4-1 | MCP Server 骨架:连接管理 + 版本握手(Node/TypeScript) | prep | NOT_STARTED | M4 |
| T4-2 | 工具代理:ListTools/CallTool 翻译 + 工具缓存(serverId 失效) | exec | NOT_STARTED | M4 |
| T4-4 | MCP Server Domain Reload 退避重试与恢复 | exec | NOT_STARTED | M4 |

## 错误处理

- 方式: 统一 ToolError 错误码体系(见 ToolContract.md §6)。C# 异常 / Node 协议错误 / 审计错误共用一套码,每个错误带 Retriable 标志。
- 重试策略: AI 据 ToolError.Retriable 决定是否重试或换工具。Domain Reload 中断按工具 ReloadSafe 决定 -- 幂等只读(ReloadSafe=true)可自动重发,写操作返回 DOMAIN_RELOAD_INTERRUPTED 默认交人确认不自动重试。
- 降级行为: 写操作两阶段(Preview 产计划 / Execute 凭令牌落地)。失败返回结构化 ToolResult{Status,Error},绝不静默崩溃;配置迁移失败回退默认 + 显著告警 + 写审计。



## 相关依赖模块

- [asset-reference](asset-reference.md) — AssetReferenceService(只读),统一处理某资源被哪些 prefab / 节点 / 组件使用。不另存副
- [authorization](authorization.md) — 权限与授权治理,两个正交维度:Capability Profile(能力面,决定哪些工具/路径可见可调)与 Author
- [prefab-index](prefab-index.md) — Prefab Index 是后续工作台的核心底座,优先抽离为 PrefabIndexService(只读)。从 UIPr
- [tool-contract](tool-contract.md) — UIProbe 工具层的唯一权威契约。UI Toolkit 工作台、MCP Server、内部 Flow 全部构造 To
- [tool-registry](tool-registry.md) — 工具注册 / 发现 / 执行的统一入口。注册内置工具与项目扩展工具,维护 descriptor / 参数 schema 
- [ui-check](ui-check.md) — UICheckService(只读,含结构化报告)-- 这是其他 Unity MCP 难以提供的差异化能力,作为早期采用
- [unity-adapters](unity-adapters.md) — Unity API 抽象接缝,可测性的前提。现有业务大量直接调用 AssetDatabase / PrefabStage
- [unity-bridge](unity-bridge.md) — Unity Editor 内的本地 HTTP JSON-RPC bridge(v0.1 仅 HTTP + loopbac

## 相关社区

_(无图社区)_
