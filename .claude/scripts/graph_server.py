#!/usr/bin/env python3
"""Minimal stdio MCP server backing the harness knowledge graph.

Replaces ``python -m graphify.serve`` with a harness-native implementation
that only depends on stdlib + networkx (already required elsewhere).

Tools exposed:
- ``query_graph(keyword, limit=10)`` — substring match across label/description
- ``get_node(node_id)`` — full attrs + one-hop neighbors
- ``get_neighbors(node_id, depth=1, limit=50)`` — BFS neighbors up to depth
- ``shortest_path(from_id, to_id)`` — node-id path via nx.shortest_path

Protocol: JSON-RPC 2.0 newline-delimited over stdio (the minimal subset
that Claude Code uses to drive MCP stdio servers).
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path
from typing import Any

SCRIPTS_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPTS_DIR))

from core.graph_bridge import _graph_from_json_data  # noqa: E402


# ---------------------------------------------------------------------------
# Tool handlers (pure functions — unit-testable without stdio)
# ---------------------------------------------------------------------------


def tool_query_graph(graph, keyword: str, limit: int = 10) -> list[dict[str, Any]]:
    """Substring match against node label and description, score-sorted."""
    if graph is None or not keyword:
        return []
    kw = keyword.lower()
    hits: list[tuple[int, str, dict]] = []
    for nid, attrs in graph.nodes(data=True):
        label = str(attrs.get("label", "")).lower()
        desc = str(attrs.get("description", "")).lower()
        score = 0
        if kw in label:
            score += 2
        if kw in desc:
            score += 1
        if score > 0:
            hits.append((score, nid, attrs))
    # Sort by -score then id for deterministic tie-break
    hits.sort(key=lambda item: (-item[0], item[1]))
    out: list[dict[str, Any]] = []
    for score, nid, attrs in hits[:limit]:
        out.append({
            "id": nid,
            "label": attrs.get("label", nid),
            "kind": attrs.get("kind", ""),
            "source_file": attrs.get("source_file", ""),
            "source_location": attrs.get("source_location", ""),
            "score": score,
        })
    return out


def tool_get_node(graph, node_id: str) -> dict[str, Any]:
    """Full node attrs + one-hop neighbors."""
    if graph is None or node_id not in graph:
        return {"error": "node not found"}
    attrs = dict(graph.nodes[node_id])
    attrs["id"] = node_id
    is_directed = getattr(graph, "is_directed", lambda: False)()
    neighbors: list[dict[str, Any]] = []
    if is_directed:
        for succ in graph.successors(node_id):
            edge_attrs = graph.edges[node_id, succ]
            neighbors.append({
                "id": succ,
                "label": graph.nodes[succ].get("label", succ),
                "relation": edge_attrs.get("relation", ""),
                "direction": "out",
            })
        for pred in graph.predecessors(node_id):
            edge_attrs = graph.edges[pred, node_id]
            neighbors.append({
                "id": pred,
                "label": graph.nodes[pred].get("label", pred),
                "relation": edge_attrs.get("relation", ""),
                "direction": "in",
            })
    else:
        for neighbor in graph.neighbors(node_id):
            edge_attrs = graph.edges[node_id, neighbor]
            neighbors.append({
                "id": neighbor,
                "label": graph.nodes[neighbor].get("label", neighbor),
                "relation": edge_attrs.get("relation", ""),
                "direction": "undirected",
            })
    return {"node": attrs, "neighbors": neighbors}


def tool_get_neighbors(
    graph, node_id: str, depth: int = 1, limit: int = 50,
) -> dict[str, Any]:
    """BFS neighbors up to given depth."""
    if graph is None or node_id not in graph:
        return {"error": "node not found"}
    import networkx as nx
    distances = nx.single_source_shortest_path_length(graph, node_id, cutoff=depth)
    items: list[tuple[int, str, dict]] = []
    for nid, dist in distances.items():
        if nid == node_id:
            continue
        items.append((dist, nid, graph.nodes[nid]))
    items.sort(key=lambda x: (x[0], x[1]))
    out = [
        {"id": nid, "label": attrs.get("label", nid), "distance": dist}
        for dist, nid, attrs in items[:limit]
    ]
    return {"neighbors": out}


def tool_shortest_path(graph, from_id: str, to_id: str) -> dict[str, Any]:
    """Shortest path between two nodes."""
    if graph is None:
        return {"error": "graph unavailable"}
    if from_id not in graph or to_id not in graph:
        return {"error": "node not found"}
    import networkx as nx
    try:
        path = nx.shortest_path(graph, from_id, to_id)
    except nx.NetworkXNoPath:
        return {"error": "no path"}
    except nx.NodeNotFound:
        return {"error": "node not found"}
    length = len(path) - 1
    truncated = False
    if len(path) > 10:
        path = path[:10]
        truncated = True
    return {"path": path, "length": length, "truncated": truncated}


# ---------------------------------------------------------------------------
# JSON-RPC dispatcher
# ---------------------------------------------------------------------------


_TOOL_DEFINITIONS = [
    {
        "name": "query_graph",
        "description": "Substring search over node labels and descriptions.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "keyword": {"type": "string"},
                "limit": {"type": "integer", "default": 10},
            },
            "required": ["keyword"],
        },
    },
    {
        "name": "get_node",
        "description": "Return full attributes for a node plus its one-hop neighbors.",
        "inputSchema": {
            "type": "object",
            "properties": {"node_id": {"type": "string"}},
            "required": ["node_id"],
        },
    },
    {
        "name": "get_neighbors",
        "description": "BFS neighbors up to a given depth, capped at limit.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "node_id": {"type": "string"},
                "depth": {"type": "integer", "default": 1},
                "limit": {"type": "integer", "default": 50},
            },
            "required": ["node_id"],
        },
    },
    {
        "name": "shortest_path",
        "description": "Shortest path between two nodes (truncated at 10).",
        "inputSchema": {
            "type": "object",
            "properties": {
                "from_id": {"type": "string"},
                "to_id": {"type": "string"},
            },
            "required": ["from_id", "to_id"],
        },
    },
]

_SERVER_INFO = {
    "protocolVersion": "2024-11-05",
    "capabilities": {"tools": {}},
    "serverInfo": {"name": "harness-graph-server", "version": "0.1.0"},
}


def _call_tool(name: str, arguments: dict, graph) -> tuple[Any, bool]:
    """Dispatch a tool call, return (result, is_error)."""
    if name == "query_graph":
        result = tool_query_graph(
            graph, arguments.get("keyword", ""), int(arguments.get("limit", 10)),
        )
        return result, False
    if name == "get_node":
        result = tool_get_node(graph, arguments.get("node_id", ""))
        return result, "error" in result
    if name == "get_neighbors":
        result = tool_get_neighbors(
            graph,
            arguments.get("node_id", ""),
            int(arguments.get("depth", 1)),
            int(arguments.get("limit", 50)),
        )
        return result, "error" in result
    if name == "shortest_path":
        result = tool_shortest_path(
            graph, arguments.get("from_id", ""), arguments.get("to_id", ""),
        )
        return result, "error" in result
    return {"error": f"unknown tool: {name}"}, True


def _handle_request(req: dict, graph) -> dict | None:
    """Dispatch a single JSON-RPC request; return response dict or None for notifications."""
    req_id = req.get("id")
    method = req.get("method", "")

    if req_id is None and not method.startswith("notifications/"):
        # Missing id and not a notification — treat as malformed, return error with null id
        req_id = None

    if method == "initialize":
        return {"jsonrpc": "2.0", "id": req_id, "result": _SERVER_INFO}
    if method == "tools/list":
        return {"jsonrpc": "2.0", "id": req_id, "result": {"tools": _TOOL_DEFINITIONS}}
    if method == "tools/call":
        params = req.get("params", {}) or {}
        name = params.get("name", "")
        arguments = params.get("arguments", {}) or {}
        result, is_error = _call_tool(name, arguments, graph)
        payload = {
            "content": [{"type": "text", "text": json.dumps(result, ensure_ascii=False)}],
            "isError": is_error,
        }
        return {"jsonrpc": "2.0", "id": req_id, "result": payload}
    if method.startswith("notifications/"):
        return None
    return {
        "jsonrpc": "2.0",
        "id": req_id,
        "error": {"code": -32601, "message": f"Method not found: {method}"},
    }


# ---------------------------------------------------------------------------
# Graph loader
# ---------------------------------------------------------------------------


def _resolve_graph_path(graph_json_path: str) -> Path:
    """Resolve the graph.json path without depending on the working directory.

    Claude Code injects ``CLAUDE_PROJECT_DIR`` into the MCP server's
    environment (the documented way for stdio servers to find project files),
    so when a relative path does not resolve against the current cwd we anchor
    it to the project root instead.
    """
    p = Path(graph_json_path)
    if p.exists() or p.is_absolute():
        return p
    project_dir = os.environ.get("CLAUDE_PROJECT_DIR")
    if project_dir:
        candidate = Path(project_dir) / graph_json_path
        if candidate.exists():
            return candidate
    return p


def _load_from_file(path: Path) -> dict | None:
    """Load graph_data dict from an explicit graph.json path."""
    if not path.exists():
        return None
    try:
        raw = path.read_text(encoding="utf-8")
        data = json.loads(raw)
    except (OSError, json.JSONDecodeError):
        return None
    return _graph_from_json_data(data)


# ---------------------------------------------------------------------------
# Main loop
# ---------------------------------------------------------------------------


def main(graph_json_path: str = ".claude/wiki/graph.json") -> int:
    """Stdio main loop. Returns exit code."""
    path = _resolve_graph_path(graph_json_path)
    graph_data = _load_from_file(path)
    if graph_data is None or graph_data.get("graph") is None:
        sys.stderr.write(
            f"[graph_server] failed to load graph from {path}. "
            "Run the harness pipeline to generate it.\n",
        )
        return 1
    graph = graph_data["graph"]

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            req = json.loads(line)
        except json.JSONDecodeError:
            sys.stdout.write(json.dumps({
                "jsonrpc": "2.0",
                "id": None,
                "error": {"code": -32700, "message": "Parse error"},
            }) + "\n")
            sys.stdout.flush()
            continue
        resp = _handle_request(req, graph)
        if resp is not None:
            sys.stdout.write(json.dumps(resp, ensure_ascii=False) + "\n")
            sys.stdout.flush()
    return 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Harness graph stdio MCP server")
    parser.add_argument(
        "graph_path",
        nargs="?",
        default=".claude/wiki/graph.json",
        help="Path to graph.json (default: .claude/wiki/graph.json)",
    )
    args = parser.parse_args()
    sys.exit(main(args.graph_path))
