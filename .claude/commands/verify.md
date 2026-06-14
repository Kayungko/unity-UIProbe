# /verify

> **Invoked by:** agent-session (module agents) or user-session

## 会话前导

执行这个 command 之前，先完成下面的初始化步骤：

1. 阅读 `.claude/progress/session-log.md`，先理解当前状态。
2. 从 `.claude/milestones/` 确认当前活动的 milestone 和 task。
3. 定位任务相关代码/模块时，先走 graph 查询（`mcp__graphify__query_graph` → `get_node` / `get_neighbors` / `shortest_path`）预定位；仅当 graph 未命中或 `.claude/wiki/graph.json` 不存在时，才用 Grep / Glob 做 raw 搜索。
4. 如果 milestone 或 wiki 自上次会话后发生变化，先运行 `/sync-progress`。
5. 读取当前 task 上下文所适用的 `.claude/rules/` 文件。

## Objective

验证代码改动 —— 自动检测当前是 PBVF 任务上下文还是独立审查。

## Workflow Policy

- `completeness_mode`: `balanced`
- `ownership_mode`: `solo`
- `question_protocol`: `structured`
- `review_depth`: `standard`

## Steps

### Step 0 — Intent Detection

读取 `.claude/progress/session-state.json`。检查是否有任务处于 `status: "IN_PROGRESS"` 且 `pbvf_phase` 属于 `{"build", "verify", "fix"}`。如果有 → 走下面的 **PBVF Mode**。如果没有（或 session-state.json 不存在）→ 走下面的 **Standalone Mode**。

### PBVF Mode (Sprint / Task Context)

### Step 1 — Locate the current task

调用 `@read-task`：读取 `session-state.json` 获取当前活动的 `task_id`，然后读取 milestone 文件，提取 **acceptance criteria**、**write_paths** 和 **tdd_link**。

### Step 2 — Run verification commands

运行 `project.json` 中的 `verification_commands`（委派给 `@run-verify`）。

### Step X.1 — Write-code structural self-check (always runs, pure Python)

在验收审查之前，运行结构化 write-check 门禁。它是纯 Python（无 LLM），覆盖 `@write-code-check` 涵盖的四类问题：

```bash
python .claude/scripts/write_code_check.py \
    --task <task_id> \
    --root <project_root> \
    --changed-files <file1> <file2> ...
```

退出语义：

- Exit **0** → 无阻塞性发现；继续 Step 3。
- Exit **1** → 至少有一项 **high** 严重度发现。HALT verify 流程，先处理报告中列出的发现再继续。
- 报告写入 `.claude/progress/write-check-{task_id}-{YYYYMMDD-HHMMSS}.md`（同时保留一份旧版 `write-check-{task_id}.md` 副本以兼容现有工具链）。

如果你有意接受这些发现（例如某个被标记的边界已作为后续任务的技术债务跟踪），用以下命令重新运行：

```bash
python .claude/scripts/write_code_check.py \
    --task <task_id> --root <project_root> \
    --changed-files ... \
    --accept-check "<one-line reason>"
```

这会把退出码翻回 0，并在 `session-log.md` 的 Pending 层追加一条记录：

```
- **acknowledged: <reason>** (write-check <task_id>, high findings count=<N>)
```

绝不要用 `--accept-check` 静默绕过真实的违规 —— 该理由会被记录下来，供 `/sprint-review` 和审查者的 pending-high 扫描后续复查。

### Step X.2 — Semantic self-check (only with `--deep`, fill-* pattern)

当 sprint 策略或手动运行需要深度审查时，加上 `--deep`：

```bash
python .claude/scripts/write_code_check.py \
    --task <task_id> --root <project_root> \
    --changed-files ... \
    --deep
```

`--deep` 通过 `fill-*` 模式运行 T8-7 语义层。Python 内部不会发生任何 LLM API 调用 —— 而是脚本生成一个指令文件，交给当前 Claude Code 会话来填写。完整流程如下：

1. **Emit.** 首次 `--deep` 运行会写出 `.claude/progress/rule-violation-{task_id}-pending.md`，包含：
   - 审查指令（代码规则审查者），
   - 每个 `.claude/rules/*.md` 的内容，
   - 指向每个改动文件的指针，
   - 一份 YAML schema，以及
   - 一个 `<fill-findings>` 占位符。

   当只发生 pending emit 且没有结构性 high 发现时，退出码为 **0** —— 此次运行**不**阻塞，它在等待填写。

2. **Fill.** 打开 pending 文件，阅读 Context 和 Schema 部分，把 `<fill-findings>` 占位符替换为一个 YAML 块，列出发现的每条规则违规（若无则填 `findings: []`）。

