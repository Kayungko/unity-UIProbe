#!/usr/bin/env python3
"""Self-contained wiki LLM-review driver.

Copied verbatim into ``<project>/.claude/scripts/wiki_review.py`` by
``scaffold_gen._copy_command_driver_scripts`` so ``/sprint-review`` can run
the emit/fill/ingest loop on machines that do not have the harness skill
installed.

Behaviour mirrors the original ``scripts/reviewer.py`` ``--emit-wiki-review``
/ ``--ingest-wiki-review`` modes; the writer/reader/emitters are inlined
from ``core.llm_instruction`` and ``validation.wiki_llm_checks`` so this
file has zero internal imports.

Usage:
    python wiki_review.py --root <project_root> --emit-wiki-review
    python wiki_review.py --root <project_root> --ingest-wiki-review

Exit codes:
    0  emit succeeded, OR ingest found no high-severity findings
    1  ingest dispatched at least one high-severity finding (sprint blocked)
    2  ingest hit a schema error or pending file is still unfilled
"""

from __future__ import annotations

import argparse
import datetime as _dt
import re
import sys
from pathlib import Path
from typing import Any


# ---------------------------------------------------------------------------
# Inlined: core.llm_instruction
# ---------------------------------------------------------------------------

PLACEHOLDER = "<fill-findings>"

_ALLOWED_SEVERITIES = frozenset({"high", "medium", "low"})
_REQUIRED_FIELDS = ("severity", "category", "detail")


class InvalidFindingsError(Exception):
    """Raised when a filled review file fails YAML parsing or schema check."""


