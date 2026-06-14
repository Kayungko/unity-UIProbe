"""Go tree-sitter code extractor.

Extracts code structure (structs, interfaces, functions, methods, imports,
calls) from Go source files using tree-sitter-go.

Special handling
----------------
* ``type_declaration`` wraps ``struct_type`` or ``interface_type`` -- name
  is extracted from the ``type_spec`` child.
* ``method_declaration`` carries a receiver parameter -- the qualified name
  is formed as ``Receiver.Method``.
* Import paths use the last segment: ``"github.com/user/pkg"`` -> ``pkg``.
* Go has no docstrings -- preceding ``comment`` nodes are extracted instead.
"""

from __future__ import annotations

from typing import Any

from core.extractors._base_treesitter import TreeSitterExtractor


class GoExtractor(TreeSitterExtractor):
    """Extract code structure from Go files via tree-sitter."""

    _language_name = "Go"
    _extensions = (".go",)
    _pip_packages = ["tree-sitter-go"]
    _grammar_imports = {".go": "tree_sitter_go"}
    _grammar_funcs = {".go": "language"}
    _class_node_types = frozenset({"type_declaration"})
    _function_node_types = frozenset({"function_declaration", "method_declaration"})
    _import_node_types = frozenset({"import_declaration", "import_spec"})
    _call_node_types = frozenset({"call_expression"})

    # ------------------------------------------------------------------ #
    # Go-specific overrides                                                #
    # ------------------------------------------------------------------ #

    def _extract_name(self, node: Any) -> str | None:
        """Extract name from a Go declaration node.

        Handles:
        - ``type_declaration``: dives into ``type_spec`` child for the name.
        - ``method_declaration``: forms ``Receiver.Method`` from receiver
          parameter and method name.
        - Other nodes: delegates to base class.
        """
        if node.type == "type_declaration":
            return self._extract_type_decl_name(node)

        if node.type == "method_declaration":
            return self._extract_method_qualified_name(node)

        return super()._extract_name(node)

    def _extract_type_decl_name(self, node: Any) -> str | None:
        """Extract the type name from a ``type_declaration`` node.

        Go wraps the actual name in a ``type_spec`` child::

            type_declaration
              type_spec
                name: type_identifier ("User")
                type: struct_type | interface_type
        """
        for i in range(node.child_count):
            child = node.child(i)
            if child is not None and child.type == "type_spec":
                name_node = child.child_by_field_name(self._name_field)
                if name_node is not None:
                    try:
                        return name_node.text.decode("utf-8", errors="replace")
                    except (AttributeError, UnicodeDecodeError):
                        return None
        return None

    def _extract_method_qualified_name(self, node: Any) -> str | None:
        """Extract ``Receiver.Method`` from a ``method_declaration`` node.

        Go methods declare their receiver inline::

            method_declaration
              receiver: parameter_list
                parameter_declaration
                  type: pointer_type > type_identifier | type_identifier
              name: field_identifier ("Name")
        """
        name_node = node.child_by_field_name(self._name_field)
        if name_node is None:
            return None
        try:
            method_name = name_node.text.decode("utf-8", errors="replace")
        except (AttributeError, UnicodeDecodeError):
            return None

        receiver = node.child_by_field_name("receiver")
        if receiver is not None:
            receiver_type = self._extract_receiver_type(receiver)
            if receiver_type:
                return f"{receiver_type}.{method_name}"
        return method_name

    def _extract_receiver_type(self, receiver_node: Any) -> str | None:
        """Extract the type name from a Go method receiver parameter list.

        Handles both value receivers ``(u User)`` and pointer receivers
        ``(u *User)``.
        """
        for i in range(receiver_node.child_count):
            param = receiver_node.child(i)
            if param is None or param.type != "parameter_declaration":
                continue
            type_node = param.child_by_field_name("type")
            if type_node is None:
                continue
            if type_node.type == "pointer_type":
                for j in range(type_node.child_count):
                    inner = type_node.child(j)
                    if inner is not None and inner.type == "type_identifier":
                        try:
                            return inner.text.decode("utf-8", errors="replace")
                        except (AttributeError, UnicodeDecodeError):
                            return None
            elif type_node.type == "type_identifier":
                try:
                    return type_node.text.decode("utf-8", errors="replace")
                except (AttributeError, UnicodeDecodeError):
                    return None
        return None

    def _extract_import_name(self, node: Any) -> str | None:
        """Extract module name from a Go import node.

        For ``import_spec`` nodes, reads the ``path`` field and returns the
        last path segment (e.g. ``"github.com/user/pkg"`` -> ``pkg``).
        For other import nodes, delegates to the base class.
        """
        if node.type == "import_spec":
            path_node = node.child_by_field_name("path")
            if path_node is not None:
                try:
                    text = path_node.text.decode(
                        "utf-8", errors="replace"
                    ).strip().strip('"')
                except (AttributeError, UnicodeDecodeError):
                    return None
                if "/" in text:
                    return text.rsplit("/", 1)[1]
                return text
        return super()._extract_import_name(node)

    def _extract_docstring(self, node: Any) -> str:
        """Extract documentation from preceding comment nodes.

        Go has no docstrings.  Instead, documentation is placed in
        ``// ...`` comments immediately before the declaration.
        """
        prev = getattr(node, "prev_named_sibling", None)
        if prev is not None and getattr(prev, "type", None) == "comment":
            try:
                text = prev.text.decode("utf-8", errors="replace").strip()
                text = text.removeprefix("//").strip()
                return text
            except (AttributeError, UnicodeDecodeError):
                return ""
        return ""
