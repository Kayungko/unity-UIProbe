# @build-sprint-context

How to generate a shared context snapshot for sprint-dispatched agents.

The main agent generates `.claude/progress/sprint-context.md` before dispatching sub-agents:

1. Read `.claude/progress/session-state.json` — extract current milestone, all task statuses, `pbvf_retry_count`, and `test_snapshot` for each task.
2. Read all `.claude/rules/*.md` files — for each rule, extract the rule ID and a one-line summary.
3. Read `.claude/project.json` — extract `verification_commands`, `bootstrap_checks`, and `workflow_policy`.
4. **Graph-first module ownership lookup**:
   a. If `.claude/wiki/graph.json` exists: read its `god_nodes` list to identify dominant modules; supplement with community data to map nodes to module slugs.
   b. Read `.claude/wiki/modules/` — extract module names and ownership from module page frontmatter (full read as fallback when graph.json is unavailable or incomplete).
5. Read `.claude/progress/session-log.md` — extract recent insights and unresolved blockers.
6. Record `workspace_readiness` in the sprint context. If not `execution_ready`, note that only `stage: "prep"` tasks are eligible for dispatch.
7. Write the assembled snapshot to `.claude/progress/sprint-context.md` with sections:
   - **Milestone Status**: current milestone, task status summary (N done, N in-progress, N not-started)
   - **Sprint Phase**: `preparation` (if workspace not execution_ready) or `execution` (if execution_ready)
   - **Workspace Readiness**: target, current state, blockers
   - **Rules Summary**: one line per rule (ID + constraint)
   - **Module Ownership**: table of module → owner → paths (from wiki/modules/)
   - **Verification Commands**: the real commands to run (from project.json)
   - **Bootstrap Checks**: governance checks that do not imply execution readiness (from project.json)
   - **Recent Insights**: any insights from session-log
   - **Continuation State**: current continuation phase, auto_fix_round, paused_reason (from session-state.json `continuation` object). If paused_reason is set, note the reason and what action is needed to resume.
   - **PBVF Retry Limits**: `max_pbvf_retries` value (from project.json, default 3) and any tasks that have exhausted retries
   - **Test Snapshots**: for each completed or in-progress task, include the latest `test_snapshot` (passed/failed, summary, timestamp)
   - **Coverage Gate Status**: for each milestone, include `coverage_gate` status, target, actual, and checked_at from session-state.json. Highlight any milestone with status `failed`.

Sub-agents read this single file instead of individually loading 6-8 files, saving ~3000 tokens per dispatch.
