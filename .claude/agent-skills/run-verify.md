# @run-verify

How to run verification commands and record results. Supports two modes: PBVF (task context) and Standalone (manual review).

## Step -2 — Intent Detection

1. Read `.claude/progress/session-state.json`.
2. Scan all task entries: is there any task with `status: "IN_PROGRESS"` AND `pbvf_phase` in `{"build", "verify", "fix"}`?
3. If **YES** → this is a **PBVF invocation**. Continue to Step -1 below.
4. If **NO** (no active PBVF task, or session-state.json does not exist) → this is a **Standalone invocation**. Skip to the **Standalone Flow** section at the end of this document.

---

## Step -1 — Workspace Readiness Gate

1. Read `.claude/progress/session-state.json`.
2. Read the current task definition from the milestone file in `.claude/milestones/`.
3. If the task has `stage: "prep"`:
   - Skip verification_commands entirely.
   - Run `bootstrap_checks` from `project.json` (if any exist).
   - All pass (or none defined) → verification PASSED for this gate.
   - Any fail → verification FAILED with bootstrap_checks output.
   - Continue to Step 1 (skip Step 0 — native compilation does not apply to prep tasks).
4. If the task has `stage: "exec"` (or no stage field):
   - If `workspace_readiness` is not `execution_ready`:
     - Do **not** run project verification commands.
     - Mark verification **FAILED** with reason: "Workspace is not execution-ready. Complete preparation tasks first, or run /sprint to enter preparation phase."
     - Record the current `readiness_blockers` in `session-log.md`.
     - Stop here.
   - Otherwise continue to Step 0.

## Step 0 — Native Compilation Gate

> Skip entirely if the current task has no `compile_machine` field (or it is null).

1. Read the milestone file → locate the current task by ID.
2. If `compile_machine` is null → skip to Step 1.
3. Read `.claude/rules/native-compilation.md` for the toolchain probe table.
4. Resolve the required toolchain from `build_command` and select that tool's probe/env key from `native-compilation.md`.
   - Examples: `msbuild` → MSVC / MSBuild, `xcodebuild` → Xcode, `cmake` → CMake, `Unity`/`Unity.exe` → Unity, `ndk-build` → Android NDK, `g++`/`clang++`/`cl` → GCC / Clang, `javac` → JDK.
   - Do **not** infer the toolchain from `output_artifact` suffix.
   - If `build_command` is empty, mark verification **FAILED** because the task definition is invalid for a native-compilation task.
5. **Resolve the toolchain** following the Probe Resolution Order in `native-compilation.md`:
   a. **settings.local.json first**: Check `.claude/settings.local.json` → `env` for the tool's env key. If a non-empty path is configured → use it directly, skip to step 6.
   b. **PATH probe**: Run the probe command for the current OS from the Toolchain Probe Table. If succeeds → skip to step 6.
   c. **Standard locations**: Enumerate standard installation directories from the table. If found → skip to step 6.
   d. **User fallback**: All probes failed → **ask the user** via AskUserQuestion:
      - Question: "[Tool] not found in PATH or standard locations. Is it installed at a custom path?"
      - Options: "Yes — I'll provide the path" / "Not installed on this machine"
   e. **User provides path** → write to `.claude/settings.local.json` → `env` → verify the path is valid.
      - Valid → skip to step 6.
      - Invalid → mark verification **FAILED**: "Provided path does not contain a valid [tool] installation."
   f. **User confirms not installed**:
      - Normalize `compile_machine` before comparing: `windows` → `win32`, `macos` → `darwin`, `linux` → `linux`, `any` → current OS.
      - Normalized `compile_machine` matches current OS → mark verification **FAILED**: "Required toolchain missing. Install the toolchain listed in native-compilation.md and retry."
      - Normalized `compile_machine` differs from current OS → set `compile_status: "pending"` in session-state.json. Append `⚠️ [pending-binary] TASK-xxx` to session-log Known Issues. Task status remains **IN_PROGRESS** (not DONE). Skip Steps 1-9.
6. **Execute build**:
   - Run `build_command` from the task definition; capture exit code and last 30 lines of output.
   - Build succeeds AND `output_artifact` exists → set `compile_status: "done"` in session-state.json → continue to Step 1.
   - Build fails → mark verification **FAILED**; record the build error output. Do not continue.

## Step 1 — Run Verification Commands

1. Read `project.json` → `verification_commands`.
   - If `verification_commands` is an **array** (legacy format): treat all entries as platform-agnostic commands.
   - If `verification_commands` is an **object**: it contains `common`, `win32`, `darwin`, `linux` keys (each an array of commands).
2. Determine the current OS platform (`win32` / `darwin` / `linux`).
3. Build the effective command list: `common` commands first, then the platform-specific commands for the current OS.
4. For each command, replace `${REPO_ROOT}` with the repository root directory (the parent of `.claude/`). Replace any other `${VAR_NAME}` with the corresponding value from `.claude/settings.local.json` → `env` (if the file exists). If a variable has no configured value, halt and report the missing env var.
5. Run each merged command in sequence.
6. Capture exit code and last 20 lines of output for each command.
7. If all commands exit 0: verification commands PASSED. Continue to Step 2.
8. If any command exits non-zero: verification commands FAILED — record the failing command and output. Continue to Step 2 anyway (acceptance criteria still need checking).

