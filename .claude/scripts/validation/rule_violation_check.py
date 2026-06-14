"""Rule-violation semantic check (fill-* pattern, no LLM calls).

T8-7 layer on top of T8-6 structural checks. This module:

1. Emits a ``rule-violation-{task_id}-pending.md`` instruction file that an
   LLM (running inside the Claude Code session that owns this codebase)
   fills in-place. The file contains:

   - the natural-language review instruction ("代码规则审查者"),
   - the full content of every file under ``.claude/rules/``,
   - pointers to each changed file,
   - a YAML schema describing the expected ``findings`` shape,
   - a ``<fill-findings>`` placeholder to be replaced by the LLM.

2. Reads the filled file back, schema-validates each finding, and returns
   a ``list[dict]`` for ``write_code_check.py`` to merge into the report.

No ``anthropic`` / ``requests`` / ``urllib`` imports. No network I/O.
Rules content is passed by reference in the Context section of the
pending file (rules files are small — no summarisation needed). When the
rules change, the next ``emit_*`` call regenerates with the new content.

Policy choice — huge diff degradation
-------------------------------------
When the cumulative line count of changed files exceeds
``DIFF_LINE_LIMIT`` (500), this module returns ``None`` from ``emit_*``
and the caller (``write_code_check.py --deep``) appends one low-severity
``diff_too_large_for_semantic_check`` finding to the structural report.
We prefer the visible "low finding" over silent skip because:

- The user needs to know why semantic coverage dropped for a given task.
- Low severity does not gate exit codes (T8-8) so it is harmless noise.
- Future work can split a huge changeset; this flag gives a reminder.
"""

from __future__ import annotations

import sys
from pathlib import Path
from typing import Any, Iterable

# Import T8-1 fill-* API from the sibling ``core`` package. Callers of
# this module ensure ``scripts/`` is on sys.path before import, mirroring
# the convention used by tests and ``write_code_check.py``.
from core.llm_instruction import (
    InvalidFindingsError,
    read_filled_findings,
    write_review_instruction,
)


# ---------------------------------------------------------------------------
# Public constants
# ---------------------------------------------------------------------------


DIFF_LINE_LIMIT = 500
"""Cumulative line-count cap above which semantic review is skipped."""


PENDING_KIND_PREFIX = "rule-violation-"
"""Stable prefix for the ``{kind}-pending.md`` filename."""


# ---------------------------------------------------------------------------
# Schema & instruction boilerplate
# ---------------------------------------------------------------------------


SCHEMA_YAML = """\
findings:
  - severity: high | medium | low
    category: rule_violation
    rule_ref: rules/<file>.md#<anchor>
    file_ref: path/to/changed_file.py:<line>
    detail: 一句话描述违反点（对应哪条规则、为什么违反）
    suggested_fix: 一句话建议如何修复
"""


INSTRUCTION_TEXT = """\
你是 **代码规则审查者**。

## 任务

扫描下方 "Context files" 中列出的**变更文件**，将其代码与
`.claude/rules/*.md` 中的规则逐条比对，标出**业务逻辑层面**的违反点
（不是 lint / 格式层面）。每条问题输出为一个 finding。

## 输出要求

- 严格按 ## Schema 给出的 YAML 结构
- 只输出 YAML，不要解释文字
- `category` 固定为 `rule_violation`
- `rule_ref` 指向触发规则（如 `rules/python-safety.md#SAFE-PY-001`）
- `file_ref` 指向变更文件 + 行号（如 `scripts/core/foo.py:42`）
- `severity` 根据规则的严重性和违反程度选择 high / medium / low
- 没有违反 → 输出 `findings: []`

## 典型案例（few-shot）

**案例 1** — `python-safety.md` 禁止宽泛异常吞没

```python
# 变更代码
def load_user(repo, uid):
    try:
        return repo.fetch(uid)
    except Exception:
        return None
```

应输出:

```yaml
findings:
  - severity: high
    category: rule_violation
    rule_ref: rules/python-safety.md#SAFE-PY-001
    file_ref: scripts/core/user.py:3
    detail: 捕获 except Exception 并静默返回 None，违反 SAFE-PY-001 禁止宽泛异常吞没
    suggested_fix: 捕获具体异常（如 RepositoryTimeoutError）并重新抛出或记录日志
```

**案例 2** — `testing-gates.md` 要求新公开行为必须有测试

```python
# 变更代码：新导出函数 normalise_path，但 tests/ 下无对应测试
def normalise_path(p: str) -> str:
    ...
```

应输出:

```yaml
findings:
  - severity: medium
    category: rule_violation
    rule_ref: rules/testing-gates.md#TEST-001
    file_ref: scripts/core/path_utils.py:1
    detail: 新导出 normalise_path 无对应测试文件，违反 TEST-001
    suggested_fix: 在 tests/ 下添加 test_path_utils.py 覆盖 normalise_path 行为
"""


