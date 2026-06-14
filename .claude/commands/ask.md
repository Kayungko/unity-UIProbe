# /ask

> **Invoked by:** user-session (用于调查问题、理解代码或评估方案，纯分析、不做任何执行)

## 会话前导

执行这个 command 之前，先完成下面的初始化步骤：

1. 阅读 `.claude/progress/session-log.md`，先理解当前状态。
2. 定位任务相关代码/模块时，先走 graph 查询（`mcp__graphify__query_graph` → `get_node` / `get_neighbors` / `shortest_path`）预定位；仅当 graph 未命中或 `.claude/wiki/graph.json` 不存在时，才用 Grep / Glob 做 raw 搜索。
3. 读取当前 task 上下文所适用的 `.claude/rules/` 文件。

## Objective

调查问题、解释代码、评估方案或权衡取舍，走"收集线索 → graph-first 定位 → 分析 → 输出结论"流程。

**这是只读命令**：只产出调查结论与建议，**不修改任何代码、不创建文件、不提交、不写 session-log**。如果调查后需要落地改动，由用户决定是否转入 `/plan`、`/debug` 或 `/quick`。

## Workflow Policy

- `completeness_mode`: `balanced`
- `ownership_mode`: `solo`
- `question_protocol`: `structured`
- `review_depth`: `standard`

## Steps

### Step 1 — 解析参数

剩余文本即调查问题 `$QUESTION`。

如果 `$QUESTION` 为空，使用 AskUserQuestion 询问：

```
AskUserQuestion(
  header: "Ask",
  question: "你想调查什么问题？（可以是 bug 现象、代码疑问、设计权衡、影响范围评估等）",
  options: []
)
```

如果仍然为空，提示用户必须提供调查问题。

展示 banner：

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 HARNESS ► ASK (调查模式 · 只读)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

◆ $QUESTION
```

### Step 2 — 收集线索 & graph-first 调查

1. 读 `session-state.json` 了解当前 milestone 和任务状态。
2. 读 `session-log.md` 了解近期进展、决策和已知问题，判断该问题是否已有相关上下文。
3. 读与问题相关的规则文件和领域文档（`.claude/rules/`、`.claude/wiki/`）。
4. **graph-first 定位**：先用 `mcp__graphify__query_graph` 定位相关节点，再用 `get_node` / `get_neighbors` / `shortest_path` 展开调用链与跨文件依赖；仅当 graph 未命中或 `.claude/wiki/graph.json` 不存在时，才用 Grep / Glob 做 raw 搜索。
5. 按需读取相关源码、测试、git log，追踪事实链。

### Step 3 — 分析 & 输出调查结论

完成调查后，向用户呈现结构化结论。**只输出结论，不做任何编辑、写入或提交。**

1. **结论** — 对 `$QUESTION` 的直接回答（根因 / 代码行为 / 设计判断），指向具体代码位置（`file:line`）。
2. **依据** — 支撑结论的关键证据（调用链、契约、测试、git 历史）。
3. **影响范围** — 涉及哪些模块/功能，是否触及模块边界。
4. **可选方案**（如问题涉及改动决策）— 给出 2-3 个方案及取舍，标注推荐项；不实现。
5. **建议下一步** — 若需落地，建议转入哪个命令（`/plan` 做规划、`/debug` 修 bug、`/quick` 小改动），并说明理由。

如果证据不足以得出结论，明确说明缺口并向用户请求更多信息，不要臆测。

## Required Artifacts

- 调查结论已呈现给用户（无文件产物、无提交）

## Completion Status

See `.claude/rules/completion-status.md` (auto-loaded via settings.json).

## Exit Condition

调查结论已呈现给用户，全程未做任何代码改动、文件创建或提交。如需落地改动，已建议用户转入相应命令。
