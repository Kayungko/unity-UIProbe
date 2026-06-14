#!/usr/bin/env python3
"""TDD red-green-refactor sprint-review driver.

Drives a per-task red-green-refactor cycle at milestone end:
  * Red   — audit each DONE task: map acceptance criteria to spec checkboxes,
            identify gaps, emit a red-plan markdown.
  * Green — run verification_commands, parse JUnit XML, mark passing test
            checkboxes, emit normalized telemetry JSON.
  * Refactor — classify failures (simple vs complex), auto-invoke /fix for
            simple failures, defer complex to the user. Cap at 3 iterations.
  * Persist — extend coverage_gate with new fields in session-state.json.

Language-neutral: JUnit XML is the only intermediate format. Works with any
test runner that can emit junit-xml (pytest, vitest, jest, go-junit-report,
gtest, nunit3-console, maven-surefire, gradle, etc.).

Usage (as a module / library):
    from tdd_sprint_review import run, red_audit, parse_junit_xml, classify_failure

Usage (as CLI):
    python scripts/tdd_sprint_review.py --root . --milestone M9
"""

from __future__ import annotations

import argparse
import json
import re
import shlex
import subprocess
import time
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Iterable, Literal

# ---------------------------------------------------------------------------
# Self-contained helpers (inlined from write_code_check so this script can be
# copied into a downstream project's .claude/scripts/ without pulling the
# harness source tree. See scaffold_gen.py::_copy_sprint_review_scripts.)
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


def _load_project_json(path: Path) -> dict[str, Any]:
    """Minimal project.json reader. Returns {} if missing or malformed."""
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}


# ---------------------------------------------------------------------------
# Constants & regexes
# ---------------------------------------------------------------------------

TOKEN_OVERLAP_THRESHOLD = 0.6
MAX_REFACTOR_ITERATIONS = 3

# Simple-failure signatures across languages. Used by classify_failure() on the
# `message` attribute of junit <failure>/<error> elements. Case-insensitive.
_SIMPLE_PATTERNS = [
    re.compile(p, re.IGNORECASE)
    for p in (
        r"\bassertion\s*error\b",
        r"\bassert(ionerror)?\b",
        r"\bassert equal\b",
        r"\bexpected\b.*\bgot\b",
        r"expect\([^)]*\)\.\w+",  # chai / jest
        r"\bassert_eq\b",  # rust / c++
        r"\bAssert\.Equal\b",  # .net / nunit / xunit
        r"\btypeerror\b",
        r"\bnameerror\b",
        r"\bimporterror\b",
        r"\bmodulenotfounderror\b",
        r"\bcannot find module\b",
        r"\bundefined is not\b",
    )
]

# Complex-failure signatures — checked BEFORE simple to avoid misclassification
# (e.g. an ImportError inside a timeout stack should still be "complex").
_COMPLEX_PATTERNS = [
    re.compile(p, re.IGNORECASE)
    for p in (
        r"\btimed?\s*out\b",
        r"\btimeout\b",
        r"\bflaky\b",
        r"\bport\s*already in use\b",
        r"\bconnection refused\b",
        r"\bno such host\b",
        r"\bsegmentation fault\b",
        r"\bsigabrt\b",
        r"\boom\b|\bout of memory\b",
    )
]


# ---------------------------------------------------------------------------
# Data classes
# ---------------------------------------------------------------------------


@dataclass
class ACCoverage:
    ac_text: str
    covered_by: list[str] = field(default_factory=list)  # matching checkbox items

    @property
    def gap(self) -> bool:
        return not self.covered_by


@dataclass
class RedReport:
    task_id: str
    spec_path: str | None
    ac_coverage: list[ACCoverage]
    unchecked_items: list[str]  # spec checkboxes still `[ ]`

    @property
    def has_red(self) -> bool:
        return any(c.gap for c in self.ac_coverage) or bool(self.unchecked_items)


@dataclass
class TestCaseResult:
    name: str
    classname: str
    time_s: float
    status: Literal["passed", "failed", "skipped", "error"]
    failure_message: str = ""


@dataclass
class NormalizedReport:
    testsuites: list[dict[str, Any]]
    totals: dict[str, int]  # {tests, passed, failed, skipped, errors}
    testcases: list[TestCaseResult]


