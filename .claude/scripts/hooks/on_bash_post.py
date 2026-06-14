#!/usr/bin/env python3
"""PostToolUse hook: git commit → schedule graph_update.

Emitted by scaffold_gen as a PostToolUse hook for Bash tools.
Reads Claude Code's hook JSON from stdin, checks whether the Bash
command was a real `git commit` (not --dry-run), and if so spawns
`scripts/graph_update.py` in the background.

Cross-platform background launch:
  - POSIX:   start_new_session=True
  - Windows: creationflags=DETACHED_PROCESS|CREATE_NO_WINDOW

All exceptions are caught at the top level; the hook always exits 0
so it never blocks the Claude Code tool chain.
"""
from __future__ import annotations

import json
import os
import re
import subprocess
import sys
from pathlib import Path

_GIT_COMMIT_RE = re.compile(r'\bgit\s+commit(\s|$)')


def _resolve_graph_update_script() -> Path | None:
    """Locate graph_update.py relative to this hook.

    Both source repo and downstream layouts now keep on_bash_post.py inside
    a ``hooks/`` subdirectory and graph_update.py one level up:
      * Source repo:  scripts/hooks/on_bash_post.py          → scripts/graph_update.py
      * Downstream:   .claude/scripts/hooks/on_bash_post.py  → .claude/scripts/graph_update.py
    The two candidates are kept as a defensive fallback in case anyone copies
    on_bash_post.py and graph_update.py into the same directory.
    """
    here = Path(__file__).resolve().parent
    for candidate in (here.parent / "graph_update.py", here / "graph_update.py"):
        if candidate.exists():
            return candidate
    return None


_GRAPH_UPDATE_SCRIPT = _resolve_graph_update_script()


def _should_trigger(command: str) -> bool:
    """Return True if *command* is a real git commit (not --dry-run)."""
    return bool(_GIT_COMMIT_RE.search(command)) and "--dry-run" not in command


def _spawn_graph_update() -> None:
    """Launch graph_update.py in a detached background process."""
    kwargs: dict = {
        "stdout": subprocess.DEVNULL,
        "stderr": subprocess.DEVNULL,
    }
    if os.name == "nt":
        # Windows: DETACHED_PROCESS (0x8) | CREATE_NO_WINDOW (0x8000000)
        kwargs["creationflags"] = 0x00000008 | 0x08000000
    else:
        kwargs["start_new_session"] = True

    if _GRAPH_UPDATE_SCRIPT is None:
        return
    subprocess.Popen(
        [sys.executable, str(_GRAPH_UPDATE_SCRIPT)], **kwargs
    )


def main() -> None:
    try:
        raw = sys.stdin.read()
        hook_input: dict = json.loads(raw) if raw.strip() else {}
        command: str = hook_input.get("tool_input", {}).get("command", "")

        if _should_trigger(command):
            _spawn_graph_update()
            print(
                "[graphify] commit detected -> graph_update scheduled",
                file=sys.stderr,
            )
    except Exception:  # noqa: BLE001 — hook must never block caller
        pass

    sys.exit(0)


if __name__ == "__main__":
    main()