3. **Re-run.** 再次执行同样的 `--deep` 命令。脚本会检测到已填写的占位符，通过 `core.llm_instruction.read_filled_findings` 对每条发现做 schema 校验，并将其合并进 write-check 报告的 `semantic_findings:` 下。

4. **Merge.** 合并后的（结构 + 语义）`high` 计数决定退出码：
   - 合并 high > 0 → exit 1（除非被 `--accept-check` 覆盖），
   - 合并 high == 0 → exit 0。

非 `--deep` 的运行永远不会触及语义层，因此永远不消耗任何 LLM token。

如果脚本打印：

```
[write-check] Semantic findings pending. Open and fill:
  .claude/progress/rule-violation-{task_id}-pending.md
Then re-run: python write_code_check.py --task {task_id} --root {root} --deep
```

不要跳过填写 —— 在把任务标记为 DONE 之前先处理它。如果审查后 pending 文件显示 `findings: []`，那就是"无违规"的正确填写状态。

### Step 3 — Acceptance criteria check

对每条验收标准，用具体证据（引用 `file:line`、命令输出或测试结果）判定 PASS 或 FAIL。产出一份清单：

- `- [x] criterion — evidence`
- `- [ ] criterion — reason`

### Step 4 — Identify changed files (layered fallback)

(a) `git diff --name-only <start-commit> HEAD`
(b) `git diff --cached --name-only`
(c) `git diff --name-only`

合并并去重。如果全部为空，从对话上下文（build 阶段输出、工具调用历史）回忆本次会话中修改过的文件。与任务的 `write_paths` 交叉核对 —— 任何在 `write_paths` 之外的文件都标记为 **BOUNDARY_VIOLATION**。

### Step 5 — Focused code review

仅使用审查启发式（按 diff 规模自适应深度、置信度门禁、硬性排除）审查改动文件。聚焦逻辑正确性、边界遵守和回归风险。

### Step 6 — TDD spec check

如果 `tdd_link` 存在，读取 `.claude/tdd/specs/{module}.spec.md`，确认与本任务验收标准相关的测试用例已勾选（`- [x]`）。记录未勾选的用例。

### Step 7 — Record evidence in session-state.json

更新 `test_snapshot`、验收结果，以及任何边界违规。

### Step 8 — Decide DONE or /fix

如果有任何验收标准 FAILED 或 `verification_commands` FAILED，或 write-code-check Step X.1 在没有 `--accept-check` 的情况下 exit 1：不要标记 DONE；转入 `/fix`。

### Standalone Mode (Manual Review)

### Step 9 — Identify recent changes

运行 `git diff --name-only HEAD~5 HEAD` 和 `git diff --cached --name-only`，列出近期修改和已暂存的文件。如果用户提供了特定范围（文件或目录），则收窄到该范围。

### Step 10 — Focused code review

使用审查启发式审查改动文件。聚焦逻辑正确性、边界情况、安全问题和错误处理。产出带置信度评分的发现。

### Step 11 — Test planning

对每个改动的模块，建议应编写或更新哪些测试。如果 `.claude/tdd/specs/` 存在，检查现有 TDD spec 测试用例是否覆盖了这些改动 —— 记录缺口。

### Step 12 — Run verification commands

如果 `project.json` 存在且包含 `verification_commands`，运行它们并报告 PASS/FAIL。如果没有 `project.json`，跳过。

### Step 13 — Output a review report

改动文件摘要、审查发现（按严重度排序）、测试建议，以及验证结果。

## Required Artifacts

在 session-state.json 中记录验证证据，并通过 `@write-session-log` 更新 session-log.md（逐条验收结果、变更文件列表、边界校验）。如果审查发现缺失的验证，更新 TDD spec。

## Review Heuristics

Review depth: `standard`

### Hard Exclusions (Auto-Discard)

- 已由 linter/formatter 覆盖的纯格式或风格问题
- import 顺序变更
- 无语义影响的纯注释变更
- 仅空白差异

### Confidence Gate

每条上报的发现都必须带一个置信度分数（1-10）。
只有置信度 >= 7 的发现才标记为可执行项。
置信度 4-6 的发现记为观察项。
置信度 <= 3 的发现丢弃。

### Verification Requirements

- task 的每条验收标准都必须有证据验证
- 现有测试无回归（运行完整的 verification_commands）
- 代码遵循声明的架构边界（检查模块 write_paths）
- 新增的公开行为有对应测试

## Completion Status

See `.claude/rules/completion-status.md` (auto-loaded via settings.json).

## Exit Condition

PBVF：每条验收标准都已用可追溯的证据验证，write-code-check Step X.1 exit 0（或经 `--accept-check` 接受），并且 —— 当调用了 `--deep` 时 —— 语义 pending 文件已填写并合并。Standalone：带发现和测试建议的审查报告。
