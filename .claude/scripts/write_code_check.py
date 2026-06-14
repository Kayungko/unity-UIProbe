#!/usr/bin/env python3
"""Structural write-code-check gate (pure Python, no LLM).

Ran by an agent after the build phase and before /verify to catch four
classes of issue that do not require semantic reasoning:

1. Files written outside the task's declared ``write_paths``.
2. Public APIs removed between two graph snapshots.
3. New utilities whose names resemble existing utilities in other modules
   (fuzzy match > 0.85).
4. TDD-spec checkboxes with no corresponding test file coverage.

Findings follow the same schema used by the LLM-review pipeline (T8-1 /
T8-2): ``severity`` / ``category`` / ``detail`` / optional ``path`` /
``file_ref`` / ``suggested_fix``.

The CLI prints a short summary and writes ``.claude/progress/write-check-
{task_id}.md`` with all findings plus a ``by_severity`` count.

``--deep`` is accepted and reserved for T8-7 (which adds a semantic-rules
layer via the fill-* pattern). This module itself performs no LLM calls
and does not import ``anthropic`` / ``requests`` / ``urllib``.
"""

from __future__ import annotations

import argparse
import datetime as _dt
import difflib
import json
import re
import sys
from pathlib import Path
from typing import Any, Iterable


# ---------------------------------------------------------------------------
# Path helpers
# ---------------------------------------------------------------------------


def _normalise_path(p: str) -> str:
    """Normalise a path string: forward slashes, no trailing slash."""
    if p is None:
        return ""
    text = str(p).replace("\\", "/").strip()
    while text.endswith("/"):
        text = text[:-1]
    return text


def _is_inside(path: str, prefix: str) -> bool:
    """Return True when ``path`` is ``prefix`` or sits beneath it."""
    path = _normalise_path(path)
    prefix = _normalise_path(prefix)
    if not prefix:
        return False
    if path == prefix:
        return True
    return path.startswith(prefix + "/")


# ---------------------------------------------------------------------------
# Finding helpers
# ---------------------------------------------------------------------------


def _finding(
    severity: str,
    category: str,
    detail: str,
    suggested_fix: str,
    *,
    path: str | None = None,
    file_ref: str | None = None,
) -> dict[str, Any]:
    """Build a finding dict with consistent field ordering."""
    record: dict[str, Any] = {
        "severity": severity,
        "category": category,
        "detail": detail,
        "suggested_fix": suggested_fix,
    }
    if path is not None:
        record["path"] = path
    if file_ref is not None:
        record["file_ref"] = file_ref
    return record


# ---------------------------------------------------------------------------
# 1. check_files_outside_write_paths
# ---------------------------------------------------------------------------


def check_files_outside_write_paths(
    task_id: str,
    changed_files: Iterable[str],
    write_paths: Iterable[str],
) -> list[dict[str, Any]]:
    """Flag any changed file that is not underneath a declared ``write_paths``.

    Severity: ``high`` — boundary violations break agent ownership guarantees.
    """
    prefixes = [_normalise_path(p) for p in (write_paths or []) if p]
    findings: list[dict[str, Any]] = []

    for raw_file in changed_files or []:
        rel = _normalise_path(raw_file)
        if not rel:
            continue
        if any(_is_inside(rel, prefix) for prefix in prefixes):
            continue
        findings.append(
            _finding(
                severity="high",
                category="files_outside_write_paths",
                detail=(
                    f"Task {task_id} modified '{rel}' which is not inside "
                    f"any declared write path."
                ),
                suggested_fix=(
                    "Move the change inside the task's write_paths, or "
                    "amend the task to declare this path."
                ),
                path=rel,
            )
        )
    return findings


# ---------------------------------------------------------------------------
# 2. check_removed_public_apis
# ---------------------------------------------------------------------------


def _collect_symbol_names(graph: dict[str, Any] | None) -> set[str]:
    if not graph:
        return set()
    nodes = graph.get("nodes", []) or []
    out: set[str] = set()
    for node in nodes:
        name = node.get("name") or node.get("label") or node.get("id")
        if not name:
            continue
        kind = node.get("kind", "")
        # Limit to code symbols — skip hubs / docs / concepts.
        if kind in {"file", "document", "concept", "decision"}:
            continue
        out.add(str(name))
    return out


