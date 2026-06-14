# authorization 模块文档

> 此文件由 wiki_gen 生成。手动编辑将在重新生成时保留。



> 关联文件: `mcp-server/src/auth`, `UIProbe/Infrastructure/Authorization`

## 职责

权限与授权治理,两个正交维度:Capability Profile(能力面,决定哪些工具/路径可见可调)与 Authorization Mode(批准策略,决定调用时是否需人确认)。Profile 含 SafeDefault/TeamAutomation/TrustedProject/AdminDebug;Mode 含 请求批准/替我批准/完全访问/自定义。配置经 mcp.config.toml(团队共享入库)+ mcp.local.toml(本地覆盖不入库)叠加。写操作经 write_allow/write_deny 路径约束。所有调用落审计 JSONL(不入库)。token 鉴权防同机恶意进程直连。v0.1 只读不触发授权判定,框架预留。

## 所属路径

- mcp-server/src/auth
- UIProbe/Infrastructure/Authorization

## 实体

### CapabilityProfile (model)

能力面配置,决定工具与路径的可见可调范围。与 Authorization Mode 正交。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `name` | string | SafeDefault/TeamAutomation/TrustedProject/AdminDebug |
| `allowedTools` | List<string> | 可调工具白名单或通配 |
| `writeAllow` | List<string> | 可写路径白名单 |
| `writeDeny` | List<string> | 禁写路径(优先于 allow) |

**不变量**:

- writeDeny 优先于 writeAllow
- SafeDefault 为最小面默认值

### AuthorizationMode (model)

批准策略,决定写操作调用是否需人确认。与 Capability Profile 正交。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `mode` | string | 请求批准/替我批准/完全访问/自定义 |
| `autoApproveTools` | List<string> | 自定义模式下免确认工具 |

**不变量**:

- 请求批准=每次写都需人确认
- 完全访问=写免确认但仍受 Profile 路径约束

### AuthDecision (model)

一次调用的授权判定结果。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `allowed` | bool |  |
| `requiresApproval` | bool |  |
| `denyReason` | string | PERMISSION_DENIED 时填 |

**不变量**:

- 先判 Profile 可见可调,再判 Mode 是否需确认,二者皆过才放行

### AuditEntry (model)

审计日志条目,JSONL 逐行追加。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `timestamp` | string | ISO8601 |
| `toolName` | string |  |
| `profile` | string |  |
| `mode` | string |  |
| `decision` | string | allowed/denied/approval-required |
| `changesSummary` | string | 写操作影响面摘要 |

**不变量**:

- 审计目录不入版本控制
- MCP 工具不得写/删审计目录


## 接口

### LoadConfig `[出站]`

加载 mcp.config.toml(共享)叠加 mcp.local.toml(本地)得到生效 Profile + Mode。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  ()
  ```
- **输出**:
  ```ts
  { profile, mode }
  ```
- **错误码**: `CONFIG_INVALID`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_

### Authorize `[出站]`

对一次工具调用做统一授权判定。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  { toolName, changes }
  ```
- **输出**:
  ```ts
  AuthDecision
  ```
- **错误码**: `PERMISSION_DENIED`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_

### Audit `[出站]`

把调用与判定结果追加写审计 JSONL。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  AuditEntry
  ```
- **输出**:
  ```ts
  void
  ```
- **错误码**: `IO_ERROR`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_


## 场景

### 统一判定流程

1. LoadConfig 得生效 Profile + Mode
2. Authorize 先按 Profile 判工具可调 + 路径 writeAllow/writeDeny
3. 再按 Mode 判是否 requiresApproval
4. 二者皆过放行,Audit 落账

**边界情况**:
- 工具不在 allowedTools -> PERMISSION_DENIED
- 写路径命中 writeDeny -> PERMISSION_DENIED
- 请求批准模式 -> requiresApproval=true 交人确认

### 团队共享+本地覆盖

1. mcp.config.toml 定义团队基线 Profile
2. mcp.local.toml 本地覆盖个别项(不入库)
3. 叠加得生效配置

**边界情况**:
- local 缺失 -> 仅用 config
- config 非法 -> CONFIG_INVALID 拒绝启动

### v0.1 只读预留

1. 只读工具调用不触发写授权判定
2. 框架与配置结构就位,v0.2 写操作接入


## 约束

### Correctness

- Profile 与 Mode 严格正交,分别判定不混淆
- writeDeny 始终优先 writeAllow
- 判定顺序固定:先 Profile 后 Mode

### Security

- token 鉴权防同机恶意进程直连 Bridge
- 审计 JSONL 不入版本控制,MCP 工具不得写/删审计目录
- mcp.local.toml 不入库避免泄露本地放权


## 设计决策

- **Capability Profile 与 Authorization Mode 正交**
  - 理由: 能力面(能做什么)与批准策略(是否需确认)是独立维度,正交组合覆盖团队/个人多场景
  - 备选方案: 单一权限等级(无法表达能力面与确认策略的独立变化)
- **config.toml 入库 + local.toml 不入库叠加**
  - 理由: 团队基线可共享审查,本地放权不污染仓库不泄露
  - 备选方案: 仅本地配置(团队无法共享基线), 仅入库配置(本地无法临时放权)
- **审计 JSONL 不入版本控制且工具不可写其目录**
  - 理由: 防止 AI 通过工具篡改/清除自己的审计痕迹
  - 备选方案: 审计入库(噪音大且可被工具改写)

## 相关任务

*暂无关联任务*

## 错误处理

- 方式: 统一 ToolError 错误码体系(见 ToolContract.md §6)。C# 异常 / Node 协议错误 / 审计错误共用一套码,每个错误带 Retriable 标志。
- 重试策略: AI 据 ToolError.Retriable 决定是否重试或换工具。Domain Reload 中断按工具 ReloadSafe 决定 -- 幂等只读(ReloadSafe=true)可自动重发,写操作返回 DOMAIN_RELOAD_INTERRUPTED 默认交人确认不自动重试。
- 降级行为: 写操作两阶段(Preview 产计划 / Execute 凭令牌落地)。失败返回结构化 ToolResult{Status,Error},绝不静默崩溃;配置迁移失败回退默认 + 显著告警 + 写审计。



## 相关依赖模块

- [mcp-server](mcp-server.md) — Node/TypeScript 实现的 MCP Server(Orchestrator),对外讲标准 MCP 协议给 A

## 相关社区

_(无图社区)_
