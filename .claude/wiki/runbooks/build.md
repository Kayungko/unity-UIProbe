# Build 手册

> 由 wiki_gen 生成。

## 命令

本项目当前没有命令行构建链路(无 .sln/.csproj/asmdef,脚本进 Unity 默认程序集),构建即由 Unity Editor 在脚本变更时自动编译。

- **C# 侧(UIProbe)**:在 Unity Editor 中打开工程,Unity 自动编译;编译结果与错误见 Console 窗口。M1(T1-2)建立 asmdef 三件套后,程序集边界才成形。
- **Node 侧(mcp-server,M4 起存在)**:`cd mcp-server && npm install && npm run build`。

> 在 governance_ready 阶段不提供 `dotnet build` 等命令行构建命令——它们不适用于 Unity Editor 工程。M1 完成后再评估 Unity batchmode 命令行构建路径(open_question)。

## 前置条件

- Unity 2022.3 LTS+(版本最终在 MCP MVP 后锁定)
- Node 侧需 Node.js LTS + npm(仅 mcp-server 子目录)

## 故障排查

- 脚本编译报错 → 看 Unity Console;Domain Reload 后若 Bridge 未重建,确认 [InitializeOnLoad] 入口存在
- asmdef 引用缺失(M1 后)→ 检查 Editor→Runtime、Tests→两者的引用关系
- Node 构建失败 → 删除 `mcp-server/node_modules` 后重新 `npm install`
