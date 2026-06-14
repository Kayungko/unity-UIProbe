# /quick

> **Invoked by:** user-session (for ad-hoc tasks outside milestone ceremony, with optional `--dispatch`)

## 会话前导

执行这个 command 之前，先完成下面的初始化步骤：

1. 阅读 `.claude/progress/session-log.md`，先理解当前状态。
2. 定位任务相关代码/模块时，先走 graph 查询（`mcp__graphify__query_graph` → `get_node` / `get_neighbors` / `shortest_path`）预定位；仅当 graph 未命中或 `.claude/wiki/graph.json` 不存在时，才用 Grep / Glob 做 raw 搜索。
3. 读取当前 task 上下文所适用的 `.claude/rules/` 文件。

## Objective

快速实现小型、独立的 ad-hoc 任务，保留 harness 核心保证（session-log 记录、原子提交、默认验证 + 结构自检 gate），但跳过完整的 milestone/sprint 流程。

使用 `--dispatch` flag 可将任务分派给拥有该模块上下文的 `core-{module}` agent 执行完整 PBVF 流程（plan → build → verify → fix）。需要更强的 TDD 保证时用 `--dispatch`。

## Workflow Policy

- `completeness_mode`: `balanced`
- `ownership_mode`: `solo`
- `question_protocol`: `structured`
- `review_depth`: `standard`

## Steps

### Step 1 — 解析参数

解析 `$ARGUMENTS`：
- `--dispatch` flag → 存储为 `$DISPATCH_MODE`（true/false），启用模块 Agent 分派
- 剩余文本 → 任务描述 `$DESCRIPTION`

如果 `$DESCRIPTION` 为空，使用 AskUserQuestion 询问：

```
AskUserQuestion(
  header: "Quick Task",
  question: "请描述你要做的任务",
  options: []
)
```

如果仍然为空，提示用户必须提供任务描述。

展示模式 banner：

**`--dispatch` 模式：**
```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 HARNESS ► QUICK TASK (DISPATCH)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

◆ 模块 Agent 分派已启用
```

**默认模式：**
```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 HARNESS ► QUICK TASK
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

◆ $DESCRIPTION
```

### Step 2 — 读取上下文

1. 读 `session-state.json` 了解当前 milestone 和任务状态。
2. 读 `session-log.md` 了解近期进展和决策。
3. 读与任务相关的规则文件和领域文档。
4. 确认任务不与正在进行的 milestone 任务冲突或重复。

### Step 2.5 — Dispatch Assessment（仅 `--dispatch` 模式）

**如果 `$DISPATCH_MODE` 为 false，直接跳到 Step 3。**

基于 Step 2 读取的上下文，评估是否可以分派给模块 Agent：

1. 读 `.claude/wiki/modules/` 构建 module-slug → paths 映射表。
2. 将任务描述中涉及的文件路径/功能域匹配到模块所有权。
3. 如果匹配到单个 `core-{module}` agent 且任务不跨模块：

   展示 Dispatch Proposal：

   ```
   ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    DISPATCH PROPOSAL
   ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

   Module: $MODULE_NAME
   Agent: core-$MODULE_SLUG
   Task: $DESCRIPTION
   Write paths: $WRITE_PATHS
   ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
   ```

   使用 AskUserQuestion 确认：
   - **"确认 dispatch"** → dispatch 给 `core-{module}` agent 执行完整 PBVF 流程（plan → build → verify → fix）。等待结果后展示给用户 → **EXIT**。
   - **"继续本地处理"** → 跳过 dispatch，继续 Step 3。

4. 如果任务跨多个模块 → 显示模块列表，建议拆分或本地处理。
5. 如果无匹配 → 继续 Step 3。

### Step 3 — 制定实施方案

基于上下文和任务描述，制定简洁的实施方案，包含：

1. **变更范围** — 需要修改/新增的文件和模块。
2. **实施步骤** — 1-5 个具体步骤。
3. **影响评估** — 对现有代码的影响，是否触及模块边界。
4. **验证方式** — 如何确认变更正确。

向用户展示方案，**等待明确确认后再继续**。如果用户要求修改方案，修正后重新展示。

### Step 4 — 实施变更

按用户确认的方案执行：

1. 遵守模块所有权边界。
2. 实现变更，保持契约明确。
3. 同步添加或更新自动化测试（如适用）。
4. 每个逻辑变更保持原子性。

### Step 5 — 验证

ad-hoc 任务默认验证，不再可跳过。

1. 运行 `verification_commands` 中的 build、test 命令（参见 project.json）。
2. 运行结构自检 gate（纯 Python，无 LLM，秒级）：

   ```bash
   python .claude/scripts/write_code_check.py \
       --task adhoc --root <project_root> \
       --changed-files <file1> <file2> ...
   ```

   - Exit **0** → 通过，继续。
   - Exit **1** → 有 high 级发现，按报告修复后重跑（最多 2 轮）。

   ad-hoc 无 milestone task 文件，write-path 边界检查会自动跳过；其余结构检查照常执行。

3. 逐项检查 Step 3 中声明的验证方式。
4. 记录验证结果为 `$VERIFICATION_RESULT`（passed / failed / partial）。

如验证失败，执行最小修复后重新验证（最多 2 轮）。

### Step 6 — 记录与提交

**6a. 更新 session-log（调用 skill，不要直接写）**

调用 `@write-session-log` 传入：

- `entry_type`: `"quick"`
- `summary`: $DESCRIPTION
- `evidence`:
  - date: $DATE
  - changes: 变更文件列表
  - verification: $VERIFICATION_RESULT
  - commit: $COMMIT_HASH
- `pending_add`: 如果 quick task 暴露了遗留问题或阻塞，列出；否则 `[]`
- `pending_resolve`: 如果本次解决了 Pending 里的条目，列出其文本

skill 负责把条目写入 Recent Activity 段，更新 Pending 层，并复核 100 行硬限。

**6b. 原子提交**

将所有代码变更和 session-log 更新作为一次原子提交：

```
feat(quick): $DESCRIPTION
```

**6c. 展示完成摘要**

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 HARNESS ► QUICK TASK COMPLETE
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Task: $DESCRIPTION
Files: $CHANGED_FILES
Verification: $VERIFICATION_RESULT
Commit: $COMMIT_HASH
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

## Required Artifacts

- 代码变更已提交
- `session-log.md` 包含 quick task 记录条目
- 验证结果（含结构自检 gate）已记录

## Completion Status

See `.claude/rules/completion-status.md` (auto-loaded via settings.json).

## Exit Condition

任务变更已提交，验证（verification_commands + 结构自检 gate）通过，session-log 已更新 quick task 记录，用户看到完成摘要。
