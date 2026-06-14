"""Python AST-based code extractor.

Extracts code structure (classes, functions, imports, calls) from Python
source files using the stdlib ``ast`` module.  Originally part of
``core.graph_bridge`` (T4-1), extracted here in T7-1 to enable the
multi-language extractor registry.

Public symbols
--------------
PythonExtractor   — concrete implementation of the ``CodeExtractor`` protocol
_AstVisitor       — AST visitor that emits graph nodes and edges
_extract_via_ast  — orchestrator that drives ``_AstVisitor`` over a file list
_STDLIB_MODULES   — frozenset of stdlib top-level module names (skipped for import edges)
_SKIP_DIRS        — frozenset of directory names to skip during file collection
"""

from __future__ import annotations

import ast
from pathlib import Path
from typing import Any


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

# Standard-library module names to skip when emitting "imports" edges.
# Rationale: stdlib imports add noise without structural insight for most
# projects; third-party and project-internal imports are more useful.
_STDLIB_MODULES: frozenset[str] = frozenset({
    "abc", "argparse", "ast", "asyncio", "base64", "builtins",
    "collections", "concurrent", "contextlib", "copy", "csv",
    "dataclasses", "datetime", "decimal", "difflib", "email",
    "enum", "errno", "functools", "gc", "glob", "hashlib",
    "heapq", "html", "http", "importlib", "inspect", "io",
    "itertools", "json", "logging", "math", "multiprocessing",
    "operator", "os", "pathlib", "pickle", "platform", "pprint",
    "queue", "random", "re", "shutil", "signal", "socket",
    "sqlite3", "string", "struct", "subprocess", "sys",
    "tempfile", "textwrap", "threading", "time", "timeit",
    "traceback", "typing", "unittest", "urllib", "uuid",
    "warnings", "weakref", "xml", "zipfile",
})

# Directories to skip during code file collection.
_SKIP_DIRS: frozenset[str] = frozenset({
    "__pycache__", ".git", ".claude", "node_modules", "venv", ".venv",
})


# ---------------------------------------------------------------------------
# AST visitor
# ---------------------------------------------------------------------------

class _AstVisitor(ast.NodeVisitor):
    """Walk a Python AST and emit graph nodes and edges.

    Node ID scheme: ``<rel_path>::<kind>::<qualified_name>``
    Examples::

        scripts/core/foo.py::document::foo.py
        scripts/core/foo.py::entity::MyClass
        scripts/core/foo.py::interface::MyClass.method()
        scripts/core/foo.py::interface::top_func()

    All emitted nodes carry:
        id, label, source_file, source_location ("line N"),
        kind, confidence ("EXTRACTED"), confidence_score (1.0),
        description (stripped docstring or "")
    """

    def __init__(self, file_path: str, root: Path) -> None:
        super().__init__()
        self._file_path = file_path  # absolute path string
        self._root = root
        # Relative path with forward slashes for portability
        try:
            self._rel_path = str(
                Path(file_path).relative_to(root)
            ).replace("\\", "/")
        except ValueError:
            self._rel_path = Path(file_path).name

        self.nodes: list[dict[str, Any]] = []
        self.edges: list[dict[str, Any]] = []

        # Track class nesting for qualified method labels
        self._class_stack: list[str] = []
        # Track current function node id for call-edge emission
        self._current_func_id: str | None = None

        # Emit the file-level document hub node
        filename = Path(file_path).name
        self._file_node_id = f"{self._rel_path}::document::{filename}"
        self.nodes.append({
            "id": self._file_node_id,
            "label": filename,
            "source_file": self._rel_path,
            "source_location": "line 1",
            "kind": "document",
            "confidence": "EXTRACTED",
            "confidence_score": 1.0,
            "description": "",
        })

    # ------------------------------------------------------------------
    # Helpers
    # ------------------------------------------------------------------

    @staticmethod
    def _get_docstring(node: ast.AST) -> str:
        """Return the stripped docstring of a function/class node, or ''."""
        doc = ast.get_docstring(node)
        return doc.strip() if doc else ""

    def _make_node(
        self,
        kind: str,
        qualified_name: str,
        label: str,
        lineno: int,
        description: str = "",
    ) -> dict[str, Any]:
        node_id = f"{self._rel_path}::{kind}::{qualified_name}"
        return {
            "id": node_id,
            "label": label,
            "source_file": self._rel_path,
            "source_location": f"line {lineno}",
            "kind": kind,
            "confidence": "EXTRACTED",
            "confidence_score": 1.0,
            "description": description,
        }

    # ------------------------------------------------------------------
    # Visitors
    # ------------------------------------------------------------------

    def visit_ClassDef(self, node: ast.ClassDef) -> None:
        qualified = node.name
        description = self._get_docstring(node)
        class_node = self._make_node(
            kind="entity",
            qualified_name=qualified,
            label=node.name,
            lineno=node.lineno,
            description=description,
        )
        self.nodes.append(class_node)

        # Recurse with class on stack so methods get qualified names
        self._class_stack.append(node.name)
        self.generic_visit(node)
        self._class_stack.pop()

    def visit_FunctionDef(self, node: ast.FunctionDef) -> None:
        self._visit_function(node)

    def visit_AsyncFunctionDef(self, node: ast.AsyncFunctionDef) -> None:
        self._visit_function(node)

    def _visit_function(
        self,
        node: ast.FunctionDef | ast.AsyncFunctionDef,
    ) -> None:
        if self._class_stack:
            # Method inside a class
            class_name = self._class_stack[-1]
            qualified = f"{class_name}.{node.name}()"
            label = f"{class_name}.{node.name}()"
        else:
            # Top-level function
            qualified = f"{node.name}()"
            label = f"{node.name}()"

        description = self._get_docstring(node)
        func_node = self._make_node(
            kind="interface",
            qualified_name=qualified,
            label=label,
            lineno=node.lineno,
            description=description,
        )
        self.nodes.append(func_node)

        # Track current function for call edges; nested functions will
        # override this temporarily (by design -- simple heuristic)
        prev_func_id = self._current_func_id
        self._current_func_id = func_node["id"]
        self.generic_visit(node)
        self._current_func_id = prev_func_id

    def visit_Import(self, node: ast.Import) -> None:
        for alias in node.names:
            module_name = alias.name.split(".")[0]  # top-level package
            if module_name in _STDLIB_MODULES:
                continue
            target_id = f"module:{module_name}"
            self.edges.append({
                "source": self._file_node_id,
                "target": target_id,
                "relation": "imports",
                "confidence": "EXTRACTED",
                "weight": 1.0,
            })
        # No need to recurse; Import has no child nodes with code
        self.generic_visit(node)

    def visit_ImportFrom(self, node: ast.ImportFrom) -> None:
        module_name = (node.module or "").split(".")[0]
        if module_name and module_name not in _STDLIB_MODULES:
            target_id = f"module:{module_name}"
            self.edges.append({
                "source": self._file_node_id,
                "target": target_id,
                "relation": "imports",
                "confidence": "EXTRACTED",
                "weight": 1.0,
            })
        self.generic_visit(node)

    def visit_Call(self, node: ast.Call) -> None:
        if self._current_func_id is not None:
            # Best-effort target resolution
            func = node.func
            if isinstance(func, ast.Name):
                target_name = func.id
            elif isinstance(func, ast.Attribute):
                target_name = func.attr
            else:
                target_name = None

            if target_name:
                self.edges.append({
                    "source": self._current_func_id,
                    "target": target_name,  # symbolic, not a node id
                    "relation": "calls",
                    "confidence": "INFERRED",
                    "weight": 0.5,
                })
        self.generic_visit(node)


