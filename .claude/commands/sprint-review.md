# /sprint-review

> **Invoked by:** user-session Calls agent: `gc`

## 会话前导

执行这个 command 之前，先完成下面的初始化步骤：

1. 阅读 `.claude/progress/session-log.md`，先理解当前状态。
2. 从 `.claude/milestones/` 确认当前活动的 milestone 和 task。
3. 定位任务相关代码/模块时，先走 graph 查询（`mcp__graphify__query_graph` → `get_node` / `get_neighbors` / `shortest_path`）预定位；仅当 graph 未命中或 `.claude/wiki/graph.json` 不存在时，才用 Grep / Glob 做 raw 搜索。
4. 如果 milestone 或 wiki 自上次会话后发生变化，先运行 `/sync-progress`。
5. 读取当前 task 上下文所适用的 `.claude/rules/` 文件。

## Objective

驱动一轮 TDD red-green-refactor 循环，覆盖当前 milestone 中的每个 task，
随后执行工作区清理、UI 审计和 LLM 驱动的 wiki 审查。

## Workflow Policy

- `completeness_mode`: `balanced`
- `ownership_mode`: `solo`
- `question_protocol`: `structured`
- `review_depth`: `standard`

## Steps

1. 读取 `session-state.json`，获取当前 milestone、task 列表和
   `coverage_gate` 状态。
2. ### Gate Entry Check
   如果并非所有 task 都是 DONE → HALT：报告哪些 task 未完成；不要
   继续门禁流程。如果 `coverage_gate.status == "passed"` → 跳到
   Workspace Hygiene（门禁已通过）。如果 `coverage_gate.status` 为
   `"pending"` 或 `"failed"` → 进入 Red-Green-Refactor 循环
   （允许对失败的门禁重新评估，给整改一次机会）。

3. ### Phase R — Red Audit (per DONE task)
   委派给 `@red-green-cycle` 步骤 1，或直接运行：
   ```
   python .claude/scripts/tdd_sprint_review.py --root <root> --milestone <id>
   ```
   该驱动器一趟执行 Phases R、G、F，并产出：
   - `.claude/progress/red-plan-{milestone}.md` —— 每个 task 的表格，把每条
     验收标准映射到匹配的 spec 勾选项（无匹配则标 `✗ GAP`），
     并为每个缺口附上 arrange/act/assert 桩，以及一份仍未勾选的
     spec 条目列表。
   - `.claude/progress/telemetry/{milestone}.json` —— 标准化的 JUnit 衍生
     报告，含每个 suite 的总计、每个 testcase 的状态，以及每条命令的
     执行元数据。
   审查 red plan。任何 `✗ GAP` 行都是 milestone 缺少的测试 —— 在这些桩
   变成真正通过的测试之前，不要批准门禁。

4. ### Phase G — Green Run
   驱动器运行 `project.json` 中的每条 `verification_command`，捕获
   JUnit XML（默认路径 `.claude/progress/junit-{milestone}.xml`），并在
   通过测试的名称与勾选项文本 token 重叠 ≥ 60% 处勾选 spec 勾选项
   （`- [ ]` → `- [x]`）。

   **语言中立性**：驱动器讲 JUnit XML，每个主流 runner 都能产出：

   | Language | Runner | Flag / plugin |
   |----------|--------|---------------|
   | Python   | pytest | `--junitxml=<path>` |
   | TS / JS  | vitest | `--reporter=junit --outputFile=<path>` |
   | TS / JS  | jest   | `jest-junit` reporter |
   | Go       | go test| pipe through `go-junit-report > <path>` |
   | Rust     | cargo  | `cargo nextest run --junit-xml=<path>` |
   | Java     | JUnit  | maven-surefire / gradle native |
   | C#       | dotnet | `--logger "junit;LogFilePath=<path>"` |
   | C / C++  | gtest  | `--gtest_output=xml:<path>` |

   如果 `verification_commands` 尚未产出 JUnit，更新它们 —— 或在 project.json
   中设置 `test_result_format: "none"` 以跳过标准化（会丢失每个 testcase 的
   telemetry；只保留 exit-code 的 green/red）。

5. ### Phase F — Refactor Loop (hybrid authority)
   对驱动器摘要中报告的每个失败：
   - **Simple classification**（AssertionError、TypeError、NameError、
     ImportError、`expect(...).to*`、`assert_eq` 等）→ 以上下文
     `{failing_test, message, task_id}` 自动调用 `/fix`。在 `/fix` 之后，重新运行
     Phase G。计入迭代次数。
   - **Complex classification**（timeout、connection refused、端口占用、
     OOM、segfault、空消息）→ 用 `AskUserQuestion` 给出失败摘要和
     诊断提示。选项：`/fix` / skip-and-note / defer。
     绝不自动修复这些。

   将循环上限设为 **3 次迭代**。达到上限时，设置 `coverage_gate.status =
   "failed"`，并写入 `coverage_gate.iterations_used = 3` 以及
   `classification` 计数。

6. ### Phase P — Persist
   更新 `session-state.json.milestones[i].coverage_gate`：
   ```json
   {
     "status": "passed" | "failed",
     "target": <int>,
     "actual": <int>,
     "report": "<one-line summary>",
     "checked_at": "<ISO-8601>",
     "red_plan_path": ".claude/progress/red-plan-{milestone}.md",
     "telemetry_path": ".claude/progress/telemetry/{milestone}.json",
     "iterations_used": <int>,
     "classification": {"simple_autofixed": <int>, "complex_deferred": <int>}
   }
   ```
   门禁仅在 `actual >= target` 且所有 verification_commands 报告
   零 failures 零 errors 时才通过。绝不降低 `target`。

