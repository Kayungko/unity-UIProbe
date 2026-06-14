"""C# tree-sitter extractor.

Extracts code structure (classes, structs, interfaces, enums, records,
methods, using directives, calls) from C# source files via tree-sitter.

Overrides import extraction to preserve the full namespace
(``using System.Linq`` -> ``module:System.Linq``) and docstring
extraction to capture XML doc comments (``///``) from preceding siblings.
"""

from __future__ import annotations

from typing import Any

from core.extractors._base_treesitter import TreeSitterExtractor


class CSharpExtractor(TreeSitterExtractor):
    """Tree-sitter extractor for C# source files."""

    _language_name = "C#"
    _extensions = (".cs",)
    _pip_packages = ["tree-sitter-c-sharp"]
    _grammar_imports = {".cs": "tree_sitter_c_sharp"}
    _grammar_funcs = {".cs": "language"}

    _class_node_types = frozenset({
        "class_declaration",
        "interface_declaration",
        "struct_declaration",
        "enum_declaration",
        "record_declaration",
    })
    _function_node_types = frozenset({
        "method_declaration",
        "constructor_declaration",
    })
    _import_node_types = frozenset({"using_directive"})
    _call_node_types = frozenset({
        "invocation_expression",
        "object_creation_expression",
    })

    # ------------------------------------------------------------------ #
    # Import extraction                                                    #
    # ------------------------------------------------------------------ #

    def _extract_import_name(self, node: Any) -> str | None:
        """Extract the full namespace from a using directive.

        ``using System.Linq`` -> ``System.Linq``
        (preserves the complete namespace path).
        """
        for i in range(node.child_count):
            child = node.child(i)
            if child is None:
                continue
            if child.type in (
                "qualified_name",
                "identifier_name",
                "identifier",
                "name",
                "scoped_identifier",
            ):
                try:
                    text = (
                        child.text.decode("utf-8", errors="replace")
                        .strip()
                        .rstrip(";")
                    )
                    return text
                except (AttributeError, UnicodeDecodeError):
                    pass
        return None

    # ------------------------------------------------------------------ #
    # Docstring extraction                                                 #
    # ------------------------------------------------------------------ #

    def _extract_docstring(self, node: Any) -> str:
        """Extract XML doc comments (``///``) preceding the declaration.

        Falls back to the base implementation when no preceding XML doc
        comment is found.
        """
        prev = getattr(node, "prev_named_sibling", None)
        if prev is not None and getattr(prev, "type", None) == "comment":
            try:
                text = prev.text.decode("utf-8", errors="replace").strip()
                if text.startswith("///"):
                    text = text[3:]
                return text.strip()
            except (AttributeError, UnicodeDecodeError):
                pass
        return super()._extract_docstring(node)
