"""LLM review-instruction framework (fill-* pattern).

This module writes **instruction files** that an LLM (running inside the
Claude Code session that owns this codebase) will fill in-place, and reads
those files back once filled. Python is the producer and consumer only —
no network / SDK calls happen here.

The pattern mirrors the existing ``<fill-entities>`` / ``<fill-interfaces>``
/ ``<fill-related-tasks>`` placeholders used elsewhere in the wiki pipeline:

    1. Python writes ``.claude/progress/{kind}-pending.md`` containing a
       natural-language instruction, a list of context files, a YAML schema
       describing the expected output shape, and a ``<fill-findings>``
       placeholder.
    2. The Claude Code session follows the instruction and replaces the
       placeholder with a YAML block of findings.
    3. Python reads the filled file back, parses the YAML, schema-validates
       each finding, and returns a ``list[dict]`` for downstream dispatch.

No ``anthropic`` / ``requests`` / ``urllib`` imports — this module has no
network dependencies. Validation errors are surfaced as
``InvalidFindingsError`` rather than swallowed.
"""

from __future__ import annotations

import datetime as _dt
import re
from pathlib import Path
from typing import Any

import yaml


# ---------------------------------------------------------------------------
# Public constants / exceptions
# ---------------------------------------------------------------------------


PLACEHOLDER = "<fill-findings>"

_ALLOWED_SEVERITIES = frozenset({"high", "medium", "low"})
_REQUIRED_FIELDS = ("severity", "category", "detail")


class InvalidFindingsError(Exception):
    """Raised when a filled review file fails YAML parsing or schema check."""


# ---------------------------------------------------------------------------
# Writer
# ---------------------------------------------------------------------------


def _utc_timestamp() -> str:
    """Return an ISO-8601 UTC timestamp (seconds precision)."""
    return _dt.datetime.now(_dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _render_context_paths(root: Path, context_paths: list[Path]) -> str:
    """Render context paths as a markdown bullet list, relative to ``root``.

    Paths that cannot be made relative to ``root`` fall back to their string
    form rather than raising — this keeps the writer tolerant of callers
    that pass already-relative or out-of-tree paths.
    """
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
    """Write a ``{kind}-pending.md`` review file with a ``<fill-findings>`` placeholder.

    Args:
        root: Repository root; the pending file is written to
            ``{root}/.claude/progress/{kind}-pending.md``.
        kind: Short identifier (e.g. ``wiki-coverage``, ``wiki-ambiguity``).
            Used both in the filename and in the file header.
        instruction: Natural-language prompt describing what the LLM should do
            and what output shape is expected.
        context_paths: Input files the reviewer should consult. Rendered as a
            bullet list relative to ``root`` when possible.
        schema_yaml: YAML-shaped schema describing each finding. Rendered
            verbatim inside a ``yaml`` fenced block so the LLM has an exact
            template to fill.
        extra_sections: Optional additional sections (``{heading: body}``),
            rendered between the Schema and Findings sections.

    Returns:
        Path to the written pending file.

    Notes:
        - Does NOT call any LLM. No network I/O.
        - Overwrites an existing pending file for the same ``kind``.
        - Creates ``{root}/.claude/progress/`` if missing.
    """
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

    body_parts.extend(
        [
            "## Findings",
            "",
            PLACEHOLDER,
            "",
        ]
    )

    out_path.write_text("\n".join(body_parts), encoding="utf-8")
    return out_path


# ---------------------------------------------------------------------------
# Reader
# ---------------------------------------------------------------------------


_FINDINGS_HEADING_RE = re.compile(
    r"^##\s+Findings\s*$",
    re.MULTILINE,
)


def _extract_findings_section(content: str) -> str | None:
    """Return the text between ``## Findings`` and the next top-level heading.

    Returns ``None`` if the ``## Findings`` heading is absent.
    """
    match = _FINDINGS_HEADING_RE.search(content)
    if match is None:
        return None
    start = match.end()
    # Find next top-level ``## `` heading to bound the section.
    tail = content[start:]
    next_heading = re.search(r"^##\s+\S", tail, re.MULTILINE)
    if next_heading is None:
        return tail
    return tail[: next_heading.start()]


def _strip_yaml_fence(block: str) -> str:
    """Strip an outer ```yaml ... ``` fence if present, else return as-is."""
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
    """Validate a single finding dict; raise InvalidFindingsError on problems."""
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
    """Parse the ``## Findings`` YAML of a review file.

    Args:
        path: Path to a review file previously produced by
            :func:`write_review_instruction` and (maybe) filled by an LLM.

    Returns:
        - ``None`` if the ``<fill-findings>`` placeholder is still present
          (the LLM has not filled the file yet).
        - A ``list[dict]`` of validated findings otherwise.

    Raises:
        InvalidFindingsError: YAML parse failure, missing/invalid ``findings``
            key, or any finding that fails schema validation.
        FileNotFoundError: If ``path`` does not exist.
    """
    p = Path(path)
    content = p.read_text(encoding="utf-8")

    if PLACEHOLDER in content:
        return None

    section = _extract_findings_section(content)
    if section is None:
        raise InvalidFindingsError(
            f"{p}: '## Findings' section not found"
        )

    yaml_text = _strip_yaml_fence(section)
    if not yaml_text.strip():
        raise InvalidFindingsError(f"{p}: Findings section is empty")

    try:
        parsed = yaml.safe_load(yaml_text)
    except yaml.YAMLError as exc:
        raise InvalidFindingsError(f"{p}: YAML parse error: {exc}") from exc

    if not isinstance(parsed, dict) or "findings" not in parsed:
        raise InvalidFindingsError(
            f"{p}: top-level 'findings' key missing"
        )

    findings = parsed["findings"]
    if not isinstance(findings, list):
        raise InvalidFindingsError(
            f"{p}: 'findings' must be a list (got {type(findings).__name__})"
        )

    validated: list[dict[str, Any]] = []
    for i, f in enumerate(findings):
        validated.append(_validate_finding(f, i))
    return validated


__all__ = [
    "InvalidFindingsError",
    "PLACEHOLDER",
    "read_filled_findings",
    "write_review_instruction",
]