# ---------------------------------------------------------------------------
# Phase R — Red audit
# ---------------------------------------------------------------------------


def match_ac_to_tests(ac_text: str, checkbox_items: Iterable[str]) -> list[str]:
    """Return the subset of checkbox_items whose token-set overlaps ac_text by
    at least ``TOKEN_OVERLAP_THRESHOLD`` of the AC's meaningful tokens.
    """
    ac_tokens = _tokenise(ac_text)
    if not ac_tokens:
        return []
    hits: list[str] = []
    for item in checkbox_items:
        item_tokens = _tokenise(item)
        if not item_tokens:
            continue
        overlap = ac_tokens & item_tokens
        if len(overlap) / len(ac_tokens) >= TOKEN_OVERLAP_THRESHOLD:
            hits.append(item)
    return hits


_AC_HEADER_RE = re.compile(r"^##\s+(Acceptance Criteria|验收标准)\s*$", re.IGNORECASE)
_NEXT_HEADER_RE = re.compile(r"^##\s+\S")
_AC_ITEM_RE = re.compile(r"^\s*-\s*\[[ xX]\]\s+(.+?)\s*$")


def parse_acceptance_criteria(task_md_path: Path) -> list[str]:
    """Extract AC bullets from a ``.claude/milestones/tasks/{task}.md`` file.

    Looks for either ``## Acceptance Criteria`` or ``## 验收标准`` heading,
    then collects ``- [ ] ...`` / ``- [x] ...`` lines until the next ``## `` header.
    """
    if not task_md_path.exists():
        return []
    text = task_md_path.read_text(encoding="utf-8")
    ac: list[str] = []
    in_section = False
    for line in text.splitlines():
        if _AC_HEADER_RE.match(line):
            in_section = True
            continue
        if in_section and _NEXT_HEADER_RE.match(line):
            break
        if in_section:
            m = _AC_ITEM_RE.match(line)
            if m:
                ac.append(m.group(1).strip())
    return ac


def red_audit(task_id: str, task_md_path: Path, spec_path: Path | None) -> RedReport:
    """Audit one task: compute AC coverage + unchecked spec items."""
    ac_list = parse_acceptance_criteria(task_md_path)
    checkbox_items: list[str] = []
    unchecked_items: list[str] = []
    if spec_path and spec_path.exists():
        spec_text = spec_path.read_text(encoding="utf-8")
        checkbox_items = _parse_spec_checkboxes(spec_text)
        # Extract still-unchecked items (those with `- [ ]`)
        for line in spec_text.splitlines():
            m = re.match(r"^\s*-\s*\[\s\]\s+(.+?)\s*$", line)
            if m:
                unchecked_items.append(m.group(1).strip())

    ac_coverage = [
        ACCoverage(ac_text=ac, covered_by=match_ac_to_tests(ac, checkbox_items))
        for ac in ac_list
    ]
    return RedReport(
        task_id=task_id,
        spec_path=str(spec_path) if spec_path else None,
        ac_coverage=ac_coverage,
        unchecked_items=unchecked_items,
    )


