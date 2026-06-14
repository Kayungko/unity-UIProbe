# UIProbe 工作台化与自有 MCP 重构 Wiki

> 由 wiki_gen 生成。

## Graph 导航

| 社区 | 主题/关键词 | God Node | 链接 |
|-----------|---------------|----------|------|
| 0 | tool-contract 模块文档, 场景, 实体, 所属路径, 接口, ... | tool-contract.md | [community-0](graph-wiki/community-0.md) |
| 1 | mcp-server 模块文档, 场景, 实体, 所属路径, 接口, ... | mcp-server.md | [community-1](graph-wiki/community-1.md) |
| 10 | API 契约, asset-reference, authorization, mcp-server, prefab-index, ... | - | [community-10](graph-wiki/community-10.md) |
| 11 | Build 手册, 前置条件, 命令, 故障排查, build.md | - | [community-11](graph-wiki/community-11.md) |
| 12 | Test 手册, 前置条件, 命令, 故障排查, test.md | - | [community-12](graph-wiki/community-12.md) |
| 2 | authorization 模块文档, 场景, 实体, 所属路径, 接口, ... | authorization.md | [community-2](graph-wiki/community-2.md) |
| 3 | prefab-index 模块文档, 场景, 实体, 所属路径, 接口, ... | prefab-index.md | [community-3](graph-wiki/community-3.md) |
| 4 | unity-bridge 模块文档, 场景, 实体, 所属路径, 接口, ... | unity-bridge.md | [community-4](graph-wiki/community-4.md) |
| 5 | tool-registry 模块文档, 场景, 实体, 所属路径, 接口, ... | tool-registry.md | [community-5](graph-wiki/community-5.md) |
| 6 | 架构概览, 声明架构, 架构决策, 模块依赖图, 模块映射, ... | 架构概览 | [community-6](graph-wiki/community-6.md) |
| 7 | unity-adapters 模块文档, 场景, 实体, 所属路径, 接口, ... | unity-adapters.md | [community-7](graph-wiki/community-7.md) |
| 8 | ui-check 模块文档, 场景, 实体, 所属路径, 接口, ... | ui-check.md | [community-8](graph-wiki/community-8.md) |
| 9 | asset-reference 模块文档, 场景, 实体, 所属路径, 接口, ... | asset-reference.md | [community-9](graph-wiki/community-9.md) |

### MCP 快速查询

```
graph_query(keyword="...") → node_ids → Read target wiki page
```

## 架构

- [架构概览](architecture/overview.md) ★★★

## 模块

- [asset-reference](modules/asset-reference.md) ★★★ — AssetReferenceService(只读),统一处理某资源被哪些 prefab / 节点 / 组件使用。不另存副本,查询时基于 prefab-index 模块的 PrefabIndex 派生。支持按资源路径、资源名、GUID、Sprite 名称、引用类型过滤查询,并可导出 CSV。 [2 files]
- [authorization](modules/authorization.md) ★★★ — 权限与授权治理,两个正交维度:Capability Profile(能力面,决定哪些工具/路径可见可调)与 Authorization Mode(批准策略,决定调用时是否需人确认)。Profile 含 SafeDefault/TeamAutomation/TrustedProject/AdminDebug;Mode 含 请求批准/替我批准/完全访问/自定义。配置经 mcp.config.toml(团队共享入库)+ mcp.local.toml(本地覆盖不入库)叠加。写操作经 write_allow/write_deny 路径约束。所有调用落审计 JSONL(不入库)。token 鉴权防同机恶意进程直连。v0.1 只读不触发授权判定,框架预留。 [2 files]
- [mcp-server](modules/mcp-server.md) ★★★ — Node/TypeScript 实现的 MCP Server(Orchestrator),对外讲标准 MCP 协议给 AI 客户端,对内通过 HTTP loopback 连 Unity Bridge。负责:连接/会话管理、版本与契约握手、工具发现缓存、把 MCP tool 调用翻译成 Bridge /rpc、Domain Reload 期间退避重试与恢复、把 Unity 端结构化错误透传成 MCP 错误。v0.1 只暴露只读工具,自身不直接碰 Unity API。多 Unity 实例时按 projectPath 路由。 [1 files]
- [prefab-index](modules/prefab-index.md) ★★★ — Prefab Index 是后续工作台的核心底座,优先抽离为 PrefabIndexService(只读)。从 UIProbeWindow_Indexer.cs 抽出查找 prefab、构建 folder tree、收集 Image/RawImage/Prefab/Material 等资源引用、保存加载 IndexCache、搜索展开的非 UI 部分。PrefabIndex 是多个能力的单一数据源 -- 引用追踪、重复检测、嵌套总览、过滤扫描都基于它派生,不各自缓存。先抽离它以验证 ToolContract + Adapter 接缝 + 黄金样本基线闭环。 [5 files]
- [tool-contract](modules/tool-contract.md) ★★★ — UIProbe 工具层的唯一权威契约。UI Toolkit 工作台、MCP Server、内部 Flow 全部构造 ToolRequest、消费 ToolResult,差异仅在传输层(进程内调用 vs JSON-RPC)。凡涉及 ToolDescriptor/ToolRequest/ToolResult/Change/Issue/Preview-Execute/错误码的描述,以本契约为准,其他模块只能引用不得另定义。 [1 files]
- [tool-registry](modules/tool-registry.md) ★★★ — 工具注册 / 发现 / 执行的统一入口。注册内置工具与项目扩展工具,维护 descriptor / 参数 schema / 安全等级 / 来源 / 启用状态,统一执行入口与 Preview/Execute 协议,并根据当前 Capability Profile 过滤、禁用或要求确认特定工具。MCP 与 UI 都不得绕过 ToolRegistry 直接调 Service。 [1 files]
- [ui-check](modules/ui-check.md) ★★★ — UICheckService(只读,含结构化报告)-- 这是其他 Unity MCP 难以提供的差异化能力,作为早期采用的核心拉力。把综合检测、重名/重复检测、过滤节点扫描统一成结构化 Issue 模型。初始检测项:重名节点、缺失 Sprite、缺失 Font、不必要 Raycast Target、空 Text、命名规范问题。过滤节点扫描并入本模块作为一类检测规则;复用 prefab-index 的 PrefabIndex。 [5 files]
- [unity-adapters](modules/unity-adapters.md) ★★★ — Unity API 抽象接缝,可测性的前提。现有业务大量直接调用 AssetDatabase / PrefabStageUtility / EditorPrefs / File 静态 API,导致 Service 抽离后仍无法单元测试。约定 Service 不直接调用静态 Unity API,而经 Adapter 接口注入 -- 生产用 Unity 实现,测试用内存假体。没有这层接缝,UIProbe.Tests.Editor.asmdef 形同虚设。 [1 files]
- [unity-bridge](modules/unity-bridge.md) ★★★ — Unity Editor 内的本地 HTTP JSON-RPC bridge(v0.1 仅 HTTP + loopback,WebSocket 留 v0.2)。暴露 /health、/rpc、/tools/list、/tools/describe;经 MainThread Dispatcher 把请求投递到 Unity 主线程执行;调用 ToolRegistry 返回结构化结果;Domain Reload 后自动重建并重新上报 capabilities。把 Domain Reload 当成常态而非异常。 [1 files]

## 规格

- [API 契约](specs/api-contracts.md) ★★

## 手册

- [Build 手册](runbooks/build.md) ★
- [Test 手册](runbooks/test.md) ★

## Graph Wiki

基于图拓扑自动生成的社区文章与 god-node 文章：[graph-wiki/index.md](graph-wiki/index.md)