def check_removed_public_apis(
    graph_before: dict[str, Any] | None,
    graph_after: dict[str, Any] | None,
) -> list[dict[str, Any]]:
    """Flag symbols present in ``graph_before`` but absent in ``graph_after``.

    Severity: ``medium`` — removals may be intentional refactors, but the
    user should confirm each one.
    """
    if graph_before is None or graph_after is None:
        return []
    before = _collect_symbol_names(graph_before)
    after = _collect_symbol_names(graph_after)
    removed = sorted(before - after)
    return [
        _finding(
            severity="medium",
            category="removed_public_api",
            detail=f"{name}",
            suggested_fix=(
                "Confirm the removal is intentional; otherwise restore the "
                "public symbol or publish a migration note."
            ),
        )
        for name in removed
    ]


# ---------------------------------------------------------------------------
# 3. check_reinvented_utils
# ---------------------------------------------------------------------------


_FUZZY_THRESHOLD = 0.85


def _node_module(node: dict[str, Any]) -> str:
    """Best-effort module extraction from a graph node."""
    if "module" in node and node["module"]:
        return str(node["module"])
    source = node.get("source_file") or ""
    parts = _normalise_path(source).split("/")
    # Heuristic: use the second path component (e.g. scripts/core/foo.py
    # -> 'core'). Falls back to the first component or empty string.
    if len(parts) >= 2:
        return parts[1] if parts[0] in {"scripts", "src", "lib"} else parts[0]
    return parts[0] if parts else ""


def check_reinvented_utils(
    changed_entities: Iterable[dict[str, Any]],
    existing_graph: dict[str, Any] | None,
) -> list[dict[str, Any]]:
    """Flag new symbols that fuzzily match existing cross-module utilities.

    Match rule: ratio > ``_FUZZY_THRESHOLD`` and a different module. The
    first existing node above the threshold is reported.

    Severity: ``medium``.
    """
    if not existing_graph or not changed_entities:
        return []

    existing_nodes = existing_graph.get("nodes", []) or []
    findings: list[dict[str, Any]] = []

    for entry in changed_entities:
        new_name = entry.get("name")
        if not new_name:
            continue
        new_module = _node_module(entry)
        best: tuple[float, dict[str, Any]] | None = None
        for node in existing_nodes:
            existing_name = node.get("name") or node.get("label")
            if not existing_name:
                continue
            if existing_name == new_name and _node_module(node) == new_module:
                # Same name + same module = same symbol, not reinvention.
                continue
            ratio = difflib.SequenceMatcher(
                None, str(new_name), str(existing_name)
            ).ratio()
            if ratio <= _FUZZY_THRESHOLD:
                continue
            if _node_module(node) == new_module:
                continue  # same module — not cross-module reinvention
            if best is None or ratio > best[0]:
                best = (ratio, node)
        if best is None:
            continue
        ratio, node = best
        existing_loc = node.get("source_file") or node.get("source_location") or ""
        findings.append(
            _finding(
                severity="medium",
                category="reinvented_utils",
                detail=(
                    f"New symbol '{new_name}' in {entry.get('source_file', '?')} "
                    f"resembles existing '{node.get('name', '?')}' in "
                    f"{existing_loc} (similarity {ratio:.2f})."
                ),
                suggested_fix=(
                    "Reuse the existing utility or move the new one into a "
                    "shared module."
                ),
                file_ref=entry.get("source_file"),
            )
        )
    return findings


# ---------------------------------------------------------------------------
# 4. check_missing_task_spec_coverage
# ---------------------------------------------------------------------------


_CHECKBOX_RE = re.compile(r"^\s*-\s*\[([ xX])\]\s+(.+?)\s*$")


def _parse_spec_checkboxes(spec_text: str) -> list[str]:
    """Extract checkbox item texts from a TDD spec."""
    items: list[str] = []
    for line in spec_text.splitlines():
        m = _CHECKBOX_RE.match(line)
        if not m:
            continue
        items.append(m.group(2).strip())
    return items


def _tokenise(text: str) -> set[str]:
    """Lower-case alphanumeric tokens >= 3 chars."""
    tokens = re.findall(r"[A-Za-z0-9]+", text.lower())
    return {t for t in tokens if len(t) >= 3}


