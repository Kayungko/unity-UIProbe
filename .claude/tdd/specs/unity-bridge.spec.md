# unity-bridge 测试规格

> 由 tdd_gen 生成。

## 概述

- 覆盖率目标: 80%
- 测试框架: xUnit
- 测试类型: unit, integration

## 可测接口

- **GET /health** `[inbound]`
  - 返回 HealthStatus,供握手与 Domain Reload 检测。
- **POST /rpc** `[inbound]`
  - JSON-RPC 调用工具,经 Dispatcher 主线程执行。请求头携带 session token。
- **GET /tools/list, /tools/describe** `[inbound]`
  - 代理 ToolRegistry 的发现接口。
- **MainThreadDispatcher** `[internal]`
  - EditorApplication.update 回调逐帧 drain 并发队列、主线程执行、结果写回 TCS。

## 测试用例

### GET /health

- [ ] 正常路径: 返回 HealthStatus,供握手与 Domain Reload 检测。
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### POST /rpc

- [ ] 正常路径: JSON-RPC 调用工具,经 Dispatcher 主线程执行。请求头携带 session token。
- [ ] 错误路径: UNITY_BUSY
- [ ] 错误路径: MAIN_THREAD_TIMEOUT
- [ ] 错误路径: DOMAIN_RELOAD_INTERRUPTED
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### GET /tools/list, /tools/describe

- [ ] 正常路径: 代理 ToolRegistry 的发现接口。
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### MainThreadDispatcher

- [ ] 正常路径: EditorApplication.update 回调逐帧 drain 并发队列、主线程执行、结果写回 TCS。
- [ ] 错误路径: MAIN_THREAD_TIMEOUT
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation


## 骨架文件

→ [tests/unity-bridgeTests.cs](../../tests/unity-bridgeTests.cs)
