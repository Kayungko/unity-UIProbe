"""Java tree-sitter extractor.

Extracts code structure (classes, interfaces, enums, records, methods,
imports, calls) from Java source files via tree-sitter.

Overrides import extraction to produce package-level targets
(``import java.util.List`` -> ``module:java.util``) and docstring
extraction to capture Javadoc (``/** */``) from preceding siblings.
"""

from __future__ import annotations

from typing import Any

from core.extractors._base_treesitter import TreeSitterExtractor


class JavaExtractor(TreeSitterExtractor):
    """Tree-sitter extractor for Java source files."""

    _language_name = "Java"
    _extensions = (".java",)
    _pip_packages = ["tree-sitter-java"]
    _grammar_imports = {".java": "tree_sitter_java"}
    _grammar_funcs = {".java": "language"}

    _class_node_types = frozenset({
        "class_declaration",
        "interface_declaration",
        "enum_declaration",
        "record_declaration",
    })
    _function_node_types = frozenset({
        "method_declaration",
        "constructor_declaration",
    })
    _import_node_types = frozenset({"import_declaration"})
    _call_node_types = frozenset({
        "method_invocation",
        "object_creation_expression",
    })

    # ------------------------------------------------------------------ #
    # Import extraction                                                    #
    # ------------------------------------------------------------------ #

    def _extract_import_name(self, node: Any) -> str | None:
        """Extract the Java package name from an import declaration.

        ``import java.util.List`` -> ``java.util``
        (strips the final class/interface name, keeps the package path).
        """
        for i in range(node.child_count):
            child = node.child(i)
            if child is None:
                continue
            if child.type in ("scoped_identifier", "identifier"):
                try:
                    text = child.text.decode("utf-8", errors="replace").strip()
                    parts = text.split(".")
                    if len(parts) >= 2:
                        return ".".join(parts[:-1])
                    return text
                except (AttributeError, UnicodeDecodeError):
                    pass
        return None

    # ------------------------------------------------------------------ #
    # Docstring extraction                                                 #
    # ------------------------------------------------------------------ #

    def _extract_docstring(self, node: Any) -> str:
        """Extract Javadoc from a ``block_comment`` preceding the declaration.

        Falls back to the base implementation when no preceding Javadoc
        comment is found.
        """
        prev = getattr(node, "prev_named_sibling", None)
        if prev is not None and getattr(prev, "type", None) == "block_comment":
            try:
                text = prev.text.decode("utf-8", errors="replace").strip()
                if text.startswith("/**"):
                    text = text[3:]
                if text.endswith("*/"):
                    text = text[:-2]
                return text.strip()
            except (AttributeError, UnicodeDecodeError):
                pass
        return super()._extract_docstring(node)