def check_missing_task_spec_coverage(
    task_id: str,
    changed_files: Iterable[str],
    tdd_spec_path: Path,
) -> list[dict[str, Any]]:
    """Flag spec checkboxes that have no apparent test coverage.

    Heuristic: tokenise each checkbox's text (lowercased words >= 3 chars).
    For each changed test file, read the text and match tokens. A checkbox
    is considered covered when >= 60% of its meaningful tokens appear in
    any single test file.

    Severity: ``low`` (reminder only, never blocks).
    """
    tdd_spec_path = Path(tdd_spec_path)
    if not tdd_spec_path.exists():
        return []
    try:
        spec_text = tdd_spec_path.read_text(encoding="utf-8")
    except OSError:
        return []

    items = _parse_spec_checkboxes(spec_text)
    if not items:
        return []

    # Build concatenated text from test-like changed files.
    test_texts: list[str] = []
    for raw in changed_files or []:
        path = Path(raw)
        name = path.name.lower()
        if not ("test" in name or name.startswith("test_") or name.endswith("_test.py")):
            continue
        try:
            if path.exists():
                test_texts.append(path.read_text(encoding="utf-8", errors="ignore"))
        except OSError:
            continue
    haystack = "\n".join(test_texts).lower()
    haystack_tokens = _tokenise(haystack)

    findings: list[dict[str, Any]] = []
    for item in items:
        tokens = _tokenise(item)
        if not tokens:
            continue
        overlap = tokens & haystack_tokens
        if not tokens or (len(overlap) / len(tokens)) < 0.6:
            findings.append(
                _finding(
                    severity="low",
                    category="missing_task_spec_coverage",
                    detail=(
                        f"Spec checkbox '{item}' does not appear to be "
                        f"covered by any changed test file."
                    ),
                    suggested_fix=(
                        "Add a unit / integration test whose name or body "
                        "references the checkbox behaviour."
                    ),
                )
            )
    return findings


# ---------------------------------------------------------------------------
# Graph loading (degradation-safe)
# ---------------------------------------------------------------------------


