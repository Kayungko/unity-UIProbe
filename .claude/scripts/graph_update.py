#!/usr/bin/env python3
"""Incremental graph update for harness wiki.

Provides ``update_graph(root, modules, force)`` which:
1. Falls back to a full ``build_graph`` when ``.claude/wiki/graph.json`` is
   missing.
2. Uses a per-file SHA-256 cache stored at ``.claude/wiki/.graph-cache.json``
   to detect changed files.  When no files changed the existing ``graph.json``
   is loaded and returned without re-running graphify.
3. When any file changed (or *force* is True) delegates to ``build_graph``
   for a full rebuild.

CLI usage (for git hooks)::

    python scripts/graph_update.py [--force]

"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path
from typing import Any

SCRIPTS_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPTS_DIR))

from core.graph_bridge import build_graph  # noqa: E402 — after sys.path setup


# ---------------------------------------------------------------------------
# SHA-256 cache helpers
# ---------------------------------------------------------------------------

_GRAPH_JSON_REL = ".claude/wiki/graph.json"
_CACHE_JSON_REL = ".claude/wiki/.graph-cache.json"


def _sha256_file(path: Path) -> str:
    """Return the hex SHA-256 digest of a file's contents."""
    h = hashlib.sha256()
    try:
        with path.open("rb") as fh:
            for chunk in iter(lambda: fh.read(65536), b""):
                h.update(chunk)
    except OSError:
        return ""
    return h.hexdigest()


