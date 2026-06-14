"""Base class for tree-sitter language extractors.

``TreeSitterExtractor`` provides grammar installation, parser caching,
tree walking, and node/edge emission.  Subclasses configure behaviour
entirely through class-level data attributes — no method overrides
are needed for the common case.

Usage example (hypothetical TypeScript extractor)::

    class TypeScriptExtractor(TreeSitterExtractor):
        _language_name = "typescript"
        _extensions = (".ts", ".tsx")
        _pip_packages = ["tree-sitter-typescript"]
        _grammar_imports = {
            ".ts": "tree_sitter_typescript",
            ".tsx": "tree_sitter_typescript",
        }
        _grammar_funcs = {
            ".ts": "language_typescript",
            ".tsx": "language_tsx",
        }
        _class_node_types = frozenset({"class_declaration", "interface_declaration"})
        _function_node_types = frozenset({"function_declaration", "method_definition"})
        _import_node_types = frozenset({"import_statement"})
        _call_node_types = frozenset({"call_expression"})

Design notes
------------
* Grammar packages are lazily installed via ``pip`` on first use.  The
  result is cached per-process so repeated calls pay no install cost.
* If tree-sitter or the grammar is unavailable, ``extract()`` degrades
  gracefully — it returns ``{"nodes": [], "edges": []}`` without raising.
* The walker uses ``TreeCursor`` for O(n) traversal rather than
  recursive ``node.children`` access.
"""

from __future__ import annotations

import importlib
import subprocess
import sys
from pathlib import Path
from typing import Any