def render_red_plan(milestone_id: str, reports: list[RedReport]) -> str:
    """Render a markdown red-plan aggregating per-task RedReports."""
    lines = [
        f"# Red Plan — {milestone_id}",
        "",
        "> Generated by tdd_sprint_review. Lists acceptance criteria that lack",
        "> matching test coverage and spec checkboxes still unchecked. Each task",
        "> below is a candidate for additional tests before the gate can pass.",
        "",
    ]
    for r in reports:
        if not r.has_red:
            continue
        lines.append(f"## Task {r.task_id}")
        lines.append("")
        lines.append(f"Spec: `{r.spec_path}`" if r.spec_path else "Spec: _(not linked)_")
        lines.append("")
        if r.ac_coverage:
            lines.append("### Acceptance Coverage")
            lines.append("")
            lines.append("| # | Criterion | Status | Covered by |")
            lines.append("|---|-----------|--------|------------|")
            for i, c in enumerate(r.ac_coverage, 1):
                status = "✗ GAP" if c.gap else "✓"
                covered = "; ".join(c.covered_by) if c.covered_by else "—"
                ac_text = c.ac_text.replace("|", "\\|")
                covered = covered.replace("|", "\\|")
                lines.append(f"| {i} | {ac_text} | {status} | {covered} |")
            lines.append("")
        gaps = [c for c in r.ac_coverage if c.gap]
        if gaps:
            lines.append("### Proposed Test Stubs (AC Gaps)")
            lines.append("")
            for c in gaps:
                lines.append(f"- **AC**: {c.ac_text}")
                lines.append(f"  - Arrange: set up inputs that exercise the criterion")
                lines.append(f"  - Act: invoke the interface under test")
                lines.append(f"  - Assert: verify the behavior the criterion demands")
            lines.append("")
        if r.unchecked_items:
            lines.append("### Still-Unchecked Spec Items")
            lines.append("")
            for item in r.unchecked_items:
                lines.append(f"- [ ] {item}")
            lines.append("")
    if len(lines) == 6:  # only header, no content
        lines.append("_No red items — all tasks have full AC coverage and no unchecked spec items._")
        lines.append("")
    return "\n".join(lines)


# ---------------------------------------------------------------------------
# Phase G — Green run (JUnit XML parsing)
# ---------------------------------------------------------------------------


def parse_junit_xml(path: Path) -> NormalizedReport:
    """Parse a JUnit XML file into a NormalizedReport.

    Accepts both ``<testsuite>`` root and ``<testsuites>`` wrapper forms.
    Raises ValueError on malformed XML or missing file.
    """
    if not path.exists():
        raise ValueError(f"JUnit XML not found: {path}")
    try:
        tree = ET.parse(str(path))
    except ET.ParseError as exc:
        raise ValueError(f"Malformed JUnit XML: {exc}") from exc
    root = tree.getroot()
    suites_elem = [root] if root.tag.endswith("testsuite") else list(root.iter("testsuite"))

    testsuites: list[dict[str, Any]] = []
    testcases: list[TestCaseResult] = []
    totals = {"tests": 0, "passed": 0, "failed": 0, "skipped": 0, "errors": 0}

    for suite in suites_elem:
        suite_info = {
            "name": suite.get("name", ""),
            "tests": int(suite.get("tests", 0) or 0),
            "failures": int(suite.get("failures", 0) or 0),
            "errors": int(suite.get("errors", 0) or 0),
            "skipped": int(suite.get("skipped", 0) or 0),
            "time": float(suite.get("time", 0) or 0),
        }
        testsuites.append(suite_info)
        for tc in suite.iter("testcase"):
            failure_msg = ""
            status: Literal["passed", "failed", "skipped", "error"] = "passed"
            fail = tc.find("failure")
            err = tc.find("error")
            skip = tc.find("skipped")
            if fail is not None:
                status = "failed"
                failure_msg = fail.get("message") or (fail.text or "")
            elif err is not None:
                status = "error"
                failure_msg = err.get("message") or (err.text or "")
            elif skip is not None:
                status = "skipped"
            testcases.append(
                TestCaseResult(
                    name=tc.get("name", ""),
                    classname=tc.get("classname", ""),
                    time_s=float(tc.get("time", 0) or 0),
                    status=status,
                    failure_message=failure_msg.strip(),
                )
            )
            totals["tests"] += 1
            if status == "passed":
                totals["passed"] += 1
            elif status == "failed":
                totals["failed"] += 1
            elif status == "skipped":
                totals["skipped"] += 1
            elif status == "error":
                totals["errors"] += 1

    return NormalizedReport(testsuites=testsuites, totals=totals, testcases=testcases)


