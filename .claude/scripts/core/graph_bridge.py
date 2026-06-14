"""Bridge layer between graphify knowledge-graph engine and harness wiki.

This module encapsulates all graphify API calls behind a stable interface
so that other harness modules never import graphify directly.  When graphify
is not installed every public function degrades gracefully -- returning
``None`` or an empty dict -- without raising exceptions.

Public API
----------
build_graph(project_root, modules, language, include_docs=True)
    Run the full graphify pipeline (collect -> extract -> build -> cluster
    -> analyze) and return a result dict, or ``None`` when graphify is
    unavailable.  ``include_docs=True`` (default) parses markdown/text
    documents in addition to code so the graph gains connecting
    contains/references edges; pass ``False`` to skip docs.

map_to_wiki_structures(graph_data, modules)
    Transform graphify nodes/edges into the entity / interface / dependency
    / design_decisions dicts consumed by ``wiki_gen``.
"""

from __future__ import annotations

import json
import re
from pathlib import Path
from typing import Any


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

# JSON serialisation constants (T4-3)
_GRAPH_SCHEMA_VERSION: int = 1
_GRAPH_JSON_REL: str = ".claude/wiki/graph.json"


# ---------------------------------------------------------------------------
# AST-based code extraction helpers (T4-1)
# Moved to core.extractors._python in T7-1; re-exported for compatibility.
# ---------------------------------------------------------------------------

from core.extractors._python import (  # noqa: E402
    _AstVisitor,
    _extract_via_ast,
    _STDLIB_MODULES,
    _SKIP_DIRS,
)


def _collect_code_files(
    root: Path,
    modules: list[dict[str, Any]],
) -> list[Path]:
    """Collect ``.py`` files from module paths, skipping noise directories.

    For each module's declared ``paths``, recursively scans for ``*.py``
    files.  Directories named ``__pycache__``, ``.git``, ``.claude``,
    ``node_modules``, ``venv``, or ``.venv`` are skipped.

    Returns:
        Deduplicated, sorted list of absolute ``Path`` objects.
    """
    collected: set[Path] = set()
    for mod in modules:
        for rel_path in mod.get("paths", []):
            target = root / rel_path
            if not target.exists():
                continue
            for py_file in target.rglob("*.py"):
                # Skip if any ancestor directory is in _SKIP_DIRS
                skip = False
                for part in py_file.relative_to(root).parts[:-1]:
                    if part in _SKIP_DIRS:
                        skip = True
                        break
                if not skip:
                    collected.add(py_file)
    return sorted(collected)




# ---------------------------------------------------------------------------
# networkx-native graph helpers (T4-2)
# ---------------------------------------------------------------------------


def _build_graph_nx(
    extraction: dict,
    directed: bool = False,
):
    """Build a networkx graph from an extraction dict.

    Args:
        extraction: Dict with ``nodes`` and ``edges`` lists.
        directed: When ``True`` returns a ``DiGraph``; otherwise ``Graph``.

    Returns:
        ``nx.DiGraph`` when *directed* is ``True``, else ``nx.Graph``.

    Raises:
        ImportError: When networkx is not installed.
    """
    import networkx as nx  # late import — keeps module importable without networkx

    g = nx.DiGraph() if directed else nx.Graph()

    node_ids: set[str] = {n["id"] for n in extraction.get("nodes", [])}

    for n in extraction.get("nodes", []):
        nid = n["id"]
        attrs = {k: v for k, v in n.items() if k != "id"}
        g.add_node(nid, **attrs)

    for e in extraction.get("edges", []):
        src = e.get("source", "")
        tgt = e.get("target", "")
        if src not in node_ids or tgt not in node_ids:
            continue  # skip dangling edges
        attrs = {k: v for k, v in e.items() if k not in ("source", "target")}
        g.add_edge(src, tgt, **attrs)

    return g


def _cluster_communities_nx(graph) -> dict:
    """Detect communities in *graph* using greedy modularity optimisation.

    Args:
        graph: A ``nx.Graph`` or ``nx.DiGraph``.

    Returns:
        ``{0: [node_id, ...], 1: [...]}`` — integer-keyed community dict.
        Returns ``{}`` for empty or zero-node graphs.

    Raises:
        ImportError: When networkx is not installed.
    """
    import networkx as nx  # noqa: F401 — late import

    if graph is None or len(graph.nodes) == 0:
        return {}

    # greedy_modularity_communities requires an undirected graph
    if isinstance(graph, nx.DiGraph):
        undirected = graph.to_undirected()
    else:
        undirected = graph

    try:
        communities = nx.algorithms.community.greedy_modularity_communities(undirected)
        return {
            i: sorted(list(community))
            for i, community in enumerate(communities)
        }
    except Exception:
        # Fall back: single community containing all nodes
        return {0: list(graph.nodes)}


def _compute_god_nodes_nx(graph, top_n: int = 10) -> list:
    """Identify high-degree hub nodes in *graph*.

    Args:
        graph: A ``nx.Graph`` or ``nx.DiGraph``.
        top_n: Maximum number of nodes to return (default 10).

    Returns:
        List of dicts with keys ``id``, ``label``, ``degree``,
        ``source_file``, ``source_location``, sorted by degree descending
        with node_id ascending as a tiebreaker.  Returns ``[]`` for empty
        graphs.

    Raises:
        ImportError: When networkx is not installed.
    """
    import networkx as nx  # noqa: F401 — late import

    if graph is None or len(graph.nodes) == 0:
        return []

    # list(graph.degree) gives [(node_id, degree), ...]
    degrees: list[tuple[str, int]] = list(graph.degree)

    # Sort: degree descending, then node_id ascending for determinism
    degrees.sort(key=lambda x: (-x[1], x[0]))

    top = degrees[:top_n]

    result: list[dict] = []
    for nid, deg in top:
        attrs = graph.nodes[nid]
        result.append({
            "id": nid,
            "label": attrs.get("label", nid),
            "degree": deg,
            "source_file": attrs.get("source_file", ""),
            "source_location": attrs.get("source_location", ""),
        })

    return result


# ---------------------------------------------------------------------------
# JSON roundtrip helpers (T4-3)
# ---------------------------------------------------------------------------


def _graph_to_json_data(
    graph,
    communities: dict,
    god_nodes: list,
) -> dict:
    """Serialise a networkx graph + metadata to a JSON-safe dict.

    Args:
        graph: A ``nx.Graph`` / ``nx.DiGraph``, or ``None``.
        communities: Integer-keyed community dict ``{0: [node_id, ...]}``.
        god_nodes: List of god-node dicts from ``_compute_god_nodes_nx``.

    Returns:
        Top-level dict with keys ``schema_version``, ``graph``,
        ``communities`` (string keys), and ``god_nodes``.
    """
    import networkx as nx  # late import

    if graph is None:
        graph_payload = None
    else:
        graph_payload = nx.node_link_data(graph)

    # JSON object keys must be strings
    communities_str: dict[str, list] = {
        str(k): list(v) for k, v in (communities or {}).items()
    }

    return {
        "schema_version": _GRAPH_SCHEMA_VERSION,
        "graph": graph_payload,
        "communities": communities_str,
        "god_nodes": god_nodes or [],
    }


def _graph_from_json_data(data: dict) -> dict:
    """Deserialise a JSON dict previously produced by ``_graph_to_json_data``.

    Args:
        data: Dict as loaded from JSON (may be missing keys — handled
            gracefully via ``.get`` with defaults).

    Returns:
        Dict with keys ``graph`` (nx.Graph or None), ``communities``
        (int-keyed), ``god_nodes`` (list), and ``extraction`` (rebuilt
        from graph nodes/edges).
    """
    # Reconstruct graph
    raw_graph = data.get("graph")
    if raw_graph is None:
        graph = None
    else:
        import networkx as nx  # late import
        graph = nx.node_link_graph(raw_graph)

    # Convert string community keys back to int when possible
    raw_communities: dict = data.get("communities") or {}
    communities: dict = {
        (int(k) if isinstance(k, str) and k.isdigit() else k): v
        for k, v in raw_communities.items()
    }

    # Rebuild extraction from graph
    if graph is not None:
        nodes = [
            {**attrs, "id": nid}
            for nid, attrs in graph.nodes(data=True)
        ]
        edges = [
            {**attrs, "source": u, "target": v}
            for u, v, attrs in graph.edges(data=True)
        ]
    else:
        nodes = []
        edges = []

    extraction = {"nodes": nodes, "edges": edges}

    return {
        "graph": graph,
        "communities": communities,
        "god_nodes": data.get("god_nodes") or [],
        "extraction": extraction,
    }


