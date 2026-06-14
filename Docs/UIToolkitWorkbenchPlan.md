# UIProbe UI Toolkit 工作台视觉层重构计划

> 分支：`plan/workbench-refactor`  
> 范围：记录 UIProbe 从 IMGUI EditorWindow 逐步迁移到 UI Toolkit 工作台界面的计划。  
> 本文只讨论 Editor UI / 视觉层 / 交互体验，不涉及 MCP Server、外部进程、Domain Reload Bridge 等内容。

---

## 1. 结论

可以使用 Unity UI Toolkit 重构 UIProbe 的工作台界面，而且这是推荐方向。

但迁移顺序不应是“先把所有 DrawXXXTab 改成 UI Toolkit”，而应是：

```text
先抽 Core Services
  ↓
再建立 Workbench Shell
  ↓
逐个模块从 IMGUI 迁移到 UI Toolkit
  ↓
最后统一主题、布局、任务面板、报告面板
```

原因：当前 UIProbe 的主要技术债不是视觉控件，而是业务逻辑仍较多分布在 `UIProbeWindow` 与各 `DrawXXXTab()` 方法中。若直接迁移 UI Toolkit，容易把旧耦合原样搬到新的 UXML / C# View 里。

---

## 2. 目标体验

UIProbe 后续应从“左侧 Tab 工具集合”升级为“Unity UI 工作台”。

目标视觉结构：

```text
UIProbe Workbench
├── Top Bar
│   ├── 当前项目状态
│   ├── 索引状态
│   ├── 最近任务
│   └── 设置 / 关于 / 更新提示
│
├── Left Navigation
│   ├── 运行时拾取
│   ├── 预制体索引
│   ├── 资源引用
│   ├── UI 检测
│   ├── 图片工具
│   ├── 截图工具
│   ├── 预制体助手
│   └── 后续 PSD / Prefab 工作流
│
├── Main Content
│   ├── 当前模块主操作区
│   ├── 预览列表
│   ├── TreeView / TableView / Inspector
│   └── 空状态 / 加载状态 / 错误状态
│
└── Bottom / Right Panel
    ├── 任务进度
    ├── 结果摘要
    ├── Issues
    ├── Planned Changes
    └── Report Export
```

---

## 3. 为什么适合 UI Toolkit

### 3.1 工作台布局更稳定

当前 IMGUI 方式适合快速做工具，但复杂工作台会遇到：

- 嵌套滚动区难维护。
- 表格、树、详情面板状态混在 OnGUI 中。
- 样式复用弱。
- 高级视觉反馈成本高。
- 不利于后续主题化和模块卡片化。

UI Toolkit 更适合长期工作台界面：

- USS 可统一主题。
- UXML 可拆分界面结构。
- TreeView / ListView 更适合索引、检测结果、资源引用。
- TwoPaneSplitView 适合左树右详情。
- VisualElement 状态更清晰，便于局部刷新。

### 3.2 更适合服务层之后的 MVVM/MVP

服务层抽出后，UI Toolkit 可以按 View / Presenter / Service 组织：

```text
WorkbenchWindow
  ↓
WorkbenchShellView
  ↓
ModuleView / Presenter
  ↓
ToolRegistry / Core Services
```

UI 只订阅状态，不直接承担业务扫描、导入、写文件。

---

## 4. 推荐目录结构

建议新增：

```text
UIProbe/
├── Editor/
│   ├── Workbench/
│   │   ├── UIProbeWorkbenchWindow.cs
│   │   ├── UIProbeWorkbenchController.cs
│   │   ├── UIProbeWorkbenchState.cs
│   │   └── UIProbeWorkbenchRouter.cs
│   │
│   ├── Views/
│   │   ├── Shell/
│   │   │   ├── WorkbenchShell.uxml
│   │   │   └── WorkbenchShell.uss
│   │   ├── PrefabIndex/
│   │   ├── AssetReferences/
│   │   ├── UIChecks/
│   │   ├── ImageTools/
│   │   ├── Reports/
│   │   └── Common/
│   │
│   └── IMGUIBridge/
│       └── LegacyIMGUIContainerAdapters.cs
│
├── Core/
│   ├── Services/
│   ├── Tools/
│   └── Models/
│
└── Resources/
    └── UIProbe/
        ├── Icons/
        └── Themes/
```

短期也可以不移动现有文件，先新增 `Editor/Workbench` 与 `Editor/Views`，逐步替换。

---

## 5. 迁移原则

### 5.1 不一次性重写所有 Tab

推荐先做新 Shell，再把旧 IMGUI Tab 嵌进去：

```csharp
var legacyContainer = new IMGUIContainer(() =>
{
    DrawIndexerTab();
});
```

这样可以先得到新的工作台外壳、导航、主题和任务面板，同时保留现有功能稳定。

### 5.2 每迁移一个模块，必须先服务化

迁移顺序建议：

```text
PrefabIndexService 抽离完成
  ↓
PrefabIndex UI Toolkit View
  ↓
AssetReferenceService 抽离完成
  ↓
AssetReference UI Toolkit View
  ↓
UICheckService 抽离完成
  ↓
UICheck UI Toolkit View
```

不要把旧业务逻辑直接塞进新的 VisualElement。

### 5.3 UI Toolkit View 只做展示和交互

View 层不应直接调用：

