# @write-session-log

How to update the session state after a task progresses through PBVF phases or completes.

## Session-State Update

1. Open `.claude/progress/session-state.json` (machine-readable truth source).
2. Update the task entry:
   - Set `status` to `"IN_PROGRESS"` or `"DONE"`.
   - Set `pbvf_phase` to the current PBVF step: `"plan"`, `"build"`, `"verify"`, or `"fix"`.
     - Update `pbvf_phase` **before** each Skill invocation (plan/build/verify/fix).
     - When marking `"DONE"`, set `pbvf_phase` to `null` (phase tracking complete).
   - If `"DONE"`: fill `completed_at` with ISO timestamp and add `evidence` summary.
   - If `"IN_PROGRESS"`: fill `started_at` (if not already set). Do **not** fill `completed_at`.
3. Update `git_state` with the current branch and commit hash.
4. Update `updated_at` with the current ISO timestamp.
5. Re-open `.claude/progress/session-log.md` and confirm the rendered sections match the updated machine-readable state.
6. Do not change the status or pbvf_phase of tasks you did not work on.

**PBVF retry tracking**:
- Before each fix retry, increment `pbvf_retry_count` in the task entry.
- If `pbvf_retry_count` reaches `max_pbvf_retries` (from `project.json`, default 3), stop retrying and report "PBVF max retries exceeded".

**Test snapshot**:
- After each verify (PASS or FAIL), update the task's `test_snapshot`: `{"passed": true/false, "summary": "<one-line test output>", "timestamp": "<ISO>"}`.

**Coverage gate update**:
- After @run-coverage completes, update the current milestone's `coverage_gate` in `session-state.json`.
- Fields: `status` (pending/passed/failed), `actual` (integer 0-100), `report` (one-line), `checked_at` (ISO timestamp).
- Do not modify coverage_gate for milestones you did not evaluate.

**Priority**: Always update `session-state.json` first. It is the authoritative source; `session-log.md` and the overview/runbook docs are derived views.

---

## Calling Convention

All callers — commands (`/quick`, `/debug`, `/amend`, `/sync-progress`, `/fix`, `/build`, `/sprint-review`) and sibling skills/agents (`@run-verify`, `@run-coverage`, `@run-ui-audit`, `gc`) — invoke this skill by writing:

    Call @write-session-log with:
    - entry_type: "quick" | "debug" | "amend" | "verify" | "fix" | "build" | "sync" | "sprint" | "sprint-review" | "gc"
    - summary: <one-line>
    - evidence: <optional structured details; fields depend on entry_type>
    - pending_add: [<new pending items — unfinished work, blockers, next steps>]
    - pending_resolve: [<pending items to strike out or remove>]

The skill is responsible for:

1. Routing the entry to the correct layer:
   - `quick` / `debug` → **Recent Activity**
   - `amend` / `verify` / `fix` / `build` / `sprint` → **Current Milestone** (as task sub-entries)
   - `sync` / `sprint-review` / `gc` → refresh Pending from `session-state.json`, no new entry; primary purpose is to enforce the 100-line compression gate
2. Applying `pending_add` / `pending_resolve` to the **Pending** layer.
3. Running the 100-line compression check after every write. This step is **mandatory** — count `session-log.md` lines after each write, and if it exceeds 100, apply progressive compression before returning to the caller.

Callers never append to `session-log.md` directly. Only this skill writes.

---

## Pending Layer (Top of File)

The **Pending** section lives at the top of `session-log.md` so the next session sees unfinished work first.

**Write triggers** (things that belong in Pending):

- `/verify` FAIL → add task with failure reason
- `/fix` did not resolve the root cause → keep the item, annotate attempt count
- Any blocker detected during `/build` or a sprint wave (missing dependency, environment gap, external ticket)
- `/amend` inserts new tasks → add them as pending
- Milestone transition: uncompleted tasks carry over as Pending

**Resolve triggers** (remove from Pending):

- Task enters `DONE` in `session-state.json`
- Blocker lifted and next step already in progress
- User explicitly waives the item

