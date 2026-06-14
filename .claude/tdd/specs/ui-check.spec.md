# ui-check 测试规格

> 由 tdd_gen 生成。

## 概述

- 覆盖率目标: 80%
- 测试框架: xUnit
- 测试类型: unit, integration

## 可测接口

- **RunChecks** `[inbound]`
  - 运行检测,产出结构化报告。
- **GetCheckResults** `[inbound]`
  - 读取上次检测结果。

## 测试用例

### RunChecks

- [ ] 正常路径: 运行检测,产出结构化报告。
- [ ] 错误路径: UNITY_OFFLINE
- [ ] 错误路径: MAIN_THREAD_TIMEOUT
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### GetCheckResults

- [ ] 正常路径: 读取上次检测结果。
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation


## 骨架文件

→ [tests/ui-checkTests.cs](../../tests/ui-checkTests.cs)