def write_graph_json(
    root: Path,
    graph_data: dict,
    force: bool = False,
) -> "Path | None":
    """Write graph data to ``.claude/wiki/graph.json``.

    Args:
        root: Project root path.
        graph_data: Dict with keys ``graph``, ``communities``, ``god_nodes``
            (same shape as :func:`build_graph` output).
        force: When ``False`` (default) an existing file is not overwritten
            (idempotent skip, returns ``None``).  Pass ``True`` to always
            write.

    Returns:
        The written :class:`~pathlib.Path` on success, or ``None`` when
        the file already existed and *force* was ``False``.
    """
    out_path = root / _GRAPH_JSON_REL

    if out_path.exists() and not force:
        return None

    out_path.parent.mkdir(parents=True, exist_ok=True)

    payload = _graph_to_json_data(
        graph=graph_data.get("graph"),
        communities=graph_data.get("communities") or {},
        god_nodes=graph_data.get("god_nodes") or [],
    )

    with out_path.open("w", encoding="utf-8") as fp:
        json.dump(payload, fp, indent=2, ensure_ascii=False)

    return out_path


def read_graph_json(root: Path) -> "dict | None":
    """Read and deserialise ``.claude/wiki/graph.json``.

    Args:
        root: Project root path.

    Returns:
        Deserialised dict (same shape as :func:`build_graph` output) on
        success, or ``None`` when the file does not exist, is not valid
        JSON, or contains unexpected structure.
    """
    in_path = root / _GRAPH_JSON_REL

    if not in_path.exists():
        return None

    try:
        data = json.loads(in_path.read_text(encoding="utf-8"))
        return _graph_from_json_data(data)
    except (json.JSONDecodeError, OSError, KeyError):
        return None


def slugify_label(value: str) -> str:
    """Convert a label to a URL-safe slug."""
    slug = re.sub(r"[^a-zA-Z0-9]+", "-", value.strip().lower()).strip("-")
    return slug or "item"


def _resolve_code_edges(code_extraction: dict[str, Any]) -> int:
    """Post-process extracted code edges so the graph actually connects.

    Extractors emit two kinds of edges that ``_build_graph_nx`` would
    otherwise drop as dangling:

    * **import edges** with ``target = "module:react"`` (external module name,
      not a node id);
    * **call edges** with ``target = "foo"`` (symbolic function name,
      not a node id).

    Without post-processing, code-only projects produce 0 edges in the
    final graph, which collapses community detection (each node becomes
    its own community).

    This function:

    1. Adds ``document -> entity`` and ``document -> interface`` contains
       edges inside each source file so that every file forms a connected
       star around its document hub.
    2. Resolves same-file call-edge targets from symbolic names to the
       matching interface node id (e.g. ``"foo"`` -> ``"src/a.py::interface::foo()"``).
       Cross-file call resolution is NOT attempted — it requires an
       import graph which is unreliable without type info.

    Mutates *code_extraction* in place; returns the number of edges added
    or rewritten (for logging/tests).
    """
    nodes = code_extraction.get("nodes", []) or []
    edges = code_extraction.get("edges", []) or []

    # --- Pass 1: file-hub -> entity/interface containment edges ----------
    file_hubs: dict[str, str] = {}
    for n in nodes:
        if n.get("kind") == "document":
            sf = n.get("source_file", "")
            if sf:
                file_hubs[sf] = n.get("id", "")

    added: list[dict[str, Any]] = []
    for n in nodes:
        kind = n.get("kind", "")
        if kind not in ("entity", "interface"):
            continue
        sf = n.get("source_file", "")
        hub_id = file_hubs.get(sf)
        if not hub_id:
            continue
        added.append({
            "source": hub_id,
            "target": n.get("id", ""),
            "relation": "contains",
            "confidence": "EXTRACTED",
            "weight": 1.0,
        })

    # --- Pass 2: best-effort same-file call resolution -------------------
    # Index interface labels per source file: {source_file: {bare_name: node_id}}
    file_interfaces: dict[str, dict[str, str]] = {}
    for n in nodes:
        if n.get("kind") != "interface":
            continue
        sf = n.get("source_file", "")
        label = n.get("label", "") or ""
        nid = n.get("id", "")
        if not (sf and label and nid):
            continue
        # Label shapes: "foo()", "Class.foo()", "ns::Class::foo()"
        bare = label.rstrip(")").split("(", 1)[0]
        # Split on both . and :: to capture last segment
        short = re.split(r"\.|::", bare)[-1]
        if short:
            file_interfaces.setdefault(sf, {}).setdefault(short, nid)

    # Source-node -> file lookup for call / import edge sources
    id_to_file = {
        n.get("id", ""): n.get("source_file", "")
        for n in nodes
        if n.get("id")
    }

    resolved = 0
    for e in edges:
        if e.get("relation") != "calls":
            continue
        tgt = e.get("target", "")
        if not tgt or "::" in tgt:
            continue  # already a node_id
        src = e.get("source", "")
        src_file = id_to_file.get(src, "")
        if not src_file:
            continue
        resolved_id = file_interfaces.get(src_file, {}).get(tgt)
        if resolved_id:
            e["target"] = resolved_id
            resolved += 1

    # --- Pass 3: cross-file import resolution ----------------------------
    # Local relative imports (``./Card``, ``../utils/foo``) should be
    # rewritten from ``module:./Card`` to the target file's document hub
    # node id.  This is what actually connects sibling files into a single
    # connected component per feature/directory.
    #
    # Matching strategy:
    # 1. Resolve the relative path against the source's file directory.
    # 2. Try several extension variants (``.ts``, ``.tsx``, ``.js``,
    #    ``.jsx``, ``.py``, ``.go``, etc.) and the ``/<name>/index.*``
    #    convention used by JS/TS package-style imports.
    # 3. If a document hub node exists for any candidate, rewrite target.
    #
    # External packages (``module:react``, ``module:@scope/pkg``) have no
    # file in the project tree and are left as symbolic strings.
    _TRY_EXTS = (".ts", ".tsx", ".js", ".jsx", ".py", ".go", ".rs",
                 ".java", ".cs", ".cpp", ".h", ".hpp")
    # Index file_hubs by several normalized keys for fast lookup
    hub_by_file = dict(file_hubs)  # source_file -> hub_id (already built in Pass 1)

    import os.path as _osp
    import_resolved = 0
    for e in edges:
        if e.get("relation") != "imports":
            continue
        tgt = e.get("target", "")
        if not tgt.startswith("module:"):
            continue
        spec = tgt[len("module:"):]
        # Only resolve local imports (relative or absolute in-project)
        if not (spec.startswith("./") or spec.startswith("../") or spec.startswith("/")):
            continue
        src = e.get("source", "")
        src_file = id_to_file.get(src, "")
        if not src_file:
            continue
        src_dir = _osp.dirname(src_file.replace("\\", "/"))
        # normpath would collapse .. correctly
        resolved_path = _osp.normpath(_osp.join(src_dir, spec)).replace("\\", "/")

        # Candidates: as-is, each extension appended, index.<ext> inside
        candidates: list[str] = [resolved_path]
        for ext in _TRY_EXTS:
            candidates.append(resolved_path + ext)
            candidates.append(_osp.join(resolved_path, "index" + ext).replace("\\", "/"))

        for candidate in candidates:
            hub_id = hub_by_file.get(candidate)
            if hub_id:
                e["target"] = hub_id
                import_resolved += 1
                break

    if added:
        code_extraction["edges"] = edges + added

    return len(added) + resolved + import_resolved