## Step 2 — Acceptance Criteria Check

1. Use @read-task to locate the current task: read `session-state.json` for the active `task_id`, then read the milestone file in `.claude/milestones/`.
2. Extract the task's **acceptance criteria** list.
3. For each acceptance criterion:
   - Determine PASS or FAIL based on: test output from Step 1, code inspection, or observable behavior.
   - Record concrete evidence: cite `file:line`, command output snippet, or test name.
4. Produce a checklist summary:
   ```
   - [x] criterion text — evidence (e.g., test `test_login` passes, see line 42 of auth.test.ts)
   - [ ] criterion text — reason for failure (e.g., endpoint returns 500 instead of 200)
   ```
5. If ALL criteria pass AND verification commands passed: overall PASSED.
6. If ANY criterion fails OR verification commands failed: overall FAILED.

## Step 3 — Changed Files Identification

1. Determine the build start point:
   - Read `session-state.json` → current task → `git_state.commit` recorded at task start.
   - If no start commit is recorded, use `git log --oneline -20` to identify the commit before the build phase began.
2. Collect changed files using a **layered fallback** (merge all non-empty results):
   a. `git diff --name-only <start-commit> HEAD` — committed changes since build start.
   b. `git diff --cached --name-only` — staged but uncommitted changes.
   c. `git diff --name-only` — unstaged working tree changes.
   d. If all three return empty, recall the files you created or modified during this session from conversation context (the build phase outputs, tool call history, and any file paths you wrote to).
   e. If still empty, note "No file changes detected — verify that the build phase produced output" and continue (do not block).
3. Deduplicate the merged file list.
4. Read the task's **write_paths** from the milestone file.
5. Cross-check:
   - Files modified **outside** write_paths → flag as **BOUNDARY_VIOLATION** (include in evidence).
   - Files within write_paths that were **not modified** → note as informational (no action required).
6. Pass the changed file list to the code review step (review heuristics in the /verify command).

## Step 4 — Record and Sync

1. Call @write-session-log with:
   - Overall PASSED/FAILED status.
   - Per-criterion checklist from Step 2.
   - Changed file list and any boundary violations from Step 3.
   - Verification command output from Step 1.
2. `session-state.json` is updated first; all derived docs are synced afterward.

---

## Standalone Flow (Manual Review)

> Entered from Step -2 when no active PBVF task is detected. This flow is for users who manually call `/verify` to review recent code changes.

### S1 — Identify Recent Changes

1. Run `git diff --name-only HEAD~5 HEAD` to list files changed in recent commits.
2. Run `git diff --cached --name-only` to list staged but uncommitted files.
3. Run `git diff --name-only` to list unstaged modifications.
4. Merge all three lists into a deduplicated **changed file set**.
5. If the user specified a scope (file path, directory, or glob), filter the set to that scope.
6. If the changed file set is empty, report "No recent changes detected" and stop.

### S2 — Focused Code Review

1. For each file in the changed set, read the diff (`git diff HEAD~5 HEAD -- <file>` or `git diff -- <file>` for unstaged).
2. Apply the review heuristics (from `.claude/rules/` or the embedded review-heuristics partial):
   - Adaptive depth based on total diff size.
   - Confidence gate: only report findings at the configured threshold.
   - Hard exclusions: skip formatting-only, import-order, whitespace-only changes.
3. Focus areas: logic correctness, edge cases, security issues, error handling, and potential regressions.
4. For each finding, include: severity, confidence score, file:line, and a one-line description.

### S3 — Test Planning

1. **Graph-first module mapping**: map changed file paths to module slugs via the knowledge graph:
   a. If `.claude/wiki/graph.json` exists: read the graph's node list to find nodes whose `source_file` matches each changed file; each node's community membership identifies its module.
   b. Fall back to directory-structure heuristics or `.claude/wiki/modules/` full read when graph.json is unavailable.
2. For each affected module:
   - If `.claude/tdd/specs/{module}.spec.md` exists: read it and check which test cases cover the changed functionality. Note uncovered gaps.
   - If no TDD spec exists: recommend test types based on the nature of changes (unit for pure functions, integration for API endpoints, E2E for user flows).
3. Output a test recommendation list: `- [ ] {test description} — {reason}`.

### S4 — Run Available Verification Commands

1. If `project.json` exists and contains `verification_commands`: run them using the same logic as Step 1 of the PBVF flow (platform-aware command merging, variable substitution).
2. If `project.json` does not exist or has no `verification_commands`: skip and note "No verification commands configured."
3. Report PASS/FAIL for each command.

### S5 — Output Review Report

Produce a structured report:

```
## Review Report

### Changed Files
<list of files with change type: modified/added/deleted>

### Findings
<findings sorted by severity, each with confidence score>

### Test Recommendations
<test gaps and recommended new tests>

### Verification Results
<PASS/FAIL per command, or "not configured">
```
