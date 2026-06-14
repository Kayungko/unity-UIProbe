# prefab-index 模块文档

> 此文件由 wiki_gen 生成。手动编辑将在重新生成时保留。



> 关联文件: `UIProbe/Core/Services`, `UIProbe/Core/ResourceScanner.cs`, `UIProbe/Core/ResourceCacheManager.cs`, `UIProbe/Data/PrefabIndexData.cs`, `UIProbe/UIProbeWindow_Indexer.cs`

## 职责

Prefab Index 是后续工作台的核心底座,优先抽离为 PrefabIndexService(只读)。从 UIProbeWindow_Indexer.cs 抽出查找 prefab、构建 folder tree、收集 Image/RawImage/Prefab/Material 等资源引用、保存加载 IndexCache、搜索展开的非 UI 部分。PrefabIndex 是多个能力的单一数据源 -- 引用追踪、重复检测、嵌套总览、过滤扫描都基于它派生,不各自缓存。先抽离它以验证 ToolContract + Adapter 接缝 + 黄金样本基线闭环。

## 所属路径

- UIProbe/Core/Services
- UIProbe/Core/ResourceScanner.cs
- UIProbe/Core/ResourceCacheManager.cs
- UIProbe/Data/PrefabIndexData.cs
- UIProbe/UIProbeWindow_Indexer.cs

## 实体

### PrefabIndex (entity)

全量 prefab 索引,含缓存。多个 Service 的共同底座。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `Items` | List<PrefabIndexItem> |  |
| `FolderTree` | FolderNode | 目录树 |
| `SchemaVersion` | int | 版本不符时自动重建而非读坏数据 |
| `BuiltAt` | DateTime |  |

**不变量**:

- IndexCache 版本不符直接重建,不尝试迁移
- 仅由 PrefabIndexService 持有,其他模块只读派生

### PrefabIndexItem (entity)

单个 prefab 的索引条目。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `Guid` | string |  |
| `AssetPath` | string |  |
| `ReferencedAssets` | List<AssetRef> | Image/RawImage/Prefab/Material 等引用 |
| `ComponentSummary` | string |  |

### PrefabIndexBuildOptions (model)

构建参数。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `RootFolders` | List<string> |  |
| `Incremental` | bool | 增量更新策略 |


## 接口

### BuildIndex `[入站]`

构建 prefab 索引(可增量)。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  PrefabIndexBuildOptions
  ```
- **输出**:
  ```ts
  PrefabIndex
  ```
- **错误码**: `UNITY_OFFLINE`, `MAIN_THREAD_TIMEOUT`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_

### LoadCache `[入站]`

加载缓存索引。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  ()
  ```
- **输出**:
  ```ts
  PrefabIndex
  ```
- **错误码**: `IO_ERROR`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_

### SaveCache `[入站]`

保存索引缓存。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  PrefabIndex
  ```
- **输出**:
  ```ts
  void
  ```
- **错误码**: `IO_ERROR`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_

### Search `[入站]`

按 query 搜索 prefab。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  PrefabIndex, string query
  ```
- **输出**:
  ```ts
  List<PrefabIndexItem>
  ```

### GetPrefabDetail `[入站]`

查看单个 prefab 详情(组件/引用)。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **输入**:
  ```ts
  { guid | path }
  ```
- **输出**:
  ```ts
  PrefabIndexItem
  ```
- **错误码**: `TOOL_NOT_FOUND`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_


## 场景

### AI 构建并搜索索引

1. AI 调 build_prefab_index
2. get_prefab_index_status 确认就绪
3. search_prefabs 按关键词搜
4. get_prefab_detail 看详情

**边界情况**:
- 几千 prefab 全量索引 -> 走 LongRunning + jobId + 进度
- 缓存版本不符 -> 自动重建

### 迁移保持 UI 行为不变

1. UIProbeWindow_Indexer.cs 保留搜索框/收藏夹/树视图/批量选择按钮
2. 业务数据改由 PrefabIndexService 提供
3. 黄金样本 diff 验证行为零变化


## 约束

### Correctness

- 只读,不触发 Domain Reload
- Search/展开匹配的非 UI 逻辑下沉到 Service

### Performance

- 几千 prefab 全量索引内存峰值可控,支持增量更新
- 大型结果走虚拟化,不为每条数据常驻 VisualElement


## 设计决策

- **PrefabIndex 作为单一数据源**
  - 理由: 引用追踪/重复检测/嵌套总览/过滤扫描都基于它派生,避免多份副本不一致
  - 备选方案: 各模块各自扫描缓存(数据不一致风险)
- **首个抽离的 Service**
  - 理由: 是工作台底座,且用它验证 ToolContract + Adapter + 黄金样本闭环

## 相关任务

| ID | 标题 | 阶段 | 状态 | 里程碑 |
|---|---|---|---|---|
| T2-1 | 抽离 PrefabIndexService(工作台底座,只读) | exec | NOT_STARTED | M2 |

## 错误处理

- 方式: 统一 ToolError 错误码体系(见 ToolContract.md §6)。C# 异常 / Node 协议错误 / 审计错误共用一套码,每个错误带 Retriable 标志。
- 重试策略: AI 据 ToolError.Retriable 决定是否重试或换工具。Domain Reload 中断按工具 ReloadSafe 决定 -- 幂等只读(ReloadSafe=true)可自动重发,写操作返回 DOMAIN_RELOAD_INTERRUPTED 默认交人确认不自动重试。
- 降级行为: 写操作两阶段(Preview 产计划 / Execute 凭令牌落地)。失败返回结构化 ToolResult{Status,Error},绝不静默崩溃;配置迁移失败回退默认 + 显著告警 + 写审计。



## 相关依赖模块

- [mcp-server](mcp-server.md) — Node/TypeScript 实现的 MCP Server(Orchestrator),对外讲标准 MCP 协议给 A

## 相关社区

_(无图社区)_
