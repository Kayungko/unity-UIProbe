# @run-coverage

How to evaluate TDD coverage for the current milestone and record results.

## Step 1 — Identify Scope

1. Read `.claude/progress/session-state.json`.
2. Find the current milestone entry and its `coverage_gate` object.
3. Gate status routing:
   - `coverage_gate.status` == `"passed"` → skip evaluation (already cleared). Continue to Step 6 — Report.
   - `coverage_gate.status` == `"failed"` → **re-evaluate** (remediation attempt). Reset status to `"pending"` and continue to Step 2.
   - `coverage_gate.status` == `"pending"` → normal evaluation. Continue to Step 2.
4. Collect the list of modules relevant to this milestone from milestone tasks (extract agent names, map `core-{slug}` to module slug).

## Step 2 — Evaluate Each Module

For each module slug:

1. Read `.claude/tdd/specs/{slug}.spec.md`.
2. Parse the `## Test Cases` section. Count all lines matching `- [ ]` (unchecked) and `- [x]` (checked).
3. Compute per-module coverage: `checked / (checked + unchecked) * 100`.
4. Record each module result: `{module, total, checked, unchecked, percent}`.

## Step 3 — Run Verification Commands (MANDATORY)

> **CRITICAL**: Tests MUST actually execute and pass. Checkbox completion alone is NOT sufficient.
> **NEVER** lower `coverage_gate.target`, skip verification commands, or mark tests as `[x]` without actual passing evidence.

1. Run all `verification_commands` from `project.json` (same as @run-verify Step 1).
2. If any verification command fails, coverage gate FAILS regardless of checklist coverage.
3. Capture test output summary (exit codes, pass/fail counts, error messages).

### Step 3.1 — Test Environment Recovery (if verification fails)

If verification commands **cannot execute** (command not found, dependency missing, environment not configured):

1. **Diagnose**: Identify the root cause — missing dependency? wrong runtime version? port conflict? database not running? missing config file?
2. **Auto-fix attempt**: Try to resolve automatically:
   - Missing dependencies → run the project's install command (npm install, pip install, etc.)
   - Missing config → check for `.env.example` or template configs and copy them
   - Port conflict → identify and report the conflicting process
3. **Re-run**: After auto-fix, re-run the failing verification commands.
4. **User escalation**: If auto-fix fails, use `AskUserQuestion` to request help:
   - Describe exactly what failed and what was tried
   - Ask: "How should we set up the test environment?" with options:
     - "I'll provide the setup steps" — follow user instructions, then re-run
     - "Skip tests for this milestone" — ONLY allowed with explicit user consent
   - If user chooses to skip: record `coverage_gate.report` = "SKIPPED: user opted out — {reason}" and set `coverage_gate.status` = "failed". Log the skip reason to session-log. The milestone CANNOT auto-advance.
5. **NEVER silently skip**: Do not proceed past a test failure without either fixing it or getting explicit user consent.

### Step 3.2 — Checkbox-Verification Binding

A test case checkbox in TDD spec may only be marked `[x]` when **both** conditions are met:

1. The corresponding test code exists in the codebase.
2. The test passes when verification_commands are run.

If a checkbox is `[x]` but the corresponding verification command fails, **uncheck it** back to `[ ]` and include it in the failure report.

## Step 4 — Compute Aggregate

1. Sum all `checked` and all `total` across modules.
2. Aggregate coverage = `sum(checked) / sum(total) * 100`.
3. Compare against `coverage_gate.target`.

## Step 5 — Record Results

1. Update `session-state.json` → current milestone → `coverage_gate`:
   - `status`: `"passed"` if aggregate >= target AND verification passed, else `"failed"`
   - `actual`: the aggregate percentage (integer)
   - `report`: one-line summary, e.g. "9/10 modules at 90% (target: 90%). Verification: PASSED."
   - `checked_at`: current ISO timestamp
2. Call @write-session-log to sync derived docs.

## Step 6 — Report

If PASSED: confirm gate is clear for milestone advancement.
If FAILED: list each module below target with its coverage percentage and the unchecked test cases. Recommend running `/build` to implement missing tests, then re-run `/sprint-review`.
