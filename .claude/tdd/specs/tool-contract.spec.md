# tool-contract 测试规格

> 由 tdd_gen 生成。

## 概述

- 覆盖率目标: 80%
- 测试框架: xUnit
- 测试类型: unit, integration

## 可测接口

- **UIProbeTool<TParams>** `[internal]`
  - 工具基类。ToolRegistry 按 Phase 调 Preview 或 Execute。
- **DescribeParams** `[internal]`
  - 返回工具参数 JSON-Schema。
- **Validate** `[internal]`
  - 语义校验,失败转 ToolResult.Issues / INVALID_PARAMS,不抛异常穿透。
- **Preview** `[internal]`
  - SupportsPreview=true 时必须重写,产出 OperationId + PlannedChanges + Risks。
- **Execute** `[internal]`
  - 凭 OperationId + ConfirmationToken 落地变更,产出 AppliedChanges + UndoId。

## 测试用例

### UIProbeTool<TParams>

- [ ] 正常路径: 工具基类。ToolRegistry 按 Phase 调 Preview 或 Execute。
- [ ] 错误路径: INVALID_PARAMS
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### DescribeParams

- [ ] 正常路径: 返回工具参数 JSON-Schema。
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### Validate

- [ ] 正常路径: 语义校验,失败转 ToolResult.Issues / INVALID_PARAMS,不抛异常穿透。
- [ ] 错误路径: INVALID_PARAMS
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### Preview

- [ ] 正常路径: SupportsPreview=true 时必须重写,产出 OperationId + PlannedChanges + Risks。
- [ ] 错误路径: INVALID_PARAMS
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### Execute

- [ ] 正常路径: 凭 OperationId + ConfirmationToken 落地变更,产出 AppliedChanges + UndoId。
- [ ] 错误路径: OPERATION_EXPIRED
- [ ] 错误路径: CONFIRMATION_REQUIRED
- [ ] 错误路径: EXECUTION_FAILED
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation


## 骨架文件

→ [tests/tool-contractTests.cs](../../tests/tool-contractTests.cs)
