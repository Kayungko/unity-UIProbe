# UIProbe 工作台化与自有 MCP 重构 — Harness 概览

> 由 scaffold_gen 生成。

此文件是 Harness 框架的概览视图。

## Source of Truth

- `.claude/project.json`


## Workflow Policy

- `completeness_mode`: `balanced`
- `ownership_mode`: `solo`
- `question_protocol`: `structured`
- `review_depth`: `standard`
- `require_ascii_diagrams`: `auto`
- `design_review_mode`: `none`
- `extraction_mode`: `standard`

## Workspace Readiness

- `就绪度目标`: `governance_ready`
- `当前状态`: `governance_ready`
- `Sprint 行为`: 由就绪度决定
- `就绪度阻塞`:
  _(none)_

## Core Components

- `CLAUDE.md` for 会话地图和导航
- `.claude/rules/` for 架构和编码约束
- `.claude/progress/` and `.claude/milestones/` for 任务进度和状态追踪
- `.claude/commands/` for PBVF 工作流命令
- `.claude/agents/` for Agent 所有权和职责
- `.claude/wiki/` for 领域知识 Wiki
- `.claude/tdd/` for 测试驱动开发规格

## Baseline Rules

- `.claude/rules/architecture.md`
- `.claude/rules/csharp-safety.md`
- `.claude/rules/platform-isolation.md`
- `.claude/rules/testing-gates.md`

## Verification Commands

_(none)_

## Bootstrap Checks

_(none)_
