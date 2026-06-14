# @read-task

How to locate and read a task's full context.

Task details are stored in individual files under `.claude/milestones/tasks/`.

1. Read `.claude/progress/session-state.json` for task status overview and `current_milestone` (preferred over parsing markdown).
2. Read the task detail file: `.claude/milestones/tasks/{task_id}.md` — this contains the full description, acceptance criteria, write paths, dependencies, and context references.
3. Extract: description, acceptance criteria, write_paths, dependencies, context references (wiki link, TDD link).
4. Confirm all dependencies are DONE (check `session-state.json` → `tasks[dep_id].status`) before proceeding.
5. **Map write_paths to graph neighbors**: for each path in write_paths, query `.claude/wiki/graph.json` to find the node and its 1-hop neighbors. This reveals downstream dependencies that this task may affect.
   - Read `graph.json` → find node whose `source_file` matches the write_path → list neighbors (e.g. `scripts/wiki_gen.py` → neighbors: graph_bridge, template_engine, wiki_quality) → check if any neighbor's wiki page needs review.
   - If a write_path has no matching node in the graph, skip it — the path may be new or not yet indexed.
6. **Graph-first wiki navigation**: if the task references a wiki module, locate relevant sections via the knowledge graph before loading full wiki pages:
   a. Check whether `.claude/wiki/graph.json` exists.
   b. If it exists: use graph data (god_nodes, communities) to identify the highest-degree nodes related to the module — read only those wiki sections first.
   c. If `.claude/wiki/graph.json` is not available: Fall back to reading the full wiki module page at `.claude/wiki/modules/{module_slug}.md`.
7. If the task file references a TDD spec, read `.claude/tdd/specs/{module_slug}.spec.md` for test requirements.