# ---------------------------------------------------------------------------
# Extraction orchestrator
# ---------------------------------------------------------------------------

def _extract_via_ast(
    files: list[Path],
    root: Path,
    cache_root: Path | None = None,  # reserved for future incremental extraction
) -> dict[str, Any]:
    """Extract code structure from Python files using the stdlib ``ast`` module.

    For each file:
    1. Read source and call ``ast.parse``.
    2. Catch ``SyntaxError`` -- skip that file and continue; do not raise.
    3. Run ``_AstVisitor`` to collect nodes and edges.

    Deduplication: if two files produce a node with the same id, the
    *first* occurrence is kept (stable ordering from sorted ``files`` list).

    Args:
        files: Absolute paths to ``.py`` files.
        root: Project root used to compute relative paths.
        cache_root: Reserved; currently unused.

    Returns:
        ``{"nodes": [...], "edges": [...]}`` compatible with the existing
        ``extraction`` format consumed by ``build_graph`` and
        ``map_to_wiki_structures``.
    """
    all_nodes: list[dict[str, Any]] = []
    all_edges: list[dict[str, Any]] = []
    seen_node_ids: set[str] = set()
    warnings: list[str] = []

    for file_path in files:
        try:
            source = file_path.read_text(encoding="utf-8", errors="replace")
        except OSError as exc:
            warnings.append(f"Cannot read {file_path}: {exc}")
            continue

        try:
            tree = ast.parse(source, filename=str(file_path))
        except SyntaxError as exc:
            warnings.append(f"SyntaxError in {file_path}: {exc}")
            continue

        visitor = _AstVisitor(str(file_path), root)
        visitor.visit(tree)

        # Merge nodes (first-seen wins)
        for node in visitor.nodes:
            node_id = node["id"]
            if node_id not in seen_node_ids:
                all_nodes.append(node)
                seen_node_ids.add(node_id)

        all_edges.extend(visitor.edges)

    return {"nodes": all_nodes, "edges": all_edges}


# ---------------------------------------------------------------------------
# PythonExtractor (CodeExtractor protocol implementation)
# ---------------------------------------------------------------------------

class PythonExtractor:
    """Extract code structure from Python files via AST analysis.

    Implements the ``CodeExtractor`` protocol defined in
    ``core.extractors``.
    """

    @property
    def language_name(self) -> str:
        return "Python"

    @property
    def extensions(self) -> tuple[str, ...]:
        return (".py",)

    def extract(self, files: list[Path], root: Path) -> dict[str, Any]:
        return _extract_via_ast(files, root)