def _resolve_call_targets(extraction: dict[str, Any]) -> int:
    """Post-pass: resolve short-name calls edge targets to concrete node ids.

    Tree-sitter extractors emit ``relation="calls"`` edges whose ``target``
    field is the bare call-site name (e.g. ``"AddIpAddress"``), not a node id.
    ``_build_graph_nx`` silently drops these as dangling edges, making the
    cross-file call graph completely invisible to ``shortest_path`` and
    ``get_neighbors``.

    This function builds a global reverse index — short-name → candidate
    node ids — over ``kind in {"entity", "interface"}`` nodes, then rewrites
    any still-unresolved calls edge whose target is a unique hit.

    Disambiguation strategy (strictly conservative):
    - Unique match (len(candidates) == 1): replace target with node_id.
    - Multiple candidates (>= 2): leave target unchanged (no guessing).
    - No match: leave target unchanged (current behaviour preserved).

    No new fields are added to the Edge schema.

    Args:
        extraction: Mutable dict with ``nodes`` and ``edges`` lists, as
            produced by the extractors and possibly already processed by
            :func:`_resolve_code_edges`.

    Returns:
        Number of targets rewritten.
    """
    nodes: list[dict[str, Any]] = extraction.get("nodes", []) or []
    edges: list[dict[str, Any]] = extraction.get("edges", []) or []

    # Build global reverse index: short-name -> list[node_id]
    # Only entity and interface nodes participate — they are the
    # symbols that callers actually reference by name.
    short_to_candidates: dict[str, list[str]] = {}
    for n in nodes:
        if n.get("kind") not in ("entity", "interface"):
            continue
        nid = n.get("id", "")
        label = n.get("label", "") or ""
        if not (nid and label):
            continue
        # Derive the bare short name the same way _resolve_code_edges does:
        # strip trailing "()", split on "." or "::", take last segment.
        bare = label.rstrip(")").split("(", 1)[0]
        short = re.split(r"\.|::", bare)[-1]
        if short:
            short_to_candidates.setdefault(short, []).append(nid)

    rewritten = 0
    for e in edges:
        if e.get("relation") != "calls":
            continue
        tgt = e.get("target", "")
        if not tgt or "::" in tgt or tgt.startswith("doc_"):
            # Already looks like a node_id — skip
            continue
        candidates = short_to_candidates.get(tgt)
        if candidates and len(candidates) == 1:
            e["target"] = candidates[0]
            rewritten += 1
        # Multiple candidates or no match: leave unchanged

    return rewritten


# Keep old name as alias for internal use
_slugify = slugify_label


# ---------------------------------------------------------------------------
# Document analysis helpers
# ---------------------------------------------------------------------------

_DOC_EXTENSIONS = {".md", ".txt", ".rst"}

# Regex patterns for markdown parsing
_HEADING_RE = re.compile(r"^(#{1,6})\s+(.+)$")
_MD_LINK_RE = re.compile(r"\[([^\]]+)\]\(([^)]+)\)")
_DECISION_RE = re.compile(
    r"^[-*]\s*\*{0,2}(决策|理由|decision|rationale|trade-?off|备选|alternative)\s*[:：]",
    re.IGNORECASE,
)


def _collect_doc_files(root: Path, modules: list[dict[str, Any]]) -> list[Path]:
    """Collect document files (.md/.txt/.rst) from ``.claude/wiki/**``.

    M5 redirect: scans ``.claude/wiki/`` (the generated wiki, which is the
    correct semantic source for the knowledge graph).  Root-level PRD/README
    and module path documents are intentionally excluded — they are either
    wiki_gen *inputs* (used and discarded) or generated duplicates.

    Index pages (e.g. ``index.md``, files named ``_index.*``) are excluded
    because they are navigation stubs, not substantive knowledge.

    The ``graph-wiki/`` subtree is **also excluded** — its ``community-N.md``
    and ``god-node-*.md`` files are graph *artifacts* (generated from this
    very extraction) and feeding them back in would pollute the concept
    space with synthetic "社区 N" nodes on every re-run.

    Args:
        root: Project root path.
        modules: Module definitions (unused — kept for API compatibility).

    Returns:
        Deduplicated, sorted list of absolute ``Path`` objects.
    """
    wiki_dir = root / ".claude" / "wiki"
    if not wiki_dir.exists():
        return []

    # Names of index/navigation pages to exclude (case-insensitive stem match)
    _INDEX_STEMS: frozenset[str] = frozenset({"index", "_index"})
    # Subdirectories under wiki/ whose contents are graph artifacts, not inputs
    _GRAPH_ARTIFACT_DIRS: frozenset[str] = frozenset({"graph-wiki"})

    all_docs: list[Path] = []
    for ext in _DOC_EXTENSIONS:
        for p in wiki_dir.rglob(f"*{ext}"):
            if p.stem.lower() in _INDEX_STEMS:
                continue
            # Skip anything under graph-wiki/ (our own prior output)
            try:
                rel_parts = p.relative_to(wiki_dir).parts
            except ValueError:
                rel_parts = ()
            if any(part in _GRAPH_ARTIFACT_DIRS for part in rel_parts[:-1]):
                continue
            all_docs.append(p)

    return sorted(set(all_docs))


def _extract_doc_nodes(
    doc_files: list[Path],
    root: Path,
) -> dict[str, Any]:
    """Extract concept nodes and reference edges from document files.

    Since graphify's ``extract()`` does not handle markdown, this function
    performs lightweight parsing directly:

    * ``# Heading`` (h1) -> node (kind="concept")
    * ``## Heading`` (h2) -> node (kind="concept", parent=h1)
    * ``[text](target.md)`` -> edge (relation="references")
    * Decision/rationale patterns -> node (kind="decision")
    * Each .md file itself -> node (kind="document")

    Returns a dict compatible with graphify's extraction format::

        {"nodes": [...], "edges": [...]}
    """
    nodes: list[dict[str, Any]] = []
    edges: list[dict[str, Any]] = []
    seen_ids: set[str] = set()

    for doc_path in doc_files:
        try:
            text = doc_path.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue

        rel_path = str(doc_path.relative_to(root)).replace("\\", "/")
        file_stem = doc_path.stem

        # Create a document-level node
        doc_id = f"doc_{_slugify(file_stem)}"
        if doc_id not in seen_ids:
            nodes.append({
                "id": doc_id,
                "label": doc_path.name,
                "source_file": rel_path,
                "source_location": "line 1",
                "confidence": "EXTRACTED",
                "kind": "document",
                "source_kind": "wiki",
            })
            seen_ids.add(doc_id)

        current_h1: str | None = None
        current_h1_id: str | None = None

        for line_num, line in enumerate(text.splitlines(), start=1):
            line_stripped = line.strip()

            # --- Heading extraction ---
            heading_match = _HEADING_RE.match(line_stripped)
            if heading_match:
                level = len(heading_match.group(1))
                heading_text = heading_match.group(2).strip()
                heading_slug = _slugify(heading_text)
                node_id = f"doc_{_slugify(file_stem)}_{heading_slug}"

                # Deduplicate
                if node_id in seen_ids:
                    # Append line number to disambiguate
                    node_id = f"{node_id}_L{line_num}"
                if node_id in seen_ids:
                    continue

                node: dict[str, Any] = {
                    "id": node_id,
                    "label": heading_text,
                    "source_file": rel_path,
                    "source_location": f"line {line_num}",
                    "confidence": "EXTRACTED",
                    "kind": "concept",
                    "source_kind": "wiki",
                }
                nodes.append(node)
                seen_ids.add(node_id)

                # Track h1 -> h2 parent relationship
                if level == 1:
                    current_h1 = heading_text
                    current_h1_id = node_id
                elif level == 2 and current_h1_id is not None:
                    edges.append({
                        "source": current_h1_id,
                        "target": node_id,
                        "relation": "contains",
                        "confidence": "EXTRACTED",
                        "weight": 0.5,
                    })

                # Link heading to its document node
                edges.append({
                    "source": doc_id,
                    "target": node_id,
                    "relation": "contains",
                    "confidence": "EXTRACTED",
                    "weight": 0.3,
                })
                continue

            # --- Decision pattern extraction ---
            if _DECISION_RE.match(line_stripped):
                # Extract the decision text after the colon
                colon_pos = max(
                    line_stripped.find(":"),
                    line_stripped.find("\uff1a"),  # fullwidth colon
                )
                decision_text = line_stripped[colon_pos + 1:].strip() if colon_pos >= 0 else line_stripped
                # Strip markdown bold markers (** or *)
                decision_text = decision_text.strip("*").strip()
                if decision_text:
                    decision_slug = _slugify(decision_text[:60])
                    decision_id = f"doc_{_slugify(file_stem)}_decision_{decision_slug}"
                    if decision_id not in seen_ids:
                        nodes.append({
                            "id": decision_id,
                            "label": decision_text,
                            "source_file": rel_path,
                            "source_location": f"line {line_num}",
                            "confidence": "EXTRACTED",
                            "kind": "decision",
                            "source_kind": "wiki",
                        })
                        seen_ids.add(decision_id)
                        # Link decision to parent heading if available
                        if current_h1_id is not None:
                            edges.append({
                                "source": current_h1_id,
                                "target": decision_id,
                                "relation": "decides",
                                "confidence": "EXTRACTED",
                                "weight": 0.5,
                            })
                continue

            # --- Link extraction ---
            for link_match in _MD_LINK_RE.finditer(line_stripped):
                link_text = link_match.group(1)
                link_target = link_match.group(2)
                # Only track internal document links (.md/.txt/.rst)
                if any(link_target.endswith(ext) for ext in _DOC_EXTENSIONS):
                    target_stem = Path(link_target).stem
                    target_doc_id = f"doc_{_slugify(target_stem)}"
                    edges.append({
                        "source": doc_id,
                        "target": target_doc_id,
                        "relation": "references",
                        "confidence": "EXTRACTED",
                        "weight": 0.3,
                    })

    return {"nodes": nodes, "edges": edges}


