"""TypeScript/JavaScript tree-sitter code extractor.

Handles ``.ts``, ``.tsx``, ``.js``, and ``.jsx`` files via the
``tree-sitter-typescript`` and ``tree-sitter-javascript`` grammars.

Subclasses ``TreeSitterExtractor`` -- most behaviour is inherited;
only arrow-function naming, import source extraction, and
``new`` expression call-name extraction need overrides.
"""

from __future__ import annotations

from typing import Any

from core.extractors._base_treesitter import TreeSitterExtractor


class TypeScriptExtractor(TreeSitterExtractor):
    """Extract code structure from TypeScript and JavaScript files."""

    _language_name = "TypeScript"
    _extensions = (".ts", ".tsx", ".js", ".jsx")
    _pip_packages = ["tree-sitter-typescript", "tree-sitter-javascript"]
    _grammar_imports = {
        ".ts": "tree_sitter_typescript",
        ".tsx": "tree_sitter_typescript",
        ".js": "tree_sitter_javascript",
        ".jsx": "tree_sitter_javascript",
    }
    _grammar_funcs = {
        ".ts": "language_typescript",
        ".tsx": "language_tsx",
        ".js": "language",
        ".jsx": "language",  # JSX uses same grammar as JS in tree-sitter-javascript
    }
    _class_node_types = frozenset({
        "class_declaration", "interface_declaration",
        "type_alias_declaration", "enum_declaration",
    })
    _function_node_types = frozenset({
        "function_declaration", "method_definition",
        "arrow_function",  # only when parent is variable_declarator
        "generator_function_declaration",
    })
    _import_node_types = frozenset({"import_statement"})
    _call_node_types = frozenset({"call_expression", "new_expression"})

    # ------------------------------------------------------------------ #
    # Overrides for language-specific extraction                          #
    # ------------------------------------------------------------------ #

    def _extract_name(self, node: Any) -> str | None:
        """Extract identifier name; special-case arrow functions.

        For ``arrow_function`` nodes the name lives on the parent
        ``variable_declarator`` (e.g. ``const handler = () => {}``).
        All other node types delegate to the base class.
        """
        if node.type == "arrow_function":
            parent = getattr(node, "parent", None)
            if parent is not None and parent.type == "variable_declarator":
                return super()._extract_name(parent)
            return None
        return super()._extract_name(node)

    def _extract_import_name(self, node: Any) -> str | None:
        """Extract module name from the import statement's ``source`` field.

        In tree-sitter-typescript/javascript ``import_statement`` carries a
        ``source`` field with the string literal.

        Returns:
        * Local relative imports (``./Card``, ``../utils/foo``) -- the FULL
          specifier is preserved so that ``_resolve_code_edges`` can map it
          to the target file's document hub.  Without the tail, all local
          imports would collapse to ``.``/``..`` and be useless.
        * Scoped packages (``@scope/pkg/sub``) -- the ``@scope/pkg`` prefix
          is returned (first two segments; the tail is irrelevant for
          external deps).
        * Everything else (``react``, ``react/jsx-runtime``) -- the
          top-level package name before the first ``/``.

        Falls back to the base class generic extraction when the field is
        absent.
        """
        source_node = node.child_by_field_name("source")
        if source_node is not None:
            try:
                text = (
                    source_node.text
                    .decode("utf-8", errors="replace")
                    .strip()
                    .strip("'\"")
                )
                if not text:
                    return None
                # Local relative/absolute paths — preserve the full spec
                if text.startswith("./") or text.startswith("../") or text.startswith("/"):
                    return text
                # Scoped package: @scope/pkg[/sub] -> @scope/pkg
                if text.startswith("@") and "/" in text:
                    parts = text.split("/", 2)
                    return "/".join(parts[:2]) if len(parts) >= 2 else text
                # Regular package: top-level only
                if "/" in text:
                    return text.split("/")[0]
                return text
            except (AttributeError, UnicodeDecodeError):
                pass
        return super()._extract_import_name(node)

    def _extract_call_name(self, node: Any) -> str | None:
        """Extract callee name; handle ``new`` expressions.

        ``new Foo()`` uses a ``constructor`` field in tree-sitter-javascript
        rather than ``function``.  Falls back to the base class for regular
        call expressions.
        """
        if node.type == "new_expression":
            constructor = node.child_by_field_name("constructor")
            if constructor is not None:
                try:
                    return constructor.text.decode("utf-8", errors="replace")
                except (AttributeError, UnicodeDecodeError):
                    pass
        return super()._extract_call_name(node)
