# @load-community-context

How to load wiki fragments for a specific graph community, avoiding full module page reads.

## Step 1 — Load Graph Data

1. Check whether `.claude/wiki/graph.json` exists.
2. If it does NOT exist: fall back to reading the full module page at `.claude/wiki/modules/{module_slug}.md`. Stop here.
3. Read `.claude/wiki/graph.json` and parse the `communities` object.

**MCP alternative**: If the graph MCP server is available, use the `graph_query` tool instead of reading `graph.json` directly:
```
graph_query({ "query": "community", "community_id": "<id>" })
```

## Step 2 — Resolve Community Nodes

1. Accept `community_id` as input (integer or string).
2. Look up `communities[community_id]` in the parsed graph data to get the list of node IDs.
3. If the community ID is not found: report "Community not found" and fall back to full module page read.

## Step 3 — Read Community Summary

1. Read the community summary page at `.claude/wiki/graph-wiki/community-{community_id}.md`.
2. This page contains a thin summary and reverse links to module wiki sections.
3. Extract the reverse link targets (e.g., `modules/{slug}.md#entities`, `modules/{slug}.md#interfaces`).

## Step 4 — Load Targeted Wiki Fragments

1. For each reverse link from Step 3, read only the referenced section from `modules/{slug}.md`.
2. Focus on entities and interfaces sections — these contain the contract definitions that agents need.
3. Skip full page reads; the community summary plus targeted sections provide sufficient context.

## Step 5 — Compile Working Context

1. Combine the community summary with the targeted wiki fragments.
2. The assembled context should include: community theme, key entities, interfaces, and cross-module links.
3. Pass this context to the requesting agent or task.

## Fallback

If any step fails (missing graph.json, invalid community ID, missing community page), fall back to reading the full module wiki page at `.claude/wiki/modules/{module_slug}.md`.

## Example Usage

```
Call @load-community-context with: community_id=0
```