class TreeSitterExtractor:
    """Base for tree-sitter language extractors.

    Subclasses set class-level attributes; the base handles the rest.
    """

    # ------------------------------------------------------------------ #
    # Subclass MUST override these                                        #
    # ------------------------------------------------------------------ #

    _language_name: str = ""
    _extensions: tuple[str, ...] = ()
    _pip_packages: list[str] = []
    _grammar_imports: dict[str, str] = {}
    _grammar_funcs: dict[str, str] = {}

    # ------------------------------------------------------------------ #
    # Node type mappings (subclass overrides for each language)            #
    # ------------------------------------------------------------------ #

    _class_node_types: frozenset[str] = frozenset()
    _function_node_types: frozenset[str] = frozenset()
    _import_node_types: frozenset[str] = frozenset()
    _call_node_types: frozenset[str] = frozenset()

    # ------------------------------------------------------------------ #
    # Field names for extracting identifiers (subclass may override)      #
    # ------------------------------------------------------------------ #

    _name_field: str = "name"
    _body_field: str = "body"

    # ------------------------------------------------------------------ #
    # Internal caches (shared across instances of the same subclass via   #
    # class-level dict; per-instance for safety)                          #
    # ------------------------------------------------------------------ #

    def __init__(self) -> None:
        self._grammar_ok: dict[str, bool] = {}
        self._parsers: dict[str, Any] = {}

    # ------------------------------------------------------------------ #
    # Properties                                                          #
    # ------------------------------------------------------------------ #

    @property
    def language_name(self) -> str:
        return self._language_name

    @property
    def extensions(self) -> tuple[str, ...]:
        return self._extensions

    # ------------------------------------------------------------------ #
    # Grammar installation                                                #
    # ------------------------------------------------------------------ #

    def _ensure_grammar(self, ext: str) -> bool:
        """Try to import the grammar module; auto-install via pip if missing.

        Returns ``True`` when the grammar is available, ``False`` otherwise.
        Results are cached so repeated calls are free.
        """
        if ext in self._grammar_ok:
            return self._grammar_ok[ext]

        grammar_module_name = self._grammar_imports.get(ext)
        if not grammar_module_name:
            self._grammar_ok[ext] = False
            return False

        # Gather packages that need installing
        missing_packages: list[str] = []

        # Check tree-sitter base
        try:
            importlib.import_module("tree_sitter")
        except ImportError:
            missing_packages.append("tree-sitter")

        # Check grammar package
        try:
            importlib.import_module(grammar_module_name)
        except ImportError:
            missing_packages.extend(self._pip_packages)

        if missing_packages:
            # Deduplicate while preserving order
            seen: set[str] = set()
            unique_packages: list[str] = []
            for pkg in missing_packages:
                if pkg not in seen:
                    seen.add(pkg)
                    unique_packages.append(pkg)

            try:
                subprocess.check_call(
                    [sys.executable, "-m", "pip", "install"]
                    + unique_packages
                    + ["-q"],
                )
            except (subprocess.CalledProcessError, FileNotFoundError, OSError) as exc:
                print(
                    f"[graph_bridge] WARN: tree-sitter grammar install failed "
                    f"for {self._language_name} ({ext}). "
                    f"Packages: {', '.join(unique_packages)}. "
                    f"Reason: {exc!r}. "
                    f"Code AST extraction for {ext} files will be skipped; "
                    f"install manually with `pip install {' '.join(unique_packages)}`.",
                    file=sys.stderr,
                )
                self._grammar_ok[ext] = False
                return False

            # Retry imports after install
            try:
                importlib.import_module("tree_sitter")
                importlib.import_module(grammar_module_name)
            except ImportError as exc:
                print(
                    f"[graph_bridge] WARN: tree-sitter grammar for "
                    f"{self._language_name} ({ext}) imported but failed to load "
                    f"after install ({exc!r}). {ext} files will be skipped.",
                    file=sys.stderr,
                )
                self._grammar_ok[ext] = False
                return False

        self._grammar_ok[ext] = True
        return True

    # ------------------------------------------------------------------ #
    # Parser creation                                                     #
    # ------------------------------------------------------------------ #

    def _get_parser(self, ext: str) -> Any:
        """Return a cached ``tree_sitter.Parser`` for *ext*, or ``None``.

        Creates the parser lazily on first call per extension.
        """
        if ext in self._parsers:
            return self._parsers[ext]

        if not self._ensure_grammar(ext):
            self._parsers[ext] = None
            return None

        grammar_module_name = self._grammar_imports.get(ext)
        func_name = self._grammar_funcs.get(ext)
        if not grammar_module_name or not func_name:
            self._parsers[ext] = None
            return None

        try:
            from tree_sitter import Language, Parser  # type: ignore[import-untyped]

            grammar_mod = importlib.import_module(grammar_module_name)
            lang_func = getattr(grammar_mod, func_name)
            language = Language(lang_func())
            parser = Parser(language)
            self._parsers[ext] = parser
            return parser
        except (ImportError, AttributeError, TypeError, OSError):
            self._parsers[ext] = None
            return None

    # ------------------------------------------------------------------ #
    # Public extraction entry point                                       #
    # ------------------------------------------------------------------ #

    def extract(
        self,
        files: list[Path],
        root: Path,
    ) -> dict[str, Any]:
        """Extract code structure from *files* and return nodes + edges.

        Files are grouped by extension.  For each group the corresponding
        tree-sitter parser is obtained via ``_get_parser``.  If the parser
        is unavailable the group is silently skipped (graceful degradation).

        Returns:
            ``{"nodes": [...], "edges": [...]}`` in the standard extraction
            format.  Empty lists when nothing can be parsed.
        """
        all_nodes: list[dict[str, Any]] = []
        all_edges: list[dict[str, Any]] = []
        seen_ids: set[str] = set()

        # Group files by extension
        by_ext: dict[str, list[Path]] = {}
        for f in files:
            ext = f.suffix.lower()
            by_ext.setdefault(ext, []).append(f)

        for ext, ext_files in by_ext.items():
            parser = self._get_parser(ext)
            if parser is None:
                continue

            for file_path in ext_files:
                try:
                    source_bytes = file_path.read_bytes()
                except OSError:
                    continue

                try:
                    tree = parser.parse(source_bytes)
                except Exception:
                    continue

                rel_path = self._rel_path(file_path, root)
                nodes, edges = self._walk_tree(tree, rel_path, root)

                for node in nodes:
                    nid = node["id"]
                    if nid not in seen_ids:
                        all_nodes.append(node)
                        seen_ids.add(nid)

                all_edges.extend(edges)

        return {"nodes": all_nodes, "edges": all_edges}

    # ------------------------------------------------------------------ #
    # Tree walking                                                        #
    # ------------------------------------------------------------------ #

    def _walk_tree(
        self,
        tree: Any,
        rel_path: str,
        root: Path,
    ) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
        """Walk a parsed tree and emit nodes and edges.

        Emits:
        * A file-level ``document`` hub node (same pattern as ``_AstVisitor``).
        * ``entity`` nodes for class/struct/interface/enum declarations.
        * ``interface`` nodes for function/method definitions.
        * ``imports`` edges for import statements.
        * ``calls`` edges for call expressions (INFERRED, weight=0.5).
        """
        nodes: list[dict[str, Any]] = []
        edges: list[dict[str, Any]] = []

        filename = Path(rel_path).name
        file_node_id = f"{rel_path}::document::{filename}"
        nodes.append({
            "id": file_node_id,
            "label": filename,
            "source_file": rel_path,
            "source_location": "line 1",
            "kind": "document",
            "confidence": "EXTRACTED",
            "confidence_score": 1.0,
            "description": "",
        })

        cursor = tree.walk()
        class_stack: list[str] = []
        current_func_id: str | None = None
        # Track depth for class stack management
        class_depths: list[int] = []

        # Iterative cursor-based traversal
        reached_root = False
        while not reached_root:
            node = cursor.node
            node_type = node.type
            depth = cursor.depth

            # Pop classes whose scope we have left
            while class_depths and depth <= class_depths[-1]:
                class_stack.pop()
                class_depths.pop()

            if node_type in self._class_node_types:
                name = self._extract_name(node)
                if name:
                    qualified = name
                    nid = f"{rel_path}::entity::{qualified}"
                    description = self._extract_docstring(node)
                    nodes.append({
                        "id": nid,
                        "label": name,
                        "source_file": rel_path,
                        "source_location": f"line {node.start_point[0] + 1}",
                        "kind": "entity",
                        "confidence": "EXTRACTED",
                        "confidence_score": 1.0,
                        "description": description,
                    })
                    class_stack.append(name)
                    class_depths.append(depth)

            elif node_type in self._function_node_types:
                name = self._extract_name(node)
                if name:
                    if class_stack:
                        class_name = class_stack[-1]
                        qualified = f"{class_name}.{name}()"
                        label = f"{class_name}.{name}()"
                    else:
                        qualified = f"{name}()"
                        label = f"{name}()"

                    nid = f"{rel_path}::interface::{qualified}"
                    description = self._extract_docstring(node)
                    nodes.append({
                        "id": nid,
                        "label": label,
                        "source_file": rel_path,
                        "source_location": f"line {node.start_point[0] + 1}",
                        "kind": "interface",
                        "confidence": "EXTRACTED",
                        "confidence_score": 1.0,
                        "description": description,
                    })
                    current_func_id = nid

            elif node_type in self._import_node_types:
                import_name = self._extract_import_name(node)
                if import_name:
                    target_id = f"module:{import_name}"
                    edges.append({
                        "source": file_node_id,
                        "target": target_id,
                        "relation": "imports",
                        "confidence": "EXTRACTED",
                        "weight": 1.0,
                    })

            elif node_type in self._call_node_types:
                if current_func_id is not None:
                    call_name = self._extract_call_name(node)
                    if call_name:
                        edges.append({
                            "source": current_func_id,
                            "target": call_name,
                            "relation": "calls",
                            "confidence": "INFERRED",
                            "weight": 0.5,
                        })

            # Move cursor: depth-first pre-order
            if cursor.goto_first_child():
                continue
            if cursor.goto_next_sibling():
                continue
            # Ascend until we can go to next sibling or reach root
            while True:
                if not cursor.goto_parent():
                    reached_root = True
                    break
                if cursor.goto_next_sibling():
                    break

        return nodes, edges

    # ------------------------------------------------------------------ #
    # Node data extraction helpers                                        #
    # ------------------------------------------------------------------ #

    def _extract_name(self, node: Any) -> str | None:
        """Extract the identifier name from a declaration node."""
        name_node = node.child_by_field_name(self._name_field)
        if name_node is not None:
            try:
                return name_node.text.decode("utf-8", errors="replace")
            except (AttributeError, UnicodeDecodeError):
                return None
        return None

    def _extract_docstring(self, node: Any) -> str:
        """Extract a doc comment or docstring from a declaration node.

        Looks for a string or comment node as the first child of the body,
        or immediately following the declaration.
        """
        body = node.child_by_field_name(self._body_field)
        if body is not None and body.child_count > 0:
            first_child = body.child(0)
            if first_child is not None:
                ntype = first_child.type
                if ntype in (
                    "string", "string_literal", "raw_string_literal",
                    "comment", "block_comment", "line_comment",
                    "expression_statement",
                ):
                    # For expression_statement wrapping a string
                    if ntype == "expression_statement" and first_child.child_count > 0:
                        inner = first_child.child(0)
                        if inner is not None and inner.type in ("string", "string_literal"):
                            try:
                                return inner.text.decode("utf-8", errors="replace").strip().strip('"\'')
                            except (AttributeError, UnicodeDecodeError):
                                return ""
                    try:
                        text = first_child.text.decode("utf-8", errors="replace").strip()
                        # Strip common comment markers
                        for prefix in ("//", "/*", "*/", "#", "///", "/**"):
                            text = text.removeprefix(prefix)
                        return text.strip().strip('"\'')
                    except (AttributeError, UnicodeDecodeError):
                        return ""
        return ""

    def _extract_import_name(self, node: Any) -> str | None:
        """Extract the top-level module name from an import node.

        Subclasses may override for language-specific import syntax.
        """
        # Generic: look for a string_literal or identifier child
        for i in range(node.child_count):
            child = node.child(i)
            if child is None:
                continue
            if child.type in (
                "string", "string_literal", "identifier",
                "scoped_identifier", "dotted_name",
            ):
                try:
                    text = child.text.decode("utf-8", errors="replace").strip().strip("'\"")
                    # Return top-level package (before first / or .)
                    for sep in ("/", "."):
                        if sep in text:
                            return text.split(sep)[0]
                    return text
                except (AttributeError, UnicodeDecodeError):
                    pass
        return None

    def _extract_call_name(self, node: Any) -> str | None:
        """Extract the callee name from a call expression node.

        Returns a symbolic name (not a fully-resolved node ID).
        Subclasses may override for language-specific call syntax.
        """
        # Try "function" field (common in many grammars)
        func_node = node.child_by_field_name("function")
        if func_node is not None:
            # For member expressions / attribute access, use the property name
            prop = func_node.child_by_field_name("property")
            if prop is not None:
                try:
                    return prop.text.decode("utf-8", errors="replace")
                except (AttributeError, UnicodeDecodeError):
                    pass
            try:
                return func_node.text.decode("utf-8", errors="replace")
            except (AttributeError, UnicodeDecodeError):
                pass
        # Fallback: first child
        if node.child_count > 0:
            first = node.child(0)
            if first is not None:
                try:
                    return first.text.decode("utf-8", errors="replace")
                except (AttributeError, UnicodeDecodeError):
                    pass
        return None

    # ------------------------------------------------------------------ #
    # Internal utilities                                                  #
    # ------------------------------------------------------------------ #

    @staticmethod
    def _rel_path(file_path: Path, root: Path) -> str:
        """Compute a portable relative path string."""
        try:
            return str(file_path.relative_to(root)).replace("\\", "/")
        except ValueError:
            return file_path.name