def _classify_node(node: dict[str, Any]) -> str | None:
    """Classify a graphify node as ``'entity'``, ``'interface'``,
    ``'design_decision'``, or None.

    Classification rules (applied in order):
    1. If the node has ``kind="concept"`` (from doc analysis),
       it is a concept -> ``'entity'``.
    2. If the node has ``kind="decision"`` (from doc analysis),
       it is a design decision -> ``'design_decision'``.
    3. If the node has ``kind="document"`` (file-level doc node),
       it is skipped -> ``None``.
    4. If the label contains parentheses (e.g. ``foo()``), it is a
       function/method -> ``'interface'``.
    5. If the label is a PascalCase identifier (no parens, no dots), it is
       likely a class/struct -> ``'entity'``.
    6. File-level hub nodes (label == filename) are skipped -> ``None``.
    7. Everything else -> ``None`` (not mapped).
    """
    label = node.get("label", "")
    if not label:
        return None

    # --- Document-origin nodes (have explicit kind) ---
    kind = node.get("kind", "")
    if kind == "concept":
        return "entity"
    if kind == "decision":
        return "design_decision"
    if kind == "document":
        return None

    # --- Code-origin nodes (inferred from label) ---

    # Skip file-level hub nodes
    source_file = node.get("source_file", "")
    if source_file:
        if label == Path(source_file).name:
            return None

    # Skip method stubs (e.g. ".auth_flow()")
    if label.startswith("."):
        return None

    # Functions / methods: label ends with "()"
    if label.endswith("()"):
        return "interface"

    # PascalCase class-like names (at least two chars, starts upper)
    if len(label) >= 2 and label[0].isupper() and not label.endswith("()"):
        return "entity"

    return None


def _confidence_to_score(confidence: str) -> float:
    """Map a graphify confidence string to a default numeric score.

    EXTRACTED -> 1.0 (directly observed in source)
    INFERRED  -> 0.5 (derived by heuristic)
    AMBIGUOUS -> 0.3 (low-confidence guess)
    """
    mapping = {
        "EXTRACTED": 1.0,
        "INFERRED": 0.5,
        "AMBIGUOUS": 0.3,
    }
    return mapping.get(confidence.upper(), 1.0)


def _node_to_entity(node: dict[str, Any]) -> dict[str, Any]:
    """Convert a graphify node classified as entity to wiki entity format.

    Provenance metadata is stored in dedicated structured fields rather than
    being embedded in the human-readable ``description`` string.
    """
    label = node.get("label", "")
    source_file = node.get("source_file", "")
    source_location = node.get("source_location", "")
    confidence = node.get("confidence", "EXTRACTED")
    confidence_score = node.get(
        "confidence_score",
        _confidence_to_score(confidence),
    )

    return {
        "name": label,
        "kind": "entity",
        "fields": [],
        "invariants": [],
        "lifecycle": "",
        "description": "",
        "provenance": confidence,
        "source_file": source_file,
        "source_location": source_location,
        "confidence_score": confidence_score,
    }


def _node_to_interface(node: dict[str, Any]) -> dict[str, Any]:
    """Convert a graphify node classified as interface to wiki interface format.

    Provenance metadata is stored in dedicated structured fields rather than
    being embedded in the human-readable ``description`` string.
    """
    label = node.get("label", "")
    # Strip trailing "()" for a cleaner name
    name = label.rstrip("()")
    source_file = node.get("source_file", "")
    source_location = node.get("source_location", "")
    confidence = node.get("confidence", "EXTRACTED")
    confidence_score = node.get(
        "confidence_score",
        _confidence_to_score(confidence),
    )

    return {
        "name": name,
        "direction": "inbound",
        "description": "",
        "params": "",
        "returns": "",
        "error_codes": [],
        "provenance": confidence,
        "source_file": source_file,
        "source_location": source_location,
        "confidence_score": confidence_score,
    }


def _node_to_design_decision(node: dict[str, Any]) -> dict[str, Any]:
    """Convert a doc-origin decision node to wiki design_decision format.

    Provenance metadata is stored in dedicated structured fields rather than
    being embedded in the human-readable ``description`` string.
    """
    label = node.get("label", "")
    source_file = node.get("source_file", "")
    source_location = node.get("source_location", "")
    confidence = node.get("confidence", "EXTRACTED")
    confidence_score = node.get(
        "confidence_score",
        _confidence_to_score(confidence),
    )

    return {
        "name": label,
        "kind": "design_decision",
        "description": "",
        "source_file": source_file,
        "source_location": source_location,
        "provenance": confidence,
        "confidence_score": confidence_score,
    }


def _assign_node_to_module(
    node: dict[str, Any],
    module_path_map: dict[str, str],
) -> str | None:
    """Determine which module a node belongs to based on its source_file.

    Returns the module slug, or ``None`` if the node cannot be matched.
    """
    source = node.get("source_file", "")
    if not source:
        return None
    # Normalise separators
    source_norm = source.replace("\\", "/")
    for path_prefix, slug in module_path_map.items():
        if source_norm.startswith(path_prefix):
            return slug
    return None


# ---------------------------------------------------------------------------
# Public API
# ---------------------------------------------------------------------------


