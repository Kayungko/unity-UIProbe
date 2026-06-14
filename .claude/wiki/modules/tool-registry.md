# tool-registry 模块文档

> 此文件由 wiki_gen 生成。手动编辑将在重新生成时保留。



> 关联文件: `UIProbe/Core/Tools`

## 职责

工具注册 / 发现 / 执行的统一入口。注册内置工具与项目扩展工具,维护 descriptor / 参数 schema / 安全等级 / 来源 / 启用状态,统一执行入口与 Preview/Execute 协议,并根据当前 Capability Profile 过滤、禁用或要求确认特定工具。MCP 与 UI 都不得绕过 ToolRegistry 直接调 Service。

## 所属路径

- UIProbe/Core/Tools

## 实体

### ToolRegistration (model)

一个已注册工具的运行时记录。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `Descriptor` | ToolDescriptor | 见 tool-contract 模块 |
| `Enabled` | bool | 运行时启用状态,受 Profile/policy 影响 |
| `Factory` | Func<UIProbeTool> | 工具实例工厂 |

**不变量**:

- Id 全局唯一,重复注册报错

### OperationTicket (model)

Preview 产出的操作票据,Execute 时校验。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `OperationId` | string |  |
| `ToolId` | string |  |
| `ExpiresAt` | DateTime | 有有效期,过期返回 OPERATION_EXPIRED |
| `PlannedChanges` | List<Change> |  |

**不变量**:

- 过期票据不可用于 Execute


## 接口

### Register `[入站]`

注册一个工具(内置或项目扩展)。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  ToolDescriptor + factory
  ```
- **输出**:
  ```ts
  void
  ```
- **错误码**: `TOOL_NOT_FOUND(重复 Id 报错)`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_

### ListTools `[入站]`

列出当前 Profile 可见的工具(NOT_IN_PROFILE 的不出现)。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  { profile, sessionId }
  ```
- **输出**:
  ```ts
  List<ToolDescriptor>
  ```

### DescribeTool `[入站]`

返回单个工具的完整 descriptor + 参数 schema。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  { toolId }
  ```
- **输出**:
  ```ts
  ToolDescriptor
  ```
- **错误码**: `TOOL_NOT_FOUND`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_

### Invoke `[入站]`

统一执行入口:校验 schema -> 按 Phase 调 Preview/Execute -> 返回 ToolResult。

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
- **错误码**: `TOOL_NOT_FOUND`, `INVALID_PARAMS`, `NOT_IN_PROFILE`, `PERMISSION_DENIED`, `CONFIRMATION_REQUIRED`, `OPERATION_EXPIRED`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_


## 场景

### 发现并描述工具

1. AI 调 ListTools 获取当前档位可见工具
2. 对候选工具调 DescribeTool 读参数 schema
3. 据 Description 选对工具

**边界情况**:
- 工具不在 Profile -> 不出现在 ListTools
- 工具不存在 -> TOOL_NOT_FOUND

### 统一执行判定

1. Invoke 收 ToolRequest
2. 校验 MinProfile <= activeProfile
3. 校验 policy allow/deny
4. 校验 enabled
5. 按授权模式 + Safety 判放行方式
6. 执行并写审计

**边界情况**:
- MinProfile 不满足 -> NOT_IN_PROFILE
- 被 deny -> PERMISSION_DENIED
- 需确认无 token -> CONFIRMATION_REQUIRED

### 项目扩展接入

1. 项目用 [UIProbeTool] Attribute 标注工具类
2. ToolRegistry 扫描注册为 project.* 来源
3. 无需重复实现 MCP 协议层

**边界情况**:
- 项目工具 MinProfile=TrustedProject,SafeDefault 下不可见


## 约束

### Correctness

- 参数反序列化与 schema 校验在 Invoke 统一做,不下放到各工具
- Execute 必须校验 OperationTicket 有效期与 ConfirmationToken

### Security

- 工具最终可用由 Profile + allow/deny policy + 用户授权 + enabled + client 权限共同决定
- 高风险工具默认不出现在普通 list_tools,除非 Profile 允许


## 设计决策

- **MCP/UI 统一经 ToolRegistry,不绕过**
  - 理由: 保证安全等级、Profile 过滤、审计、Preview/Execute 一致,避免双实现
  - 备选方案: MCP 直接调 Service(绕过治理,被否决)
- **Attribute 驱动的项目扩展 API**
  - 理由: 项目只写业务工具,不重复实现协议/连接/权限层
  - 备选方案: 项目各自新建 MCP server(正是要消除的现状)

## 相关任务

| ID | 标题 | 阶段 | 状态 | 里程碑 |
|---|---|---|---|---|
| T1-4 | ToolRegistry 骨架:注册/发现/调用(经 Adapter,只读路径) | exec | NOT_STARTED | M1 |
| T4-3 | v0.1 只读工具接入 Registry 并端到端打通(AI 向验收) | exec | NOT_STARTED | M4 |

## 错误处理

- 方式: 统一 ToolError 错误码体系(见 ToolContract.md §6)。C# 异常 / Node 协议错误 / 审计错误共用一套码,每个错误带 Retriable 标志。
- 重试策略: AI 据 ToolError.Retriable 决定是否重试或换工具。Domain Reload 中断按工具 ReloadSafe 决定 -- 幂等只读(ReloadSafe=true)可自动重发,写操作返回 DOMAIN_RELOAD_INTERRUPTED 默认交人确认不自动重试。
- 降级行为: 写操作两阶段(Preview 产计划 / Execute 凭令牌落地)。失败返回结构化 ToolResult{Status,Error},绝不静默崩溃;配置迁移失败回退默认 + 显著告警 + 写审计。



## 相关依赖模块

- [mcp-server](mcp-server.md) — Node/TypeScript 实现的 MCP Server(Orchestrator),对外讲标准 MCP 协议给 A

## 相关社区

_(无图社区)_
