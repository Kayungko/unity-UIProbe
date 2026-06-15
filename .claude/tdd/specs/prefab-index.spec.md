# prefab-index 测试规格

> 由 tdd_gen 生成,经 /plan(T2-1)落地为可执行规格。

## 概述

- 覆盖率目标: 80%
- 测试框架: NUnit(Unity Test Framework, EditMode)
- 测试类型: unit(经内存假体) + golden-sample 回归
- 验证宿主: `E:\uiprobe-compile-host`(junction 挂 UIProbe);batchmode runTests EditMode

## 可测接口(PrefabIndexService)

- **BuildIndex** `[inbound]` — 经 IAssetGateway 扫描 prefab,产出 PrefabIndex(可增量)。
- **LoadCache** `[inbound]` — 经 IFileSystem 读取缓存 JSON;SchemaVersion 不符则视为缺失。
- **SaveCache** `[inbound]` — 经 IFileSystem 写缓存 JSON(覆盖前 Backup)。
- **Search** `[inbound]` — 在已构建索引上按 query 过滤(路径/名称子串)。
- **GetPrefabDetail** `[inbound]` — 按 GUID/路径取单个 prefab 详情;缺失返回 TOOL_NOT_FOUND。

## 测试用例(T2-1 落地)

### BuildIndex

- [ ] 正常: 假体 seed N 个 prefab → 索引 Items 数 == N,按 AssetPath ordinal 排序确定。
- [ ] 增量: 已有索引 + BuildIndex(Incremental=true) 仅刷新变更项,未变更项保留。
- [ ] 进度: IProgress<float> 单调非降,终值 1.0。
- [ ] 边界: 空根目录 → 空索引(Items.Count==0),不抛异常。
- [ ] 接缝: 全程零静态 AssetDatabase 调用(经 IAssetGateway),证明可在内存假体下运行。

### LoadCache

- [ ] 正常: 写入的缓存可原样回读,Items 与 SchemaVersion 一致。
- [ ] SchemaVersion 失配: 缓存版本 < 当前 → LoadCache 报告失效(触发调用方重建)。
- [ ] 缺失: 文件不存在 → 返回空/失效标记,不抛 IO 异常。

### SaveCache

- [ ] 正常: SaveCache 后 IFileSystem.Exists 为真,内容为合法 JSON。
- [ ] 覆盖备份: 已存在缓存再次 SaveCache → 先经 IFileSystem.Backup 留还原令牌。

### Search

- [ ] 正常: query 命中路径/名称子串的子集,顺序稳定。
- [ ] 边界: 空 query → 返回全部;无命中 → 空列表(非 null)。

### GetPrefabDetail

- [ ] 正常: 存在的 GUID → 返回含 ReferencedAssets/ComponentSummary 的详情。
- [ ] 错误: 不存在的 GUID/路径 → ToolError 码 TOOL_NOT_FOUND。

### Golden 回归(沿用 T1-5 机制)

- [ ] 受控假体索引导出快照(text/csv/json)与录制基线逐行一致;迁移前后零差异。

## 骨架文件

→ 落地于 `UIProbe/Tests/Editor/PrefabIndexServiceTests.cs`(T2-1 write-path)。
