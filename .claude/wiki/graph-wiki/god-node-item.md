# 架构概览

> 由 graph_artifacts 生成。摘要 + 反向链接索引。

## 元数据

- **度数**: 23
- **来源**: .claude/wiki/architecture/overview.md:1
- **所属社区**: 6

架构概览 是一个高连接度节点（度数 23）。属于社区 6。来源：.claude/wiki/architecture/overview.md。

## 邻居（一阶）

| 节点 | 关系 | 来源 | Wiki |
|---|---|---|---|
| overview.md | contains | architecture/overview.md:1 | overview.md |
| 声明架构 | contains | architecture/overview.md:5 | 声明架构 |
| 模块映射 | contains | architecture/overview.md:10 | 模块映射 |
| 边界规则 | contains | architecture/overview.md:24 | 边界规则 |
| 架构决策 | contains | architecture/overview.md:29 | 架构决策 |
| 单一数据源,保证引用结果与索引一致 | decides | architecture/overview.md:32 | 单一数据源,保证引用结果与索引一致 |
| 防止 AI 通过工具篡改/清除自己的审计痕迹 | decides | architecture/overview.md:41 | 防止 AI 通过工具篡改/清除自己的审计痕迹 |
| MCP 生态成熟,AI 客户端对接成本低,与 Unity 端职责清晰分离 | decides | architecture/overview.md:44 | MCP 生态成熟,AI 客户端对接成本低,与 Unity 端职责清晰分离 |
| Domain Reload 后工具集可能变化,serverId 变化是天然失效信号 | decides | architecture/overview.md:47 | Domain Reload 后工具集可能变化,serverId 变化是天然失效信号 |
| 先验证端到端链路稳定性,写操作与授权治理留 v0.2 | decides | architecture/overview.md:50 | 先验证端到端链路稳定性,写操作与授权治理留 v0.2 |