def build_graph(
    project_root: str,
    modules: list[dict[str, Any]],
    language: str,
    include_docs: bool = True,
) -> dict[str, Any] | None:
    """Build a knowledge graph from the project source tree.

    Uses harness-native AST extraction and networkx for graph construction,
    community detection, and god-node analysis.

    Multi-language routing: automatically detects file extensions present in
    module paths and routes to the appropriate extractor from the registry.
    Python files use the stdlib ``ast`` extractor; all other languages use
    tree-sitter-based extractors (graceful skip when grammar unavailable).

    Smart wiki fallback: when no code files are found (docs-only projects),
    ``.claude/wiki/`` pages are automatically parsed so the project still
    gets god_nodes, communities, and GRAPH_REPORT.md.  When *include_docs*
    is explicitly ``True``, wiki docs are parsed in addition to code.

    Note: root-level PRD/README documents are intentionally excluded from
    the graph.  They are wiki_gen *inputs*, not knowledge-graph knowledge.

    Args:
        project_root: Absolute path to the repository root.
        modules: Module definitions from project.json (each has ``paths``).
        language: Primary language of the project (accepted for backward
            compatibility; extraction routing is auto-detected).
        include_docs: When ``True``, force-include markdown/text documents
            alongside code.  When ``False`` (default), docs are parsed only
            as fallback when no code was found.

    Returns:
        A dict with keys ``graph``, ``communities``, ``god_nodes``, and
        ``extraction``; or ``None`` when no extraction data is available.
        When networkx is not installed, returns a dict with ``graph: None``,
        ``communities: {}``, ``god_nodes: []``, and a valid ``extraction``.
    """
    from core.extractors import detect_languages, get_extractor

    import sys as _sys
    root_path = Path(project_root)

    # Pre-flight: warn when modules declare no paths (a common pitfall —
    # code extraction silently gets 0 files and the graph falls back to
    # docs-only, which surprises users who expected entity/interface nodes).
    total_paths = sum(len(m.get("paths", []) or []) for m in modules)
    if not modules:
        print(
            "[graph_bridge] WARN: build_graph called with empty `modules`; "
            "code AST extraction will be skipped and the graph will fall back "
            "to document-only extraction from .claude/wiki/. "
            "Supply --modules pointing at JSON(s) with `paths` fields.",
            file=_sys.stderr,
        )
    elif total_paths == 0:
        print(
            f"[graph_bridge] WARN: none of the {len(modules)} module(s) declare "
            f"`paths`; code AST extraction will be skipped. "
            f"Add source directories (e.g. \"paths\": [\"src/foo\"]) to each module.",
            file=_sys.stderr,
        )

    # Multi-language code extraction via extractor registry
    files_by_ext = detect_languages(root_path, modules)
    merged_nodes: list[dict[str, Any]] = []
    merged_edges: list[dict[str, Any]] = []
    seen_ids: set[str] = set()

    for ext, file_list in sorted(files_by_ext.items()):
        extractor = get_extractor(ext)
        if extractor is None:
            continue
        result = extractor.extract(sorted(set(file_list)), root_path)
        for node in result.get("nodes", []):
            nid = node.get("id", "")
            if nid and nid not in seen_ids:
                merged_nodes.append(node)
                seen_ids.add(nid)
        merged_edges.extend(result.get("edges", []))

    # If modules declared paths but extractors produced zero nodes, surface
    # the most likely cause (tree-sitter grammar install failures for
    # non-Python files).
    if total_paths > 0 and files_by_ext and not merged_nodes:
        exts = ", ".join(sorted(files_by_ext.keys()))
        print(
            f"[graph_bridge] WARN: found code files ({exts}) but all "
            f"extractors returned 0 nodes. Likely cause: tree-sitter grammars "
            f"not installed. Check stderr above for 'grammar install failed' "
            f"warnings. The graph will fall back to docs-only extraction.",
            file=_sys.stderr,
        )

    code_extraction: dict[str, Any] | None = (
        {"nodes": merged_nodes, "edges": merged_edges} if merged_nodes else None
    )

    # Post-process code edges so the graph actually forms connected components
    # (extractors emit import/call edges targeting external module names or
    # unresolved symbols; _build_graph_nx drops those as dangling).
    if code_extraction is not None:
        _resolve_code_edges(code_extraction)

    # Document extraction: forced by include_docs, OR auto-fallback when no code.
    doc_extraction: dict[str, Any] | None = None
    should_parse_docs = include_docs or code_extraction is None
    if should_parse_docs:
        doc_files = _collect_doc_files(root_path, modules)
        if doc_files:
            doc_extraction = _extract_doc_nodes(doc_files, root_path)

    # Merge
    if code_extraction and doc_extraction:
        extraction: dict[str, Any] = {
            "nodes": code_extraction.get("nodes", []) + doc_extraction.get("nodes", []),
            "edges": code_extraction.get("edges", []) + doc_extraction.get("edges", []),
        }
    elif code_extraction:
        extraction = code_extraction
    elif doc_extraction:
        extraction = doc_extraction
    else:
        return None

    nodes = extraction.get("nodes", [])
    if not nodes:
        return None

    # Cross-file calls edge target resolution: rewrite short-name targets to
    # concrete node ids where a unique match exists across the merged extraction.
    # Must run after merge so entity/interface nodes from all source files are
    # visible to the reverse index.
    _resolve_call_targets(extraction)

    # Graph + clustering + god_nodes via networkx (was graphify.*)
    try:
        graph = _build_graph_nx(extraction, directed=False)
        communities = _cluster_communities_nx(graph)
        god_nodes_data = _compute_god_nodes_nx(graph, top_n=10)
    except ImportError:
        # networkx unavailable — return extraction-only payload
        # (harness still works without network graph)
        return {
            "graph": None,
            "communities": {},
            "god_nodes": [],
            "extraction": extraction,
        }

    # Stamp module_slug onto graph nodes so downstream consumers
    # (wiki_quality._find_decoupled_modules, graph_artifacts, etc.)
    # can look up module ownership directly from node attributes.
    module_path_map: dict[str, str] = {}
    known_slugs: set[str] = set()
    for mod in modules:
        name = mod.get("name", "")
        if name:
            slug = name.lower().replace(" ", "-")
            known_slugs.add(slug)
            for rel_path in mod.get("paths", []):
                norm = rel_path.replace("\\", "/").rstrip("/") + "/"
                module_path_map[norm] = slug
    for nid in graph.nodes():
        attrs = graph.nodes[nid]
        # Primary: path-prefix match against declared module.paths
        assigned = _assign_node_to_module(attrs, module_path_map) if module_path_map else None
        # Doc-fallback: wiki module pages (`.claude/wiki/modules/{slug}.md`)
        # don't match module.paths but still belong to a module.
        if not assigned:
            sf = attrs.get("source_file", "").replace("\\", "/")
            if "/wiki/modules/" in sf:
                stem = sf.rsplit("/", 1)[-1]
                if stem.endswith(".md"):
                    stem = stem[:-3]
                if stem and (not known_slugs or stem in known_slugs):
                    assigned = stem
        if assigned:
            attrs["module_slug"] = assigned

    # Stamp community id onto each god-node dict so downstream consumers
    # (wiki_gen._build_graph_nav_section, graph-wiki templates, MCP clients)
    # can correlate god-nodes with their community without re-scanning.
    node_to_community: dict[str, int] = {}
    for cid, members in (communities or {}).items():
        for member in members:
            node_to_community[member] = cid
    for gn in god_nodes_data:
        gn_id = gn.get("id", "")
        if gn_id in node_to_community:
            gn["community"] = node_to_community[gn_id]

    return {
        "graph": graph,
        "communities": communities,
        "god_nodes": god_nodes_data,
        "extraction": extraction,
    }


def lookup_neighbors_for_paths(
    graph_data: dict[str, Any],
    paths: list[str],
) -> dict[str, dict[str, Any]]:
    """Map file paths to their graph nodes and 1-hop neighbors.

    For each *path*, find the graph node(s) whose ``source_file`` attribute
    matches (normalised to forward slashes).  For each matched node, collect
    its immediate neighbors together with their attributes.

    Args:
        graph_data: The dict returned by :func:`build_graph`.  Must have at
            least ``graph`` and ``communities`` keys.
        paths: List of file paths to look up (e.g. ``["scripts/wiki_gen.py"]``).

    Returns:
        A dict keyed by each input path.  Values are dicts with:

        * ``node_id`` — matched node ID, or ``None`` when no match.
        * ``neighbors`` — list of neighbor dicts with keys ``id``, ``label``,
          ``source_file``, and ``community``.
        * ``wiki_page`` — path to the module wiki page
          (e.g. ``.claude/wiki/modules/{slug}.md``), or ``None``.
    """
    if not graph_data or not paths:
        return {}

    graph = graph_data.get("graph")
    if graph is None:
        return {p: {"node_id": None, "neighbors": [], "wiki_page": None} for p in paths}

    communities: dict[str, list[str]] = graph_data.get("communities", {}) or {}

    # Build reverse map: node_id -> community_id
    node_to_community: dict[str, str] = {}
    for comm_id, members in communities.items():
        for nid in members:
            node_to_community[nid] = str(comm_id)

    # Build reverse map: normalised source_file -> list of node_ids
    source_to_nodes: dict[str, list[str]] = {}
    for nid in graph.nodes:
        sf = graph.nodes[nid].get("source_file", "")
        if sf:
            sf_norm = sf.replace("\\", "/")
            source_to_nodes.setdefault(sf_norm, []).append(nid)

    result: dict[str, dict[str, Any]] = {}
    for path in paths:
        path_norm = path.replace("\\", "/")
        matched_nodes = source_to_nodes.get(path_norm, [])
        if not matched_nodes:
            result[path] = {"node_id": None, "neighbors": [], "wiki_page": None}
            continue

        # Use the first matched node (deterministic: source_to_nodes built
        # in graph.nodes iteration order).
        node_id = matched_nodes[0]
        node_attrs = graph.nodes[node_id]

        # Derive wiki_page from source_file via module path matching
        wiki_page: str | None = None
        sf = node_attrs.get("source_file", "")
        if sf:
            slug = node_attrs.get("module_slug")
            if not slug:
                # Fall back to _assign_node_to_module style path matching
                sf_norm = sf.replace("\\", "/")
                # Try common module prefixes
                for prefix in ("scripts/core/", "scripts/"):
                    if sf_norm.startswith(prefix):
                        remainder = sf_norm[len(prefix):]
                        # Module slug is the first path segment or filename stem
                        slug = remainder.split("/")[0].replace(".py", "")
                        break
            if slug:
                wiki_page = f".claude/wiki/modules/{slug}.md"

        # Collect 1-hop neighbors
        neighbors: list[dict[str, Any]] = []
        for neighbor_id in graph.neighbors(node_id):
            n_attrs = graph.nodes[neighbor_id]
            neighbors.append({
                "id": neighbor_id,
                "label": n_attrs.get("label", ""),
                "source_file": n_attrs.get("source_file", ""),
                "community": node_to_community.get(neighbor_id, ""),
            })

        result[path] = {
            "node_id": node_id,
            "neighbors": neighbors,
            "wiki_page": wiki_page,
        }

    return result


# ---------------------------------------------------------------------------
# T8-4 task-context query helpers
#
# These helpers power the M8 context-injection pipeline (T8-5): they convert
# a graph_data payload plus a list of task write_paths into bite-sized
# structures that fit inside a task file header without LLM or network.
#
# All three are pure functions with graceful degradation:
#   * ``graph_data`` may be ``None`` or missing the ``graph`` key,
#   * ``paths`` / ``modules`` / ``module_slugs`` may be empty,
#   * no exception is ever raised for malformed but well-typed input.
# ---------------------------------------------------------------------------

