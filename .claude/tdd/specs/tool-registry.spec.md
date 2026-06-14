# tool-registry 测试规格

> 由 tdd_gen 生成。

## 概述

- 覆盖率目标: 80%
- 测试框架: xUnit
- 测试类型: unit, integration

## 可测接口

- **Register** `[inbound]`
  - 注册一个工具(内置或项目扩展)。
- **ListTools** `[inbound]`
  - 列出当前 Profile 可见的工具(NOT_IN_PROFILE 的不出现)。
- **DescribeTool** `[inbound]`
  - 返回单个工具的完整 descriptor + 参数 schema。
- **Invoke** `[inbound]`
  - 统一执行入口:校验 schema -> 按 Phase 调 Preview/Execute -> 返回 ToolResult。

## 测试用例

### Register

- [ ] 正常路径: 注册一个工具(内置或项目扩展)。
- [ ] 错误路径: TOOL_NOT_FOUND(重复 Id 报错)
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### ListTools

- [ ] 正常路径: 列出当前 Profile 可见的工具(NOT_IN_PROFILE 的不出现)。
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### DescribeTool

- [ ] 正常路径: 返回单个工具的完整 descriptor + 参数 schema。
- [ ] 错误路径: TOOL_NOT_FOUND
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### Invoke

- [ ] 正常路径: 统一执行入口:校验 schema -> 按 Phase 调 Preview/Execute -> 返回 ToolResult。
- [ ] 错误路径: TOOL_NOT_FOUND
- [ ] 错误路径: INVALID_PARAMS
- [ ] 错误路径: NOT_IN_PROFILE
- [ ] 错误路径: PERMISSION_DENIED
- [ ] 错误路径: CONFIRMATION_REQUIRED
- [ ] 错误路径: OPERATION_EXPIRED
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation


## 骨架文件

→ [tests/tool-registryTests.cs](../../tests/tool-registryTests.cs)
