# ui-check 模块文档

> 此文件由 wiki_gen 生成。手动编辑将在重新生成时保留。



> 关联文件: `UIProbe/Core/Services`, `UIProbe/UIProbeWindow_DuplicateChecker.cs`, `UIProbe/UIProbeWindow_DuplicateCheckerBatch.cs`, `UIProbe/UIProbeWindow_FilterNodeScanner.cs`, `UIProbe/Data/UIProbeChecker.cs`

## 职责

UICheckService(只读,含结构化报告)-- 这是其他 Unity MCP 难以提供的差异化能力,作为早期采用的核心拉力。把综合检测、重名/重复检测、过滤节点扫描统一成结构化 Issue 模型。初始检测项:重名节点、缺失 Sprite、缺失 Font、不必要 Raycast Target、空 Text、命名规范问题。过滤节点扫描并入本模块作为一类检测规则;复用 prefab-index 的 PrefabIndex。

## 所属路径

- UIProbe/Core/Services
- UIProbe/UIProbeWindow_DuplicateChecker.cs
- UIProbe/UIProbeWindow_DuplicateCheckerBatch.cs
- UIProbe/UIProbeWindow_FilterNodeScanner.cs
- UIProbe/Data/UIProbeChecker.cs

## 实体

### UICheckRequest (model)

检测请求。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `Targets` | List<string> | prefab 路径列表或全量 |
| `EnabledRules` | List<string> | 启用的规则集 |

### UICheckReport (entity)

结构化检测报告,UICheck 的单一数据源。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `Issues` | List<Issue> | 见 tool-contract 的 Issue 模型 |
| `Summary` | CheckSummary | 按 severity/rule 聚合 |
| `RanAt` | DateTime |  |

**不变量**:

- Issue 字段含 severity/ruleId/prefabPath/nodePath/componentType/message/suggestedFixId/canAutoFix,为后续 preview/apply fix 做准备


## 接口

### RunChecks `[入站]`

运行检测,产出结构化报告。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  UICheckRequest
  ```
- **输出**:
  ```ts
  UICheckReport
  ```
- **错误码**: `UNITY_OFFLINE`, `MAIN_THREAD_TIMEOUT`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_

### GetCheckResults `[入站]`

读取上次检测结果。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  ()
  ```
- **输出**:
  ```ts
  UICheckReport
  ```


## 场景

### AI 触发检测拿结构化报告

1. AI 调 run_ui_checks 指定 targets/rules
2. Service 基于 PrefabIndex 逐 prefab 跑规则
3. get_check_results 读结构化 Issue 列表
4. export_report 导出 md/csv/json

**边界情况**:
- 全量检测大项目 -> LongRunning + 进度
- 无问题 -> 空 Issue 列表 + Summary

### DuplicateChecker / FilterNodeScanner 并入

1. 重名/重复检测作为一组规则
2. 过滤节点扫描作为一类规则
3. 统一输出 Issue 模型


## 约束

### Correctness

- 只读,不修改 prefab
- Issue 模型统一,为 v0.2 的 preview/apply fix 预留 suggestedFixId/canAutoFix

### Quality

- 结构化报告是 v0.1 差异化能力,字段须完整可被 AI 消费


## 设计决策

- **Duplicate/FilterScanner 并入 UICheckService**
  - 理由: 都是基于 PrefabIndex 的检测规则,统一 Issue 模型避免多套结果结构
  - 备选方案: 各自独立 Service(结果模型分裂)
- **结构化报告作为 v0.1 核心拉力**
  - 理由: 其他 Unity MCP 难以提供,差异化早期采用动机

## 相关任务

| ID | 标题 | 阶段 | 状态 | 里程碑 |
|---|---|---|---|---|
| T2-3 | 抽离 UICheckService + 结构化报告(并入 Duplicate/FilterScanner,只读) | exec | NOT_STARTED | M2 |

## 错误处理

- 方式: 统一 ToolError 错误码体系(见 ToolContract.md §6)。C# 异常 / Node 协议错误 / 审计错误共用一套码,每个错误带 Retriable 标志。
- 重试策略: AI 据 ToolError.Retriable 决定是否重试或换工具。Domain Reload 中断按工具 ReloadSafe 决定 -- 幂等只读(ReloadSafe=true)可自动重发,写操作返回 DOMAIN_RELOAD_INTERRUPTED 默认交人确认不自动重试。
- 降级行为: 写操作两阶段(Preview 产计划 / Execute 凭令牌落地)。失败返回结构化 ToolResult{Status,Error},绝不静默崩溃;配置迁移失败回退默认 + 显著告警 + 写审计。



## 相关依赖模块

- [mcp-server](mcp-server.md) — Node/TypeScript 实现的 MCP Server(Orchestrator),对外讲标准 MCP 协议给 A

## 相关社区

_(无图社区)_