# Output size caps (per declaration in T8-4 acceptance criteria).
_NEIGHBOR_INTERFACE_CAP: int = 10
_BLAST_RADIUS_CAP: int = 15
_DECISIONS_CAP: int = 5

# Edge relations that count as "upstream reference" for blast-radius purposes.
_BLAST_UPSTREAM_RELATIONS: frozenset[str] = frozenset({"imports", "calls"})


def neighbor_interfaces_for_paths(
    graph_data: dict[str, Any] | None,
    paths: list[str] | None,
) -> dict[str, list[dict[str, Any]]]:
    """Return adjacent interface/entity nodes for each path.

    Builds on :func:`lookup_neighbors_for_paths` (added in M6 T6-6): it
    fetches 1-hop neighbors first, then keeps only nodes whose ``kind`` is
    ``entity`` or ``interface``.  Results are capped at
    :data:`_NEIGHBOR_INTERFACE_CAP` (10) per path, ordered by graph
    degree descending so the most-connected symbols surface first.

    Args:
        graph_data: Dict produced by :func:`build_graph`, or ``None``.
        paths: File paths (write_paths from a task file), or empty / ``None``.

    Returns:
        ``{path: [{id, label, source_file, source_location, kind}, ...]}``.
        Empty dict when ``graph_data`` is falsy or ``paths`` is empty.
        A path that matches no node maps to ``[]``.
    """
    if not graph_data or not paths:
        return {}

    graph = graph_data.get("graph")
    if graph is None:
        return {p: [] for p in paths}

    # Delegate the node-match + 1-hop traversal to the existing helper so
    # path-normalisation logic stays in one place.
    base = lookup_neighbors_for_paths(graph_data, paths)

    result: dict[str, list[dict[str, Any]]] = {}
    for path in paths:
        entry = base.get(path) or {}
        neighbor_ids = [n.get("id", "") for n in entry.get("neighbors", []) if n.get("id")]

        filtered: list[tuple[dict[str, Any], int]] = []
        for nid in neighbor_ids:
            attrs = graph.nodes[nid] if nid in graph.nodes else {}
            kind = attrs.get("kind", "")
            if kind not in ("entity", "interface"):
                continue
            filtered.append(
                (
                    {
                        "id": nid,
                        "label": attrs.get("label", ""),
                        "source_file": attrs.get("source_file", ""),
                        "source_location": attrs.get("source_location", ""),
                        "kind": kind,
                    },
                    graph.degree(nid),
                )
            )

        # Sort by degree desc, then id asc for determinism.
        filtered.sort(key=lambda pair: (-pair[1], pair[0]["id"]))
        result[path] = [item for item, _deg in filtered[:_NEIGHBOR_INTERFACE_CAP]]

    return result


def blast_radius_for_paths(
    graph_data: dict[str, Any] | None,
    paths: list[str] | None,
) -> dict[str, list[str]]:
    """Return upstream source_files that import/call each path's nodes.

    Walks the **directed** extraction edges (not the undirected nx graph,
    which loses source/target orientation) and collects every distinct
    source_file whose node points at a node residing in *path* via an
    ``imports`` or ``calls`` edge.

    Args:
        graph_data: Dict produced by :func:`build_graph`, or ``None``.
        paths: File paths to compute blast radius for, or empty / ``None``.

    Returns:
        ``{path: [upstream_source_file, ...]}``.  Duplicates removed.  Self
        references (upstream == path) filtered out.  Each list capped at
        :data:`_BLAST_RADIUS_CAP` (15).  Empty dict when inputs are falsy.
    """
    if not graph_data or not paths:
        return {}

    extraction: dict[str, Any] = graph_data.get("extraction") or {}
    nodes: list[dict[str, Any]] = extraction.get("nodes", []) or []
    edges: list[dict[str, Any]] = extraction.get("edges", []) or []

    if not nodes or not edges:
        return {p: [] for p in paths}

    # Map node_id -> source_file (normalised) for fast lookup.
    id_to_source: dict[str, str] = {}
    for n in nodes:
        nid = n.get("id", "")
        sf = n.get("source_file", "") or ""
        if nid:
            id_to_source[nid] = sf.replace("\\", "/")

    # Map normalised source_file -> set of node_ids residing in it.
    file_to_ids: dict[str, set[str]] = {}
    for nid, sf in id_to_source.items():
        if sf:
            file_to_ids.setdefault(sf, set()).add(nid)

    result: dict[str, list[str]] = {}
    for path in paths:
        path_norm = path.replace("\\", "/")
        target_ids = file_to_ids.get(path_norm, set())
        if not target_ids:
            result[path] = []
            continue

        upstream_files: list[str] = []
        seen: set[str] = set()
        for edge in edges:
            relation = edge.get("relation", "")
            if relation not in _BLAST_UPSTREAM_RELATIONS:
                continue
            tgt = edge.get("target", "")
            if tgt not in target_ids:
                continue
            src = edge.get("source", "")
            src_file = id_to_source.get(src, "")
            if not src_file or src_file == path_norm:
                continue
            if src_file in seen:
                continue
            seen.add(src_file)
            upstream_files.append(src_file)
            if len(upstream_files) >= _BLAST_RADIUS_CAP:
                break

        result[path] = upstream_files

    return result


def decisions_for_modules(
    modules: list[dict[str, Any]] | None,
    module_slugs: list[str] | None,
) -> dict[str, list[dict[str, Any]]]:
    """Return ``design_decisions`` entries per module slug.

    Slug matching uses :func:`slugify_label` on each module's ``name`` field
    so callers can pass plain slugs like ``"core"`` even when the module
    name in JSON is ``"Core Library"``.

    Args:
        modules: Module dicts (each may carry a ``design_decisions`` list),
            or ``None``.
        module_slugs: Requested slugs, or ``None``.

    Returns:
        ``{slug: [{decision, rationale, alternatives?}, ...]}``.  Each list
        capped at :data:`_DECISIONS_CAP` (5) and preserves source order.
        An unknown slug maps to ``[]``.  Empty dict when either input is
        falsy.
    """
    if not modules or not module_slugs:
        return {}

    # Build slug -> decisions list map once.
    by_slug: dict[str, list[dict[str, Any]]] = {}
    for mod in modules:
        name = mod.get("name", "")
        if not name:
            continue
        slug = slugify_label(name)
        decisions = mod.get("design_decisions", []) or []
        # Defensive copy each decision so downstream mutation cannot leak
        # back into the caller-supplied modules list.
        sanitised: list[dict[str, Any]] = []
        for d in decisions:
            if isinstance(d, dict):
                sanitised.append(dict(d))
        by_slug[slug] = sanitised

    result: dict[str, list[dict[str, Any]]] = {}
    for slug in module_slugs:
        result[slug] = by_slug.get(slug, [])[:_DECISIONS_CAP]
    return result


def build_report(graph_data: dict[str, Any]) -> str:
    """Build a markdown report from graph data produced by :func:`build_graph`.

    The report contains three sections:

    * **God Nodes** — top-10 high-connectivity nodes, sorted by degree descending.
    * **Surprising Connections** — top-5 edges scored by cross-community bonus,
      INFERRED confidence weight, and cross-file-type bonus (code↔doc scores
      higher than code↔code).  Each entry includes a "why surprising" phrase.
    * **Suggested Questions** — 4-5 questions the graph is well-positioned to
      answer, derived from the god nodes and community structure.

    Args:
        graph_data: The dict returned by :func:`build_graph`.

    Returns:
        A markdown string.  When *graph_data* is empty or has no useful data
        a minimal placeholder report is returned.
    """
    if not graph_data:
        return _build_empty_report()

    god_nodes: list[dict[str, Any]] = graph_data.get("god_nodes", []) or []
    communities: dict[str, Any] = graph_data.get("communities", {}) or {}
    extraction: dict[str, Any] = graph_data.get("extraction", {}) or {}
    nodes: list[dict[str, Any]] = extraction.get("nodes", []) or []
    edges: list[dict[str, Any]] = extraction.get("edges", []) or []

    # --- Section 1: God Nodes ---
    god_section = _build_god_nodes_section(god_nodes, nodes)

    # --- Section 2: Surprising Connections ---
    surprising_section = _build_surprising_connections_section(edges, nodes, communities)

    # --- Section 3: Suggested Questions ---
    questions_section = _build_suggested_questions_section(god_nodes, communities, nodes)

    return (
        "## God Nodes\n\n"
        + god_section
        + "\n\n## Surprising Connections\n\n"
        + surprising_section
        + "\n\n## Suggested Questions\n\n"
        + questions_section
    )


