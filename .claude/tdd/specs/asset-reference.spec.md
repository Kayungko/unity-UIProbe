# asset-reference 测试规格

> 由 tdd_gen 生成。

## 概述

- 覆盖率目标: 80%
- 测试框架: xUnit
- 测试类型: unit, integration

## 可测接口

- **FindReferences** `[inbound]`
  - 查询某资源被哪些 prefab/节点/组件引用。
- **ExportCsv** `[inbound]`
  - 导出引用结果为 CSV。

## 测试用例

### FindReferences

- [ ] 正常路径: 查询某资源被哪些 prefab/节点/组件引用。
- [ ] 错误路径: INVALID_PARAMS
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### ExportCsv

- [ ] 正常路径: 导出引用结果为 CSV。
- [ ] 错误路径: IO_ERROR
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation


## 骨架文件

→ [tests/asset-referenceTests.cs](../../tests/asset-referenceTests.cs)
