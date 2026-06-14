# prefab-index 测试规格

> 由 tdd_gen 生成。

## 概述

- 覆盖率目标: 80%
- 测试框架: xUnit
- 测试类型: unit, integration

## 可测接口

- **BuildIndex** `[inbound]`
  - 构建 prefab 索引(可增量)。
- **LoadCache** `[inbound]`
  - 加载缓存索引。
- **SaveCache** `[inbound]`
  - 保存索引缓存。
- **Search** `[inbound]`
  - 按 query 搜索 prefab。
- **GetPrefabDetail** `[inbound]`
  - 查看单个 prefab 详情(组件/引用)。

## 测试用例

### BuildIndex

- [ ] 正常路径: 构建 prefab 索引(可增量)。
- [ ] 错误路径: UNITY_OFFLINE
- [ ] 错误路径: MAIN_THREAD_TIMEOUT
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### LoadCache

- [ ] 正常路径: 加载缓存索引。
- [ ] 错误路径: IO_ERROR
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Asset missing: null mesh / missing material / texture load failure
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### SaveCache

- [ ] 正常路径: 保存索引缓存。
- [ ] 错误路径: IO_ERROR
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### Search

- [ ] 正常路径: 按 query 搜索 prefab。
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### GetPrefabDetail

- [ ] 正常路径: 查看单个 prefab 详情(组件/引用)。
- [ ] 错误路径: TOOL_NOT_FOUND
- [ ] 推断错误: Invalid input: NaN/Inf in transform or vector input
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation


## 骨架文件

→ [tests/prefab-indexTests.cs](../../tests/prefab-indexTests.cs)