def run_verification(
    commands: list[str],
    cwd: Path,
    junit_path: Path,
    timeout_s: int = 600,
) -> dict[str, Any]:
    """Run verification_commands, return per-command exec metadata.

    Writes combined output to the tracker dict so the caller can decide
    whether / how to surface it. Does NOT raise on non-zero exit codes.
    """
    runs: list[dict[str, Any]] = []
    junit_path.parent.mkdir(parents=True, exist_ok=True)
    for cmd in commands:
        started = time.time()
        try:
            proc = subprocess.run(
                cmd if isinstance(cmd, list) else shlex.split(cmd, posix=False),
                cwd=str(cwd),
                capture_output=True,
                text=True,
                timeout=timeout_s,
                check=False,
            )
            rc = proc.returncode
            stdout = proc.stdout
            stderr = proc.stderr
        except subprocess.TimeoutExpired as exc:
            rc = -1
            stdout = ""
            stderr = f"TimeoutExpired after {timeout_s}s: {exc}"
        except FileNotFoundError as exc:
            rc = -2
            stdout = ""
            stderr = f"Command not found: {exc}"
        duration_ms = int((time.time() - started) * 1000)
        runs.append({
            "command": cmd,
            "started_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(started)),
            "duration_ms": duration_ms,
            "exit_code": rc,
            "stdout_tail": (stdout or "")[-2000:],
            "stderr_tail": (stderr or "")[-2000:],
        })
    return {"runs": runs, "junit_path": str(junit_path)}


def mark_passing_checkboxes(spec_path: Path, passing_names: Iterable[str]) -> int:
    """Tick `- [ ]` checkboxes whose text matches a passing testcase name.

    Uses token-overlap; same threshold as AC→test matching. Returns the count
    of newly-ticked checkboxes.
    """
    if not spec_path.exists():
        return 0
    text = spec_path.read_text(encoding="utf-8")
    passing_set = list(passing_names)
    count = 0
    new_lines: list[str] = []
    for line in text.splitlines():
        m = re.match(r"^(\s*-\s*\[)\s(\]\s+)(.+?)(\s*)$", line)
        if m:
            item_text = m.group(3).strip()
            item_tokens = _tokenise(item_text)
            ticked = False
            if item_tokens:
                for tc_name in passing_set:
                    tc_tokens = _tokenise(tc_name)
                    if not tc_tokens:
                        continue
                    overlap = item_tokens & tc_tokens
                    if len(overlap) / len(item_tokens) >= TOKEN_OVERLAP_THRESHOLD:
                        ticked = True
                        break
            if ticked:
                new_lines.append(f"{m.group(1)}x{m.group(2)}{m.group(3)}{m.group(4)}")
                count += 1
                continue
        new_lines.append(line)
    if count > 0:
        spec_path.write_text("\n".join(new_lines) + "\n" if text.endswith("\n") else "\n".join(new_lines), encoding="utf-8")
    return count


# ---------------------------------------------------------------------------
# Phase F — Refactor classification
# ---------------------------------------------------------------------------


def classify_failure(message: str) -> Literal["simple", "complex"]:
    """Classify a junit failure/error message into simple (auto-fixable) or
    complex (needs user input). Complex patterns win ties to stay safe.
    """
    if not message:
        return "complex"
    for pat in _COMPLEX_PATTERNS:
        if pat.search(message):
            return "complex"
    for pat in _SIMPLE_PATTERNS:
        if pat.search(message):
            return "simple"
    return "complex"


# ---------------------------------------------------------------------------
# Orchestration — run()
# ---------------------------------------------------------------------------


def _resolve_spec_path(root: Path, task: dict[str, Any]) -> Path | None:
    tdd_link = task.get("tdd_link")
    if tdd_link:
        p = (root / tdd_link).resolve()
        if p.exists():
            return p
    agent = task.get("agent", "") or ""
    if agent.startswith("core-"):
        slug = agent[5:].split(",", 1)[0].strip()
        p = root / ".claude" / "tdd" / "specs" / f"{slug}.spec.md"
        if p.exists():
            return p
    return None


