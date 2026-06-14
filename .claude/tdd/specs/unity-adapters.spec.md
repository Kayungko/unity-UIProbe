# unity-adapters 测试规格

> 由 tdd_gen 生成。

## 概述

- 覆盖率目标: 80%
- 测试框架: xUnit
- 测试类型: unit, integration

## 可测接口

- **UnityAssetGateway** `[internal]`
  - IAssetGateway 的生产实现,真实调用 AssetDatabase。
- **InMemoryAssetGateway** `[internal]`
  - IAssetGateway 的测试假体,内存模拟资源表。
- **UnityFileSystem / InMemoryFileSystem** `[internal]`
  - IFileSystem 的生产 / 测试实现对。

## 测试用例

### UnityAssetGateway

- [ ] 正常路径: IAssetGateway 的生产实现,真实调用 AssetDatabase。
- [ ] 错误路径: IO_ERROR
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### InMemoryAssetGateway

- [ ] 正常路径: IAssetGateway 的测试假体,内存模拟资源表。
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation

### UnityFileSystem / InMemoryFileSystem

- [ ] 正常路径: IFileSystem 的生产 / 测试实现对。
- [ ] 错误路径: IO_ERROR
- [ ] 推断错误: Unexpected failure / runtime exception
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation
- [ ] 边界条件: Empty/null input values
- [ ] 边界条件: User cancels mid-operation


## 骨架文件

→ [tests/unity-adaptersTests.cs](../../tests/unity-adaptersTests.cs)
