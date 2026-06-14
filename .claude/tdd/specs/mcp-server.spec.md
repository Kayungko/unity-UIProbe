# mcp-server 测试规格

> 由 tdd_gen 生成。

## 概述

- 覆盖率目标: 80%
- 测试框架: xUnit
- 测试类型: unit, integration

## 可测接口

- **ListTools (MCP)** `[inbound]`
  - MCP 客户端发现工具,代理 Bridge /tools/list 并走缓存。
- **CallTool (MCP)** `[inbound]`
  - MCP 客户端调用工具,翻译为 Bridge POST /rpc。
- **Handshake** `[outbound]`
  - 连接 Bridge 时校验 contractVersion/uiProbeVersion。
- **WaitForReload** `[internal]`
  - Domain Reload 期间轮询 /health 等待新 serverId 稳定后恢复。

## 测试用例

### ListTools (MCP)

- [ ] 正常路径: MCP 客户端发现工具,代理 Bridge /tools/list 并走缓存。
- [ ] 错误路径: UNITY_OFFLINE
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### CallTool (MCP)

- [ ] 正常路径: MCP 客户端调用工具,翻译为 Bridge POST /rpc。
- [ ] 错误路径: UNITY_OFFLINE
- [ ] 错误路径: UNITY_BUSY
- [ ] 错误路径: DOMAIN_RELOAD_INTERRUPTED
- [ ] 错误路径: MAIN_THREAD_TIMEOUT
- [ ] 错误路径: TOOL_NOT_FOUND
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### Handshake

- [ ] 正常路径: 连接 Bridge 时校验 contractVersion/uiProbeVersion。
- [ ] 错误路径: VERSION_MISMATCH
- [ ] 错误路径: UNITY_OFFLINE
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### WaitForReload

- [ ] 正常路径: Domain Reload 期间轮询 /health 等待新 serverId 稳定后恢复。
- [ ] 错误路径: RELOAD_TIMEOUT
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Asset missing: null mesh / missing material / texture load failure
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation


## 骨架文件

→ [tests/mcp-serverTests.cs](../../tests/mcp-serverTests.cs)
