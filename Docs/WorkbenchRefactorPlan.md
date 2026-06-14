# UIProbe 工作台化重构计划（非 MCP 部分）

> 分支：`plan/workbench-refactor`  
> 范围：先记录 UIProbe 往“Unity UI 工作台”扩展所需的核心重构。  
> MCP 相关设计暂不展开，待后续确认 MCP 架构、边界、核心工具清单后另补文档。

---

## 1. 背景与目标

UIProbe 当前已经具备较完整的 Unity UI 开发辅助能力，包括预制体索引、资源引用追踪、综合检测、运行时拾取、图片规范化、批量命名、大红大金资源导入、截图、富文本生成、预制体助手等模块。

当前主要问题不是功能不足，而是功能大多仍围绕 `UIProbeWindow` 与 `DrawXXXTab()` 面板入口组织。随着后续扩展到 PSD 导入、Prefab 生成/更新、批量修复、报告流水线、自动化调用等场景，需要先把业务能力从 EditorWindow 中抽离，形成可复用、可测试、可编排的工作台内核。

本阶段目标：

1. 保持现有用户可见功能稳定。
2. 将核心业务能力从 UI 层逐步抽离为 Service / Tool 层。
3. 为后续 PSD 工作流、批量修复、报告系统、MCP/CLI 自动化打基础。
4. 不在本阶段实现 MCP 协议和外部进程桥接。

---

## 2. 总体架构方向

目标结构：

```text
UIProbe
├── Editor UI 层
│   ├── UIProbeWindow
│   ├── 各 Tab 绘制逻辑
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

- EditorWindow 只负责展示和交互，不直接承载业务算法。
- Service 负责稳定业务能力，可被 UI、未来 CLI、未来 MCP 共用。
- Tool 层负责统一输入输出、执行模式、安全等级、预览与执行约定。
- 写入型操作必须优先支持 preview，再支持 execute。
- 所有涉及 Unity Editor API 的逻辑需要明确主线程执行边界。

---

## 3. 第一阶段：整理工程边界

### 3.1 建议目录

```text
UIProbe/
├── Editor/
│   ├── Windows/
│   ├── Tabs/
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
- 后续 PSD → Prefab 流程。

---

## 8. 第六阶段：PSD / Prefab 工作流预留

本阶段不实现 PSD 解析，但预留服务边界。

### 8.1 目标流程

```text
PSD 解析
  ↓
图层 / 图片 / 文本 / 样式数据
  ↓
资源导出与规范化
  ↓
匹配已有 prefab / 组件 / 资源规则
  ↓
生成或更新 prefab
  ↓
运行 UI 检测
  ↓
导出报告与可撤销变更
```

### 8.2 预留服务

```text
PsdImportService
PrefabGenerationService
PrefabUpdateService
PrefabBindingRuleService
```

### 8.3 安全约定

任何 prefab 写入都必须支持：

- Preview。
- 变更列表。
- 备份或 Undo。
- 明确失败项。
- 报告导出。

---

## 9. 非目标范围

本分支当前不处理：

- MCP Server 实现。
- 外部 Node/Python 进程。
- WebSocket / HTTP Bridge。
- Domain Reload heartbeat。
- MCP tools schema。
- 与 Claude / Cursor / Codex 的接入。

这些内容会在 MCP 方向和核心功能确认后，另行补充到独立文档。

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
9. 再讨论 MCP Bridge 与外部 Orchestrator 文档。

---

## 11. 待讨论问题

- 是否在本轮就引入 asmdef。
- 是否将所有配置从 EditorPrefs 彻底迁移到 `UIProbeConfig`。
- 是否将当前 `partial class UIProbeWindow` 按 Tab 拆到 `Editor/Tabs`。
- Tool 层是否先做内部调用，还是直接按未来 MCP schema 设计。
- 报告系统优先 Markdown 还是 JSON/CSV。
- 写入型操作的 Undo 标准如何统一。
