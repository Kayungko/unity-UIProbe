# unity-adapters 模块文档

> 此文件由 wiki_gen 生成。手动编辑将在重新生成时保留。



> 关联文件: `UIProbe/Infrastructure/UnityAdapters`

## 职责

Unity API 抽象接缝,可测性的前提。现有业务大量直接调用 AssetDatabase / PrefabStageUtility / EditorPrefs / File 静态 API,导致 Service 抽离后仍无法单元测试。约定 Service 不直接调用静态 Unity API,而经 Adapter 接口注入 -- 生产用 Unity 实现,测试用内存假体。没有这层接缝,UIProbe.Tests.Editor.asmdef 形同虚设。

## 所属路径

- UIProbe/Infrastructure/UnityAdapters

## 实体

### IAssetGateway (interface)

封装 AssetDatabase / PrefabStage 相关静态调用。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `FindAssets` | method | 按 filter 查 GUID 列表 |
| `LoadAssetAtPath` | method |  |
| `MoveAsset` | method |  |
| `GUIDToAssetPath` | method |  |

**不变量**:

- 所有调用须在主线程(经 Dispatcher)

### IFileSystem (interface)

封装文件读写 / 备份 / 存在性检查。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `ReadAllText` | method |  |
| `WriteAllText` | method |  |
| `Backup` | method | 覆盖前备份,支持 FileBackup 撤销 |
| `Exists` | method |  |

**不变量**:

- 写路径受 authorization 模块的 write_allow/write_deny 约束

### IEditorPrefs (interface)

封装配置读写,替代直接 EditorPrefs 静态调用。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

| 字段 | 类型 | 约束 |
|-------|------|-------------|
| `GetString` | method |  |
| `SetString` | method |  |
| `HasKey` | method |  |


## 接口

### UnityAssetGateway `[出站]`

IAssetGateway 的生产实现,真实调用 AssetDatabase。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **错误码**: `IO_ERROR`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_

### InMemoryAssetGateway `[出站]`

IAssetGateway 的测试假体,内存模拟资源表。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |


### UnityFileSystem / InMemoryFileSystem `[出站]`

IFileSystem 的生产 / 测试实现对。

| 来源 | 位置 |
|------------|--------|
| DECLARED |  |

- **错误码**: `IO_ERROR`
  - _错误码按 `architecture/overview.md` → 错误策略声明的方式抛出（默认 `throw Error`）。_


## 场景

### Service 经接缝注入

1. Service 构造函数注入 IAssetGateway/IFileSystem/IEditorPrefs
2. 生产环境注入 Unity 实现
3. 测试环境注入内存假体

**边界情况**:
- 遗漏注入直接静态调用 -> 该 Service 不可单测(应在 code review 拦截)

### 黄金样本回归

1. 对模块录制黄金样本(输入 prefab/图集 -> 输出快照/CSV)
2. 经假体或真实实现跑 Service
3. 迁移前后 diff 对比保证用户可见行为零变化

**边界情况**:
- 大图 GetPixels 内存峰值 -- 假体需可控数据规模


## 约束

### Correctness

- Service 依赖接口而非静态 API
- 项目内资源尽量走 AssetDatabase 而非裸 File,降低 meta/GUID 风险

### Performance

- 几千 prefab 全量索引、大图 GetPixels 的内存峰值需可控,留增量更新空间


## 设计决策

- **三个核心 Adapter 接口(Asset/File/EditorPrefs)**
  - 理由: 覆盖现有业务最主要的静态 Unity 依赖,接缝最小且够用
  - 备选方案: 完整 mock 整个 UnityEditor(过度工程), 不抽接缝(则无法单测,被否决)

## 相关任务

| ID | 标题 | 阶段 | 状态 | 里程碑 |
|---|---|---|---|---|
| T1-3 | 定义 Adapter 接缝接口 + Unity 实现 + 内存假体 | prep | NOT_STARTED | M1 |
| T1-5 | 黄金样本回归基线机制 | exec | NOT_STARTED | M1 |

## 错误处理

- 方式: 统一 ToolError 错误码体系(见 ToolContract.md §6)。C# 异常 / Node 协议错误 / 审计错误共用一套码,每个错误带 Retriable 标志。
- 重试策略: AI 据 ToolError.Retriable 决定是否重试或换工具。Domain Reload 中断按工具 ReloadSafe 决定 -- 幂等只读(ReloadSafe=true)可自动重发,写操作返回 DOMAIN_RELOAD_INTERRUPTED 默认交人确认不自动重试。
- 降级行为: 写操作两阶段(Preview 产计划 / Execute 凭令牌落地)。失败返回结构化 ToolResult{Status,Error},绝不静默崩溃;配置迁移失败回退默认 + 显著告警 + 写审计。



## 相关依赖模块

- [mcp-server](mcp-server.md) — Node/TypeScript 实现的 MCP Server(Orchestrator),对外讲标准 MCP 协议给 A

## 相关社区

_(无图社区)_
