"""Multi-language code extractor registry.

Provides a ``CodeExtractor`` protocol and a registry that maps file
extensions to concrete extractor implementations.  Extractors are
registered eagerly at import time so that ``get_extractor(".py")``
works immediately.

Public API
----------
CodeExtractor         -- ``typing.Protocol`` that all extractors implement
EXTRACTOR_REGISTRY    -- maps file extension -> extractor instance
register_extractor()  -- add an extractor for each of its declared extensions
get_extractor()       -- look up extractor by extension
collect_code_files()  -- generalised file collector (any set of extensions)
detect_languages()    -- scan module paths for all registered extensions
"""

from __future__ import annotations

from pathlib import Path
from typing import Any, Protocol, runtime_checkable

from core.extractors._cpp import CppExtractor
from core.extractors._csharp import CSharpExtractor
from core.extractors._go import GoExtractor
from core.extractors._java import JavaExtractor
from core.extractors._python import PythonExtractor, _SKIP_DIRS
from core.extractors._rust import RustExtractor
from core.extractors._typescript import TypeScriptExtractor


# ---------------------------------------------------------------------------
# Protocol
# ---------------------------------------------------------------------------

@runtime_checkable
class CodeExtractor(Protocol):
    """Interface that every language extractor must satisfy."""

    @property
    def language_name(self) -> str: ...

    @property
    def extensions(self) -> tuple[str, ...]: ...

    def extract(self, files: list[Path], root: Path) -> dict[str, Any]: ...


# ---------------------------------------------------------------------------
# Registry
# ---------------------------------------------------------------------------

EXTRACTOR_REGISTRY: dict[str, CodeExtractor] = {}


def register_extractor(extractor: CodeExtractor) -> None:
    """Register *extractor* for each of its declared extensions."""
    for ext in extractor.extensions:
        EXTRACTOR_REGISTRY[ext] = extractor


def get_extractor(extension: str) -> CodeExtractor | None:
    """Return the registered extractor for *extension*, or ``None``."""
    return EXTRACTOR_REGISTRY.get(extension)


# ---------------------------------------------------------------------------
# Generalised file collector
# ---------------------------------------------------------------------------

def collect_code_files(
    root: Path,
    modules: list[dict[str, Any]],
    extensions: set[str],
) -> list[Path]:
    """Collect source files from module paths, filtering by *extensions*.

    Behaves identically to the original ``_collect_code_files`` in
    ``graph_bridge`` but accepts an arbitrary set of file extensions
    instead of hard-coding ``*.py``.

    Args:
        root: Project root path.
        modules: Module definitions (each with a ``paths`` list).
        extensions: Set of file extensions to include (e.g. ``{".py"}``).

    Returns:
        Deduplicated, sorted list of absolute ``Path`` objects.
    """
    collected: set[Path] = set()
    for mod in modules:
        for rel_path in mod.get("paths", []):
            target = root / rel_path
            if not target.exists():
                continue
            for ext in extensions:
                pattern = f"*{ext}"
                for matched_file in target.rglob(pattern):
                    # Skip if any ancestor directory is in _SKIP_DIRS
                    skip = False
                    for part in matched_file.relative_to(root).parts[:-1]:
                        if part in _SKIP_DIRS:
                            skip = True
                            break
                    if not skip:
                        collected.add(matched_file)
    return sorted(collected)


# ---------------------------------------------------------------------------
# Multi-language file detection
# ---------------------------------------------------------------------------


def detect_languages(
    root: Path,
    modules: list[dict[str, Any]],
) -> dict[str, list[Path]]:
    """Scan module paths for all file extensions registered in ``EXTRACTOR_REGISTRY``.

    Groups discovered files by extension so that the caller can route each
    group to the appropriate extractor via :func:`get_extractor`.

    Args:
        root: Project root path.
        modules: Module definitions (each with a ``paths`` list).

    Returns:
        ``{".py": [Path, ...], ".ts": [Path, ...], ...}`` — only extensions
        that have at least one matching file are included.
    """
    all_extensions = set(EXTRACTOR_REGISTRY.keys())
    if not all_extensions:
        return {}

    result: dict[str, list[Path]] = {}
    seen: set[Path] = set()

    for mod in modules:
        for rel_path in mod.get("paths", []):
            target = root / rel_path
            if not target.exists():
                continue
            for ext in all_extensions:
                pattern = f"*{ext}"
                for matched_file in target.rglob(pattern):
                    # Skip if any ancestor directory is in _SKIP_DIRS
                    skip = False
                    try:
                        parts = matched_file.relative_to(root).parts[:-1]
                    except ValueError:
                        parts = ()
                    for part in parts:
                        if part in _SKIP_DIRS:
                            skip = True
                            break
                    if skip:
                        continue
                    if matched_file not in seen:
                        seen.add(matched_file)
                        result.setdefault(ext, []).append(matched_file)

    # Sort each group for determinism
    for ext in result:
        result[ext] = sorted(result[ext])

    return result


# ---------------------------------------------------------------------------
# Eager registration
# ---------------------------------------------------------------------------

register_extractor(PythonExtractor())
register_extractor(TypeScriptExtractor())
register_extractor(JavaExtractor())
register_extractor(CSharpExtractor())
register_extractor(RustExtractor())
register_extractor(GoExtractor())
register_extractor(CppExtractor())
