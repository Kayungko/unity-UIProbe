# /plan

> **Invoked by:** agent-session (lead or module agents) | user-session (with `--dispatch`)

## 会话前导

执行这个 command 之前，先完成下面的初始化步骤：

1. 阅读 `.claude/progress/session-log.md`，先理解当前状态。
2. 从 `.claude/milestones/` 确认当前活动的 milestone 和 task。
3. 定位任务相关代码/模块时，先走 graph 查询（`mcp__graphify__query_graph` → `get_node` / `get_neighbors` / `shortest_path`）预定位；仅当 graph 未命中或 `.claude/wiki/graph.json` 不存在时，才用 Grep / Glob 做 raw 搜索。
4. 如果 milestone 或 wiki 自上次会话后发生变化，先运行 `/sync-progress`。
5. 读取当前 task 上下文所适用的 `.claude/rules/` 文件。

## Objective

在动代码之前，锁定满足任务的最小正确实现方案。

## Workflow Policy

- `completeness_mode`: `balanced`
- `ownership_mode`: `solo`
- `question_protocol`: `structured`
- `review_depth`: `standard`

## Steps

### Step 0 — Session Detection & Dispatch Assessment

**Sprint 守卫：** 如果 `.claude/progress/sprint-context.md` 存在且 `session-state.json` → `continuation.phase` == `"sprint"` → 跳过分派，直接执行 Step 1-7。

**用户直接调用时：**

1. 解析 `$ARGUMENTS`：提取 `--dispatch` flag 和任务/范围描述 `$SCOPE_DESCRIPTION`。
2. 如果 `--dispatch` 存在：
   a. 读 `.claude/wiki/modules/` 构建 module-slug → paths 映射表。
   b. 将 `$SCOPE_DESCRIPTION` 中涉及的路径/功能域匹配到模块所有权。
   c. 如果匹配到单个 `core-{module}` agent：

      展示 Dispatch Proposal：

      ```
      ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
       DISPATCH PROPOSAL
      ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

      Module: $MODULE_NAME
      Agent: core-$MODULE_SLUG
      Scope: $SCOPE_DESCRIPTION
      Write paths: $WRITE_PATHS
      ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
      ```

      使用 AskUserQuestion 确认：
      - **"确认 dispatch"** → dispatch 给 `core-{module}` agent 执行 `/plan`，等待返回结果后展示给用户 → EXIT。
      - **"继续本地处理"** → 跳过 dispatch，继续 Step 1。

   d. 如果匹配多个模块 → 显示模块列表，让用户选择目标模块或本地处理。
   e. 如果无 `--dispatch` → 继续 Step 1。
3. 如果无 `--dispatch` → 继续 Step 1。

### Step 1 — 阅读 session-log 和当前活动的 milestone task 文件。

### Step 2 — 阅读相关的 rule 文件和 wiki 模块文档。

### Step 3 — 质疑范围：指出满足任务的最小改动。

示例：如果任务说"加用户认证"，最小范围是登录端点 + JWT 校验 —— 而不是完整的 RBAC 系统。

### Step 4 — 描述架构、接口、失败模式和依赖边界。

示例："api 模块新增 POST /login 端点 → 调用 domain.authenticateUser → 返回 JWT。失败：凭证无效 → 401。"

### Step 5 — 把每条验收标准映射到验证方式，并更新 TDD spec。

示例：验收"用户可登录" → 测试用例"POST /login 携带有效凭证返回 200 + token"。

### Step 6 — 只列出无法从仓库推导出的未决决策。

### Step 7 — 本步骤不要写代码。

## Required Artifacts

如果 task 有非平凡的代码路径，更新 `.claude/tdd/` 中相关的测试规格。

## Completion Status

See `.claude/rules/completion-status.md` (auto-loaded via settings.json).

## Exit Condition

存在一份具体的实现方案，明确了范围、架构、验证方式和未决决策。
