# UIProbe 工作台化重构计划（非 MCP 部分）

> 分支：`plan/workbench-refactor`  
> 范围：UIProbe 往"Unity UI 工作台"扩展所需的核心服务化重构。  
> 上位文档：`RefactorRoadmap.md`（关键路径、MVP、决策总账）。工具结果/契约一律引用 `ToolContract.md`，不在本文档另行定义。  
> 边界：PSD 解析、PSD → Prefab、通用 Prefab 自动化属于其他项目需求，不纳入本仓库规划。  
> 平台基线：**Unity 2022.3 LTS+**。

---

## 0. 基于现状的关键修正（实证）

抽查现有代码后，以下与原计划假设不符，规划须据此调整：

- **`Core/` 目录已被占用**：已存在 `ResourceCacheManager.cs`、`ResourceScanner.cs`。新增 Service 不可当空地规划，需先理清与既有 Core 类的职责边界，避免撞名/重叠。
- **主窗口实际有约 15 个 Tab，本计划的 Service 蓝图只覆盖约 8 个**。Picker（运行时拾取）、界面记录（UIRecordDiffer）、Adaptor、FilterNodeScanner、RichTextGenerator、NestingOverview 在蓝图里无归属，需补全功能对照清单（见 §12），否则迁移易烂尾。
- **`UIProbeConfig` 已有 `version` 字段 + `MigrateFromEditorPrefs()`**：复用此迁移雏形，扩展为统一迁移链，不另起炉灶。
- **全仓库无 asmdef**：测试与 package 化的前置工作，见 §3.2 与 `DistributionAndVersioning.md` §2.1。

## 0.1 实施顺序的硬性纠正

**`ToolContract`（统一 ToolResult / Change）必须在第一个 Service 之前冻结**。原 §10 把它排在第 8 步，会导致每个 Service 各写一套结果模型再返工。本计划的实施顺序据此调整（见 §10）。

---

## 1. 背景与目标

UIProbe 当前已经具备较完整的 Unity UI 开发辅助能力，包括预制体索引、资源引用追踪、综合检测、运行时拾取、图片规范化、批量命名、大红大金资源导入、截图、富文本生成、预制体助手等模块。

当前主要问题不是功能不足，而是功能大多仍围绕 `UIProbeWindow` 与 `DrawXXXTab()` 面板入口组织。随着后续扩展到更完整的 UI 工作台体验、批量检测、批量修复、报告流水线、自动化调用等场景，需要先把业务能力从 EditorWindow 中抽离，形成可复用、可测试、可编排的工作台内核。

本阶段目标：

1. 保持现有用户可见功能稳定。
2. 将核心业务能力从 UI 层逐步抽离为 Service / Tool 层。
3. 为后续批量修复、报告系统、MCP/CLI 自动化打基础。
4. 不在本阶段实现 MCP 协议和外部进程桥接。
5. 不把 PSD 解析、PSD 导入、PSD → Prefab、通用 Prefab 自动生成作为 UIProbe 当前目标。

---

## 2. 总体架构方向

目标结构：

```text
UIProbe
├── Editor UI 层
│   ├── UIProbeWindow
│   ├── UIProbeWorkbenchWindow（后续 UI Toolkit 工作台入口）
│   ├── 各 Tab / Panel 绘制逻辑
│   └── 用户交互、确认弹窗、预览展示
│
├── Workbench Tool 层
│   ├── ToolRegistry
│   ├── ToolDescriptor
│   ├── ToolRequest / ToolResult
│   └── Preview / Execute 双阶段工具约定
│
├── Core Service 层
│   ├── PrefabIndexService
│   ├── AssetReferenceService
│   ├── DuplicateCheckService
│   ├── UICheckService
│   ├── ImageProcessingService
│   ├── RedGoldImportService
│   ├── ScreenshotService
│   ├── ReportService
│   ├── ConfigService
│   └── StorageService
│
└── Data / Model 层
    ├── PrefabIndexData
    ├── CheckResultData
    ├── ImageProcessingData
    ├── RedGoldDataTypes
    └── ReportData
```

核心原则：

- EditorWindow / UI Toolkit View 只负责展示和交互，不直接承载业务算法。
- Service 负责稳定业务能力，可被 UI、未来 CLI、未来 MCP 共用。
- Tool 层负责统一输入输出、执行模式、安全等级、预览与执行约定。
- 写入型操作必须优先支持 preview，再支持 execute。
- 所有涉及 Unity Editor API 的逻辑需要明确主线程执行边界。
- 本仓库聚焦 UIProbe 现有能力的工作台化，不承接另一个项目的 PSD / Prefab 自动化目标。

---

## 3. 第一阶段：整理工程边界

### 3.1 建议目录

