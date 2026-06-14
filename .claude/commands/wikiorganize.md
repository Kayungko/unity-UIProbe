# /wikiorganize

> **Invoked by:** user-session

## 会话前导

执行这个 command 之前，先完成下面的初始化步骤：

1. 阅读 `.claude/progress/session-log.md`，先理解当前状态。
2. 定位任务相关代码/模块时，先走 graph 查询（`mcp__graphify__query_graph` → `get_node` / `get_neighbors` / `shortest_path`）预定位；仅当 graph 未命中或 `.claude/wiki/graph.json` 不存在时，才用 Grep / Glob 做 raw 搜索。
3. 读取当前 task 上下文所适用的 `.claude/rules/` 文件。

## Objective

使用 graphify 重新分析项目（源代码 + 文档），用最新图谱数据刷新 wiki 页面，并清理孤立的 wiki 页面。

## Workflow Policy

- `completeness_mode`: `balanced`
- `ownership_mode`: `solo`
- `question_protocol`: `structured`
- `review_depth`: `standard`

## Steps

### Step 1 -- Read Context

1. 读 `.claude/progress/session-state.json` 了解当前 milestone。
2. 读 `.claude/project.json` 获取项目配置。注意：自 M3 起，
   `modules` 和 `milestones` 数组不再存放在 `project.json` 内部。
3. 定位 modules。优先级顺序：
   - `.scratch/modules/` 目录（拆分格式，每个模块一个 JSON）-- **首选**
   - `.scratch/modules.json`（单一数组文件）-- 回退
   若两者都不存在，以清晰的提示中止：用户必须先声明
   modules（其 `paths` 指向源代码），wikiorganize 才能把代码
   抽取进图谱。
4. 同理定位 milestones：`.scratch/milestones/` 或 `.scratch/milestones.json`。
5. 确认所有 milestone task 均为 DONE（或确认用户有意在 milestone 进行中重整）。

### Step 2 -- Reorganize Wiki

1. **定位 skill 脚本目录。** `wiki_organize.py` 随 harness skill 一起分发，
   而非用户项目，因此其路径是绝对路径。
   标准安装位置（按此顺序检查是否存在）：

   - Windows: `%USERPROFILE%\.claude\skills\harnessframework\scripts\`
   - macOS/Linux: `$HOME/.claude/skills/harnessframework/scripts/`
   - Claude Code plugin dirs: `$CLAUDE_PROJECT_DIR/.claude/plugins/*/skills/harnessframework/scripts/`
     或类似的 plugin 路径

   运行前先确认 `<SKILL_SCRIPTS>/wiki_organize.py` 存在。

2. **调用 CLI**（注意脚本为绝对路径）：
   ```
   # Windows (PowerShell)
   python "$env:USERPROFILE\.claude\skills\harnessframework\scripts\wiki_organize.py" `
       --root "<project_root>" `
       --project "<project_root>\.claude\project.json" `
       --modules "<project_root>\.scratch\modules" `
       --milestones "<project_root>\.scratch\milestones" `
       --force

   # macOS / Linux / git-bash
   python "$HOME/.claude/skills/harnessframework/scripts/wiki_organize.py" \
       --root "<project_root>" \
       --project "<project_root>/.claude/project.json" \
       --modules "<project_root>/.scratch/modules" \
       --milestones "<project_root>/.scratch/milestones" \
       --force
   ```
   `--modules` 和 `--milestones` 既接受单一 JSON 文件（数组），
   也接受存放拆分 JSON 的目录。CLI 本身不依赖 CWD ——
   始终为 `--root`、`--project`、`--modules`、`--milestones` 使用项目的绝对路径。
2. 该管线将：
   - **Preflight step** —— 为 `modules[*].paths` 中检测到的每种
     语言（TypeScript、Go、Rust、Java、C#、C++）自动安装 tree-sitter
     grammar 包。进度通过 stderr 打印为：
     ```
     [harness] Languages detected: TypeScript (.ts, .tsx)
     [harness] Ensuring tree-sitter grammars...
     [harness]   .ts: OK
     [harness]   .tsx: OK
     [harness] Preflight complete.
     ```
     纯 Python 项目跳过此步（标准库 `ast` 已足够）。用户
     无需手动 `pip install` 任何东西。若安装失败
     （例如无网络），管线会记录 WARN 并以降级模式继续
     （对受影响语言回退为 doc-only）。
   - 从源代码（经 modules.paths）和文档（`.claude/wiki/`）
     构建全新的知识图谱。
   - 用最新图谱数据重新生成全部 wiki 页面。
   - 重新生成 graph-wiki/ 社区文章和 graph.json。
   - 移除图谱中已不存在的孤立 graph-wiki 页面。
   - 运行 wiki 健康检查。
3. 如果输出报告 `nodes: N` 但所有 kind 都是 `concept`/`decision`/`document`
   （没有 `entity`/`interface`），说明代码 AST 抽取被跳过了。检查：
   - `modules[*].paths` 是否确实指向源代码目录
   - preflight step 的 stderr 输出 —— 任何 `FAILED` 行都表示
     某个 grammar 包未能安装（通常是网络问题）

### Step 3 -- Report Changes

报告一份摘要，包含：
- 新增和更新的 wiki 页面。
- 移除的孤立页面。
- 图谱统计（nodes、edges、communities）。
- 剩余的健康问题（死链、占位符、覆盖缺口）。

### Step 4 -- Record in Session Log

向 `session-log.md` 追加一条 wiki organize 记录：

```markdown
### Wiki Organize ($DATE)
- **Refreshed pages:** $COUNT
- **Removed orphans:** $ORPHAN_LIST
- **Graph stats:** $NODES nodes, $EDGES edges, $COMMUNITIES communities
- **Health issues:** $ISSUE_COUNT
```

## Required Artifacts

- 已用最新图谱数据刷新 wiki 页面。
- 已移除孤立的 graph-wiki 页面。
- session log 已更新本次重整摘要。

## Completion Status

See `.claude/rules/completion-status.md` (auto-loaded via settings.json).

## Exit Condition

已用最新图谱数据刷新 wiki 页面，孤立页面已移除，并向用户报告了摘要。
