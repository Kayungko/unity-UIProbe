# UIProbe 工作台化与自有 MCP 重构 — 会话地图

> 由 scaffold_gen 生成。手动修改将在重新生成时被覆盖。

## Session Start Checklist

1. 阅读本文件了解项目概览
2. 检查 `.claude/progress/session-state.json` 了解当前进度
3. 查询 `graph.json` 或 MCP 预定位任务相关节点与社区
4. 阅读分配的任务文件
5. 确认工作区就绪状态
6. 按 Plan-Build-Verify-Fix 流程执行

## Search Protocol — MCP First

**MUST** 先用 graph 结构查询，再做 raw 文本搜索：

1. `mcp__graphify__query_graph("<keyword>")` — 定位相关节点
2. `mcp__graphify__get_node` / `get_neighbors` / `shortest_path` — 展开邻居关系与跨文件调用链
3. **仅当上述未命中** 时再用 `Grep` / `Glob` 做 raw 搜索

**AVOID** 在 `graph.json` 已存在的情况下跳过步骤 1-2 直接对仓库做大范围正则 Grep。

示例：

```
mcp__graphify__query_graph({ query: "TcpClient", limit: 5 })
```

## Operating Model

- 单一事实源: [.claude/project.json](.claude/project.json)
- Flow: `/plan` -> `/build` -> `/verify` -> `/fix`
- Change flow: `/amend`




## Workflow Policy

详见 [`.claude/project.json`](.claude/project.json)

## Workspace Readiness

- 就绪度目标: `governance_ready`
- 当前状态: `governance_ready`
- Sprint 行为由就绪度决定
- 就绪度阻塞:
  _(none)_

## Rules

- `.claude/rules/architecture.md`
- `.claude/rules/completion-status.md`
- `.claude/rules/csharp-safety.md`
- `.claude/rules/decision-protocol.md`
- `.claude/rules/platform-isolation.md`
- `.claude/rules/scope-discipline.md`
- `.claude/rules/testing-gates.md`
- `.claude/rules/thread-safety.md`

## Module Ownership

| Module | Owner | Paths | Wiki |
|--------|-------|-------|------|
| asset-reference | core-asset-reference | `UIProbe/Core/Services`, `UIProbe/UIProbeWindow_AssetReferences.cs` | [wiki](.claude/wiki/modules/asset-reference.md) |
| authorization | core-authorization | `mcp-server/src/auth`, `UIProbe/Infrastructure/Authorization` | [wiki](.claude/wiki/modules/authorization.md) |
| mcp-server | core-mcp-server | `mcp-server/src` | [wiki](.claude/wiki/modules/mcp-server.md) |
| prefab-index | core-prefab-index | `UIProbe/Core/Services`, `UIProbe/Core/ResourceScanner.cs`, `UIProbe/Core/ResourceCacheManager.cs`, `UIProbe/Data/PrefabIndexData.cs`, `UIProbe/UIProbeWindow_Indexer.cs` | [wiki](.claude/wiki/modules/prefab-index.md) |
| tool-contract | core-tool-contract | `UIProbe/Core/Tools/Models` | [wiki](.claude/wiki/modules/tool-contract.md) |
| tool-registry | core-tool-registry | `UIProbe/Core/Tools` | [wiki](.claude/wiki/modules/tool-registry.md) |
| ui-check | core-ui-check | `UIProbe/Core/Services`, `UIProbe/UIProbeWindow_DuplicateChecker.cs`, `UIProbe/UIProbeWindow_DuplicateCheckerBatch.cs`, `UIProbe/UIProbeWindow_FilterNodeScanner.cs`, `UIProbe/Data/UIProbeChecker.cs` | [wiki](.claude/wiki/modules/ui-check.md) |
| unity-adapters | core-unity-adapters | `UIProbe/Infrastructure/UnityAdapters` | [wiki](.claude/wiki/modules/unity-adapters.md) |
| unity-bridge | core-unity-bridge | `UIProbe/Infrastructure/Bridge` | [wiki](.claude/wiki/modules/unity-bridge.md) |

## Commands

