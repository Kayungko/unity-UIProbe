#!/usr/bin/env python3
"""PreToolUse hook: graphify-first warn.

Emitted by scaffold_gen as a PreToolUse hook for Glob|Grep tools.
Writes a one-line reminder to stderr, then exits 0 (non-blocking).

Stateless by design (SCOPE-001): no tag files, no per-session state.
Claude sees the same reminder on every Glob/Grep call; the marginal
cost is a single stderr line, far cheaper than one unnecessary Grep.
"""
from __future__ import annotations

import sys


_MESSAGE = (
    "[graphify] Before raw search: "
    'try mcp__graphify__query_graph("<keyword>") first.'
)


def main() -> None:
    print(_MESSAGE, file=sys.stderr)
    sys.exit(0)


if __name__ == "__main__":
    main()