def load_graph(root: Path, which: str = "current") -> dict[str, Any] | None:
    """Load ``.claude/wiki/graph.json`` if present; otherwise return None.

    ``which`` is reserved for future use (e.g. loading a pre-build snapshot).
    Callers treat None as 'skip graph-dependent checks'.
    """
    root = Path(root)
    candidate = root / ".claude" / "wiki" / "graph.json"
    if not candidate.exists():
        return None
    try:
        return json.loads(candidate.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return None


# ---------------------------------------------------------------------------
# Task file parsing
# ---------------------------------------------------------------------------


def parse_task_write_paths(root: Path, task_id: str) -> list[str]:
    """Extract declared write paths from ``.claude/milestones/tasks/{id}.md``.

    Supports both ``## 写入路径`` (zh-CN) and ``## Write paths`` (en) headers.
    Path lines may be backtick-quoted (``- `scripts/core/foo.py```) or
    plain (``- scripts/core/foo.py``), and may carry trailing annotations
    such as ``（新建）`` — those are stripped.

    Missing task file → return ``[]`` (caller must handle the empty case).
    """
    root = Path(root)
    task_file = root / ".claude" / "milestones" / "tasks" / f"{task_id}.md"
    if not task_file.exists():
        return []

    try:
        text = task_file.read_text(encoding="utf-8")
    except OSError:
        return []

    lines = text.splitlines()
    in_section = False
    paths: list[str] = []
    header_re = re.compile(
        r"^##\s+(写入路径|Write\s+paths|Write-Paths|Paths)\s*$",
        re.IGNORECASE,
    )
    for line in lines:
        if header_re.match(line):
            in_section = True
            continue
        if in_section:
            if line.startswith("## "):
                break  # next section
            # Prefer backtick-quoted paths (may have trailing annotations
            # like "（新建）"). Fall back to a plain bullet.
            m = re.match(r"^\s*-\s+`([^`\n]+)`", line)
            if not m:
                m = re.match(r"^\s*-\s+([^\s].*?)\s*$", line)
            if m:
                candidate = m.group(1).strip()
                # Ignore bullets that look like placeholders.
                if candidate and not candidate.startswith("*"):
                    paths.append(_normalise_path(candidate))
    return paths


# ---------------------------------------------------------------------------
# Report writer
# ---------------------------------------------------------------------------


def _count_severities(findings: list[dict[str, Any]]) -> dict[str, int]:
    counts = {"high": 0, "medium": 0, "low": 0}
    for f in findings:
        sev = str(f.get("severity", "")).lower()
        if sev in counts:
            counts[sev] += 1
    return counts


def _render_finding(f: dict[str, Any], indent: str = "  ") -> list[str]:
    """Render a single finding as a bullet-list YAML entry (multi-line)."""
    out: list[str] = []
    out.append(f"{indent}- severity: {f.get('severity', '')}")
    out.append(f"{indent}  category: {f.get('category', '')}")
    out.append(
        f"{indent}  detail: {json.dumps(f.get('detail', ''), ensure_ascii=False)}"
    )
    for optional in ("rule_ref", "file_ref", "path"):
        if optional in f and f[optional] is not None:
            out.append(
                f"{indent}  {optional}: "
                f"{json.dumps(f[optional], ensure_ascii=False)}"
            )
    if "suggested_fix" in f and f["suggested_fix"] is not None:
        out.append(
            f"{indent}  suggested_fix: "
            f"{json.dumps(f['suggested_fix'], ensure_ascii=False)}"
        )
    return out


def _format_report(
    task_id: str,
    findings: list[dict[str, Any]],
    semantic_findings: list[dict[str, Any]] | None = None,
) -> str:
    """Produce the markdown report body.

    When ``semantic_findings`` is provided (T8-7 ``--deep`` mode), the
    report splits structural and semantic findings while still emitting
    a unified ``by_severity`` count across both.
    """
    has_semantic = semantic_findings is not None
    all_findings = list(findings) + list(semantic_findings or [])
    counts = _count_severities(all_findings)

    lines: list[str] = []
    lines.append(f"# Write-code Check Report — {task_id}")
    lines.append("")
    lines.append("```yaml")
    lines.append(f"task_id: {task_id}")

    if has_semantic:
        # Split view — keeps T8-6 fields alongside T8-7 semantic layer.
        lines.append("structural_findings:")
        if not findings:
            lines.append("  []")
        else:
            for f in findings:
                lines.extend(_render_finding(f))
        lines.append("semantic_findings:")
        if not semantic_findings:
            lines.append("  []")
        else:
            for f in semantic_findings:
                lines.extend(_render_finding(f))
    else:
        # Backward-compatible single list for non-deep mode.
        lines.append("findings:")
        if not findings:
            lines.append("  []")
        else:
            for f in findings:
                lines.extend(_render_finding(f))

    lines.append("summary:")
    lines.append("  by_severity:")
    lines.append(f"    high: {counts['high']}")
    lines.append(f"    medium: {counts['medium']}")
    lines.append(f"    low: {counts['low']}")
    lines.append("```")
    lines.append("")
    return "\n".join(lines)


def _format_timestamp(now: _dt.datetime | None = None) -> str:
    """Return a filename-safe ``YYYYMMDD-HHMMSS`` stamp (local time)."""
    now = now or _dt.datetime.now()
    return now.strftime("%Y%m%d-%H%M%S")


def write_report(
    root: Path,
    task_id: str,
    findings: list[dict[str, Any]],
    semantic_findings: list[dict[str, Any]] | None = None,
    *,
    timestamp: str | None = None,
) -> Path:
    """Write the structural (+ optional semantic) findings report.

    ``semantic_findings`` is an optional second list that ``write-code-
    check --deep`` passes through from T8-7's ``ingest_rule_violation_
    findings``. When present, the report distinguishes the two layers;
    when absent the output is the T8-6-compatible single-list shape.

    T8-8 behaviour:

    - ``timestamp=None`` (default) → legacy filename
      ``write-check-{task_id}.md``. Preserves back-compat for T8-6
      direct callers and tests.
    - ``timestamp=<YYYYMMDD-HHMMSS>`` → timestamped filename
      ``write-check-{task_id}-{timestamp}.md``.

    The CLI writes **both** on every run: one timestamped for the audit
    trail, one legacy for tools that look up the canonical name.
    """
    root = Path(root)
    progress_dir = root / ".claude" / "progress"
    progress_dir.mkdir(parents=True, exist_ok=True)
    if timestamp:
        name = f"write-check-{task_id}-{timestamp}.md"
    else:
        name = f"write-check-{task_id}.md"
    out = progress_dir / name
    out.write_text(
        _format_report(task_id, findings, semantic_findings),
        encoding="utf-8",
    )
    return out


# ---------------------------------------------------------------------------
# Session-log Pending layer writer (T8-8 --accept-check support)
# ---------------------------------------------------------------------------


# Regex matches either the English or Chinese Pending heading. We keep this
# permissive because @write-session-log skill lets the user pick either
# wording in different project locales.
_PENDING_HEADING_RE = re.compile(
    r"^##\s+(待办\s*\(Pending\)|Pending(\s*/\s*待办)?|Pending\s*层)\s*$",
    re.IGNORECASE,
)


def _append_accept_check_to_session_log(
    root: Path,
    task_id: str,
    reason: str,
    high_count: int,
) -> Path | None:
    """Append an 'acknowledged' entry to the Pending layer of session-log.

    Follows the @write-session-log convention: a single bullet ending in
    the acknowledgment metadata, inserted at the end of the existing
    Pending section so it sits alongside other open items.

    Creates the file with a minimal Pending scaffold if it does not yet
    exist — this mirrors the behaviour of @write-session-log when a new
    workspace has never written a log before.

    Returns the path that was written, or ``None`` when the file cannot
    be touched (e.g. non-writable filesystem). Errors are swallowed so
    that the CLI does not crash on an unrelated I/O problem; the caller
    sees exit 0 for the accepted check regardless.
    """
    root = Path(root)
    progress_dir = root / ".claude" / "progress"
    try:
        progress_dir.mkdir(parents=True, exist_ok=True)
    except OSError:
        return None
    log_path = progress_dir / "session-log.md"

    entry = (
        f"- **acknowledged: {reason}** "
        f"(write-check {task_id}, high findings count={high_count})"
    )

    try:
        if not log_path.exists():
            # Minimal scaffold mirroring @write-session-log's Pending layer.
            log_path.write_text(
                "# Session Log\n\n"
                "## 待办 (Pending)\n\n"
                "<!-- 未完成 / 阻塞 / 下一步。永不压缩。 -->\n\n"
                f"{entry}\n\n"
                "## Archive\n",
                encoding="utf-8",
            )
            return log_path

        text = log_path.read_text(encoding="utf-8")
    except OSError:
        return None

    lines = text.splitlines()
    pending_start: int | None = None
    for i, line in enumerate(lines):
        if _PENDING_HEADING_RE.match(line):
            pending_start = i
            break

    if pending_start is None:
        # No Pending section found — append one at the end so the entry is
        # still recoverable.
        appended = text.rstrip() + "\n\n## 待办 (Pending)\n\n" + entry + "\n"
        try:
            log_path.write_text(appended, encoding="utf-8")
        except OSError:
            return None
        return log_path

    # Find the next ## heading that terminates the Pending section.
    end = len(lines)
    for j in range(pending_start + 1, len(lines)):
        if lines[j].startswith("## "):
            end = j
            break

    # Insert the entry just before ``end``; trim trailing blank lines inside
    # the Pending block so the new bullet sits next to existing ones.
    insert_at = end
    while insert_at - 1 > pending_start and lines[insert_at - 1].strip() == "":
        insert_at -= 1
    new_lines = lines[:insert_at] + [entry] + lines[insert_at:]
    try:
        log_path.write_text("\n".join(new_lines) + "\n", encoding="utf-8")
    except OSError:
        return None
    return log_path


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------


def _run_checks(
    root: Path,
    task_id: str,
    changed_files: list[str],
    deep: bool,
) -> list[dict[str, Any]]:
    findings: list[dict[str, Any]] = []

    # 1. Write-path boundary. Ad-hoc runs (/quick, /debug) have no milestone
    # task file, hence no declared boundary to enforce — skip the check so the
    # gate does not flag every changed file. A task file that exists but
    # declares no write_paths is a config error and is still flagged.
    task_file = root / ".claude" / "milestones" / "tasks" / f"{task_id}.md"
    if task_file.exists():
        write_paths = parse_task_write_paths(root, task_id)
        findings.extend(
            check_files_outside_write_paths(task_id, changed_files, write_paths)
        )
    else:
        findings.append(
            _finding(
                severity="low",
                category="write_paths_unavailable",
                detail=(
                    f"No task file for '{task_id}'; skipping write-path "
                    f"boundary check (ad-hoc run)."
                ),
                suggested_fix=(
                    "Run inside a milestone task to enforce write-path "
                    "boundaries."
                ),
            )
        )

    # 2 + 3. Graph-dependent checks. Missing graph → skip gracefully.
    graph = load_graph(root, which="current")
    if graph is not None:
        # Without a 'before' snapshot we cannot diff removed APIs; skip
        # quietly. Full before/after wiring is a pipeline concern (T8-8).
        pass

    # 4. TDD spec coverage. Resolve default spec path as core.spec.md;
    # callers with different modules should pass their spec explicitly,
    # which T8-8 will wire up. For now we skip when no spec is found.
    default_spec = root / ".claude" / "tdd" / "specs" / "core.spec.md"
    findings.extend(
        check_missing_task_spec_coverage(
            task_id=task_id,
            changed_files=changed_files,
            tdd_spec_path=default_spec,
        )
    )

    # --deep is handled in main() so that the semantic layer can split
    # its own output into structural_findings vs semantic_findings.
    _ = deep
    return findings


def _run_semantic_layer(
    root: Path,
    task_id: str,
    changed_files: list[str],
) -> tuple[list[dict[str, Any]] | None, Path | None, dict[str, Any]]:
    """Run the T8-7 fill-* semantic layer.

    Returns a tuple ``(semantic_findings, pending_path, info)`` where:

    - ``semantic_findings`` is ``None`` when the layer is skipped
      (no diff or oversized diff) — caller keeps the structural-only
      report shape. Otherwise a (possibly empty) list.
    - ``pending_path`` is the path to the emitted instruction file,
      or ``None`` when nothing was emitted.
    - ``info`` carries diagnostics for the CLI stdout ("pending",
      "filled", "skipped").
    """
    # Late import keeps the non-deep CLI free of validation coupling.
    from validation.rule_violation_check import (  # noqa: WPS433
        emit_rule_violation_instruction,
        ingest_rule_violation_findings,
        pending_path_for,
    )
    from core.llm_instruction import PLACEHOLDER  # noqa: WPS433

    rules_dir = Path(root) / ".claude" / "rules"

    # Resolve changed-files relative to root when they're not absolute,
    # so emit_* can count line lengths correctly.
    resolved: list[Path] = []
    for f in changed_files:
        p = Path(f)
        if not p.is_absolute():
            p = (Path(root) / p).resolve()
        resolved.append(p)

    pending_path = pending_path_for(Path(root), task_id)

    # If a pending file already exists from a previous deep run AND the
    # LLM has filled it, ingest directly without overwriting. This makes
    # the second run idempotent (emit on first run → fill → ingest on
    # second run, without losing the fill).
    if pending_path.exists():
        existing = pending_path.read_text(encoding="utf-8")
        if PLACEHOLDER not in existing:
            findings = ingest_rule_violation_findings(
                root=Path(root), task_id=task_id
            )
            if findings:
                return findings, pending_path, {"status": "filled"}
            # Filled but empty or schema-invalid-swallowed → treat as
            # "no findings" from the LLM.
            return [], pending_path, {"status": "filled_empty"}

    # Either no pending file yet, or placeholder is still present.
    # Emit (or re-emit) the instruction file.
    pending = emit_rule_violation_instruction(
        root=Path(root),
        rules_dir=rules_dir,
        changed_files=resolved,
        task_id=task_id,
    )

    if pending is None:
        # Emit skipped → diff empty or too large.
        if not changed_files:
            return None, None, {"status": "skipped_empty_diff"}
        return None, None, {"status": "skipped_huge_diff"}

    return [], pending, {"status": "pending_fill"}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Structural write-code-check (+ optional T8-7 semantic layer "
            "via --deep). Pure Python, no LLM calls."
        ),
    )
    parser.add_argument("--task", required=True, help="Task id, e.g. t8-6")
    parser.add_argument(
        "--root",
        required=True,
        help="Repository root containing .claude/",
    )
    parser.add_argument(
        "--changed-files",
        nargs="*",
        default=[],
        help="List of files changed during this task; required if git is "
        "unavailable in the execution environment.",
    )
    parser.add_argument(
        "--deep",
        action="store_true",
        help=(
            "Enable the T8-7 semantic layer: emit a rule-violation-"
            "<task>-pending.md for the LLM to fill, and merge its "
            "findings on subsequent runs."
        ),
    )
    parser.add_argument(
        "--accept-check",
        metavar="REASON",
        default=None,
        help=(
            "Acknowledge any high-severity findings with the given "
            "reason. Flips the exit code back to 0 AND appends an "
            "entry to the Pending layer of session-log.md."
        ),
    )
    args = parser.parse_args(argv)

    root = Path(args.root).resolve()
    task_id = args.task
    changed_files = list(args.changed_files or [])

    structural_findings = _run_checks(
        root=root,
        task_id=task_id,
        changed_files=changed_files,
        deep=bool(args.deep),
    )

    semantic_findings: list[dict[str, Any]] | None = None
    pending_path: Path | None = None
    info: dict[str, Any] = {}

    if args.deep:
        semantic_findings, pending_path, info = _run_semantic_layer(
            root=root,
            task_id=task_id,
            changed_files=changed_files,
        )
        # Add a low-severity marker for oversized diffs so the user can
        # see why the semantic layer skipped this task.
        if info.get("status") == "skipped_huge_diff":
            structural_findings.append(
                _finding(
                    severity="low",
                    category="diff_too_large_for_semantic_check",
                    detail=(
                        f"Task {task_id} diff exceeds the semantic-check "
                        f"size limit; skipping rule-violation review."
                    ),
                    suggested_fix=(
                        "Split the changeset into smaller tasks or run "
                        "write-code-check --deep on the tighter subset."
                    ),
                )
            )

    # T8-8: write two reports — timestamped (for audit trail) and legacy
    # (for back-compat with tooling / tests that look up the canonical
    # name). Timestamped file is the "primary" report referenced in
    # reviewer pending-high scans.
    timestamp = _format_timestamp()
    stamped_path = write_report(
        root=root,
        task_id=task_id,
        findings=structural_findings,
        semantic_findings=semantic_findings,
        timestamp=timestamp,
    )
    legacy_path = write_report(
        root=root,
        task_id=task_id,
        findings=structural_findings,
        semantic_findings=semantic_findings,
    )

    all_findings = list(structural_findings) + list(semantic_findings or [])
    counts = _count_severities(all_findings)
    print(
        f"[write-check] {task_id}: "
        f"high={counts['high']} medium={counts['medium']} low={counts['low']}"
    )
    print(f"[write-check] report: {stamped_path}")
    if legacy_path != stamped_path:
        print(f"[write-check] legacy report: {legacy_path}")

    if args.deep:
        status = info.get("status", "")
        if status == "pending_fill" and pending_path is not None:
            print(
                f"[write-check] Semantic findings pending. Open and fill: "
                f"{pending_path}. Then re-run: write_code_check --deep "
                f"--task {task_id} --root {root}"
            )
        elif status == "filled" and pending_path is not None:
            print(
                f"[write-check] Semantic layer merged "
                f"{len(semantic_findings or [])} findings from {pending_path}"
            )
        elif status == "filled_empty":
            print("[write-check] Semantic layer: LLM reported no violations.")
        elif status == "skipped_empty_diff":
            print("[write-check] Semantic layer skipped: no changed files.")
        elif status == "skipped_huge_diff":
            print(
                "[write-check] Semantic layer skipped: diff exceeds "
                f"the size limit."
            )

    # Exit-code gating — only combined high severity blocks.
    high_count = counts["high"]
    if high_count == 0:
        return 0

    if args.accept_check:
        # --accept-check flips the gate to 0 and records the override.
        log_path = _append_accept_check_to_session_log(
            root=root,
            task_id=task_id,
            reason=str(args.accept_check),
            high_count=high_count,
        )
        if log_path is not None:
            print(
                f"[write-check] acknowledged {high_count} high finding(s); "
                f"logged to {log_path}"
            )
        else:
            print(
                f"[write-check] acknowledged {high_count} high finding(s); "
                f"session-log update skipped (I/O)"
            )
        return 0

    print(
        f"[write-check] {high_count} high finding(s) block this task. "
        f"Fix them, or re-run with --accept-check '<reason>'."
    )
    return 1


if __name__ == "__main__":
    sys.exit(main())