```text
UIProbe/
├── Editor/
│   ├── Windows/
│   ├── Tabs/
│   ├── Panels/
│   └── GUI/
│
├── Core/
│   ├── Context/
│   ├── Services/
│   ├── Tools/
│   └── Models/
│
├── Data/
│   └── 现有数据类逐步迁移或保留
│
└── Infrastructure/
    ├── Storage/
    ├── Config/
    ├── Reporting/
    └── UnityAdapters/
```

短期可以先不强制移动所有文件，避免一次性大改造成风险；可以先新增 `Core/Services` 与 `Core/Tools`，再逐个模块迁移。

### 3.2 asmdef / package 化

```text
UIProbe.Runtime.asmdef       可选，放纯数据/运行时安全类型
UIProbe.Editor.asmdef        Editor 工具主体（Editor-only，引用 UI Toolkit 程序集）
UIProbe.Tests.Editor.asmdef  测试
```

现状全仓库无 asmdef、在 Assets 下全局编译。asmdef 化是测试与 package 化的**前置工作**，不能一直推迟。package 分发细节见 `DistributionAndVersioning.md` §2.1。

### 3.3 Unity API 抽象接缝（可测性前提）

现有业务大量直接调用 `AssetDatabase` / `PrefabStageUtility` / `EditorPrefs` / `File` 静态 API，导致 Service 抽离后仍无法单元测试。**约定：Service 不直接调用静态 Unity API，而经 Adapter 接口注入**：

```csharp
public interface IAssetGateway { /* FindAssets / Load / Move / GUID 等 */ }
public interface IFileSystem   { /* 读写 / 备份 / 存在性 */ }
public interface IEditorPrefs  { /* 配置读写 */ }
```

Service 依赖接口，生产用 Unity 实现，测试用内存假体。没有这层接缝，`UIProbe.Tests.Editor.asmdef` 形同虚设。

### 3.4 测试策略与回归基线

- **单元测试**：纯算法（如 `ImageNormalizer` 的内容边界计算）与经 Adapter 注入的 Service 逻辑。
- **黄金样本回归基线**：当前零测试，渐进迁移最大风险是"抽离后行为悄悄变了"。先对每个模块录制黄金样本（输入 prefab/图集 → 输出快照/CSV），作为迁移前后 diff 基线，保证用户可见行为零变化。
- 优先为即将抽离的模块补基线，再动手抽离。

---

## 4. 第二阶段：抽离 Prefab Index 能力

Prefab Index 是后续工作台的核心底座，优先抽离。

### 4.1 目标服务

```csharp
public sealed class PrefabIndexService
{
    PrefabIndex BuildIndex(PrefabIndexBuildOptions options);
    PrefabIndex LoadCache();
    void SaveCache(PrefabIndex index);
    IReadOnlyList<PrefabIndexItem> Search(PrefabIndex index, string query);
    IReadOnlyList<AssetReferenceResult> FindAssetReferences(PrefabIndex index, AssetReferenceQuery query);
}
```

### 4.2 迁移内容

从当前 Indexer 面板逻辑中抽出：

- 查找 prefab。
- 构建 folder tree。
- 收集 Image / RawImage / Prefab / Material 等资源引用。
- 保存和加载 IndexCache。
- 搜索与展开匹配逻辑中的非 UI 部分。

### 4.3 UI 保持方式

`UIProbeWindow_Indexer.cs` 保留：

- 搜索框。
- 收藏夹展示。
- 树形视图绘制。
- 批量选择按钮。

但业务数据来自 `PrefabIndexService`。

---

## 5. 第三阶段：抽离资源引用与检测能力

### 5.1 AssetReferenceService

目标：统一处理“某资源被哪些 prefab / 节点 / 组件使用”。

```csharp
public sealed class AssetReferenceService
{
    IReadOnlyList<AssetReferenceResult> FindReferences(AssetReferenceQuery query);
    string ExportCsv(IReadOnlyList<AssetReferenceResult> results, ExportOptions options);
}
```

支持查询：

- 按资源路径。
- 按资源名。
- 按 GUID。
- 按 Sprite 名称。
- 按引用类型过滤。

### 5.2 DuplicateCheckService / UICheckService

目标：把综合检测结果变成统一模型。

```csharp
public sealed class UICheckService
{
    UICheckReport RunChecks(UICheckRequest request);
}
```

初始检测项：

- 重名节点。
- 缺失 Sprite。
- 缺失 Font。
- 不必要 Raycast Target。
- 空 Text。
- 命名规范问题。

结果结构应统一包含：

```text
severity
ruleId
prefabPath
nodePath
componentType
message
suggestedFixId
canAutoFix
```

为后续 preview/apply fix 做准备。

---

## 6. 第四阶段：图片工具服务化