- [.claude/commands/plan.md](.claude/commands/plan.md) — 规划任务实现
- [.claude/commands/build.md](.claude/commands/build.md)
- [.claude/commands/verify.md](.claude/commands/verify.md)
- [.claude/commands/fix.md](.claude/commands/fix.md)
- [.claude/commands/sprint.md](.claude/commands/sprint.md) — 波次调度并行执行
- [.claude/commands/sprint-review.md](.claude/commands/sprint-review.md)
- [.claude/commands/sync-progress.md](.claude/commands/sync-progress.md)
- [.claude/commands/amend.md](.claude/commands/amend.md) — 修改任务范围
- [.claude/commands/quick.md](.claude/commands/quick.md) — 快速单任务执行
- [.claude/commands/debug.md](.claude/commands/debug.md) — 调试辅助
- [.claude/commands/ask.md](.claude/commands/ask.md) — 调查问题（只读，不执行）
- [.claude/commands/wikiorganize.md](.claude/commands/wikiorganize.md) — 重整 wiki 文档

## Testing

- 验证命令:
  _(none)_

- 覆盖率追踪: [.claude/tdd/index.md](.claude/tdd/index.md)

## Agent Skills (internal)

- [.claude/agent-skills/read-task.md](.claude/agent-skills/read-task.md)
- [.claude/agent-skills/run-verify.md](.claude/agent-skills/run-verify.md)
- [.claude/agent-skills/write-session-log.md](.claude/agent-skills/write-session-log.md)
- [.claude/agent-skills/load-community-context.md](.claude/agent-skills/load-community-context.md)

## Agents

- [.claude/agents/lead.md](.claude/agents/lead.md) — 编排
- [.claude/agents/core-asset-reference.md](.claude/agents/core-asset-reference.md) — asset-reference
- [.claude/agents/core-authorization.md](.claude/agents/core-authorization.md) — authorization
- [.claude/agents/core-mcp-server.md](.claude/agents/core-mcp-server.md) — mcp-server
- [.claude/agents/core-prefab-index.md](.claude/agents/core-prefab-index.md) — prefab-index
- [.claude/agents/core-tool-contract.md](.claude/agents/core-tool-contract.md) — tool-contract
- [.claude/agents/core-tool-registry.md](.claude/agents/core-tool-registry.md) — tool-registry
- [.claude/agents/core-ui-check.md](.claude/agents/core-ui-check.md) — ui-check
- [.claude/agents/core-unity-adapters.md](.claude/agents/core-unity-adapters.md) — unity-adapters
- [.claude/agents/core-unity-bridge.md](.claude/agents/core-unity-bridge.md) — unity-bridge
- [.claude/agents/review.md](.claude/agents/review.md) — 审查
- [.claude/agents/gc.md](.claude/agents/gc.md) — 清理

## Knowledge Base

- [Wiki Index](.claude/wiki/index.md) — 领域知识库
- [TDD Strategy](.claude/tdd/index.md) — 测试规格和骨架
- [Architecture](.claude/wiki/architecture/overview.md)
- [API Contracts](.claude/wiki/specs/api-contracts.md)

## Notes

- 渐进式迁移 -- 旧 UIProbeWindow 全程保持可用,每抽离一个模块保持原 UI 行为不变并补最小验证。
- 实施顺序硬性纠正 -- ToolContract(统一 ToolResult/Change)必须在第一个 Service 之前冻结。
- 可测性接缝 -- Service 不直接调静态 Unity API,经 IAssetGateway/IFileSystem/IEditorPrefs 注入;生产用 Unity 实现,测试用内存假体。
- 现状对齐 -- Core/ 已被 ResourceCacheManager/ResourceScanner 占用;主窗口实际约 15 个 Tab;UIProbeConfig 已有 version + MigrateFromEditorPrefs 迁移雏形,复用扩展不另起炉灶。
- 全仓库无 asmdef,在 Assets 下全局编译;asmdef 化是测试与 package 化的前置工作。
- 首发 MVP 为 AI 向(v0.1):只读工具,不触发 Domain Reload;v0.2 写操作 / 测试 / 编译 / Play Mode 推后。
- MCP Server 用 Node/TypeScript(独立 npm 包);UI Toolkit 工作台整体推后到 MCP MVP 之后。
- platforms=desktop 指 Unity Editor 内运行;MCP Server 是配套的 Node 进程,经 HTTP loopback + jobId 轮询与 Unity Bridge 通信。
- `.claude/settings.json` 已配置 PreToolUse 提示（文件搜索前先走 graph MCP 查询）与 PostToolUse（`git commit` 后自动重建 graph）。
- 所有里程碑完成后，建议执行 `/wikiorganize` 整理 wiki 文档
