#!/usr/bin/env python3
"""PostToolUse hook: trigger graph_update for code-file Edit/Write.

Cross-platform wrapper invoked via `python .claude/scripts/hooks/graph_update_on_write.py`.
Reads $TOOL_INPUT_file_path, filters by extension, delegates to graph_update.py.
"""
from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path

CODE_EXTS = {
    ".py", ".ts", ".tsx", ".js", ".jsx",
    ".go", ".rs", ".cs", ".cpp", ".c", ".java",
}


def main() -> int:
    file_path = os.environ.get("TOOL_INPUT_file_path", "")
    ext = Path(file_path).suffix.lower()
    if ext not in CODE_EXTS:
        return 0
    subprocess.run(
        [sys.executable, ".claude/scripts/graph_update.py", "--force"],
        check=False,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
