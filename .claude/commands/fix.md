# /fix

> **Invoked by:** agent-session (module agents) | user-session (with `--dispatch`)

## 会话前导

执行这个 command 之前，先完成下面的初始化步骤：

1. 阅读 `.claude/progress/session-log.md`，先理解当前状态。
2. 从 `.claude/milestones/` 确认当前活动的 milestone 和 task。
3. 定位任务相关代码/模块时，先走 graph 查询（`mcp__graphify__query_graph` → `get_node` / `get_neighbors` / `shortest_path`）预定位；仅当 graph 未命中或 `.claude/wiki/graph.json` 不存在时，才用 Grep / Glob 做 raw 搜索。
4. 如果 milestone 或 wiki 自上次会话后发生变化，先运行 `/sync-progress`。
5. 读取当前 task 上下文所适用的 `.claude/rules/` 文件。

## Objective

诊断根因并修复 /verify 中发现的失败。

## Workflow Policy

- `completeness_mode`: `balanced`
- `ownership_mode`: `solo`
- `question_protocol`: `structured`
- `review_depth`: `standard`

## Steps

### Step 0 — Session Detection & Dispatch Assessment

**Sprint 守卫：** 如果 `.claude/progress/sprint-context.md` 存在且 `session-state.json` → `continuation.phase` == `"sprint"` → 跳过分派，直接执行 Step 1-4。

**用户直接调用时：**

1. 解析 `$ARGUMENTS`：提取 `--dispatch` flag 和失败描述 `$FAILURE_DESCRIPTION`。
2. 如果 `--dispatch` 存在：
   a. 读 `session-state.json` 获取失败证据（或使用用户提供的错误信息）。
   b. 识别失败的文件/测试，读 `.claude/wiki/modules/` 映射到模块所有权。
   c. 如果匹配到单个 `core-{module}` agent：

      展示 Dispatch Proposal：

      ```
      ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
       DISPATCH PROPOSAL
      ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

      Module: $MODULE_NAME
      Agent: core-$MODULE_SLUG
      Failure: $FAILURE_DESCRIPTION
      Affected files: $FILE_LIST
      ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
      ```

      使用 AskUserQuestion 确认：
      - **"确认 dispatch"** → dispatch 给 `core-{module}` agent 执行 `/fix` + `/verify` 循环，等待结果后展示给用户 → EXIT。
      - **"继续本地处理"** → 跳过 dispatch，继续 Step 1。

   d. 如果所有权不明确 → 让用户确认目标模块或本地处理。
3. 如果无 `--dispatch` → 继续 Step 1。

### Step 1 — 从 session-state.json 读取失败证据。

### Step 2 — 识别根因（而非仅仅是症状）。

### Step 3 — 应用最小修复。

### Step 3.5 — 通过 `@write-session-log` 记录修复。

调用 `@write-session-log`，传入：

- `entry_type`: `"fix"`
- `summary`: 一句话描述修复了什么
- `evidence`: root_cause、fix_summary、files_changed、引用的失败快照
- `pending_resolve`: 本次修复清除的 pending 项（原始 verify 失败）
- `pending_add`: 修复过程中发现的任何新 pending 项（例如"回归测试待补"、后续重构）

如果修复**未**解决根因，保留该 pending 项（不要传给 `pending_resolve`），并在 `evidence` 中记录尝试次数。

### Step 4 — 返回 /verify 确认修复。

## Required Artifacts

在返回 /verify 之前，**通过 `@write-session-log`**（Step 3.5）保留失败证据链。

## Completion Status

See `.claude/rules/completion-status.md` (auto-loaded via settings.json).

## Exit Condition

修复解决了根因，且 /verify 现在通过。
