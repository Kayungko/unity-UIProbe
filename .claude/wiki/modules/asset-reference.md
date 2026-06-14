# asset-reference 模块文档

> 此文件由 wiki_gen 生成。手动编辑将在重新生成时保留。



> 关联文件: `UIProbe/Core/Services`, `UIProbe/UIProbeWindow_AssetReferences.cs`

## 职责

AssetReferenceService(只读),统一处理某资源被哪些 prefab / 节点 / 组件使用。不另存副本,查询时基于 prefab-index 模块的 PrefabIndex 派生。支持按资源路径、资源名、GUID、Sprite 名称、引用类型过滤查询,并可导出 CSV。

## 所属路径

- UIProbe/Core/Services
- UIProbe/UIProbeWindow_AssetReferences.cs

## 实体

### AssetReferenceQuery (model)

引用查询条件。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `AssetPath` | string | 可选 |
| `AssetName` | string | 可选 |
| `Guid` | string | 可选 |
| `SpriteName` | string | 可选 |
| `ReferenceTypeFilter` | List<string> | 按引用类型过滤 |

**不变量**:

- 至少提供一个查询维度

### AssetReferenceResult (model)

一条引用结果。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `AssetPath` | string |  |
| `PrefabPath` | string |  |
| `NodePath` | string |  |
| `ComponentType` | string |  |
| `ReferenceType` | string | Image/RawImage/Material 等 |


## 接口

### FindReferences `[入站]`

查询某资源被哪些 prefab/节点/组件引用。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  AssetReferenceQuery
  ```
- **输出**:
  ```ts
  List<AssetReferenceResult>
  ```
- **错误码**: `INVALID_PARAMS`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_

### ExportCsv `[入站]`

导出引用结果为 CSV。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  List<AssetReferenceResult>, ExportOptions
  ```
- **输出**:
  ```ts
  string(reportPath)
  ```
- **错误码**: `IO_ERROR`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_


## 场景

### AI 查资源影响面

1. AI 调 find_asset_references 传资源路径/GUID/Sprite 名
2. Service 基于 PrefabIndex 派生引用列表
3. 返回结构化结果

**边界情况**:
- 资源未被引用 -> 空列表(非错误)
- PrefabIndex 未构建 -> 提示先 build_prefab_index

### 导出引用报告

1. 查询结果
2. ExportCsv 写受控目录
3. 返回 reportPath


## 约束

### Correctness

- 只读,基于 PrefabIndex 派生不另存副本

### Performance

- 大结果集走流式/分页,避免一次性物化


## 设计决策

- **依赖 prefab-index 而非自建索引**
  - 理由: 单一数据源,保证引用结果与索引一致
  - 备选方案: 独立扫描(数据不一致 + 重复成本)

## 相关任务

| ID | 标题 | 阶段 | 状态 | 里程碑 |
|---|---|---|---|---|
| T2-2 | 抽离 AssetReferenceService(基于 PrefabIndex 派生,只读) | exec | NOT_STARTED | M2 |

## 错误处理

- 方式: 统一 ToolError 错误码体系(见 ToolContract.md §6)。C# 异常 / Node 协议错误 / 审计错误共用一套码,每个错误带 Retriable 标志。
- 重试策略: AI 据 ToolError.Retriable 决定是否重试或换工具。Domain Reload 中断按工具 ReloadSafe 决定 -- 幂等只读(ReloadSafe=true)可自动重发,写操作返回 DOMAIN_RELOAD_INTERRUPTED 默认交人确认不自动重试。
- 降级行为: 写操作两阶段(Preview 产计划 / Execute 凭令牌落地)。失败返回结构化 ToolResult{Status,Error},绝不静默崩溃;配置迁移失败回退默认 + 显著告警 + 写审计。



## 相关依赖模块

- [mcp-server](mcp-server.md) — Node/TypeScript 实现的 MCP Server(Orchestrator),对外讲标准 MCP 协议给 A

## 相关社区

_(无图社区)_