def _load_cache(root: Path) -> dict[str, str]:
    """Load the SHA-256 file cache.  Returns {} when the cache is absent or corrupt."""
    cache_path = root / _CACHE_JSON_REL
    if not cache_path.exists():
        return {}
    try:
        return json.loads(cache_path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return {}


def _save_cache(root: Path, cache: dict[str, str]) -> None:
    """Persist the SHA-256 cache to disk, creating parent dirs as needed."""
    cache_path = root / _CACHE_JSON_REL
    cache_path.parent.mkdir(parents=True, exist_ok=True)
    cache_path.write_text(json.dumps(cache, indent=2), encoding="utf-8")


_SOURCE_GLOBS = (
    "*.py", "*.md", "*.txt", "*.rst",
    # Multi-language code extensions (match EXTRACTOR_REGISTRY)
    "*.ts", "*.tsx", "*.js", "*.jsx",
    "*.go", "*.rs", "*.java", "*.cs",
    "*.cpp", "*.cc", "*.cxx", "*.h", "*.hpp", "*.hxx",
)


def _collect_source_files(root: Path, modules: list[dict[str, Any]]) -> list[Path]:
    """Collect code and document files from declared module paths.

    Mirrors the inputs of ``build_graph(include_docs=True)`` so that the cache
    invalidates on any change that would influence the graph — including
    markdown/text/reST docs, not just Python sources.
    """
    files: list[Path] = []
    for mod in modules:
        for rel_path in mod.get("paths", []):
            target = root / rel_path
            if not target.exists():
                continue
            if target.is_dir():
                for pattern in _SOURCE_GLOBS:
                    files.extend(target.rglob(pattern))
            else:
                files.append(target)
    # Also include root-level documents (README.md, PRD.md, etc.)
    for pattern in ("*.md", "*.txt", "*.rst"):
        files.extend(root.glob(pattern))
    # Deduplicate and sort for determinism
    return sorted(set(files))


def _compute_file_hashes(files: list[Path]) -> dict[str, str]:
    """Return a dict mapping str(path) -> SHA-256 hex for each file."""
    return {str(f): _sha256_file(f) for f in files}


def _has_changes(new_hashes: dict[str, str], old_cache: dict[str, str]) -> bool:
    """Return True when any file hash differs from the cache."""
    if set(new_hashes.keys()) != set(old_cache.keys()):
        return True
    return any(old_cache.get(k) != v for k, v in new_hashes.items())


# ---------------------------------------------------------------------------
# graph.json serialisation helpers
# ---------------------------------------------------------------------------

def _graph_to_serialisable(graph_data: dict[str, Any]) -> dict[str, Any]:
    """Prepare a graph_data dict for JSON serialisation.

    The ``graph`` key holds a NetworkX object (or None).  Replace it with a
    JSON-safe dict representation so we can persist incremental results.
    """
    serialisable = {
        "communities": graph_data.get("communities", {}),
        "god_nodes": graph_data.get("god_nodes", []),
        "extraction": graph_data.get("extraction", {}),
    }

    graph_obj = graph_data.get("graph")
    if graph_obj is None:
        serialisable["graph"] = None
        return serialisable

    try:
        import networkx as nx  # noqa: WPS433

        if isinstance(graph_obj, (nx.Graph, nx.DiGraph)):
            serialisable["graph"] = nx.node_link_data(graph_obj)
            return serialisable
    except ImportError:
        pass

    # Last resort: store None if we cannot serialise
    serialisable["graph"] = None
    return serialisable


def _graph_from_serialisable(
    data: dict[str, Any],
) -> dict[str, Any]:
    """Reconstruct a graph_data dict from the JSON representation.

    Restores the ``graph`` key as a NetworkX object when possible; falls back
    to None so callers can still access extraction/communities/god_nodes.
    """
    communities = data.get("communities", {})
    god_nodes = data.get("god_nodes", [])
    extraction = data.get("extraction", {})
    raw_graph = data.get("graph")

    graph_obj = None
    if raw_graph is not None:
        try:
            import networkx as nx  # noqa: WPS433

            graph_obj = nx.node_link_graph(raw_graph)
        except ImportError:
            pass
        except (KeyError, TypeError, ValueError):
            # Malformed persisted graph — treat as missing, let caller rebuild.
            pass

    return {
        "graph": graph_obj,
        "communities": communities,
        "god_nodes": god_nodes,
        "extraction": extraction,
    }


# ---------------------------------------------------------------------------
# Public API
# ---------------------------------------------------------------------------


def update_graph(
    root: Path,
    modules: list[dict[str, Any]],
    force: bool = False,
) -> dict[str, Any] | None:
    """Incrementally update the knowledge graph.

    Strategy
    --------
    1. If ``force`` is True, always rebuild from scratch.
    2. If ``.claude/wiki/graph.json`` does not exist, fall back to a full
       ``build_graph`` run and persist the result.
    3. Otherwise, compute SHA-256 hashes for all source files.  If the hashes
       match the cache, load and return the existing ``graph.json``.
    4. If any file changed, run a full ``build_graph`` rebuild and update both
       ``graph.json`` and ``.graph-cache.json``.

    Args:
        root: Absolute path to the repository root.
        modules: Module definitions from ``project.json`` (each has ``paths``).
        force: When True skip cache check and always rebuild.

    Returns:
        A dict with keys ``graph``, ``communities``, ``god_nodes``, and
        ``extraction`` — identical shape to ``build_graph``; or ``None``
        when graphify is unavailable and no doc nodes could be extracted.
    """
    graph_json_path = root / _GRAPH_JSON_REL

    # --- Step 1: force or missing graph.json → full rebuild ---
    if force or not graph_json_path.exists():
        return _full_rebuild(root, modules, graph_json_path)

    # --- Step 2: compare file hashes to cache ---

    source_files = _collect_source_files(root, modules)
    new_hashes = _compute_file_hashes(source_files)
    old_cache = _load_cache(root)

    if not _has_changes(new_hashes, old_cache):
        # Nothing changed — load existing graph.json
        try:
            raw = json.loads(graph_json_path.read_text(encoding="utf-8"))
            return _graph_from_serialisable(raw)
        except (json.JSONDecodeError, OSError):
            # Corrupt cache — fall through to full rebuild
            pass

    # --- Step 3: files changed → full rebuild and update cache ---
    result = _full_rebuild(root, modules, graph_json_path)
    if result is not None:
        _save_cache(root, new_hashes)
    return result


def _full_rebuild(
    root: Path,
    modules: list[dict[str, Any]],
    graph_json_path: Path,
) -> dict[str, Any] | None:
    """Run a full build_graph and persist the result to graph.json."""
    # Detect primary language from modules (rough heuristic)
    language = _infer_language(modules)
    graph_data = build_graph(
        project_root=str(root),
        modules=modules,
        language=language,
        include_docs=True,
    )

    if graph_data is None:
        return None

    # Persist to graph.json
    graph_json_path.parent.mkdir(parents=True, exist_ok=True)
    serialisable = _graph_to_serialisable(graph_data)
    try:
        graph_json_path.write_text(
            json.dumps(serialisable, indent=2, default=str),
            encoding="utf-8",
        )
    except OSError as exc:
        raise RuntimeError(
            f"Failed to write graph.json to {graph_json_path}: {exc}"
        ) from exc

    # Also update the hash cache
    source_files = _collect_source_files(root, modules)
    new_hashes = _compute_file_hashes(source_files)
    _save_cache(root, new_hashes)

    return graph_data


def _infer_language(modules: list[dict[str, Any]]) -> str:
    """Detect the primary language from file extensions in module paths.

    Scans each module's declared paths for files with known code extensions
    and returns the language name of the most common extension.  Falls back
    to ``"python"`` when no files are found or module paths are empty.

    The result is informational (logging/reporting) -- ``build_graph`` now
    auto-detects languages per-file via ``detect_languages()``.
    """
    from collections import Counter

    # Extension -> language name mapping (matches EXTRACTOR_REGISTRY)
    _EXT_TO_LANG: dict[str, str] = {
        ".py": "python",
        ".ts": "typescript", ".tsx": "typescript",
        ".js": "javascript", ".jsx": "javascript",
        ".go": "go",
        ".rs": "rust",
        ".java": "java",
        ".cs": "csharp",
        ".cpp": "cpp", ".cc": "cpp", ".cxx": "cpp",
        ".h": "cpp", ".hpp": "cpp", ".hxx": "cpp",
    }

    _SKIP_DIRS: frozenset[str] = frozenset({
        "__pycache__", ".git", ".claude", "node_modules", "venv", ".venv",
    })

    counts: Counter[str] = Counter()

    for mod in modules:
        for rel_path in mod.get("paths", []):
            # We cannot resolve absolute paths without a root; scan relative.
            # This function is called from _full_rebuild which has root, but
            # the signature only receives modules.  Use Path(".") as fallback.
            target = Path(rel_path)
            if not target.exists():
                continue
            if target.is_file():
                ext = target.suffix.lower()
                lang = _EXT_TO_LANG.get(ext)
                if lang:
                    counts[lang] += 1
                continue
            for child in target.rglob("*"):
                if not child.is_file():
                    continue
                # Skip noise directories
                try:
                    parts = child.parts
                except ValueError:
                    continue
                skip = False
                for part in parts[:-1]:
                    if part in _SKIP_DIRS:
                        skip = True
                        break
                if skip:
                    continue
                ext = child.suffix.lower()
                lang = _EXT_TO_LANG.get(ext)
                if lang:
                    counts[lang] += 1

    if not counts:
        return "python"
    return counts.most_common(1)[0][0]


# ---------------------------------------------------------------------------
# CLI entry point (for git hooks: python scripts/graph_update.py)
# ---------------------------------------------------------------------------


def _load_modules_from_project(root: Path) -> list[dict[str, Any]]:
    """Load module definitions from .claude/project.json."""
    project_json = root / ".claude" / "project.json"
    if not project_json.exists():
        return []
    try:
        data = json.loads(project_json.read_text(encoding="utf-8"))
        return data.get("modules", [])
    except (json.JSONDecodeError, OSError):
        return []


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Incrementally update the harness knowledge graph."
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="Force full rebuild even when no files changed.",
    )
    parser.add_argument(
        "--root",
        default=".",
        help="Repository root directory (default: current directory).",
    )
    args = parser.parse_args()

    root = Path(args.root).resolve()
    modules = _load_modules_from_project(root)

    result = update_graph(root=root, modules=modules, force=args.force)
    if result is None:
        print("[graph_update] No graph data produced (graphify unavailable or empty project).")
        return

    extraction = result.get("extraction") or {}
    node_count = len(extraction.get("nodes", []))
    print(f"[graph_update] Graph updated: {node_count} nodes.")


if __name__ == "__main__":
    main()
