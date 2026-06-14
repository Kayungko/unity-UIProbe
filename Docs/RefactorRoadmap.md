# UIProbe 重构路线图（统领文档）

> 分支：`plan/workbench-refactor`
> 角色：本文档是 UIProbe 工作台化 + 自有 MCP 重构的**唯一统领入口**。其余文档（服务化、UI Toolkit、MCP 替代、授权、工具契约、分发版本）都是本文档的展开，先读本文再读其余。
> 状态：方案已收敛，决策已锁定，进入实施规划阶段。

---

## 1. 平台与范围基线

| 项 | 决策 |
|---|---|
| 目标 Unity 版本 | **2022.3 LTS+**（UI Toolkit 的 TreeView / TwoPaneSplitView / ListView 动态高度以此为可用基线） |
| 首发 MVP 方向 | **AI 向**（先打通 MCP 链路 + 只读工具，UI Toolkit 整体推后） |
| MCP Server 语言 | **Node / TypeScript** |
| 明确不做 | PSD 解析、PSD 导入、PSD → Prefab、通用 Prefab 自动生成流水线（属其他项目） |
| 兼容策略 | **不做 legacy 兼容层**；UIProbe MCP 开发完成后直接替代其他 Unity MCP |

---

## 2. 唯一关键路径：Service 化 MVP

三大块（服务化重构 / UI Toolkit / MCP）都依赖同一个最小底座。把它单独定义为**唯一关键路径**，避免三线并行陷入"旧能力被拆散、新能力未成型"的真空期。

```text
Service 化 MVP（关键路径，必须最先完成）
├── ToolContract            统一工具契约（见 ToolContract.md）
├── ToolRegistry            工具注册 / 发现 / 执行入口
├── Unity API Adapter 接缝   AssetDatabase / File / EditorPrefs / PrefabStage 经注入，不直接静态调用
├── PrefabIndexService      （只读）
├── AssetReferenceService   （只读）
└── UICheckService          （只读，含结构化报告 —— 差异化能力）
```

完成这个最小集后，UI 向与 AI 向两条线才从这里分叉：

```text
              ┌──────────────► AI 向（首发）：Unity Bridge + Node MCP Server + 只读工具
Service 化 MVP ┤
              └──────────────► UI 向（推后）：UI Toolkit 工作台 Shell + 逐模块迁移
```

**实施顺序的硬性纠正**：`ToolContract`（统一 ToolResult / Change）必须在**第一个 Service 之前**冻结。原计划把它排在第 8 步，会导致每个 Service 各写一套结果模型再返工。

---

## 3. 两个 MVP 的验收标准

### 3.1 首发：AI 向 MVP（v0.1）

**目标**：装上 UIProbe 后，AI client（Claude / Cursor / Codex）当天能完成一组只读的 UI 工程理解任务。

**验收标准（端到端可用）**：
- AI 能通过 MCP 判断 Unity 是否在线、是否编译中、当前项目与选中对象。
- AI 能让 UIProbe 构建/查询 Prefab 索引、搜索 prefab、查看 prefab 详情。
- AI 能查询"某资源被哪些 prefab/节点/组件引用"。
- AI 能触发 UI 检测并拿到**结构化报告**（这是其他 Unity MCP 难以提供的差异化能力，作为早期采用的核心拉力）。
- 全程只读，不触发 Domain Reload；Domain Reload 发生时链路能自动恢复。

**不在 v0.1**：测试 / 编译 / Play Mode / 任何写操作 / 高风险档位（全部推到 v0.2+，因为它们触发 Domain Reload，需 Bridge 恢复机制先稳）。

### 3.2 推后：UI 向 MVP

**目标**：新建 UI Toolkit 工作台窗口（Experimental 入口），把 **Prefab 索引**一个模块做成完整 UI Toolkit 界面，其余模块用 `IMGUIContainer` 过渡嵌入。

**前置条件**：AI 向 MVP 跑通、Service 化 MVP 稳定。
**理由推后**：现有 IMGUI 界面仍可正常使用，UI Toolkit 在 2022.3 是成熟技术、风险低，可让位给技术最不确定的 MCP 链路。

---

## 4. 分阶段能力路线