def _build_empty_report() -> str:
    """Return a placeholder report when no graph data is available."""
    return (
        "## God Nodes\n\n"
        "_No graph data available._\n\n"
        "## Surprising Connections\n\n"
        "_No graph data available._\n\n"
        "## Suggested Questions\n\n"
        "_No graph data available._"
    )


def _get_node_degree(node_id: str, edges: list[dict[str, Any]]) -> int:
    """Count edges incident to node_id."""
    return sum(
        1 for e in edges
        if e.get("source") == node_id or e.get("target") == node_id
    )


def _build_god_nodes_section(
    god_nodes: list[dict[str, Any]],
    nodes: list[dict[str, Any]],
) -> str:
    """Render the God Nodes section.

    god_nodes from graphify already contains degree info when available.
    Sort by degree descending, take top 10.
    """
    if not god_nodes:
        return "_No high-connectivity nodes detected._"

    # Build node lookup for metadata
    node_lookup: dict[str, dict[str, Any]] = {n.get("id", ""): n for n in nodes}

    # Enrich god_nodes with metadata from extraction when missing
    enriched: list[dict[str, Any]] = []
    for gn in god_nodes:
        node_id = gn.get("id", "")
        label = gn.get("label", node_id)
        degree = gn.get("degree", 0)
        # Try to get source info from extraction
        meta = node_lookup.get(node_id, {})
        source_file = meta.get("source_file", gn.get("source_file", "unknown"))
        source_location = meta.get("source_location", gn.get("source_location", ""))
        confidence = meta.get("confidence", gn.get("confidence", "EXTRACTED"))

        source_ref = source_file
        if source_location:
            source_ref = f"{source_file}:{source_location}"

        # Plain-English description
        description = (
            f"Highly connected node with degree {degree}; "
            f"acts as a hub linking multiple modules or concepts."
        )

        enriched.append({
            "label": label,
            "degree": degree,
            "source_ref": source_ref,
            "description": description,
        })

    # Sort by degree descending, top-10
    enriched.sort(key=lambda x: x["degree"], reverse=True)
    top = enriched[:10]

    lines: list[str] = []
    for i, item in enumerate(top, start=1):
        lines.append(
            f"{i}. **{item['label']}** (degree: {item['degree']}) "
            f"— `{item['source_ref']}` — {item['description']}"
        )
    return "\n".join(lines)


def _classify_file_type(source_file: str) -> str:
    """Return 'doc' for documentation files, 'code' for source files."""
    ext = Path(source_file).suffix.lower() if source_file else ""
    if ext in {".md", ".txt", ".rst"}:
        return "doc"
    return "code"


def _build_surprising_connections_section(
    edges: list[dict[str, Any]],
    nodes: list[dict[str, Any]],
    communities: dict[str, Any],
) -> str:
    """Render the Surprising Connections section.

    Scoring heuristic (higher = more surprising):
    - +2.0  if edge crosses community boundary
    - +1.5  if edge has INFERRED confidence
    - +1.0  if edge is code↔doc (cross-file-type)
    - +0.5  if edge weight > 0.8 (strong signal despite surprise)
    """
    if not edges:
        return "_No edges found in the graph._"

    # Build node -> community mapping
    node_community: dict[str, Any] = {}
    for comm_id, members in communities.items():
        for member in (members if isinstance(members, list) else []):
            node_community[member] = comm_id

    # Build node lookup
    node_lookup: dict[str, dict[str, Any]] = {n.get("id", ""): n for n in nodes}

    scored: list[dict[str, Any]] = []
    for edge in edges:
        src_id = edge.get("source", "")
        tgt_id = edge.get("target", "")
        if not src_id or not tgt_id:
            continue

        src_node = node_lookup.get(src_id, {})
        tgt_node = node_lookup.get(tgt_id, {})
        src_label = src_node.get("label", src_id)
        tgt_label = tgt_node.get("label", tgt_id)
        src_file = src_node.get("source_file", "")
        tgt_file = tgt_node.get("source_file", "")
        confidence = edge.get("confidence", "EXTRACTED")
        weight = float(edge.get("weight", 0.5))
        relation = edge.get("relation", "related")

        score = 0.0
        reasons: list[str] = []

        # Cross-community bonus
        src_comm = node_community.get(src_id)
        tgt_comm = node_community.get(tgt_id)
        if src_comm is not None and tgt_comm is not None and src_comm != tgt_comm:
            score += 2.0
            reasons.append("spans separate communities")

        # INFERRED confidence bonus
        if confidence == "INFERRED":
            score += 1.5
            reasons.append("INFERRED (not directly declared)")

        # Cross-file-type bonus
        src_type = _classify_file_type(src_file)
        tgt_type = _classify_file_type(tgt_file)
        if src_type != tgt_type:
            score += 1.0
            reasons.append(f"{src_type}↔{tgt_type} cross-type link")

        # Strong-weight bonus
        if weight > 0.8:
            score += 0.5
            reasons.append("high edge weight")

        if score <= 0.0:
            continue  # Not surprising

        why = "; ".join(reasons) if reasons else "unusual structural position"

        scored.append({
            "src_label": src_label,
            "tgt_label": tgt_label,
            "relation": relation,
            "score": score,
            "why": why,
        })

    if not scored:
        # Fall back: show top edges by weight even if no cross-community signal
        for edge in edges[:5]:
            src_id = edge.get("source", "")
            tgt_id = edge.get("target", "")
            src_node = node_lookup.get(src_id, {})
            tgt_node = node_lookup.get(tgt_id, {})
            scored.append({
                "src_label": src_node.get("label", src_id),
                "tgt_label": tgt_node.get("label", tgt_id),
                "relation": edge.get("relation", "related"),
                "score": float(edge.get("weight", 0.0)),
                "why": "notable structural relationship",
            })

    scored.sort(key=lambda x: x["score"], reverse=True)
    top5 = scored[:5]

    lines: list[str] = []
    for i, item in enumerate(top5, start=1):
        lines.append(
            f"{i}. **{item['src_label']}** → **{item['tgt_label']}** "
            f"({item['relation']}) "
            f"— _Why surprising: {item['why']}_"
        )
    return "\n".join(lines)


def _build_suggested_questions_section(
    god_nodes: list[dict[str, Any]],
    communities: dict[str, Any],
    nodes: list[dict[str, Any]],
) -> str:
    """Render the Suggested Questions section.

    Generates 4-5 questions tailored to the graph's structure.
    """
    questions: list[str] = []

    # Q1: Top god node
    if god_nodes:
        top_node = max(god_nodes, key=lambda n: n.get("degree", 0))
        label = top_node.get("label", "the central hub")
        questions.append(
            f"What other modules or concepts depend on **{label}**, "
            f"and what breaks if it changes?"
        )

    # Q2: Community structure
    community_count = len(communities)
    if community_count >= 2:
        questions.append(
            f"The graph has {community_count} communities — "
            f"what are the integration points between them, "
            f"and which edges cross community boundaries?"
        )
    else:
        questions.append(
            "What are the primary integration points across modules in this project?"
        )

    # Q3: INFERRED edges (implicit dependencies)
    node_lookup: dict[str, dict[str, Any]] = {n.get("id", ""): n for n in nodes}
    questions.append(
        "Which implicit (INFERRED) dependencies in the graph are most likely "
        "to cause unexpected failures during refactoring?"
    )

    # Q4: Doc↔code bridges
    questions.append(
        "Which documented concepts (from .md files) have the strongest "
        "structural ties to source code, and are they aligned with the implementation?"
    )

    # Q5: Second god node diversity (if available)
    if len(god_nodes) >= 2:
        labels = [gn.get("label", "") for gn in god_nodes[:3]]
        labels_str = ", ".join(f"**{l}**" for l in labels if l)
        questions.append(
            f"Among the top god nodes ({labels_str}), which one has the most "
            f"cross-module reach and should be prioritised for documentation coverage?"
        )
    else:
        questions.append(
            "Which module is most underrepresented in the graph, "
            "and what additional documentation would improve graph coverage?"
        )

    # Return 4-5 questions as a numbered list
    lines = [f"{i}. {q}" for i, q in enumerate(questions[:5], start=1)]
    return "\n".join(lines)