**Rules**:

- Pending items MUST be written via `@write-session-log` (use `pending_add` / `pending_resolve`).
- Pending items are **never compressed** — they stay verbatim even when the file crosses the 100-line limit.
- Pending only records **unfinished work**. Completed items drop out immediately; do not accumulate a "done" history here — that belongs in Archive / Recent Activity / Current Milestone.

## Session-Log Compression

Session-log.md uses a **layered structure** (Pending → Archive → Recent Activity → Current Milestone) to keep the file concise while preserving full context for the current work:

### Structure

```markdown
# {project_name} Session Log

## Pending / 待办
<!-- Open-first: unfinished work, blockers, next steps. Never compressed. -->
- [ ] {TASK-ID}: {verify failure reason or blocker summary}
- [ ] Blocker: {one-line description + what we are waiting on}
- [ ] Next: {next milestone / task planning hint}

## Archive
<!-- Compressed history: one summary block per completed milestone -->

### M1 {name} (DONE — {date})
- {completed}/{total} tasks, coverage {actual}% (target {target}%), {issues} issues
- Key decisions: {1-2 sentence summary of architectural/design decisions made}
- Carried forward: {unresolved issues or "none"}

## Recent Activity
<!-- Most recent 3 quick/debug entries in detail; older entries compressed to one line -->

## Current Milestone: M{n} {name}
<!-- Full detail for all tasks in the active milestone -->

### {task_id}: {title} ({status})
- plan: {timestamp} — {scope summary}
- build: {timestamp} — {implementation notes}
- verify: {PASSED|FAILED} — {evidence summary}
- fix: {if applicable, what was fixed}
```

### Compression Rules

**When to compress**: Every time a milestone's coverage gate changes to `"passed"` (all tasks DONE + tests passed), compress that milestone's detailed entries into an Archive summary block.

**How to compress**:
1. Read the `## Current Milestone` section for the completed milestone.
2. Summarize into a **3-5 line** Archive block:
   - Task completion count and coverage result
   - Key decisions made during the milestone (architectural choices, technology switches, design trade-offs)
   - Any unresolved issues carried forward to the next milestone
3. Replace the detailed entries with the summary block under `## Archive`.
4. Start a new `## Current Milestone: M{next} {name}` section for the next active milestone.

**What to preserve in Archive** (do NOT discard):
- Decisions that affect downstream work
- Known issues or tech debt carried forward
- Coverage gate results

**What to drop** (safe to compress away):
- Individual PBVF phase timestamps and detailed build logs
- Verification command output details
- File change lists and boundary check details
- Fix retry details (only keep final outcome)

### Quick Task / Debug Entries

Entries from `/quick` and `/debug` that are NOT part of a milestone:
- Keep the **3 most recent** entries in full detail under a `## Recent Activity` section (between Archive and Current Milestone).
- Older quick/debug entries: compress to a single line each: `- {date}: {type} — {one-line summary}`.
- If more than 10 compressed entries accumulate, keep only the latest 10.

### Hard Limit: 100 Lines

After every write, count the total lines of `session-log.md`. If it exceeds **100 lines**, apply progressive compression until it fits (Pending is **never** touched):

1. **First pass** — Merge all Archive milestone blocks into a single summary table:
   ```
   | Milestone | Status | Coverage | Key Decision | Carried Forward |
   |-----------|--------|----------|--------------|-----------------|
   | M1 基础搭建 | DONE | 95% | JWT auth | none |
   | M2 核心流程 | DONE | 90% | GraphQL | #123 pagination |
   ```
2. **Second pass** — If still over 100: remove all compressed quick/debug one-liners from Recent Activity, keep only the 3 detailed entries.
3. **Third pass** — If still over 100: reduce Current Milestone task entries to essential fields only (status + final verify result, drop plan/build/fix phase details).

**Never delete / never compress**:
- **Pending items** (the whole top section stays verbatim)
- Current Milestone task statuses
- Carried-forward issues
- The Archive summary table

These are the minimum viable context for the next session.