| 阶段 | 内容 | 安全等级 |
|---|---|---|
| **v0.1（首发）** | 只读 Editor 状态 + UIProbe 领域只读能力（约 17 个工具），全部 ReadOnly/PreviewOnly | 只读 |
| **v0.2** | console 读写、trigger/get compile、take_screenshot；图片规范化 / 批量命名的 preview/execute | →WriteSafe |
| **v0.3** | Play Mode、EditMode/PlayMode 测试、项目扩展 Tool API、RedGold 导入 execute | WriteSafe，需 Domain Reload 恢复 |
| **高风险（独立档位，不排期）** | 菜单执行、文件读写、editor script、反射、外部进程 | TrustedProject / AdminDebug |
| **UI Toolkit 迁移** | Prefab 索引 → 资源引用 → UI 检测 → 图片工具（最后）；逐模块从 IMGUI 迁移 | 与 MCP 共用 Service |

---

## 5. 文档地图

| 文档 | 职责 |
|---|---|
| `RefactorRoadmap.md`（本文） | 统领：平台基线、关键路径、MVP 验收、阶段路线、文档地图 |
| `ToolContract.md` | Tool 层契约单一来源（UI 与 MCP 共用）。**所有结果/工具描述以此为准** |
| `WorkbenchRefactorPlan.md` | 非 MCP 服务化重构（Service 抽离、Adapter 接缝、撤销分级、测试策略） |
| `UIToolkitWorkbenchPlan.md` | UI Toolkit 视觉层迁移（推后执行） |
| `MCPReplacementPlan.md` | MCP 总体架构、Unity Bridge、Dispatcher、Domain Reload、工具清单 |
| `MCPAuthorizationModel.md` | 授权模式与 Capability Profile 正交治理、配置、审计 |
| `DistributionAndVersioning.md` | UPM + Node 版本绑定、config/cache 迁移、分发形态 |

冲突仲裁规则：涉及工具契约以 `ToolContract.md` 为准；涉及优先级与里程碑以本文档为准。

---

## 6. 横切关注点登记（不得遗漏）

这些点在早期文档缺失，现登记为全程必须处理的横切项：

1. **统一错误码体系** —— C# 异常 / Node 协议错误 / 审计错误共用一套错误码，AI 可据此决策重试/换工具。定义见 `ToolContract.md`。
2. **AI 端工具描述质量** —— `describe_tool` 的 name/description/参数 schema 须达到"AI 能据此选对工具"的质量标准，作为 v0.1 验收的一部分。
3. **可测性接缝** —— Service 不直接调用静态 Unity API，经 Adapter 注入；配套黄金样本回归基线（迁移前后 diff）。
4. **撤销能力分级** —— 不假设单一 Change 模型通吃；RedGold 的多级栈/表格回滚需独立分级。
5. **token 鉴权** —— loopback 之外，需防止同机恶意进程直连；完全访问档位尤其需要 client 鉴别。
6. **审计隐私与存储** —— 审计 JSONL 含路径/代码 hash，明确存储位置、是否脱敏、是否纳入版本控制（默认不纳入）。
7. **大项目性能/内存** —— 几千 prefab 全量索引、大图 `GetPixels()` 的内存峰值与增量更新策略。
8. **现状对齐** —— `Core/` 目录已被 `ResourceCacheManager`/`ResourceScanner` 占用；主窗口实际 15 个 Tab；`UIProbeConfig` 已有 version + 迁移雏形。规划须基于现状而非空地假设。

---

## 7. 决策总账（已锁定）

- 平台：Unity 2022.3 LTS+；首发 AI 向；Server 用 Node/TS。
- 关键路径：Service 化 MVP（ToolContract + ToolRegistry + Adapter 接缝 + 3 个只读 Service）。
- 传输：HTTP loopback + jobId 轮询（WebSocket 留 v0.2）；不做独立 CLI，Flow 仅作 Core 内部编排。
- 运行时：主线程经 `EditorApplication.update` Dispatcher；多实例端口探测 + serverId 路由；Domain Reload 靠 serverId 变化检测，写操作标 `interrupted`。
- 工具：不做 legacy 兼容；来源仅 `builtin` / `project` / `experimental`；统一 ToolContract（含 operationId / confirmationToken / undoId / 错误码 / schema 版本）；长任务用 ToolContext 注入取消/进度/日志。
- 权限：授权模式 ⊥ Capability Profile 正交（Profile 卡能力上限，授权模式管放行方式）；配置 `mcp.config.toml`（版控）+ `mcp.local.toml`（本地仅能收紧）；loopback 强制；AdminDebug 强制 loopback；审计 JSONL 留 30 天。
- 质量：Adapter 注入 + 黄金样本回归基线；撤销能力分级。

---

## 8. 待后续细化（非阻塞）

- v0.2/v0.3 各工具的 preview/execute 细节。
- Tool Center UI 的信息架构（待 UI Toolkit Shell 定型）。
- 高风险档位（AdminDebug）的具体工具实现。
- 多 Unity 实例 + 多 UIProbe 版本并存时的路由细节。