# ---------------------------------------------------------------------------
# Path helpers
# ---------------------------------------------------------------------------


def pending_path_for(root: Path, task_id: str) -> Path:
    """Return the canonical pending-file path for a given task."""
    return (
        Path(root) / ".claude" / "progress"
        / f"{PENDING_KIND_PREFIX}{task_id}-pending.md"
    )


def _cumulative_line_count(changed_files: Iterable[Path]) -> int:
    """Count total lines across all existing changed files.

    Missing files (e.g. user passed a path that was never written) are
    silently skipped for counting purposes. This keeps the gate tolerant
    of CLI input that lists files the caller no longer has on disk.
    """
    total = 0
    for p in changed_files:
        try:
            path = Path(p)
            if not path.exists() or not path.is_file():
                continue
            with path.open("r", encoding="utf-8", errors="ignore") as fh:
                for _ in fh:
                    total += 1
        except OSError:
            continue
    return total


def _collect_rules_files(rules_dir: Path) -> list[Path]:
    """Return a sorted list of ``.md`` files under ``rules_dir``."""
    if not rules_dir or not Path(rules_dir).is_dir():
        return []
    return sorted(Path(rules_dir).glob("*.md"))


# ---------------------------------------------------------------------------
# emit_rule_violation_instruction
# ---------------------------------------------------------------------------


def emit_rule_violation_instruction(
    root: Path,
    rules_dir: Path,
    changed_files: list[Path],
    task_id: str,
) -> Path | None:
    """Write a ``rule-violation-{task_id}-pending.md`` review instruction.

    Args:
        root: Repository root. The pending file lives in
            ``{root}/.claude/progress/``.
        rules_dir: Directory containing ``*.md`` rule files (typically
            ``{root}/.claude/rules``).
        changed_files: Files modified by the current task. Paths can be
            absolute or relative to ``root``.
        task_id: Task identifier used in the filename (e.g. ``t8-7``).

    Returns:
        Path to the written pending file, or ``None`` when the semantic
        layer is skipped:

        - No changed files given → no review possible.
        - Cumulative line count of existing changed files exceeds
          ``DIFF_LINE_LIMIT`` → defer to a future, smaller changeset.

        In both skip cases, the caller is expected to surface an
        informative signal (e.g. a low-severity
        ``diff_too_large_for_semantic_check`` finding).

    This function does **not** call any LLM. It only writes a file for
    an external session to fill.
    """
    if not task_id or not str(task_id).strip():
        raise ValueError("task_id must be a non-empty string")

    if not changed_files:
        return None

    changed_paths = [Path(p) for p in changed_files]
    total_lines = _cumulative_line_count(changed_paths)
    if total_lines > DIFF_LINE_LIMIT:
        return None

    rules_files = _collect_rules_files(Path(rules_dir))
    # Context lists rule files first, then changed files — matches the
    # order the LLM should consult them in.
    context_paths = [*rules_files, *changed_paths]

    return write_review_instruction(
        root=Path(root),
        kind=f"{PENDING_KIND_PREFIX}{task_id}",
        instruction=INSTRUCTION_TEXT,
        context_paths=context_paths,
        schema_yaml=SCHEMA_YAML,
    )


# ---------------------------------------------------------------------------
# ingest_rule_violation_findings
# ---------------------------------------------------------------------------


def ingest_rule_violation_findings(
    root: Path,
    task_id: str,
    *,
    strict: bool = False,
) -> list[dict[str, Any]]:
    """Read & parse the filled pending file.

    Args:
        root: Repository root used to locate the pending file.
        task_id: Task identifier (matches the ``emit_*`` call).
        strict: When ``True``, re-raise :class:`InvalidFindingsError`
            instead of swallowing to ``[]``. Default ``False`` so the CLI
            stays unblocking when an LLM returns malformed YAML.

    Returns:
        - ``[]`` if the pending file does not exist.
        - ``[]`` if the placeholder ``<fill-findings>`` is still present
          (LLM has not filled yet).
        - ``[]`` on schema-validation failure when ``strict=False`` (a
          warning is printed to stderr so the user can fix the file).
        - ``list[dict]`` of validated findings otherwise.
    """
    path = pending_path_for(Path(root), task_id)
    if not path.exists():
        return []

    try:
        findings = read_filled_findings(path)
    except InvalidFindingsError as exc:
        if strict:
            raise
        print(
            f"[rule-violation] {path}: schema-invalid findings — skipping "
            f"semantic merge ({exc})",
            file=sys.stderr,
        )
        return []
    except FileNotFoundError:
        # Race between exists() and read_*; treat as missing.
        return []

    if findings is None:
        # Placeholder still present.
        return []
    return findings


__all__ = [
    "DIFF_LINE_LIMIT",
    "PENDING_KIND_PREFIX",
    "SCHEMA_YAML",
    "INSTRUCTION_TEXT",
    "emit_rule_violation_instruction",
    "ingest_rule_violation_findings",
    "pending_path_for",
]
