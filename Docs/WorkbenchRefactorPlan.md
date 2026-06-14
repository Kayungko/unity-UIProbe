# UIProbe 工作台化重构计划（非 MCP 部分）

> 分支：`plan/workbench-refactor`  
> 范围：先记录 UIProbe 往“Unity UI 工作台”扩展所需的核心重构。  
> MCP 相关设计暂不展开，待后续确认 MCP 架构、边界、核心工具清单后另补文档。  
> 边界更新：PSD 解析、PSD → Prefab 自动生成、通用 Prefab 自动化属于其他项目需求，不纳入本仓库当前工作台规划。

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

后续建议补充：

```text
UIProbe.Runtime.asmdef       可选，放纯数据/运行时安全类型
UIProbe.Editor.asmdef        当前 Editor 工具主体
UIProbe.Tests.Editor.asmdef  后续测试
```

本阶段可以先记录，不强制实施。

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
- 多级撤销。
- 报告导出。

---

## 7. 第五阶段：工作台任务与报告模型

随着操作变多，需要统一任务结果与报告格式。

### 7.1 统一 ToolResult

```csharp
public sealed class UIProbeToolResult
{
    public bool Success;
    public string Message;
    public List<UIProbeIssue> Issues;
    public List<UIProbeChange> PlannedChanges;
    public List<UIProbeChange> AppliedChanges;
    public string ReportPath;
    public string Error;
}
```

### 7.2 统一 Change 模型

```text
changeType: create / update / delete / rename / move / import / export
assetPath
oldValue
newValue
canUndo
backupPath
```

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

## 10. 建议实施顺序

1. 新增文档与分支，确认工作台化方向。
2. 建立 `Core/Services` 与 `Core/Tools` 基础目录。
3. 抽离 `PrefabIndexService`。
4. 抽离 `AssetReferenceService`。
5. 抽离 `UICheckService` / `DuplicateCheckService`。
6. 抽离 `ImageProcessingService`。
7. 抽离 `RedGoldImportService`。
8. 建立统一 `ToolResult` / `ReportService`。
9. 建立内部 Flow 编排边界。
10. 再评估 UI Toolkit 工作台壳层与 MCP 独立文档。

---

## 11. 备注

工作台化重构不是一次性重写，而是渐进式迁移：旧 `UIProbeWindow` 保持可用，新 Service / Tool 层逐步接管业务逻辑。每抽离一个模块，都应保持原 UI 行为不变，并补充最小验证步骤。