def _load_milestone(root: Path, milestone_id: str) -> dict[str, Any] | None:
    # Try milestones.json first
    mj = root / ".claude" / "milestones.json"
    if mj.exists():
        try:
            data = json.loads(mj.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            data = []
        if isinstance(data, list):
            for m in data:
                if str(m.get("id", "")).upper() == milestone_id.upper():
                    return m
    # Fallback: parse .claude/milestones/{id}.md task table (best-effort)
    return None


def run(root: Path, milestone_id: str) -> dict[str, Any]:
    """Run one full red-green-refactor pass for the given milestone.

    Returns a summary dict with keys:
      * red_plan_path: str | None
      * telemetry_path: str | None
      * totals: dict (junit totals)
      * coverage_gate: dict (proposed gate update)
      * failures: list of {test_name, classification, message}
    """
    root = Path(root).resolve()
    project_path = root / ".claude" / "project.json"
    project = _load_project_json(project_path) if project_path.exists() else {}

    milestone = _load_milestone(root, milestone_id) or {"id": milestone_id, "tasks": []}
    tasks = milestone.get("tasks", [])

    # --- Phase R: red audit ---
    reports: list[RedReport] = []
    for task in tasks:
        tid = str(task.get("id", ""))
        if not tid:
            continue
        task_md = root / ".claude" / "milestones" / "tasks" / f"{tid.lower()}.md"
        spec = _resolve_spec_path(root, task)
        reports.append(red_audit(tid, task_md, spec))
    red_plan_path = root / ".claude" / "progress" / f"red-plan-{milestone_id}.md"
    red_plan_path.parent.mkdir(parents=True, exist_ok=True)
    red_plan_path.write_text(render_red_plan(milestone_id, reports), encoding="utf-8")

    # --- Phase G: green run ---
    verification_commands = list(project.get("verification_commands") or [])
    result_path_tmpl = project.get("test_result_path") or ".claude/progress/junit-{milestone}.xml"
    junit_path = root / result_path_tmpl.format(milestone=milestone_id)
    telemetry_path = root / ".claude" / "progress" / "telemetry" / f"{milestone_id}.json"
    telemetry_path.parent.mkdir(parents=True, exist_ok=True)

    exec_meta = run_verification(verification_commands, root, junit_path)
    normalized: NormalizedReport | None
    try:
        normalized = parse_junit_xml(junit_path)
    except ValueError:
        normalized = None

    telemetry = {
        "milestone": milestone_id,
        "runs": exec_meta["runs"],
        "testsuites": (normalized.testsuites if normalized else []),
        "totals": (normalized.totals if normalized else {"tests": 0, "passed": 0, "failed": 0, "skipped": 0, "errors": 0}),
    }
    telemetry_path.write_text(
        json.dumps(telemetry, ensure_ascii=False, indent=2), encoding="utf-8"
    )

    # Tick spec checkboxes for passing tests
    if normalized:
        passing_names = [tc.name for tc in normalized.testcases if tc.status == "passed"]
        for task in tasks:
            spec = _resolve_spec_path(root, task)
            if spec:
                mark_passing_checkboxes(spec, passing_names)

    # --- Phase F: classify failures ---
    failures: list[dict[str, Any]] = []
    simple_count = 0
    complex_count = 0
    if normalized:
        for tc in normalized.testcases:
            if tc.status in ("failed", "error"):
                cls = classify_failure(tc.failure_message)
                failures.append({
                    "test_name": tc.name,
                    "classname": tc.classname,
                    "classification": cls,
                    "message": tc.failure_message[:500],
                })
                if cls == "simple":
                    simple_count += 1
                else:
                    complex_count += 1

    totals = telemetry["totals"]
    gate_status = "passed" if (totals["failed"] == 0 and totals["errors"] == 0 and totals["tests"] > 0) else "failed"
    coverage_gate = {
        "status": gate_status,
        "red_plan_path": str(red_plan_path.relative_to(root)).replace("\\", "/"),
        "telemetry_path": str(telemetry_path.relative_to(root)).replace("\\", "/"),
        "iterations_used": 0,
        "classification": {
            "simple_autofixed": 0,
            "complex_deferred": complex_count,
        },
    }
    # Defer auto-fix invocation to the command layer (requires /fix dispatch);
    # keep this driver side-effect-free except for file writes.
    return {
        "red_plan_path": coverage_gate["red_plan_path"],
        "telemetry_path": coverage_gate["telemetry_path"],
        "totals": totals,
        "coverage_gate": coverage_gate,
        "failures": failures,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description="TDD sprint-review driver")
    parser.add_argument("--root", required=True, help="Repository root")
    parser.add_argument("--milestone", required=True, help="Milestone id (e.g. M9)")
    args = parser.parse_args()
    summary = run(Path(args.root), args.milestone)
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