7. ### Workspace Hygiene
   调用 GC agent 扫描孤儿产物。

8. 审查已完成 task 的代码质量。

9. ### UI Visual Audit
   如果项目有 UI（`project_type` 为 web-app、web-service、desktop-app
   或 mobile-app）：运行 `@run-ui-audit`，对关键页面/界面截图，
   与 wiki spec 和 TDD 场景比对，并报告
   布局 / 渲染 / 内容缺失问题。

10. ### Wiki Semantic Review (LLM-driven, fill-* pattern)
    本步骤仅在 `/sprint-review` 期间运行；`/verify` 和 `/build` 永远不会
    触发它（成本控制）。循环是 **emit → fill → ingest**：

    1. **Emit pending files.** 运行：
       ```bash
       python .claude/scripts/wiki_review.py --root <project_root> --emit-wiki-review
       ```
       这会在 `.claude/progress/` 下写出三个文件：
       - `wiki-coverage-pending.md` —— PRD 与 wiki 的覆盖缺口
       - `wiki-ambiguity-pending.md` —— 接口签名清晰度
       - `wiki-conflict-pending.md` —— 跨模块矛盾

    2. **Fill `<fill-findings>`.** 依次打开每个 pending 文件。阅读
       `## Instruction` 部分，查阅 `## Context files` 下列出的每个路径，
       并遵循 `## Schema` 块。把 `## Findings` 下的
       `<fill-findings>` 占位符替换为一个形如下面的 YAML 块：
       ```yaml
       findings:
         - severity: high | medium | low
           category: <fixed per file — coverage_gap / signature_ambiguity / cross_module_conflict>
           source_ref: <wiki path:Line or PRD §section>
           module: <module slug, optional>
           detail: <one-sentence problem description>
           suggested_fix: <one-sentence remediation>
       ```
       只写 YAML —— 无散文，无前言。包在 ```yaml 围栏块中。每个文件
       上限 20 条发现，最重要的在前。若无问题则填 `findings: []`。

    3. **Ingest.** 运行：
       ```bash
       python .claude/scripts/wiki_review.py --root <project_root> --ingest-wiki-review
       ```
       - `high` 严重度发现变为 errors（exit 非零，sprint 阻塞）。
       - `medium` / `low` 变为 warnings（非阻塞）。
       - 已填写的 pending 文件归档到 `.claude/progress/history/`。
       - 未填写的文件原地保留并附警告 —— 重新填写后
         重新运行 ingest。
       - Schema 错误以 warning 报告，不会崩溃。

    4. **Remediate & repeat.** 如果报告了任何 `high` 发现，把每条的
       `suggested_fix` 应用到 wiki，然后从 (1) 重启本步骤。
       持续直到 ingest 报告无 `high` 发现。`medium` / `low`
       发现仅供参考，可以延后处理。

11. 如果 `coverage_gate.status == "failed"` 或 wiki 语义审查存在
    未解决的 `high` 发现：报告哪些 task 处于红色、哪些
    勾选项仍未勾选，以及哪些 wiki 发现仍在阻塞。
    不要清除门禁 —— sprint 无法推进。不要建议
    降低 target。

12. 报告所有发现。如果门禁通过，且 UI 审计无关键问题，
    且 wiki 语义审查报告无 `high` 发现，则说明 `/sprint`
    现在可以自动推进到下一个 milestone。

13. ### Session-Log Compression Gate
    调用 `@write-session-log`，`entry_type: "sprint-review"`。该 skill
    会从 `session-state.json` 刷新 Pending 层（带入本次审查的任何新
    阻塞），并通过渐进式压缩强制执行 100 行硬上限。不要直接向
    `session-log.md` 追加内容。

## Required Artifacts

保持 session-state.json 和 session-log.md 与实际结果和未解决的阻塞对齐。
`red-plan-{milestone}.md` 和 `telemetry/{milestone}.json` 会被保留供下次迭代参考。

## Review Heuristics

Review depth: `standard`

### Hard Exclusions (Auto-Discard)

- 已被 linter/formatter 覆盖的纯格式或风格问题
- import 排序变更
- 无语义影响的纯注释变更
- 纯空白 diff

### Confidence Gate

每条报告的发现都必须包含置信度评分（1-10）。
只有置信度 >= 7 的发现才应标记为可执行项。
置信度 4-6 的发现应记为观察项。
置信度 <= 3 的发现应丢弃。

### Verification Requirements

- task 的所有验收标准都必须用证据验证
- 现有测试无回归（运行完整的 verification_commands）
- 代码遵循声明的架构边界（检查模块 write_paths）
- 新增的公开行为有对应的测试

### Workspace Hygiene

- 检查孤儿文件（未关联到 task 的 wiki 页面、TDD spec、agent 文件）
- 检查死代码（未使用的 import、不可达分支）
- 检查文档-代码漂移（wiki 声称的内容 vs 实际实现）
- 检查测试缺口（无测试覆盖的公开 API）

## Completion Status

See `.claude/rules/completion-status.md` (auto-loaded via settings.json).

## Exit Condition

Red-green-refactor 循环完成（red plan 已产出、green run 已执行、
refactor 循环在上限内终止）、清理扫描完成、UI 审计
已记录，且 wiki 语义审查无未解决的 `high` 发现。
