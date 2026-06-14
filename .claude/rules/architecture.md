# ARCH-001 遵守声明的模块边界

## 规则

> **模块导航提示**: 查询 `.claude/wiki/graph.json`（god_nodes / communities）快速定位跨边界依赖节点，再按需读取对应 wiki 模块页。graph.json 不存在时直接读 `.claude/wiki/modules/` 相关页面。

依赖关系必须遵循声明的架构顺序。本项目中的模块：asset-reference, authorization, mcp-server, prefab-index, tool-contract, tool-registry, ui-check, unity-adapters, unity-bridge。

```text
错误做法：
src/components/Card -> 直接 import 并修改 src/store 的内部实现细节

正确做法：
src/components/Card -> 通过 hooks/useCard 暴露的 action 与 store 交互
```

原因：直接跨层调用绕过 contract，导致测试困难，破坏 Agent 所有权边界。
适用范围：所有声明了模块边界的项目。
