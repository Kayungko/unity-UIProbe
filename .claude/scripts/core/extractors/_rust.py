"""Rust language extractor (tree-sitter based).

Handles ``.rs`` files.  Subclasses ``TreeSitterExtractor`` and adds
Rust-specific handling for:

* ``impl`` blocks -- functions inside get qualified names (``MyStruct.method()``)
  but the impl itself does NOT emit an entity node.
* ``use`` declarations -- extracts the crate name (first path segment).
* Doc comments (``///`` and ``//!``) -- collected from adjacent ``line_comment``
  siblings preceding the declaration.
* ``macro_invocation`` -- extracts the macro name before ``!``.
"""

from __future__ import annotations

from pathlib import Path
from typing import Any

from core.extractors._base_treesitter import TreeSitterExtractor


class RustExtractor(TreeSitterExtractor):
    """Extract code structure from Rust source files via tree-sitter."""

    _language_name = "Rust"
    _extensions = (".rs",)
    _pip_packages = ["tree-sitter-rust"]
    _grammar_imports = {".rs": "tree_sitter_rust"}
    _grammar_funcs = {".rs": "language"}
    _class_node_types = frozenset({"struct_item", "enum_item", "trait_item"})
    _function_node_types = frozenset({"function_item"})
    _import_node_types = frozenset({"use_declaration"})
    _call_node_types = frozenset({"call_expression", "macro_invocation"})

    # Rust-specific: impl blocks push to class_stack without emitting entity
    _impl_node_types: frozenset[str] = frozenset({"impl_item"})

    # ------------------------------------------------------------------ #
    # Overridden tree walker (adds impl_item handling)                     #
    # ------------------------------------------------------------------ #

    def _walk_tree(
        self,
        tree: Any,
        rel_path: str,
        root: Path,
    ) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
        """Walk a parsed Rust tree, emitting nodes and edges.

        Extends the base walker with ``impl_item`` support: when an
        ``impl Foo { ... }`` block is encountered, ``Foo`` is pushed onto
        the class stack so inner ``function_item`` nodes receive qualified
        names (e.g. ``Foo.bar()``).  The impl block itself does NOT produce
        an entity node.
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
        class_depths: list[int] = []

        reached_root = False
        while not reached_root:
            node = cursor.node
            node_type = node.type
            depth = cursor.depth

            # Pop classes / impls whose scope we have left
            while class_depths and depth <= class_depths[-1]:
                class_stack.pop()
                class_depths.pop()

            # -- Rust-specific: impl blocks --------------------------
            if node_type in self._impl_node_types:
                name = self._extract_impl_type_name(node)
                if name:
                    class_stack.append(name)
                    class_depths.append(depth)

            # -- Entity nodes (struct / enum / trait) -----------------
            elif node_type in self._class_node_types:
                name = self._extract_name(node)
                if name:
                    nid = f"{rel_path}::entity::{name}"
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

            # -- Interface nodes (function_item) ----------------------
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

            # -- Import edges (use declaration) -----------------------
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

            # -- Call edges (call_expression / macro_invocation) ------
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

            # -- Cursor advance: depth-first pre-order ----------------
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

    # ------------------------------------------------------------------ #
    # Rust-specific extraction helpers                                     #
    # ------------------------------------------------------------------ #

    def _extract_impl_type_name(self, node: Any) -> str | None:
        """Extract the type name from an ``impl`` block.

        In tree-sitter-rust ``impl_item`` stores the implemented type in
        a ``type`` field (e.g. ``Point`` in ``impl Point { ... }``).
        Falls back to the ``name`` field if ``type`` is absent.
        """
        type_node = node.child_by_field_name("type")
        if type_node is not None:
            try:
                return type_node.text.decode("utf-8", errors="replace")
            except (AttributeError, UnicodeDecodeError):
                pass
        # Fallback to name field
        return self._extract_name(node)

    def _extract_import_name(self, node: Any) -> str | None:
        """Extract the crate name from a Rust ``use`` declaration.

        ``use std::collections::HashMap`` -> ``"std"``
        ``use crate::utils``              -> ``"crate"``

        Scans children for ``scoped_identifier``, ``identifier``, or
        related node types and returns the first path segment (before
        the first ``::``).
        """
        for i in range(node.child_count):
            child = node.child(i)
            if child is None:
                continue
            if child.type in (
                "scoped_identifier",
                "scoped_use_list",
                "identifier",
                "use_list",
                "use_as_clause",
                "use_wildcard",
            ):
                try:
                    text = child.text.decode(
                        "utf-8", errors="replace",
                    ).strip()
                    # Split by :: to get the crate (first segment)
                    for sep in ("::", "/", "."):
                        if sep in text:
                            return text.split(sep)[0]
                    return text
                except (AttributeError, UnicodeDecodeError):
                    pass
        return None

    def _extract_call_name(self, node: Any) -> str | None:
        """Extract the callee name from a call or macro invocation.

        For ``macro_invocation``: returns the macro name before ``!``
        (e.g. ``println`` from ``println!(...)``).

        For ``call_expression``: delegates to the base implementation.
        """
        if node.type == "macro_invocation":
            if node.child_count > 0:
                first = node.child(0)
                if first is not None:
                    try:
                        return first.text.decode("utf-8", errors="replace")
                    except (AttributeError, UnicodeDecodeError):
                        pass
            return None
        return super()._extract_call_name(node)

    def _extract_docstring(self, node: Any) -> str:
        """Extract doc comments (``///`` or ``//!``) from preceding siblings.

        In Rust, doc comments are ``line_comment`` nodes that appear as
        siblings *before* the declaration, not as children of the body.
        Falls back to the base implementation if no doc comments are found.
        """
        lines: list[str] = []
        prev = getattr(node, "prev_sibling", None)
        # Guard against mock objects where prev_sibling is a callable
        if callable(prev):
            prev = None
        while prev is not None and getattr(prev, "type", None) == "line_comment":
            try:
                text = prev.text.decode("utf-8", errors="replace").strip()
            except (AttributeError, UnicodeDecodeError):
                break
            if text.startswith("///") or text.startswith("//!"):
                cleaned = text.lstrip("/").lstrip("!").strip()
                lines.insert(0, cleaned)
                prev = getattr(prev, "prev_sibling", None)
                if callable(prev):
                    prev = None
            else:
                break

        if lines:
            return " ".join(lines)

        return super()._extract_docstring(node)
