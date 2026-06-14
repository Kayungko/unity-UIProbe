# /sync-progress

> **Invoked by:** agent-session or user-session

## 会话前导

执行这个 command 之前，先完成下面的初始化步骤：

1. 阅读 `.claude/progress/session-log.md`，先理解当前状态。
2. 从 `.claude/milestones/` 确认当前活动的 milestone 和 task。
3. 定位任务相关代码/模块时，先走 graph 查询（`mcp__graphify__query_graph` → `get_node` / `get_neighbors` / `shortest_path`）预定位；仅当 graph 未命中或 `.claude/wiki/graph.json` 不存在时，才用 Grep / Glob 做 raw 搜索。
4. 对比 session-log、session-state.json、milestone 文件和 wiki 数据。
5. 读取当前 task 上下文所适用的 `.claude/rules/` 文件。

## Objective

将 session-state.json 和 session-log.md 与当前工作区状态重新同步。

## Workflow Policy

- `completeness_mode`: `balanced`
- `ownership_mode`: `solo`
- `question_protocol`: `structured`
- `review_depth`: `standard`

## Steps

1. 读取 .claude/milestones/ 中的所有 milestone 文件。
2. 将 task 状态与 session-state.json 比对。
3. 更新 session-state.json 以反映实际的文件系统状态。
4. 调用 `@write-session-log`，传入 `entry_type: "sync"`。该 skill 会从 `session-state.json` 重新推导 Pending 层（将未完成的 task、未解决的 blocker、coverage 失败暴露为 pending 项），刷新派生视图，并强制执行 100 行上限。不要直接追加写入 `session-log.md`。
5. **知识图谱更新**：如果 `.claude/wiki/graph.json` 存在，运行 `python .claude/scripts/graph_update.py`（增量更新 —— 只重新处理变更的文件）。否则运行 `/build` 或完整管线以首次生成它。

## Required Artifacts

让 session-state.json 和 session-log.md 与实际结果和未解决的 blocker 保持一致。

## Completion Status

See `.claude/rules/completion-status.md` (auto-loaded via settings.json).

## Exit Condition

session-state.json 和 session-log.md 准确反映工作区状态。
