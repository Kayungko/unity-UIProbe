"""C++ tree-sitter code extractor.

Handles ``.cpp``, ``.cc``, ``.cxx``, ``.h``, ``.hpp``, ``.hxx`` files.
Adds C++-specific handling for namespaces, templates, and ``#include``
directives.

Special handling
----------------
* **Namespace** -- tracks nesting for qualified names like ``ns::Class``.
* **Templates** -- parameters are stripped automatically by tree-sitter's
  grammar; the ``name`` field contains the bare identifier.
* **``.h`` files** -- treated as C++ by default.
* **Header guards / ``#pragma once``** -- only ``preproc_include`` is
  matched in ``_import_node_types``; other preprocessor nodes are ignored.
* **Forward declarations** -- only ``function_definition`` is matched,
  so bare ``declaration`` nodes (forward decls) are skipped.
"""

from __future__ import annotations

from pathlib import Path
from typing import Any

from core.extractors._base_treesitter import TreeSitterExtractor


class CppExtractor(TreeSitterExtractor):
    """Extract code structure from C++ files via tree-sitter."""

    _language_name = "C++"
    _extensions = (".cpp", ".cc", ".cxx", ".h", ".hpp", ".hxx")
    _pip_packages = ["tree-sitter-cpp"]
    _grammar_imports = {
        ".cpp": "tree_sitter_cpp", ".cc": "tree_sitter_cpp",
        ".cxx": "tree_sitter_cpp", ".h": "tree_sitter_cpp",
        ".hpp": "tree_sitter_cpp", ".hxx": "tree_sitter_cpp",
    }
    _grammar_funcs = {
        ".cpp": "language", ".cc": "language", ".cxx": "language",
        ".h": "language", ".hpp": "language", ".hxx": "language",
    }
    _class_node_types = frozenset({
        "class_specifier", "struct_specifier", "enum_specifier",
    })
    _function_node_types = frozenset({"function_definition"})
    _import_node_types = frozenset({"preproc_include"})
    _call_node_types = frozenset({"call_expression"})

    # C++-specific: namespace tracking
    _namespace_node_types: frozenset[str] = frozenset({"namespace_definition"})

    # ------------------------------------------------------------------ #
    # Overrides                                                           #
    # ------------------------------------------------------------------ #

    def _extract_name(self, node: Any) -> str | None:
        """Extract identifier, with fallback through the declarator chain.

        C++ ``function_definition`` nodes store the name inside a nested
        ``declarator`` field rather than a direct ``name`` field.  The base
        implementation is tried first (covers class/struct/enum); if it
        returns ``None`` the declarator chain is walked.
        """
        result = super()._extract_name(node)
        if result is not None:
            return result

        if node.type in self._function_node_types:
            declarator = node.child_by_field_name("declarator")
            if declarator is None:
                return None
            inner = declarator.child_by_field_name("declarator")
            target = inner if inner is not None else declarator
            # qualified_identifier has a 'name' sub-field
            name_part = target.child_by_field_name("name")
            if name_part is not None:
                try:
                    return name_part.text.decode("utf-8", errors="replace")
                except (AttributeError, UnicodeDecodeError):
                    return None
            try:
                return target.text.decode("utf-8", errors="replace")
            except (AttributeError, UnicodeDecodeError):
                pass
        return None

    def _extract_import_name(self, node: Any) -> str | None:
        """Extract the header name from a ``#include`` directive.

        Looks for the ``path`` field (``system_lib_string`` for ``<...>``
        or ``string_literal`` for ``"..."``), strips delimiters, and
        returns the top-level name (before the first ``/`` or ``.``).
        """
        path_node = node.child_by_field_name("path")
        if path_node is not None:
            try:
                text = path_node.text.decode("utf-8", errors="replace").strip()
                text = text.strip('<>"')
                if not text:
                    return None
                for sep in ("/", "."):
                    if sep in text:
                        return text.split(sep)[0]
                return text
            except (AttributeError, UnicodeDecodeError):
                pass
        return super()._extract_import_name(node)

    # ------------------------------------------------------------------ #
    # Tree walking with namespace awareness                               #
    # ------------------------------------------------------------------ #

    def _walk_tree(
        self,
        tree: Any,
        rel_path: str,
        root: Path,
    ) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
        """Walk a parsed tree with C++ namespace tracking.

        Extends the base traversal by maintaining a ``namespace_stack``
        alongside the ``class_stack``.  Entity and interface labels are
        prefixed with the enclosing namespace (``ns::ClassName``).
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
        namespace_stack: list[str] = []
        current_func_id: str | None = None
        class_depths: list[int] = []
        namespace_depths: list[int] = []

        reached_root = False
        while not reached_root:
            node = cursor.node
            node_type = node.type
            depth = cursor.depth

            # Pop scopes that have been exited
            while class_depths and depth <= class_depths[-1]:
                class_stack.pop()
                class_depths.pop()
            while namespace_depths and depth <= namespace_depths[-1]:
                namespace_stack.pop()
                namespace_depths.pop()

            if node_type in self._namespace_node_types:
                name = self._extract_name(node)
                if name:
                    namespace_stack.append(name)
                    namespace_depths.append(depth)

            elif node_type in self._class_node_types:
                name = self._extract_name(node)
                if name:
                    if namespace_stack:
                        qualified = "::".join(namespace_stack) + "::" + name
                    else:
                        qualified = name
                    nid = f"{rel_path}::entity::{qualified}"
                    description = self._extract_docstring(node)
                    nodes.append({
                        "id": nid,
                        "label": qualified,
                        "source_file": rel_path,
                        "source_location": f"line {node.start_point[0] + 1}",
                        "kind": "entity",
                        "confidence": "EXTRACTED",
                        "confidence_score": 1.0,
                        "description": description,
                    })
                    class_stack.append(qualified)
                    class_depths.append(depth)

            elif node_type in self._function_node_types:
                name = self._extract_name(node)
                if name:
                    if class_stack:
                        class_name = class_stack[-1]
                        qualified = f"{class_name}.{name}()"
                    elif namespace_stack:
                        ns_prefix = "::".join(namespace_stack)
                        qualified = f"{ns_prefix}::{name}()"
                    else:
                        qualified = f"{name}()"
                    label = qualified

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
            while True:
                if not cursor.goto_parent():
                    reached_root = True
                    break
                if cursor.goto_next_sibling():
                    break

        return nodes, edges
