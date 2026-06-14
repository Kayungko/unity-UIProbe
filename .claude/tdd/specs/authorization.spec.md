# authorization 测试规格

> 由 tdd_gen 生成。

## 概述

- 覆盖率目标: 80%
- 测试框架: xUnit
- 测试类型: unit, integration

## 可测接口

- **LoadConfig** `[internal]`
  - 加载 mcp.config.toml(共享)叠加 mcp.local.toml(本地)得到生效 Profile + Mode。
- **Authorize** `[internal]`
  - 对一次工具调用做统一授权判定。
- **Audit** `[internal]`
  - 把调用与判定结果追加写审计 JSONL。

## 测试用例

### LoadConfig

- [ ] 正常路径: 加载 mcp.config.toml(共享)叠加 mcp.local.toml(本地)得到生效 Profile + Mode。
- [ ] 错误路径: CONFIG_INVALID
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Asset missing: null mesh / missing material / texture load failure
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### Authorize

- [ ] 正常路径: 对一次工具调用做统一授权判定。
- [ ] 错误路径: PERMISSION_DENIED
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### Audit

- [ ] 正常路径: 把调用与判定结果追加写审计 JSONL。
- [ ] 错误路径: IO_ERROR
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation


## 骨架文件

→ [tests/authorizationTests.cs](../../tests/authorizationTests.cs)