# Label-to-module matching ----------------------------------------------------
# Generic tokens that would match too many things; excluded from heuristic match.
_LABEL_MATCH_STOPWORDS: frozenset[str] = frozenset({
    # English
    "the", "and", "for", "with", "of", "to", "in", "on", "is", "a", "an",
    "module", "system", "layer", "service", "component", "core", "app",
    # Chinese (2+ char vague nouns / suffixes)
    "管理", "系统", "模块", "服务", "核心", "应用", "功能", "页面", "界面",
    "设计", "实现", "结构", "配置", "工具",
})


def _extract_match_tokens(text: str) -> set[str]:
    """Extract candidate match tokens from a summary/description string.

    Keeps alphanumeric/underscore runs (latin) and contiguous CJK sequences
    of length >= 2. Strips STOPWORDS.  Case-insensitive (lowercased).
    """
    if not text:
        return set()
    tokens: set[str] = set()
    # Latin/identifier tokens (length >= 3 to avoid "is"/"of" noise beyond stopwords)
    for tok in re.findall(r"[A-Za-z0-9_]+", text):
        tok_lower = tok.lower()
        if len(tok_lower) >= 3 and tok_lower not in _LABEL_MATCH_STOPWORDS:
            tokens.add(tok_lower)
    # CJK contiguous sequences (length >= 2)
    for tok in re.findall(r"[\u4e00-\u9fff]{2,}", text):
        if tok not in _LABEL_MATCH_STOPWORDS:
            tokens.add(tok)
    return tokens


def _match_module_by_label(
    label: str,
    modules: list[dict[str, Any]],
) -> str | None:
    """Return the slug of the module whose name/keywords/summary best matches label.

    Heuristic:
    - Each module contributes match tokens from its name, slug, explicit
      ``keywords`` list (optional, from project.json), and summary.
    - A token "hits" if it appears as a case-insensitive substring of label.
    - When multiple modules hit, the one with the longest matched token wins
      (so ``"@dnd-kit"`` beats generic ``"dnd"``).  Ties broken by declaration
      order (first wins).

    Returns the module slug, or ``None`` when nothing hits.
    """
    if not label or not modules:
        return None
    label_lower = label.lower()
    best_slug: str | None = None
    best_strength = 0
    for mod in modules:
        name = mod.get("name", "")
        slug = _slugify(name)
        tokens: set[str] = set()
        if name:
            tokens.add(name.lower())
        if slug:
            tokens.add(slug)
        for kw in mod.get("keywords", []) or []:
            if isinstance(kw, str) and kw:
                tokens.add(kw.lower())
        tokens.update(_extract_match_tokens(mod.get("summary", "")))
        tokens -= _LABEL_MATCH_STOPWORDS
        for tok in tokens:
            if tok and tok in label_lower and len(tok) > best_strength:
                best_strength = len(tok)
                best_slug = slug
    return best_slug


def _build_parent_map(
    edges: list[dict[str, Any]],
    id_to_kind: dict[str, str],
) -> dict[str, list[str]]:
    """Build child_id → [parent_id] from ``contains`` edges.

    Document hub nodes (``kind=="document"``) are excluded as parents because
    their semantic signal is too weak for propagation (every heading is
    contained under its document file).
    """
    parent_map: dict[str, list[str]] = {}
    for edge in edges:
        if edge.get("relation") != "contains":
            continue
        src = edge.get("source", "")
        tgt = edge.get("target", "")
        if not src or not tgt or src == tgt:
            continue
        if id_to_kind.get(src) == "document":
            continue
        parent_map.setdefault(tgt, []).append(src)
    return parent_map


def _propagate_module_via_parents(
    parent_map: dict[str, list[str]],
    module_map: dict[str, str | None],
    max_iters: int = 10,
) -> None:
    """Fill unassigned node→module entries by inheriting from assigned parents.

    Iterates to fixpoint (no change in a full pass) or ``max_iters`` exhausted.
    Mutates ``module_map`` in place.
    """
    for _ in range(max_iters):
        changed = False
        for child, parents in parent_map.items():
            if module_map.get(child) is not None:
                continue
            for parent in parents:
                parent_slug = module_map.get(parent)
                if parent_slug is not None:
                    module_map[child] = parent_slug
                    changed = True
                    break
        if not changed:
            break


def map_to_wiki_structures(
    graph_data: dict[str, Any],
    modules: list[dict[str, Any]],
) -> dict[str, dict[str, Any]]:
    """Map graphify graph data to wiki-compatible structures.

    Args:
        graph_data: The dict returned by :func:`build_graph`.
            Must contain ``extraction`` with ``nodes`` and ``edges``.
        modules: Module definitions (each must have ``name`` and ``paths``).

    Returns:
        ``{module_slug: {"entities": [...], "interfaces": [...],
        "dependency_edges": [...], "design_decisions": [...]}}``

        When *graph_data* is ``None`` or empty the function returns ``{}``.
    """
    if not graph_data:
        return {}

    extraction = graph_data.get("extraction", {})
    nodes = extraction.get("nodes", [])
    edges = extraction.get("edges", [])

    if not nodes:
        return {}

    # Build module-path -> slug lookup (normalised forward slashes)
    module_path_map: dict[str, str] = {}
    for mod in modules:
        slug = _slugify(mod["name"])
        for rel_path in mod.get("paths", []):
            normalised = rel_path.replace("\\", "/").rstrip("/") + "/"
            module_path_map[normalised] = slug

    # Also map without trailing slash for exact file matches
    module_path_map_no_slash: dict[str, str] = {}
    for mod in modules:
        slug = _slugify(mod["name"])
        for rel_path in mod.get("paths", []):
            normalised = rel_path.replace("\\", "/").rstrip("/")
            module_path_map_no_slash[normalised] = slug

    def _find_module(source_file: str) -> str | None:
        source_norm = source_file.replace("\\", "/")
        # Try prefix match with trailing slash first
        for prefix, slug in module_path_map.items():
            if source_norm.startswith(prefix):
                return slug
        # Then try exact prefix without trailing slash
        for prefix, slug in module_path_map_no_slash.items():
            if source_norm == prefix or source_norm.startswith(prefix + "/"):
                return slug
        return None

    # Initialise result dict with empty structures per module
    result: dict[str, dict[str, Any]] = {}
    for mod in modules:
        slug = _slugify(mod["name"])
        result[slug] = {
            "entities": [],
            "interfaces": [],
            "dependency_edges": [],
            "design_decisions": [],
        }

    # For doc-origin nodes without any match, assign to the first module as
    # a last-resort fallback (root-level docs without keyword hits).
    first_slug = _slugify(modules[0]["name"]) if modules else None

    # --- Pass 1: self-match (path → label) for every classified node ---
    classified: list[tuple[dict[str, Any], str]] = []  # (node, kind)
    module_map: dict[str, str | None] = {}
    for node in nodes:
        kind = _classify_node(node)
        if kind is None:
            continue
        nid = node.get("id", "")
        if not nid:
            continue
        classified.append((node, kind))
        slug = _find_module(node.get("source_file", ""))
        if slug is None and node.get("kind") in ("concept", "decision", "document"):
            slug = _match_module_by_label(node.get("label", ""), modules)
        module_map[nid] = slug  # may be None here; propagation may fill it

    # --- Pass 2: propagate via contains-edge parents (fixpoint) ---
    id_to_kind = {n.get("id", ""): n.get("kind", "") for n in nodes}
    parent_map = _build_parent_map(edges, id_to_kind)
    _propagate_module_via_parents(parent_map, module_map)

    # --- Pass 3: first_slug fallback + emit ---
    node_module_map: dict[str, str] = {}  # node_id -> module_slug (final)
    for node, kind in classified:
        nid = node.get("id", "")
        slug = module_map.get(nid)
        if slug is None and node.get("kind") in ("concept", "decision", "document"):
            slug = first_slug
        if slug is None or slug not in result:
            continue
        node_module_map[nid] = slug
        if kind == "entity":
            result[slug]["entities"].append(_node_to_entity(node))
        elif kind == "interface":
            result[slug]["interfaces"].append(_node_to_interface(node))
        elif kind == "design_decision":
            result[slug]["design_decisions"].append(_node_to_design_decision(node))

    # Extract cross-module dependency edges
    for edge in edges:
        source_id = edge.get("source", "")
        target_id = edge.get("target", "")
        source_slug = node_module_map.get(source_id)
        target_slug = node_module_map.get(target_id)
        if (
            source_slug is not None
            and target_slug is not None
            and source_slug != target_slug
        ):
            dep_edge = {
                "from_module": source_slug,
                "to_module": target_slug,
                "relation": edge.get("relation", "related"),
                "confidence": edge.get("confidence", "EXTRACTED"),
            }
            result[source_slug]["dependency_edges"].append(dep_edge)

    return result
