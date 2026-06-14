# @write-code-check

How to run the structural write-code-check gate before handing a task off
to `/verify`. This is a pure-Python gate (no LLM calls) that catches four
classes of issue:

1. Files modified outside the task's declared `write_paths` (severity: **high**).
2. Public APIs removed between graph snapshots (severity: **medium**).
3. New utilities that duplicate existing cross-module symbols via fuzzy
   name matching (severity: **medium**).
4. TDD-spec checkboxes with no corresponding test file coverage
   (severity: **low**).

Run this skill **after** `/build` finishes and **before** `/verify`. It
complements verification rather than replacing it.

## Step 1 — Collect Changed Files

1. If `git` is available in the execution environment, the caller may
   rely on `git diff --name-only <start-commit> HEAD` to compute the
   changed file list.
2. If `git` is unavailable (or the task's build step ran outside a
   checkout), you **must** pass the changed file list explicitly via
   `--changed-files a.py b.py ...`.
3. Path separators are normalised internally; forward or backward slashes
   both work.

## Step 2 — Invoke the CLI

```
python scripts/write_code_check.py \
    --task <task_id> \
    --root <project_root> \
    [--changed-files a.py b.py ...] \
    [--deep]
```

- `--task` — lowercase task id (for example `t8-6`).
- `--root` — repository root containing `.claude/`.
- `--changed-files` — optional list; required when git diff is not available.
- `--deep` — **reserved for T8-7**. Accepts the flag but performs no
  semantic LLM analysis in the T8-6 implementation. Safe to pass; it is
  a no-op today.

## Step 3 — Read the Report

The CLI writes `.claude/progress/write-check-{task_id}.md`. The report
contains a YAML block with this shape:

```yaml
task_id: <id>
findings:
  - severity: high | medium | low
    category: files_outside_write_paths | removed_public_api |
              reinvented_utils | missing_task_spec_coverage
    detail: <one-line description>
    suggested_fix: <one-line remediation>
    path: <optional — file relative to repo root>
    file_ref: <optional — file + optional :line>
summary:
  by_severity:
    high: <count>
    medium: <count>
    low: <count>
```

## Step 4 — Act on Findings

- **high** findings indicate a boundary violation. Fix them before
  proceeding to `/verify` — otherwise the agent has written outside its
  declared scope.
- **medium** findings should be reviewed. Removed APIs may be a legitimate
  refactor; resemblance findings warrant a quick check for reuse.
- **low** findings are reminders. Missing TDD coverage does not block
  progress but should be reconciled in the same milestone.

T8-6 itself does not gate on exit code — it reports and moves on. T8-8
will wire exit-code-based gating into `/verify`.

## Degradation

| Condition                                       | Behaviour                      |
|------------------------------------------------|--------------------------------|
| Task file missing                               | write-paths list empty; skip   |
| `.claude/wiki/graph.json` missing               | skip reinvented / removed API  |
| TDD spec missing                                | skip coverage check            |
| `--changed-files` omitted and no git available  | pass empty list; no crash      |

No degradation path raises; every check returns an empty list when its
inputs are not present.

## Example Usage

```
Call @write-code-check with: task_id=t8-6, changed_files=scripts/core/foo.py
```

Equivalent CLI:

```
python scripts/write_code_check.py \
    --task t8-6 \
    --root . \
    --changed-files scripts/core/foo.py
```
