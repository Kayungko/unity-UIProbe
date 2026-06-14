# /amend

> **Invoked by:** user-session (during implementation when task changes are needed)

## 会话前导

执行这个 command 之前，先完成下面的初始化步骤：

1. 阅读 `.claude/progress/session-log.md`，先理解当前状态。
2. 从 `.claude/milestones/` 确认当前活动的 milestone 和 task。
3. 定位任务相关代码/模块时，先走 graph 查询（`mcp__graphify__query_graph` → `get_node` / `get_neighbors` / `shortest_path`）预定位；仅当 graph 未命中或 `.claude/wiki/graph.json` 不存在时，才用 Grep / Glob 做 raw 搜索。
4. 如果 milestone 或 wiki 自上次会话后发生变化，先运行 `/sync-progress`。
5. 读取当前 task 上下文所适用的 `.claude/rules/` 文件。

## Objective

通过"先讨论后执行"的结构化工作流，修改、插入或删除当前 milestone 中的 task。所有改动都必须与用户讨论并经确认后，才能写入任何文件。

## Workflow Policy

- `completeness_mode`: `balanced`
- `ownership_mode`: `solo`
- `question_protocol`: `structured`
- `review_depth`: `standard`

## Steps

### Step 1 — Listen to Intent

请用户用一两句话描述他们想改什么。不要给出固定的改动类型菜单。不要一上来就索要 TASK-ID。让用户自由表达。

### Step 2 — Read Context and Restate Understanding

1. 读 `session-state.json` 了解当前 milestone 和 task 状态。
2. 读 `session-log.md` 了解近期决策和已知问题。
3. 读相关的 milestone task 文件，了解 task 结构与依赖。
4. 用自然语言复述你对所请求改动的理解。
5. 如有歧义，提出**一个**澄清问题（最关键的那个）。如无歧义，直接进入 Step 3。

### Step 3 — Propose Complete Change Plan (DO NOT write files yet)

提出一份完整的改动方案，包含以下全部内容：

1. **Change summary** —— 将要修改 / 插入 / 删除什么。
2. **Affected tasks** —— 对每个被改动的 task，展示完整的更新后字段：
   - Title、description、acceptance criteria、assigned agent、dependencies、write_paths。
   - 对新增 task：完整的 task 定义，包括 ID 分配。
   - 对删除 task：ID 及移除原因。
3. **Dependency chain impact** —— 哪些下游 task 需要更新依赖。
4. **Milestone impact** —— 该改动是否影响 milestone 范围或时间线。
5. **Session-log call preview** —— 将要发出的精确 `@write-session-log` 调用（entry_type、summary、evidence 字段、pending_add）。

**在继续之前，等待用户的明确确认。** 如果用户要求修改方案，则修订后再次呈现。

### Step 4 — Execute Changes

仅在用户确认之后：

1. **重新读取**最新的 task 文件和 session-state.json（不要依赖 Step 2 的缓存内容）。
2. 将已确认的改动应用到 milestone task 文件。
3. 同步所有受影响 task 的依赖字段（上游和下游均包括）。
4. 如果新增了 task，将它们以 `NOT_STARTED` 状态加入 `session-state.json`。
5. 如果删除了 task，将它们从 `session-state.json` 中移除。
6. 调用 `@write-session-log`，传入：
   - `entry_type`: `"amend"`
   - `summary`: 一句话描述本次修改
   - `evidence`: timestamp、受影响 task（ID + 变更字段）、依赖链影响、milestone 影响
   - `pending_add`: 新插入 task 的 ID（它们起始为未完成状态），以及新暴露的任何 blocker
   - `pending_resolve`: 被移除的 ID 或本次修改完成的 task
   不要直接追加写入 `session-log.md`。

## Required Artifacts

- 应用了改动的、更新后的 milestone task 文件
- 反映 task 清单变化的、更新后的 `session-state.json`
- **通过 `@write-session-log`** 更新的 `session-log.md`，包含修改记录、理由，以及为新插入 task 添加的 pending 条目

## Completion Status

See `.claude/rules/completion-status.md` (auto-loaded via settings.json).

## Exit Condition

所有已确认的改动都已应用到 task 文件，session-state.json 与更新后的 task 清单保持一致，且 `@write-session-log` 已记录本次修改（包含理由，以及为新插入 task 添加的 pending 条目）。