当前图片规范化、批量命名、大红大金导入已有较多 Data 层文件，应继续服务化。

### 6.1 ImageProcessingService

```csharp
public sealed class ImageProcessingService
{
    ImageScanResult Scan(ImageScanOptions options);
    ImageNormalizePreview PreviewNormalize(ImageNormalizeRequest request);
    ImageNormalizeResult ExecuteNormalize(ImageNormalizeRequest request);
    BatchRenamePreview PreviewRename(BatchRenameRequest request);
    BatchRenameResult ExecuteRename(BatchRenameRequest request);
}
```

要求：

- 扫描和预览不写文件。
- execute 写文件前返回明确风险与冲突信息。
- 项目内资源尽量使用 AssetDatabase API，降低 meta/GUID 风险。
- 保留日志与撤销能力。

### 6.2 RedGoldImportService

```csharp
public sealed class RedGoldImportService
{
    RedGoldPreview LoadPreview(RedGoldImportOptions options);
    RedGoldImportResult Execute(RedGoldExecuteRequest request);
    RedGoldUndoResult UndoLast();
}
```

保留并强化：

- 表格解析。
- 图片匹配。
- 命名分配。
- 品质输出路径。
- 覆盖前备份。
- 多级撤销（映射到 `ToolContract.md` §10 的 `UndoCapability.MultiLevelStack`：多级栈 + 表格回滚；区别于图片规范化的 `FileBackup` 与 prefab 改名的 `UnityUndo`）。
- 报告导出。

---

## 7. 第五阶段：工作台任务与报告模型

随着操作变多，需要统一任务结果与报告格式。

### 7.1 统一 ToolResult（引用 `ToolContract.md`）

**不在本文档定义结果模型**。`ToolResult`（含 `ToolStatus` 状态机、`Error` 错误码、`OperationId`、`Issues`、`PlannedChanges`/`AppliedChanges`、`Risks`）以 `ToolContract.md` §4 为唯一权威定义。本阶段的工作是：让已抽离的各 Service 不再各自返回裸 `bool` / 字符串，而是统一经 Tool 层产出 `ToolResult`。

> 注意：原计划在此处定义的 `UIProbeToolResult`（裸 `bool Success`）已废弃。任何 Service 结果都应映射到 `ToolContract.md` 的 `ToolResult`，避免双重定义与返工。

### 7.2 统一 Change 模型（引用 `ToolContract.md`）

`Change` 与撤销能力分级（`UndoCapability`：None / UnityUndo / FileBackup / MultiLevelStack）同样以 `ToolContract.md` §10 为准，本文档不另行定义。报告导出消费的就是这套 `Change` / `Issue` 模型。

### 7.3 ReportService

目标导出：

- Markdown。
- CSV。
- JSON。

报告适用：

- UI 检测。
- 资源引用。
- 批量重命名。
- 图片规范化。
- 大红大金导入。

---

## 8. 第六阶段：工作台 Flow 与批量任务预留

本阶段不实现外部 MCP/CLI，但内部服务层应预留可编排边界，方便未来把多个能力组合成稳定 Flow。

### 8.1 建议内部 Flow

```text
ScanProjectUIFlow
CheckSelectedPrefabFlow
FindAssetImpactFlow
ExportUIAuditReportFlow
NormalizeImageFolderFlow
RedGoldImportFlow
```

### 8.2 Flow 设计原则

- Flow 只编排 Tool / Service，不直接写 UI。
- Flow 返回统一 `UIProbeToolResult` 或派生结果。
- Flow 内部写操作仍遵循 preview / execute。
- Flow 运行过程要输出可导出的报告。
- Flow 应能被 Editor UI、未来 CLI、未来 MCP 复用。

---

## 9. 非目标范围

本分支当前不处理：

- MCP Server 实现。
- 外部 Node/Python 进程。
- WebSocket / HTTP Bridge。
- Domain Reload heartbeat。
- MCP tools schema。
- 与 Claude / Cursor / Codex 的接入。
- PSD 解析。
- PSD 导入。
- PSD → Prefab 自动生成。
- 通用 Prefab 自动生成 / 更新流水线。

这些 MCP 相关内容会在 MCP 方向和核心功能确认后，另行补充到独立文档。PSD / Prefab 自动化属于其他项目需求，不在本仓库当前计划中展开。

---

## 10. 建议实施顺序（已据 §0.1 纠正）

> 关键纠正：`ToolContract`（统一 `ToolResult` / `Change`）与 Adapter 接缝必须在第一个 Service 之前冻结，否则每个 Service 各写一套结果模型再返工。原顺序把统一结果模型排在第 8 步，已废弃。关键路径与 MVP 验收以 `RefactorRoadmap.md` 为准。

