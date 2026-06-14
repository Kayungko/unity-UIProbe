# /sprint

> **Invoked by:** user-session Calls agent: `lead`

## 会话前导

执行这个 command 之前，先完成下面的初始化步骤：

1. 阅读 `.claude/progress/session-log.md`，先理解当前状态。
2. 从 `.claude/milestones/` 确认当前活动的 milestone 和 task。
3. 定位任务相关代码/模块时，先走 graph 查询（`mcp__graphify__query_graph` → `get_node` / `get_neighbors` / `shortest_path`）预定位；仅当 graph 未命中或 `.claude/wiki/graph.json` 不存在时，才用 Grep / Glob 做 raw 搜索。
4. 如果 milestone 或 wiki 自上次会话后发生变化，先运行 `/sync-progress`。
5. 读取当前 task 上下文所适用的 `.claude/rules/` 文件。

## Objective

以 sprint 模式派发 Lead agent，执行当前 milestone 中所有未阻塞的 task。

## Workflow Policy

- `completeness_mode`: `balanced`
- `ownership_mode`: `solo`
- `question_protocol`: `structured`
- `review_depth`: `standard`

## Steps

### Phase 1: Wave Planning

1. 读 session-state.json 和 session-log，确定当前 milestone、task 状态和 workspace_readiness。
2. 确定 sprint 阶段：若非 execution_ready → PREPARATION（只做准备类 task）。若 execution_ready → EXECUTION（所有 task）。
3. 调用 Lead agent 做波次规划：Lead 读取状态、计算波次、返回结构化的 JSON 派发计划。
4. 解析派发计划：提取波次列表和 task 提示词。

### Phase 2: Execution Loop

5. 波次执行循环：每个波次内，用一条消息携带多个前台 `Agent` 工具调用来并行派发模块 agent（不要设 `run_in_background: true`）。并行子 agent 必须同步运行，使所有结果在同一轮返回 —— 后台化的 agent 会破坏波次协调、拖慢下一波。**每个 `Agent` 调用的 `subagent_type` 必须等于该 task 在 lead JSON 计划派发条目中声明的 `agent` 值（例如该模块拥有的 task 用 `core-api`）。不要替换成 `general-purpose` 或任何其他 agent —— 只有声明的所有者携带该 task 的 write-path 权限和模块 wiki 上下文；派给错误的 agent 会悄无声息地破坏模块所有权、产出错误的 diff。** 波次内每个 agent 返回后，收集结果并更新 session-state.json，再开始下一波。
6. 所有准备类 task DONE 后：重新推导 readiness。若阻塞已解除 → 提升为 execution_ready。
7. 用 task 结果更新 session-state.json；通过 `@write-session-log` 同步 session-log（每个 task 结果一次调用，entry_type: "sprint"）。

### Phase 3: Post-Sprint

8. Post-Sprint：GC 扫描 → Auto-Fix（最多 2 轮）→ 回归验证 → Milestone Gate。
9. Coverage Gate 检查：读 session-state.json 的 milestone.coverage_gate.status。若为 'pending' 且所有 task DONE → 运行 /sprint-review 评估覆盖率。若为 'failed' → 暂停推进（HALT），把 continuation.paused_reason 设为 'coverage_gate_failed'，报告哪些模块需要更多测试覆盖。不要推进到下一个 milestone。**绝不降低 coverage_gate.target，也不跳过验证来绕过失败的 gate** —— 唯一的出路是：修测试、修环境，或获得用户明确同意跳过。
10. Auto-Advance：若 milestone gate 通过且 coverage_gate.status 为 'passed'，把 current_milestone 推进到下一个并继续。若该 milestone 完全没有 TDD spec，不要自动推进 —— 用 AskUserQuestion 向用户确认：'This milestone has no TDD specs. Advance without test coverage?' 选项：'Yes, advance' / 'No, create TDD specs first'。只有用户明确同意才推进；通过 `@write-session-log` 记录该决定（entry_type: "sprint"，evidence 含用户的同意原话）。

## Required Artifacts

保持 session-state.json 与实际结果对齐；session-log.md 通过 `@write-session-log` 同步（未解决的阻塞反映在 Pending 层）。



## Completion Status

See `.claude/rules/completion-status.md` (auto-loaded via settings.json).

## Exit Condition

所有 milestone task 带验证证据 DONE，或在 milestone gate 暂停。