```text
AssetDatabase.FindAssets
PrefabUtility
File.WriteAllText
EditorPrefs
JsonUtility 持久化
```

这些应进入 Service / Infrastructure 层。

---

## 6. 第一批适合迁移的模块

### 6.1 Prefab Index

优先级最高。

适合使用：

```text
Toolbar
SearchField
TreeView
ListView
TwoPaneSplitView
Inspector-like Detail Panel
```

目标体验：

```text
左侧：Prefab 文件夹树
中间：Prefab 搜索结果 / 列表
右侧：Prefab 详情、资源引用、快捷操作
底部：索引状态、最近更新时间、导出入口
```

### 6.2 Asset References

适合使用表格化结果视图：

```text
资源路径
Prefab
节点路径
组件类型
引用类型
额外信息
```

后续可以接 ReportService 导出 CSV / Markdown / JSON。

### 6.3 UI Checks

适合做成 Issues 面板：

```text
severity
ruleId
prefabPath
nodePath
message
suggestedFix
```

并预留 Preview Fix / Apply Fix 按钮。

### 6.4 Image Tools

图片工具 UI 复杂，建议稍后迁移。

当前大红大金、批量命名、图片规范化都有大量状态和预览逻辑。先服务化，再迁移。短期可以继续用 IMGUIContainer 嵌入。

---

## 7. 工作台状态模型

建议引入统一状态：

```csharp
public sealed class UIProbeWorkbenchState
{
    public string ActiveModuleId;
    public bool IsIndexReady;
    public string LastIndexUpdateTime;
    public List<UIProbeTaskState> RecentTasks;
    public List<UIProbeIssue> CurrentIssues;
    public UIProbeToolResult LastResult;
}
```

每个模块维护自己的 ViewState：

```csharp
public sealed class PrefabIndexViewState
{
    public string SearchText;
    public IReadOnlyList<PrefabIndexItem> Results;
    public PrefabIndexItem SelectedItem;
    public bool IsLoading;
    public string ErrorMessage;
}
```

---

## 8. 视觉主题方向

建议先做一套轻量 USS 主题：

```text
--uiprobe-bg-main
--uiprobe-bg-panel
--uiprobe-border
--uiprobe-text-main
--uiprobe-text-muted
--uiprobe-accent
--uiprobe-warning
--uiprobe-danger
--uiprobe-success
```

实际 USS 中可用 class 管理：

```text
.uiprobe-shell
.uiprobe-sidebar
.uiprobe-nav-item
.uiprobe-nav-item--active
.uiprobe-card
.uiprobe-section-title
.uiprobe-issue-error
.uiprobe-issue-warning
.uiprobe-issue-info
.uiprobe-toolbar
.uiprobe-empty-state
```

避免在 C# 中硬编码大量颜色。

---

## 9. 与现有 IMGUI 的兼容策略

短期允许双栈并存：

```text
新 Workbench Shell：UI Toolkit
旧模块内容：IMGUIContainer
新迁移模块：纯 UI Toolkit
```

过渡期菜单可以保留：

```text
UI Probe/打开面板              -> 旧 UIProbeWindow
UI Probe/打开工作台 Experimental -> 新 UIProbeWorkbenchWindow
```

等核心模块迁移稳定后，再把默认入口切到新工作台。

---

## 10. 风险与注意事项

### 10.1 UI Toolkit 不是业务重构替代品

如果业务逻辑仍在 Window / View 中，UI Toolkit 只会让代码换一种形式继续耦合。

### 10.2 大型 List / Tree 要注意虚拟化

Prefab 索引、资源引用、检测结果可能很多。必须优先用虚拟化列表，不要为每条数据手写大量 VisualElement 并长期常驻。

### 10.3 写入型操作仍需 Preview / Execute

无论 UI 多漂亮，写入型能力都必须保持：

```text
Preview
  ↓
显示 Planned Changes / Risks
  ↓
用户确认
  ↓
Execute
  ↓
Report / Undo
```

### 10.4 不要第一版就追求完全主题系统

先统一布局、导航、列表、卡片、Issues 面板；高级主题可后置。

---

## 11. 建议实施顺序

1. 保留现有 `UIProbeWindow`。
2. 新增 `UIProbeWorkbenchWindow`，作为 Experimental 入口。
3. 用 UI Toolkit 搭建 Shell：Top Bar / Sidebar / Main / Bottom Result Panel。
4. 旧模块先通过 `IMGUIContainer` 嵌入。
5. 抽离 `PrefabIndexService`。
6. 用 UI Toolkit 重写 Prefab Index 模块。
7. 抽离并重写 Asset References 模块。
8. 抽离并重写 UI Checks 模块。
9. 建立统一 Task / Issue / Report 面板。
10. 图片工具服务化后再逐步迁移。
11. 稳定后将默认入口从旧面板切换到新工作台。

---

## 12. 与 MCP 的关系

本计划不实现 MCP。

但 UI Toolkit 工作台与未来 MCP 共用同一个基础方向：

```text
UI Toolkit View
        ↓
ToolRegistry
        ↓
Core Services
```

未来 MCP 也应走：

```text
External MCP / Orchestrator
        ↓
ToolRegistry
        ↓
Core Services
```

因此 UI Toolkit 重构应避免直接绑定 UI 事件到业务算法，而应绑定到 Tool / Service 层。这样后续 MCP 接入时，不需要重复实现同一套能力。