1. 新增文档与分支，确认工作台化方向。
2. **冻结 `ToolContract.md`**（`ToolResult` / `Change` / `Issue` / 错误码 / Preview-Execute），作为所有 Service 的产出契约。
3. **建立 asmdef**（`UIProbe.Editor` / `UIProbe.Runtime` / `UIProbe.Tests.Editor`，见 §3.2）与 Adapter 接缝（`IAssetGateway` / `IFileSystem` / `IEditorPrefs`，见 §3.3）。
4. 建立 `Core/Services` 与 `Core/Tools` 基础目录，搭最小 `ToolRegistry`。
5. 为即将抽离的模块录制黄金样本回归基线（见 §3.4）。
6. 抽离 `PrefabIndexService`（首个 Service，验证契约 + 接缝 + 基线闭环）。
7. 抽离 `AssetReferenceService`。
8. 抽离 `UICheckService` / `DuplicateCheckService`。
9. 抽离 `ImageProcessingService`。
10. 抽离 `RedGoldImportService`。
11. 补全 `ReportService`（消费 §7 的 `ToolResult` / `Change`）。
12. 建立内部 Flow 编排边界。
13. 再评估 UI Toolkit 工作台壳层（见 `UIToolkitWorkbenchPlan.md`）与 MCP 链路（见 `MCPReplacementPlan.md`）。

---

## 11. 备注

工作台化重构不是一次性重写，而是渐进式迁移：旧 `UIProbeWindow` 保持可用，新 Service / Tool 层逐步接管业务逻辑。每抽离一个模块，都应保持原 UI 行为不变，并补充最小验证步骤。

---

## 12. 全功能对照清单（15 Tab → Service 归属）

原蓝图（§2）的 Service 只覆盖约 8 个能力，而主窗口实际有约 15 个 Tab。未归属的能力若不在迁移前明确落点，渐进迁移极易烂尾。下表把每个 Tab 映射到目标 Service 与**单一数据源 owner**（该数据的权威持有者，其他模块只读引用，避免多份副本不一致）。

| Tab / 能力 | 目标 Service | 单一数据源 owner | 备注 |
|---|---|---|---|
| Indexer（预制体索引） | `PrefabIndexService` | `PrefabIndex`（含缓存） | 工作台底座，最先抽离 |
| 资源引用追踪 | `AssetReferenceService` | 查询时基于 `PrefabIndex` 派生 | 不另存副本，依赖 Index |
| 综合检测 | `UICheckService` | `UICheckReport` | Issue 模型见 ToolContract §10 |
| 重名/重复检测 | `DuplicateCheckService` | 复用 `PrefabIndex` | 可并入 UICheckService 的规则集 |
| 图片规范化 | `ImageProcessingService` | `ImageScanResult` | 撤销 = `FileBackup` |
| 批量命名 | `ImageProcessingService` | 复用扫描结果 | Preview/Execute 两阶段 |
| 大红大金导入 | `RedGoldImportService` | `RedGoldData` + 多级撤销栈 | 撤销 = `MultiLevelStack` |
| 截图 | `ScreenshotService` | 输出文件路径 | 写控目录 |
| 富文本生成（RichTextGenerator） | `RichTextService`（新增归属） | 生成结果无持久态 | 原蓝图遗漏，补归属 |
| 运行时拾取（Picker） | `RuntimePickService`（新增归属） | 运行时选中态，非持久 | PlayMode 边界，需主线程约束 |
| 界面记录差异（UIRecordDiffer） | `UIRecordService`（新增归属） | 录制快照 | 原蓝图遗漏，diff 基线可复用 |
| Adaptor（适配） | `AdaptorService`（新增归属） | 适配规则配置 | 原蓝图遗漏 |
| 过滤节点扫描（FilterNodeScanner） | 并入 `UICheckService` | 复用 `PrefabIndex` | 作为一类检测规则 |
| 嵌套总览（NestingOverview） | 并入 `PrefabIndexService` 查询 | 基于 `PrefabIndex` 派生 | 视图层能力，无独立数据源 |
| 配置 | `ConfigService` | `UIProbeConfig`（已有 version + `MigrateFromEditorPrefs`） | 复用现有迁移雏形，见 §0 |

原则：
- **单一数据源**：`PrefabIndex` 是多个能力的共同底座，只由 `PrefabIndexService` 持有；引用追踪、重复检测、嵌套总览、过滤扫描都基于它派生，不各自缓存。
- **新增归属的四项**（RichText / Picker / UIRecord / Adaptor）原蓝图未覆盖，此处先确立 Service 落点，具体抽离顺序排在核心五个 Service 之后。
- **PlayMode / 主线程敏感能力**（Picker、截图）须显式标注主线程执行边界（见 §2 核心原则）。
