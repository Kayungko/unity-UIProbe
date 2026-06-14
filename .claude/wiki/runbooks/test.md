# Test 手册

> 由 wiki_gen 生成。

## 命令

测试基础设施在 M1(T1-2)建立之前不存在,因此 project.json 的 verification_commands 故意留空。M1 后的测试手段:

- **C# 单测(UIProbe.Tests.Editor)**:Unity Editor → Window → General → Test Runner → EditMode 标签页运行;经内存假体(InMemoryAssetGateway/FileSystem/EditorPrefs)与黄金样本回归。
- **Node 测试(mcp-server,M4 起)**:`cd mcp-server && npm test`(vitest 或 node:test,以 mock Bridge 验证握手/代理/reload)。

> Unity batchmode 命令行跑测试(`Unity -runTests -testPlatform EditMode ...`)的具体路径列为 open_question,M1 完成后再确认并回填到 verification_commands。

## 前置条件

- M1(T1-2)已建立 UIProbe.Tests.Editor.asmdef 并引入 Unity Test Framework(com.unity.test-framework)
- M1(T1-3)已提供三组内存假体;M1(T1-5)已建立黄金样本回归机制

## 故障排查

- Test Runner 不显示测试 → 确认 Tests.Editor.asmdef 引用了 nunit + UnityEditor.TestRunner 且 includePlatforms 限 Editor
- 黄金样本误失败 → 先确认是否为预期行为变化,需刷新基线时用显式刷新开关(勿误覆盖)
- Node 测试连不上 mock Bridge → 检查 stub HTTP 端口与 token 文件路径