def _utc_timestamp() -> str:
    return _dt.datetime.now(_dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _render_context_paths(root: Path, context_paths: list[Path]) -> str:
    if not context_paths:
        return "_(no context files)_"
    lines: list[str] = []
    for p in context_paths:
        try:
            rel = Path(p).resolve().relative_to(Path(root).resolve())
            rendered = rel.as_posix()
        except ValueError:
            rendered = str(p).replace("\\", "/")
        lines.append(f"- {rendered}")
    return "\n".join(lines)


def write_review_instruction(
    root: Path,
    kind: str,
    instruction: str,
    context_paths: list[Path],
    schema_yaml: str,
    extra_sections: dict[str, str] | None = None,
) -> Path:
    if not isinstance(kind, str) or not kind.strip():
        raise ValueError("kind must be a non-empty string")

    progress_dir = Path(root) / ".claude" / "progress"
    progress_dir.mkdir(parents=True, exist_ok=True)
    out_path = progress_dir / f"{kind}-pending.md"

    timestamp = _utc_timestamp()
    body_parts: list[str] = [
        f"# LLM Review Pending — {kind} ({timestamp})",
        "",
        "## Instruction",
        "",
        instruction.strip() if instruction else "",
        "",
        "## Context files",
        "",
        _render_context_paths(Path(root), list(context_paths)),
        "",
        "## Schema",
        "",
        "```yaml",
        schema_yaml.strip(),
        "```",
        "",
    ]
    if extra_sections:
        for heading, body in extra_sections.items():
            if not heading:
                continue
            body_parts.append(f"## {heading}")
            body_parts.append("")
            body_parts.append(body.rstrip() if body else "")
            body_parts.append("")
    body_parts.extend(["## Findings", "", PLACEHOLDER, ""])

    out_path.write_text("\n".join(body_parts), encoding="utf-8")
    return out_path


_FINDINGS_HEADING_RE = re.compile(r"^##\s+Findings\s*$", re.MULTILINE)


def _extract_findings_section(content: str) -> str | None:
    match = _FINDINGS_HEADING_RE.search(content)
    if match is None:
        return None
    start = match.end()
    tail = content[start:]
    next_heading = re.search(r"^##\s+\S", tail, re.MULTILINE)
    if next_heading is None:
        return tail
    return tail[: next_heading.start()]


def _strip_yaml_fence(block: str) -> str:
    text = block.strip()
    fence_match = re.match(
        r"^```(?:ya?ml)?\s*\n(?P<body>.*?)\n```\s*$",
        text,
        re.DOTALL,
    )
    if fence_match:
        return fence_match.group("body")
    return text


def _validate_finding(finding: Any, index: int) -> dict[str, Any]:
    if not isinstance(finding, dict):
        raise InvalidFindingsError(
            f"finding[{index}] is not a mapping (got {type(finding).__name__})"
        )
    for field in _REQUIRED_FIELDS:
        if field not in finding:
            raise InvalidFindingsError(
                f"finding[{index}] missing required field: {field!r}"
            )
        value = finding[field]
        if value is None or (isinstance(value, str) and not value.strip()):
            raise InvalidFindingsError(
                f"finding[{index}] field {field!r} is empty"
            )
    severity = finding["severity"]
    if not isinstance(severity, str) or severity not in _ALLOWED_SEVERITIES:
        raise InvalidFindingsError(
            f"finding[{index}] severity {severity!r} not in "
            f"{sorted(_ALLOWED_SEVERITIES)}"
        )
    return finding


def read_filled_findings(path: Path) -> list[dict[str, Any]] | None:
    try:
        import yaml  # PyYAML
    except ImportError as exc:
        raise InvalidFindingsError(
            "PyYAML is required to ingest wiki-review findings. "
            "Install it with: pip install pyyaml"
        ) from exc

    p = Path(path)
    content = p.read_text(encoding="utf-8")

    if PLACEHOLDER in content:
        return None

    section = _extract_findings_section(content)
    if section is None:
        raise InvalidFindingsError(f"{p}: '## Findings' section not found")

    yaml_text = _strip_yaml_fence(section)
    if not yaml_text.strip():
        raise InvalidFindingsError(f"{p}: Findings section is empty")

    try:
        parsed = yaml.safe_load(yaml_text)
    except yaml.YAMLError as exc:
        raise InvalidFindingsError(f"{p}: YAML parse error: {exc}") from exc

    if not isinstance(parsed, dict) or "findings" not in parsed:
        raise InvalidFindingsError(f"{p}: top-level 'findings' key missing")

    findings = parsed["findings"]
    if not isinstance(findings, list):
        raise InvalidFindingsError(
            f"{p}: 'findings' must be a list (got {type(findings).__name__})"
        )

    return [_validate_finding(f, i) for i, f in enumerate(findings)]


# ---------------------------------------------------------------------------
# Inlined: validation.wiki_llm_checks (three emitters)
# ---------------------------------------------------------------------------

_PRD_CANDIDATES: tuple[str, ...] = (
    "docs/PRD.md",
    "docs/prd.md",
    "PRD.md",
    "prd.md",
    "docs/requirements.md",
)


def _build_schema(fixed_category: str) -> str:
    return (
        "findings:\n"
        "  - severity: high | medium | low   # required, one of the three literal values\n"
        f"    category: {fixed_category}          # required, must be this fixed value\n"
        "    source_ref: string                # required, e.g. wiki/modules/foo.md:L42 or PRD §3.2\n"
        "    module: string                    # optional, module slug (e.g. core, generators)\n"
        "    detail: string                    # required, one-sentence description of the problem\n"
        "    suggested_fix: string             # required, one-sentence remediation suggestion\n"
    )


_COMMON_INSTRUCTION_FOOTER = (
    "\n"
    "Output rules:\n"
    "- Emit YAML only under the ## Findings heading — no prose, no commentary, no prefix.\n"
    "- Wrap the YAML in a ```yaml fenced code block.\n"
    "- Findings cap: at most 20 entries. Report the most important issues first.\n"
    "- Every finding must include ALL required fields listed in Schema "
    "(severity, category, source_ref, detail, suggested_fix). Optional fields "
    "may be omitted only when truly not applicable.\n"
    "- severity must be exactly one of: high, medium, low.\n"
    "- If you find no issues, emit:\n"
    "  ```yaml\n"
    "  findings: []\n"
    "  ```\n"
)


def _collect_wiki_modules(root: Path) -> list[Path]:
    wiki_dir = Path(root) / ".claude" / "wiki" / "modules"
    if not wiki_dir.is_dir():
        return []
    return sorted(wiki_dir.glob("*.md"))


def _find_prd(root: Path) -> Path | None:
    for rel in _PRD_CANDIDATES:
        candidate = Path(root) / rel
        if candidate.is_file():
            return candidate
    return None


def emit_coverage_instruction(root: Path) -> Path | None:
    root = Path(root)
    wiki_files = _collect_wiki_modules(root)
    if not wiki_files:
        return None

    prd = _find_prd(root)
    context_paths: list[Path] = list(wiki_files)
    if prd is not None:
        context_paths = [prd, *wiki_files]

    if prd is not None:
        prd_note = (
            "A PRD has been provided in the context files. Use it as the "
            "ground-truth requirements list; every PRD requirement that is "
            "not addressed by at least one wiki/modules/*.md file is a "
            "coverage_gap finding."
        )
    else:
        prd_note = (
            "PRD 不可得 (PRD is not available). Fall back to README.md and "
            "CLAUDE.md (and any docs/*.md) as the implicit requirements "
            "baseline: flag features described there that are absent from "
            "wiki/modules/*.md."
        )

    instruction = (
        "你是 wiki 质量审查者 (wiki quality reviewer). Your task is to "
        "compare the PRD against .claude/wiki/modules/*.md and identify "
        "features, behaviours, or requirements that are missing, "
        "under-documented, or only partially covered by the wiki.\n"
        "\n"
        f"{prd_note}\n"
        f"{_COMMON_INSTRUCTION_FOOTER}"
    )

    return write_review_instruction(
        root=root,
        kind="wiki-coverage",
        instruction=instruction,
        context_paths=context_paths,
        schema_yaml=_build_schema("coverage_gap"),
    )


def emit_signature_ambiguity_instruction(root: Path) -> Path | None:
    root = Path(root)
    wiki_files = _collect_wiki_modules(root)
    if not wiki_files:
        return None

    instruction = (
        "你是 wiki 质量审查者 (wiki quality reviewer). For each module in "
        ".claude/wiki/modules/*.md, inspect the Interfaces section and "
        "flag entries whose params, returns, or errors descriptions are "
        "unclear, ambiguous, or internally inconsistent.\n"
        "\n"
        "Signals to look for:\n"
        "- params without types, shapes, or accepted value ranges;\n"
        "- returns described only as 'result' / 'data' / 'object' with no shape;\n"
        "- errors list missing (function clearly fails) or using vague terms "
        "like 'on failure' without naming the error type;\n"
        "- mismatch between signature (function name / arity) and prose "
        "description of params.\n"
        f"{_COMMON_INSTRUCTION_FOOTER}"
    )

    return write_review_instruction(
        root=root,
        kind="wiki-ambiguity",
        instruction=instruction,
        context_paths=list(wiki_files),
        schema_yaml=_build_schema("signature_ambiguity"),
    )


def emit_cross_module_conflict_instruction(root: Path) -> Path | None:
    root = Path(root)
    wiki_files = _collect_wiki_modules(root)
    if not wiki_files:
        return None

    instruction = (
        "你是 wiki 质量审查者 (wiki quality reviewer). Compare the "
        "constraints, interfaces, and entities across all modules in "
        ".claude/wiki/modules/*.md and flag statements that directly "
        "contradict each other.\n"
        "\n"
        "Signals to look for:\n"
        "- constraints with incompatible upper/lower bounds for the same resource;\n"
        "- same-named entity declared with different field types, field sets, "
        "or lifecycles in two modules;\n"
        "- interface signatures (params/returns) that disagree between a "
        "module that exposes the contract and a module that consumes it;\n"
        "- policy statements ('always X') that are flatly negated elsewhere.\n"
        f"{_COMMON_INSTRUCTION_FOOTER}"
    )

    return write_review_instruction(
        root=root,
        kind="wiki-conflict",
        instruction=instruction,
        context_paths=list(wiki_files),
        schema_yaml=_build_schema("cross_module_conflict"),
    )


_EMITTERS: tuple[tuple[str, Any], ...] = (
    ("wiki-coverage", emit_coverage_instruction),
    ("wiki-ambiguity", emit_signature_ambiguity_instruction),
    ("wiki-conflict", emit_cross_module_conflict_instruction),
)


# ---------------------------------------------------------------------------
# Driver entrypoints
# ---------------------------------------------------------------------------


def cmd_emit(root: Path) -> int:
    written: list[Path] = []
    skipped: list[str] = []
    for kind, emitter in _EMITTERS:
        out = emitter(root)
        if out is None:
            skipped.append(kind)
            continue
        written.append(Path(out))

    for p in written:
        try:
            rel = p.resolve().relative_to(root)
            print(f"WROTE: {rel.as_posix()}")
        except ValueError:
            print(f"WROTE: {p}")

    if skipped:
        print(
            f"SKIPPED: {', '.join(skipped)} (no wiki/modules content)",
            file=sys.stderr,
        )
    if not written:
        print("No pending files written (wiki/modules missing).")
    return 0


def _archive_pending(pending_path: Path, kind: str) -> Path:
    history_dir = pending_path.parent / "history"
    history_dir.mkdir(parents=True, exist_ok=True)
    stamp = _dt.datetime.now().strftime("%Y%m%d-%H%M%S")
    dest = history_dir / f"{kind}-{stamp}.md"
    counter = 1
    while dest.exists():
        dest = history_dir / f"{kind}-{stamp}-{counter}.md"
        counter += 1
    pending_path.rename(dest)
    return dest


def _format_finding(f: dict[str, Any], kind: str) -> str:
    detail = str(f.get("detail", "")).strip() or "(no detail)"
    source_ref = str(f.get("source_ref", "")).strip()
    module = str(f.get("module", "")).strip()
    suggested = str(f.get("suggested_fix", "")).strip()
    parts: list[str] = [f"[{kind}] {detail}"]
    if source_ref:
        parts.append(f"@ {source_ref}")
    if module:
        parts.append(f"(module: {module})")
    if suggested:
        parts.append(f"fix: {suggested}")
    return " ".join(parts)


def cmd_ingest(root: Path) -> int:
    progress_dir = root / ".claude" / "progress"
    if not progress_dir.is_dir():
        print(
            f"WARN: no pending wiki review found ({progress_dir} does not exist; "
            "run --emit-wiki-review first)",
            file=sys.stderr,
        )
        return 2

    pending: list[tuple[str, Path]] = []
    for kind, _emitter in _EMITTERS:
        p = progress_dir / f"{kind}-pending.md"
        if p.is_file():
            pending.append((kind, p))

    if not pending:
        print(
            f"WARN: no wiki-*-pending.md in {progress_dir}; run --emit-wiki-review first",
            file=sys.stderr,
        )
        return 2

    high_count = 0
    warn_count = 0
    unfilled: list[str] = []
    schema_errors: list[str] = []

    for kind, path in pending:
        try:
            findings = read_filled_findings(path)
        except InvalidFindingsError as exc:
            schema_errors.append(f"[{kind}] invalid findings schema in {path.name}: {exc}")
            continue

        if findings is None:
            unfilled.append(kind)
            continue

        for f in findings:
            severity = str(f.get("severity", "")).lower()
            line = _format_finding(f, kind)
            if severity == "high":
                print(f"ERROR: {line}")
                high_count += 1
            else:
                print(f"WARN: {line}")
                warn_count += 1

        try:
            _archive_pending(path, kind)
        except OSError as exc:
            print(
                f"WARN: [{kind}] findings dispatched but archive failed: {exc}",
                file=sys.stderr,
            )

    for msg in schema_errors:
        print(f"WARN: {msg}", file=sys.stderr)
    for kind in unfilled:
        print(
            f"WARN: pending LLM fill for {kind}, re-run after /sprint-review",
            file=sys.stderr,
        )

    print(
        f"SUMMARY: high={high_count} warn={warn_count} "
        f"unfilled={len(unfilled)} schema_errors={len(schema_errors)}"
    )

    if high_count > 0:
        return 1
    if unfilled or schema_errors:
        return 2
    return 0


def main() -> None:
    parser = argparse.ArgumentParser(description="Wiki LLM-review driver")
    parser.add_argument("--root", required=True, help="Project root containing .claude/")
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--emit-wiki-review", action="store_true",
                       help="Emit pending instruction files and exit.")
    group.add_argument("--ingest-wiki-review", action="store_true",
                       help="Ingest filled pending files and dispatch findings.")
    args = parser.parse_args()

    root = Path(args.root).resolve()
    if args.emit_wiki_review:
        sys.exit(cmd_emit(root))
    if args.ingest_wiki_review:
        sys.exit(cmd_ingest(root))


if __name__ == "__main__":
    main()
