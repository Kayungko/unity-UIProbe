# /build

> **Invoked by:** agent-session (module agents)

## 会话前导

执行这个 command 之前，先完成下面的初始化步骤：

1. 阅读 `.claude/progress/session-log.md`，先理解当前状态。
2. 从 `.claude/milestones/` 确认当前活动的 milestone 和 task。
3. 定位任务相关代码/模块时，先走 graph 查询（`mcp__graphify__query_graph` → `get_node` / `get_neighbors` / `shortest_path`）预定位；仅当 graph 未命中或 `.claude/wiki/graph.json` 不存在时，才用 Grep / Glob 做 raw 搜索。
4. 如果 milestone 或 wiki 自上次会话后发生变化，先运行 `/sync-progress`。
5. 读取当前 task 上下文所适用的 `.claude/rules/` 文件。

## Objective

实现 /plan 产出的方案，交付满足任务的可运行代码。

## Workflow Policy

- `completeness_mode`: `balanced`
- `ownership_mode`: `solo`
- `question_protocol`: `structured`
- `review_depth`: `standard`

## Steps

1. 重读 plan 的产出，确认范围。
2. **RED** — 先写或更新 TDD spec 里的测试。运行它们，确认在写任何实现之前它们因预期原因 FAIL。一个在实现前就通过的测试什么都证明不了。
3. **GREEN** — 在声明的 write_paths 范围内写代码，让失败的测试通过。不要为了迁就实现去改测试。
4. 每个逻辑变更后运行验证命令，保持测试套件绿。
5. 调用 @write-session-log（entry_type: "build"）记录进展笔记（不要直接追加到 session-log.md）。

## Required Artifacts

保持支持文档对齐；session-log.md 通过 `@write-session-log` 与当前 task 状态同步。



## Completion Status

See `.claude/rules/completion-status.md` (auto-loaded via settings.json).

## Exit Condition

所有验收标准已实现，本地验证通过。
